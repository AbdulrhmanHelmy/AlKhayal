#requires -version 5.1
<#
.SYNOPSIS
    Reusable local acceptance-test runner for AlKhayal QA scenarios (PowerShell 5.1 compatible).

.DESCRIPTION
    Reads the data-only scenario manifest (docs/qa/scenario-manifest.json), runs exactly the
    mapped xUnit tests for the requested scope using exact FullyQualifiedName filters, and
    emits a JSON result with per-scenario PASS/PARTIAL/FAIL/SKIP/BLOCKED statuses, durations, and
    failure summaries.

    Status semantics (never infer a scenario pass from an aggregate suite result):
      PASS     every mapped test for the scenario ran in this run and passed AND the caller
               recorded no manual/browser/desktop/hardware remainder in the manifest
      PARTIAL  every mapped test for the scenario ran and passed, but the scenario carries an
               unexecuted manual remainder (browser/native/desktop/hardware); PARTIAL is never
               converted to PASS and the manual remainder is reported in the record
      FAIL     a mapped test failed, or a mapped test did not appear in the run results
      SKIP     mapped scenario outside the requested run scope
      BLOCKED  unmapped placeholder, no automated mapping, missing tool (dotnet/node), or a
               build failure; also used for a blocked frontend checkpoint

    Exit codes: 0 = no FAIL (PARTIAL is not a FAIL; it means the automated portion passed with
    an open manual remainder), 1 = one or more FAIL (or a failed -All coarse checkpoint),
    2 = database safety gate failed closed, 3 = infrastructure failure (missing tool/build).

    Safety gate: before ANY write-capable test process the runner creates a unique absolute
    SQLite path under the system temp directory (never a pre-existing path), sets the
    ALKHAYAL_DATABASE_PATH process environment for the runner and its children, prints the
    effective path, and verifies the canonical path is outside %LOCALAPPDATA%\AlKhayal and
    differs from the normal alkhayal.db and the repository seeded-database.db. Any guard
    violation fails closed (exit 2) before any test process starts. No source database is
    copied and no unknown path is ever cleaned up.

.PARAMETER All
    Run the complete .NET test suite (both test projects, unfiltered) and the frontend
    registry suite as coarse checkpoints, then attribute per-scenario statuses from the
    per-test results. A global suite pass is never treated as a per-scenario pass.

.PARAMETER Phase
    Run only scenarios whose phase equals this number (1..5). Only mapped relevant tests
    are executed, never the full suite.

.PARAMETER Module
    Run only scenarios whose module equals this value (case-insensitive).

.PARAMETER Scenario
    Run only the scenario with this exact ID (for example QA-004).

.PARAMETER NoBuild
    Skip the build step (test binaries must already be current).

.PARAMETER OutputJson
    Optional path for a UTF-8 JSON result file. JSON is always also written to stdout.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -Scenario QA-004
#>
[CmdletBinding()]
param(
    [switch]$All,
    [ValidateRange(1, 5)]
    [int]$Phase,
    [string]$Module,
    [string]$Scenario,
    [string]$ManifestPath,
    [string]$OutputJson,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptRoot = $PSScriptRoot
if (-not $ScriptRoot) { $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path }
$RepoRoot = Split-Path -Parent $ScriptRoot
if (-not $ManifestPath) { $ManifestPath = Join-Path $RepoRoot 'docs\qa\scenario-manifest.json' }

$UnitProject = Join-Path $RepoRoot 'tests\AlKhayal.UnitTests\AlKhayal.UnitTests.csproj'
$IntegrationProject = Join-Path $RepoRoot 'tests\AlKhayal.IntegrationTests\AlKhayal.IntegrationTests.csproj'
$FrontendTestScript = Join-Path $RepoRoot 'frontend\tests\run-tests.js'

$RunId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$startedUtc = (Get-Date).ToUniversalTime().ToString('o')
$ResultsDir = Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'alkhayal-qa-results' $RunId)

function Write-Info {
    param([string]$Message)
    Write-Host "[acceptance] $Message"
}

function New-ResultObject {
    # Build a mutable ordered result record for one scenario.
    param([object]$Entry)
    return [ordered]@{
        id              = $Entry.id
        phase           = $Entry.phase
        module          = $Entry.module
        title           = $Entry.title
        mapped          = [bool]$Entry.mapped
        partial         = [bool]$Entry.partial
        status          = 'SKIP'
        reason          = $null
        durationMs      = 0
        manualRemainder = $Entry.manualRemainder
        notes           = $Entry.notes
        tests           = @()
        failureSummary  = $null
    }
}

function Get-ToolVersion {
    param([string]$Command, [string]$VersionArgument)
    try {
        $output = & $Command $VersionArgument 2>&1
        if ($LASTEXITCODE -eq 0) { return (("$output" | Select-Object -First 1)).Trim() }
    } catch { }
    return $null
}

function Invoke-DatabaseSafetyGate {
    # Fail-closed guard executed before any write-capable test process.
    $gate = [ordered]@{
        enforced               = $true
        passed                 = $false
        databasePath           = $null
        canonicalPath          = $null
        differsFromDefault     = $false
        outsideAppDataAlKhayal = $false
        differsFromSeeded      = $false
        previousEnvValue       = $env:ALKHAYAL_DATABASE_PATH
        violations             = @()
    }

    $tempRoot = [System.IO.Path]::GetTempPath()
    $dbPath = $null
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $candidateDir = Join-Path $tempRoot ('alkhayal-qa-' + [Guid]::NewGuid().ToString('N'))
        $candidate = Join-Path $candidateDir 'alkhayal-qa.db'
        if (-not (Test-Path -LiteralPath $candidate)) { $dbPath = $candidate; break }
    }
    if (-not $dbPath) {
        $gate.violations = @('Could not generate a unique non-existing database path under the system temp directory after 8 attempts.')
        return $gate
    }

    $canonical = [System.IO.Path]::GetFullPath($dbPath)
    $gate.databasePath = $canonical
    $gate.canonicalPath = $canonical

    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $appDir = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'AlKhayal'))
    $defaultDb = Join-Path $appDir 'alkhayal.db'
    $seededDb = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'seeded-database.db'))

    $compare = [System.StringComparison]::OrdinalIgnoreCase
    if ($canonical.Equals($defaultDb, $compare)) {
        $gate.violations += 'Candidate path equals the normal operator database alkhayal.db.'
    }
    if ($canonical.StartsWith($appDir, $compare)) {
        $gate.violations += ('Candidate path is inside {0}.' -f $appDir)
    }
    if ($canonical.Equals($seededDb, $compare)) {
        $gate.violations += 'Candidate path equals the repository seeded-database.db.'
    }
    if (Test-Path -LiteralPath $canonical) {
        $gate.violations += 'Candidate path already exists; a pre-existing path must never be touched.'
    }

    $gate.differsFromDefault = -not $canonical.Equals($defaultDb, $compare)
    $gate.outsideAppDataAlKhayal = -not $canonical.StartsWith($appDir, $compare)
    $gate.differsFromSeeded = -not $canonical.Equals($seededDb, $compare)
    $gate.passed = ($gate.violations.Count -eq 0)

    if ($gate.passed) {
        # Set the process environment for this runner and every child test process.
        $env:ALKHAYAL_DATABASE_PATH = $canonical
        Write-Info ('SAFETY GATE PASSED')
        Write-Info ('  effective ALKHAYAL_DATABASE_PATH = {0}' -f $canonical)
        Write-Info ('  canonical path                   = {0}' -f $canonical)
        Write-Info ('  DIFFERS_FROM_DEFAULT             = {0} (operator default: {1})' -f $gate.differsFromDefault, $defaultDb)
        Write-Info ('  OUTSIDE_APPDATA_ALKHAYAL         = {0} ({1})' -f $gate.outsideAppDataAlKhayal, $appDir)
        Write-Info ('  DIFFERS_FROM_SEEDED_DATABASE     = {0}' -f $gate.differsFromSeeded)
    } else {
        Write-Info 'SAFETY GATE FAILED - failing closed; no test process will be started.'
        foreach ($violation in $gate.violations) { Write-Info ('  violation: {0}' -f $violation) }
    }
    return $gate
}

function Read-TrxResults {
    param([string]$TrxPath)
    $results = New-Object System.Collections.ArrayList
    if (-not (Test-Path -LiteralPath $TrxPath)) { return , $results }
    [xml]$xml = Get-Content -LiteralPath $TrxPath -Raw
    foreach ($node in $xml.SelectNodes("//*[local-name()='UnitTestResult']")) {
        $testName = $node.GetAttribute('testName')
        $outcome = $node.GetAttribute('outcome')
        $durationMs = 0
        try { $durationMs = [int][Math]::Round([TimeSpan]::Parse($node.GetAttribute('duration')).TotalMilliseconds) } catch { }
        $message = $null
        $errorNodes = $node.SelectNodes("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
        if ($errorNodes -and $errorNodes.Count -gt 0) {
            $message = $errorNodes[0].InnerText
            if ($message) { $message = ($message -replace '\r?\n', ' - ') }
            if ($message -and $message.Length -gt 600) { $message = $message.Substring(0, 600) + ' [...]' }
        }
        [void]$results.Add([pscustomobject]@{
                TestName  = $testName
                Outcome   = $outcome
                DurationMs = $durationMs
                Message   = $message
            })
    }
    return , $results
}

function Get-TestOutcomeForFqn {
    # Match exact FQN, or theory cases whose testName starts with FQN followed by '('.
    param([string]$FullyQualifiedName, [object]$TrxResults)
    $matched = @($TrxResults | Where-Object {
            $_.TestName -eq $FullyQualifiedName -or $_.TestName.StartsWith($FullyQualifiedName + '(')
        })
    if ($matched.Count -eq 0) { return $null }
    $failed = @($matched | Where-Object { $_.Outcome -ne 'Passed' })
    $firstFailure = $null
    if ($failed.Count -gt 0) { $firstFailure = $failed[0] }
    return [pscustomobject]@{
        FullyQualifiedName = $FullyQualifiedName
        Found              = $true
        Cases              = $matched.Count
        Passed             = ($failed.Count -eq 0)
        Outcome            = $(if ($failed.Count -eq 0) { 'Passed' } else { $failed[0].Outcome })
        DurationMs         = [int]($matched | Measure-Object -Property DurationMs -Sum).Sum
        Message            = $(if ($firstFailure) { $firstFailure.Message } else { $null })
    }
}

function Invoke-DotnetTest {
    param(
        [string]$ProjectPath,
        [string]$Filter,
        [string]$TrxFileName,
        [switch]$NoBuild,
        [string]$ProjectLabel
    )
    $trxPath = Join-Path $ResultsDir $TrxFileName
    $arguments = @('test', $ProjectPath)
    if ($NoBuild) { $arguments += '--no-build' }
    $arguments += @(
        '--logger', ('trx;logfilename={0}' -f $TrxFileName),
        '--results-directory', $ResultsDir
    )
    if ($Filter) { $arguments += @('--filter', $Filter) }

    Write-Info ('Running {0} tests{1}: dotnet {2}' -f $ProjectLabel, $(if ($Filter) { ' (exact-FQN filter)' } else { ' (full suite)' }), ($arguments -join ' '))
    $tail = $null
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    $outputText = ($output | ForEach-Object { $_.ToString() }) -join "`n"

    $summary = [ordered]@{
        label      = $ProjectLabel
        ran        = $true
        filter     = $Filter
        exitCode   = $exitCode
        durationMs = $stopwatch.ElapsedMilliseconds
        trxPath    = $trxPath
        total      = 0
        passed     = 0
        failed     = 0
        skipped    = 0
    }
    $trxResults = Read-TrxResults -TrxPath $trxPath
    $summary.total = $trxResults.Count
    $summary.passed = @($trxResults | Where-Object { $_.Outcome -eq 'Passed' }).Count
    $summary.failed = @($trxResults | Where-Object { $_.Outcome -eq 'Failed' }).Count
    $summary.skipped = @($trxResults | Where-Object { $_.Outcome -notin @('Passed', 'Failed') }).Count
    Write-Info ('{0}: exit={1} total={2} passed={3} failed={4} other={5} durationMs={6}' -f `
            $ProjectLabel, $exitCode, $summary.total, $summary.passed, $summary.failed, $summary.skipped, $summary.durationMs)
    if ($exitCode -ne 0) {
        $tail = ($outputText -split "`n" | Select-Object -Last 25) -join "`n"
        Write-Info ('{0} reported a nonzero exit code. Output tail:{1}{2}' -f $ProjectLabel, [Environment]::NewLine, $tail)
    }
    return [pscustomobject]@{ Summary = $summary; Results = $trxResults; OutputTail = $tail }
}

# ---------------------------------------------------------------------------
# 1. Load manifest and resolve scope
# ---------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $ManifestPath)) {
    Write-Info "FATAL: manifest not found at $ManifestPath"
    exit 3
}
$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$allScenarios = @($manifest.scenarios)
Write-Info ('Manifest {0}: version {1}, {2} scenarios.' -f $ManifestPath, $manifest.manifestVersion, $allScenarios.Count)

$scopeParts = @()
if ($All) { $scopeParts += 'all' }
if ($PSBoundParameters.ContainsKey('Phase')) { $scopeParts += ('phase={0}' -f $Phase) }
if ($Module) { $scopeParts += ('module={0}' -f $Module) }
if ($Scenario) { $scopeParts += ('scenario={0}' -f $Scenario) }
if ($scopeParts.Count -eq 0) { $scopeParts = @('all') ; $All = $true }
$runMode = ($scopeParts -join ',')

$inScope = @($allScenarios | Where-Object {
        $keep = $true
        if ($All) { return $true }
        if ($PSBoundParameters.ContainsKey('Phase') -and $_.phase -ne $Phase) { $keep = $false }
        if ($keep -and $Module -and $_.module -ne $Module) { $keep = $false }
        if ($keep -and $Scenario -and $_.id -ne $Scenario) { $keep = $false }
        return $keep
    })
if (-not $All) {
    # Explicit filters combine with AND; an empty result means the caller asked for
    # something the manifest cannot satisfy.
    if ($inScope.Count -eq 0) {
        Write-Info 'FATAL: no scenario matches the requested scope in the manifest.'
        exit 3
    }
}
Write-Info ('Run mode: {0}; {1} of {2} scenarios in scope.' -f $runMode, $inScope.Count, $allScenarios.Count)

# ---------------------------------------------------------------------------
# 2. Tool checks (missing dotnet/node -> BLOCKED, exit 3)
# ---------------------------------------------------------------------------
$dotnetVersion = Get-ToolVersion -Command 'dotnet' -VersionArgument '--version'
$nodeVersion = Get-ToolVersion -Command 'node' -VersionArgument '--version'
Write-Info ('Tools: dotnet={0} node={1}' -f $(if ($dotnetVersion) { $dotnetVersion } else { 'MISSING' }), $(if ($nodeVersion) { $nodeVersion } else { 'MISSING' }))

$needDotnet = @($inScope | Where-Object { $_.mapped -and @($_.tests).Count -gt 0 }).Count -gt 0
$needNode = $All
if ($needDotnet -and -not $dotnetVersion) {
    Write-Info 'FATAL: dotnet is not available; mapped scenarios cannot run and will be reported BLOCKED.'
}
if ($needNode -and -not $nodeVersion) {
    Write-Info 'node is not available; the frontend coarse checkpoint will be reported BLOCKED.'
}

# ---------------------------------------------------------------------------
# 3. Database safety gate (before ANY write-capable test process)
# ---------------------------------------------------------------------------
$anyTestExecutionPlanned = ($needDotnet -and $dotnetVersion)
$safetyGate = [ordered]@{
    enforced               = $false
    passed                 = $null
    databasePath           = $null
    canonicalPath          = $null
    differsFromDefault     = $null
    outsideAppDataAlKhayal = $null
    differsFromSeeded      = $null
    previousEnvValue       = $env:ALKHAYAL_DATABASE_PATH
    violations             = @()
}
if ($anyTestExecutionPlanned) {
    $gateResult = Invoke-DatabaseSafetyGate
    $safetyGate = $gateResult
    if (-not $gateResult.passed) {
        # Fail closed: emit JSON with every planned scenario BLOCKED and exit 2.
        $results = New-Object System.Collections.ArrayList
        foreach ($entry in $allScenarios) {
            $record = New-ResultObject -Entry $entry
            if ($inScope.id -contains $entry.id) {
                $record.status = 'BLOCKED'
                $record.reason = 'Database safety gate failed closed; no test process was started.'
            } else {
                $record.status = 'SKIP'
                $record.reason = 'Outside the requested run scope.'
            }
            [void]$results.Add($record)
        }
        $payload = [ordered]@{
            runner        = 'scripts/run-acceptance-tests.ps1'
            runMode       = $runMode
            startedUtc    = (Get-Date).ToUniversalTime().ToString('o')
            finishedUtc   = (Get-Date).ToUniversalTime().ToString('o')
            tools         = [ordered]@{ dotnet = $dotnetVersion; node = $nodeVersion }
            safetyGate    = $safetyGate
            build         = [ordered]@{ attempted = $false; succeeded = $null; scope = $null }
            checkpoints   = [ordered]@{ }
            exitCode      = 2
            totals        = [ordered]@{ pass = 0; partial = 0; fail = 0; skip = @($results | Where-Object { $_.status -eq 'SKIP' }).Count; blocked = @($results | Where-Object { $_.status -eq 'BLOCKED' }).Count }
            scenarios     = $results
        }
        $json = $payload | ConvertTo-Json -Depth 6
        if ($OutputJson) { [IO.File]::WriteAllText($OutputJson, $json, (New-Object System.Text.UTF8Encoding($false))) }
        Write-Output $json
        exit 2
    }
}

# ---------------------------------------------------------------------------
# 4. Build (test projects only; the running desktop UI process locks solution
#    build output, so a full AlKhayal.slnx build is deliberately not attempted)
# ---------------------------------------------------------------------------
$buildInfo = [ordered]@{
    attempted = $false
    succeeded = $null
    scope     = 'test projects only (tests/AlKhayal.UnitTests, tests/AlKhayal.IntegrationTests); full solution build skipped because the running desktop UI process locks solution build output'
    output    = $null
}
$buildOk = $true
if ($anyTestExecutionPlanned -and -not $NoBuild) {
    $buildInfo.attempted = $true
    $buildOutput = New-Object System.Collections.ArrayList
    foreach ($project in @($UnitProject, $IntegrationProject)) {
        Write-Info ('Building {0}' -f (Split-Path -Leaf $project))
        $output = & dotnet build $project --no-restore 2>&1
        $buildExit = $LASTEXITCODE
        foreach ($line in $output) { [void]$buildOutput.Add($line.ToString()) }
        if ($buildExit -ne 0) {
            $buildOk = $false
            Write-Info ('Build FAILED for {0} (exit {1}).' -f $project, $buildExit)
            break
        }
    }
    $buildInfo.succeeded = $buildOk
    $buildInfo.output = (($buildOutput | Select-Object -Last 15) -join "`n")
} elseif ($NoBuild) {
    $buildInfo.scope = 'skipped by -NoBuild'
}

# ---------------------------------------------------------------------------
# 5. Execute tests
# ---------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $ResultsDir)) { New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null }

$unitRun = $null
$integrationRun = $null
$frontendCheckpoint = [ordered]@{ ran = $false; exitCode = $null; passed = $null; durationMs = $null; outputTail = $null; blockedReason = $null }

if ($All) {
    # Coarse checkpoints: complete unfiltered suites for both test projects + frontend.
    if ($dotnetVersion -and $buildOk) {
        $unitRun = Invoke-DotnetTest -ProjectPath $UnitProject -Filter $null -TrxFileName 'unit-full.trx' -NoBuild:(-not $buildInfo.attempted) -ProjectLabel 'unit-suite'
        $integrationRun = Invoke-DotnetTest -ProjectPath $IntegrationProject -Filter $null -TrxFileName 'integration-full.trx' -NoBuild:(-not $buildInfo.attempted) -ProjectLabel 'integration-suite'
    }
    if ($nodeVersion) {
        Write-Info 'Running frontend registry suite (coarse checkpoint): node frontend/tests/run-tests.js'
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $frontendOutput = & node $FrontendTestScript 2>&1
        $frontendExit = $LASTEXITCODE
        $sw.Stop()
        $frontendText = ($frontendOutput | ForEach-Object { $_.ToString() }) -join "`n"
        $frontendCheckpoint.ran = $true
        $frontendCheckpoint.exitCode = $frontendExit
        $frontendCheckpoint.passed = ($frontendExit -eq 0)
        $frontendCheckpoint.durationMs = $sw.ElapsedMilliseconds
        $frontendCheckpoint.outputTail = (($frontendText -split "`n" | Select-Object -Last 20) -join "`n")
        Write-Info ('frontend-suite: exit={0} durationMs={1}' -f $frontendExit, $sw.ElapsedMilliseconds)
    } else {
        $frontendCheckpoint.blockedReason = 'node is not available.'
        Write-Info 'frontend-suite: BLOCKED (node missing).'
    }
} else {
    # Filtered runs: execute ONLY the union of mapped tests for in-scope scenarios.
    $fqnsByProject = @{}
    foreach ($entry in $inScope) {
        if (-not $entry.mapped) { continue }
        foreach ($test in @($entry.tests)) {
            if (-not $fqnsByProject.ContainsKey($test.project)) { $fqnsByProject[$test.project] = New-Object System.Collections.ArrayList }
            [void]$fqnsByProject[$test.project].Add($test.fullyQualifiedName)
        }
    }
    if ($dotnetVersion -and $buildOk) {
        $unitFqns = @($fqnsByProject['AlKhayal.UnitTests'] | Select-Object -Unique)
        $integrationFqns = @($fqnsByProject['AlKhayal.IntegrationTests'] | Select-Object -Unique)
        if ($unitFqns.Count -gt 0) {
            $filter = ($unitFqns | ForEach-Object { 'FullyQualifiedName={0}' -f $_ }) -join ' | '
            $unitRun = Invoke-DotnetTest -ProjectPath $UnitProject -Filter $filter -TrxFileName 'unit-filtered.trx' -NoBuild:(-not $buildInfo.attempted) -ProjectLabel 'unit-filtered'
        }
        if ($integrationFqns.Count -gt 0) {
            $filter = ($integrationFqns | ForEach-Object { 'FullyQualifiedName={0}' -f $_ }) -join ' | '
            $integrationRun = Invoke-DotnetTest -ProjectPath $IntegrationProject -Filter $filter -TrxFileName 'integration-filtered.trx' -NoBuild:(-not $buildInfo.attempted) -ProjectLabel 'integration-filtered'
        }
    }
}

# ---------------------------------------------------------------------------
# 6. Attribute per-scenario statuses (never from aggregate results)
# ---------------------------------------------------------------------------
$unitResults = $(if ($unitRun) { $unitRun.Results } else { New-Object System.Collections.ArrayList })
$integrationResults = $(if ($integrationRun) { $integrationRun.Results } else { New-Object System.Collections.ArrayList })

function Get-OutcomeForTest {
    param([string]$Project, [string]$FullyQualifiedName)
    $pool = $(if ($Project -eq 'AlKhayal.UnitTests') { $unitResults } else { $integrationResults })
    return Get-TestOutcomeForFqn -FullyQualifiedName $FullyQualifiedName -TrxResults $pool
}

$results = New-Object System.Collections.ArrayList
foreach ($entry in $allScenarios) {
    $record = New-ResultObject -Entry $entry
    $inScopeThisRun = ($inScope.id -contains $entry.id)

    if (-not $inScopeThisRun) {
        $record.status = 'SKIP'
        $record.reason = 'Outside the requested run scope.'
        [void]$results.Add($record)
        continue
    }

    if (-not $entry.mapped -and @($entry.tests).Count -eq 0) {
        if ($entry.placeholder) {
            $record.status = 'BLOCKED'
            $record.reason = 'Unmapped placeholder: no canonical scenario definition or test mapping exists yet; it must never be reported PASS.'
        } else {
            $record.status = 'BLOCKED'
            $record.reason = 'No automated test mapping (manual/native or unimplemented scope); automated coverage must be added before this can pass.'
        }
        [void]$results.Add($record)
        continue
    }

    if (-not $dotnetVersion) {
        $record.status = 'BLOCKED'
        $record.reason = 'dotnet is not available on this machine.'
        [void]$results.Add($record)
        continue
    }
    if (-not $buildOk) {
        $record.status = 'BLOCKED'
        $record.reason = 'Test project build failed; no tests could run.'
        [void]$results.Add($record)
        continue
    }

    $testRecords = New-Object System.Collections.ArrayList
    $scenarioFailed = $false
    $failureParts = New-Object System.Collections.ArrayList
    $scenarioDuration = 0
    foreach ($test in @($entry.tests)) {
        $outcome = Get-OutcomeForTest -Project $test.project -FullyQualifiedName $test.fullyQualifiedName
        if (-not $outcome) {
            $scenarioFailed = $true
            [void]$testRecords.Add([ordered]@{
                    fullyQualifiedName = $test.fullyQualifiedName
                    project            = $test.project
                    outcome            = 'Missing'
                    cases              = 0
                    durationMs         = 0
                    message            = 'Mapped test did not appear in the run results (selector mismatch or discovery failure).'
                })
            [void]$failureParts.Add(('{0}: mapped test absent from run results' -f $test.fullyQualifiedName))
            continue
        }
        [void]$testRecords.Add([ordered]@{
                fullyQualifiedName = $test.fullyQualifiedName
                project            = $test.project
                outcome            = $outcome.Outcome
                cases              = $outcome.Cases
                durationMs         = $outcome.DurationMs
                message            = $outcome.Message
            })
        $scenarioDuration += $outcome.DurationMs
        if (-not $outcome.Passed) {
            $scenarioFailed = $true
            $messageText = $(if ($outcome.Message) { $outcome.Message } else { 'no failure message captured' })
            [void]$failureParts.Add(('{0}: {1}' -f $test.fullyQualifiedName, $messageText))
        }
    }
    $record.tests = @($testRecords)
    $record.durationMs = $scenarioDuration

    if ($scenarioFailed) {
        $record.status = 'FAIL'
        $record.failureSummary = ($failureParts -join ' | ')
        $record.reason = 'One or more mapped tests failed or were absent from the run results.'
    } elseif ($entry.partial -and $entry.manualRemainder) {
        # A passing automated portion with an unexecuted manual remainder is PARTIAL, never PASS.
        $record.status = 'PARTIAL'
        $record.reason = ('All mapped tests for this scenario ran and passed in this run; manual remainder remains unexecuted: {0}' -f $entry.manualRemainder)
    } elseif ($entry.partial) {
        # partial=true but no manualRemainder text: still PARTIAL so coverage is never overstated.
        $record.status = 'PARTIAL'
        $record.reason = 'All mapped tests for this scenario ran and passed in this run; the scenario is marked partial in the manifest and its non-automated remainder has not been executed.'
    } else {
        $record.status = 'PASS'
        $record.reason = 'All mapped tests for this scenario ran and passed in this run; the manifest records no manual remainder.'
    }
    [void]$results.Add($record)
}

$totals = [ordered]@{
    pass    = @($results | Where-Object { $_.status -eq 'PASS' }).Count
    partial = @($results | Where-Object { $_.status -eq 'PARTIAL' }).Count
    fail    = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
    skip    = @($results | Where-Object { $_.status -eq 'SKIP' }).Count
    blocked = @($results | Where-Object { $_.status -eq 'BLOCKED' }).Count
}

$exitCode = 0
if ($totals.fail -gt 0) { $exitCode = 1 }
elseif ($safetyGate.enforced -and ($safetyGate.passed -eq $false)) { $exitCode = 2 }
elseif (-not $dotnetVersion -and $needDotnet) { $exitCode = 3 }
elseif ($buildInfo.attempted -and ($buildInfo.succeeded -eq $false)) { $exitCode = 3 }
elseif ($All -and $frontendCheckpoint.ran -and ($frontendCheckpoint.passed -ne $true)) { $exitCode = 1 }

$checkpoints = [ordered]@{ }
if ($unitRun) {
    $checkpoints.unitTests = $unitRun.Summary
} else {
    $checkpoints.unitTests = [ordered]@{ ran = $false; blockedReason = $(if (-not $dotnetVersion) { 'dotnet missing' } elseif (-not $buildOk) { 'build failed' } else { 'not in run scope' }) }
}
if ($integrationRun) {
    $checkpoints.integrationTests = $integrationRun.Summary
} else {
    $checkpoints.integrationTests = [ordered]@{ ran = $false; blockedReason = $(if (-not $dotnetVersion) { 'dotnet missing' } elseif (-not $buildOk) { 'build failed' } else { 'not in run scope' }) }
}
$checkpoints.frontendSuite = $frontendCheckpoint

$payload = [ordered]@{
    runner        = 'scripts/run-acceptance-tests.ps1'
    manifestPath  = $ManifestPath
    manifestVersion = $manifest.manifestVersion
    runMode       = $runMode
    startedUtc    = $null
    finishedUtc   = (Get-Date).ToUniversalTime().ToString('o')
    tools         = [ordered]@{ dotnet = $dotnetVersion; node = $nodeVersion }
    safetyGate    = $safetyGate
    build         = $buildInfo
    checkpoints   = $checkpoints
    exitCode      = $exitCode
    totals        = $totals
    scenarios     = $results
    trxDirectory  = $ResultsDir
}
$payload.startedUtc = $startedUtc
$json = $payload | ConvertTo-Json -Depth 6
if ($OutputJson) { [IO.File]::WriteAllText($OutputJson, $json, (New-Object System.Text.UTF8Encoding($false))) }

Write-Info ('Totals: PASS={0} PARTIAL={1} FAIL={2} SKIP={3} BLOCKED={4}; exit code {5}.' -f $totals.pass, $totals.partial, $totals.fail, $totals.skip, $totals.blocked, $exitCode)
foreach ($record in $results) {
    if ($record.status -in @('FAIL', 'BLOCKED')) {
        Write-Info ('{0} {1}{2}' -f $record.id, $record.status, $(if ($record.failureSummary) { ' - ' + $record.failureSummary } elseif ($record.reason) { ' - ' + $record.reason } else { '' }))
    }
}
Write-Output $json
exit $exitCode
