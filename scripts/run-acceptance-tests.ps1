#requires -version 5.1
# AlKhayal POS acceptance-test runner (QA Batch F; extends the Batch 2 runner contract).
#
# Runs mapped automated acceptance scenarios locally, without AI assistance.
# Compatible with Windows PowerShell 5.1. No dependencies beyond dotnet and node.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -All
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -Phase 1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -Phase 4 -Module purchasing
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -Module inventory
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -Scenario QA-001
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -Scenario QA-001,QA-061
#
# Manifest contract (docs/qa/scenario-manifest.json, data-only):
#   - A scenario with a non-empty "tests" array maps to exact xUnit
#     FullyQualifiedName selectors and is executed through dotnet test.
#   - A scenario with "group": "frontend" maps to the single coarse frontend
#     suite (node frontend/tests/run-tests.js). That pass proves registry /
#     renderer / static-source / VM assertions only and is NEVER reported as a
#     business-transaction or native-UI proof.
#   - A scenario with neither is reported BLOCKED (manual-only or
#     not-implemented by design) and is never counted as a pass.
#
# Data safety:
#   - The runner NEVER targets the normal operator database. It generates a
#     per-process disposable SQLite path under the system temp directory,
#     exports ALKHAYAL_DATABASE_PATH to every child test process, and refuses
#     to run unless the resolved full path differs from
#     %LOCALAPPDATA%\AlKhayal\alkhayal.db (and from the repository
#     seeded-database.db).
#   - It never deletes any pre-existing path (its own artifacts are left in
#     place as audit evidence).
#   - Performance testing against the normal seeded database is excluded by
#     design: no such suite is invoked and the seeded database is never
#     referenced.
#
# Status semantics (see the manifest statusSemantics block):
#   PASS    - every mapped test ran in this run and passed, no manual remainder.
#   PARTIAL - every mapped test ran and passed, but the scenario carries a
#             documented manual remainder (manualRemainder). Never a full PASS.
#   FAIL    - a mapped test failed, or a mapped test did not appear in the run
#             results (selector/discovery problem is loud, not silent).
#   SKIP    - mapped scenario outside the requested run scope (not executed).
#   BLOCKED - selected scenario with no automated mapping (manual-only /
#             not-implemented by design), or tooling could not run mapped tests
#             (dotnet/node missing, no TRX, timeout).
#
# Exit codes: 0 = no FAIL (PARTIAL / out-of-scope SKIP / by-design BLOCKED do
#             not fail the run); 1 = any FAIL; 2 = mapped tests could not run
#             (tooling/infrastructure BLOCKED); 3 = usage or isolation error.

param(
    [switch]$All,
    [int[]]$Phase,
    [string[]]$Module,
    [string[]]$Scenario,
    [string]$ResultsJson = "",
    [string]$OutputJson = "",
    [switch]$NoBuild,
    [int]$TimeoutMinutes = 45
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- helpers

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Same-Path([string]$Left, [string]$Right) {
    return ([string]$Left).TrimEnd('\') -ieq ([string]$Right).TrimEnd('\')
}

function StartsWith-Path([string]$Path, [string]$Root) {
    $p = ([string]$Path).TrimEnd('\')
    $r = ([string]$Root).TrimEnd('\')
    return $p.StartsWith($r + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-LoggedProcess {
    param(
        [string]$FileName,
        [string]$Arguments,
        [string]$WorkingDirectory,
        [hashtable]$Environment,
        [int]$TimeoutMilliseconds
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FileName
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    if ($Environment) {
        foreach ($key in $Environment.Keys) {
            $psi.EnvironmentVariables[[string]$key] = [string]$Environment[$key]
        }
    }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    $timedOut = $false
    $startError = $null
    try {
        [void]$proc.Start()
    } catch {
        $startError = $_.Exception.Message
    }
    if ($null -ne $startError) {
        return [pscustomobject]@{ Started = $false; TimedOut = $false; ExitCode = -1; DurationMs = $sw.ElapsedMilliseconds; Output = ''; ErrorText = $startError }
    }
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    if (-not $proc.WaitForExit($TimeoutMilliseconds)) {
        $timedOut = $true
        try { $proc.Kill() } catch { }
    }
    try { $proc.WaitForExit() } catch { }
    $sw.Stop()
    $out = ''
    $err = ''
    try { $out = $outTask.Result } catch { }
    try { $err = $errTask.Result } catch { }
    return [pscustomobject]@{
        Started    = $true
        TimedOut   = $timedOut
        ExitCode   = $proc.ExitCode
        DurationMs = $sw.ElapsedMilliseconds
        Output     = [string]$out
        ErrorText  = [string]$err
    }
}

function Parse-TrxFile([string]$Path) {
    $parsed = @()
    try {
        $raw = Get-Content -LiteralPath $Path -Raw
        [xml]$trx = $raw
    } catch {
        return $parsed
    }
    $definitions = @{}
    $units = @($trx.TestRun.TestDefinitions.UnitTest)
    foreach ($unit in $units) {
        if ($unit.TestMethod -and $unit.TestMethod.className -and $unit.TestMethod.name) {
            $definitions[[string]$unit.id] = ([string]$unit.TestMethod.className + '.' + [string]$unit.TestMethod.name)
        }
    }
    $unitResults = @($trx.TestRun.Results.UnitTestResult)
    foreach ($res in $unitResults) {
        $fqn = $null
        $key = [string]$res.testId
        if ($definitions.ContainsKey($key)) { $fqn = $definitions[$key] } else { $fqn = [string]$res.testName }
        $message = ''
        $stack = ''
        if ($res.Output -and $res.Output.ErrorInfo) {
            if ($res.Output.ErrorInfo.Message) { $message = [string]$res.Output.ErrorInfo.Message }
            if ($res.Output.ErrorInfo.StackTrace) { $stack = [string]$res.Output.ErrorInfo.StackTrace }
        }
        $durMs = [int64]0
        try {
            if ($res.duration) {
                $durMs = [int64][TimeSpan]::Parse([string]$res.duration, [System.Globalization.CultureInfo]::InvariantCulture).TotalMilliseconds
            }
        } catch { $durMs = [int64]0 }
        $parsed += [pscustomobject]@{
            Fqn        = [string]$fqn
            Outcome    = [string]$res.outcome
            DurationMs = $durMs
            Message    = $message
            Stack      = $stack
        }
    }
    return $parsed
}

function Clip-Text([string]$Text, [int]$Max) {
    $t = [string]$Text
    $t = $t -replace "`r", '' -replace "`n", ' | '
    $t = $t.Trim()
    if ($t.Length -gt $Max) { $t = $t.Substring(0, $Max) + '...' }
    if ([string]::IsNullOrWhiteSpace($t)) { $t = '(no detail)' }
    return $t
}

# ---------------------------------------------------------------- setup

$RepoRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..')
$ManifestPath = Join-Path $RepoRoot 'docs\qa\scenario-manifest.json'
$SolutionPath = Join-Path $RepoRoot 'AlKhayal.slnx'
$FrontendTestPath = Join-Path $RepoRoot 'frontend\tests\run-tests.js'

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    Write-Output "ERROR: scenario manifest not found: $ManifestPath"
    exit 3
}
$Manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$AllScenarios = @($Manifest.scenarios)

$Usage = @"
Usage:
  -All                     run every scenario (full dotnet suite + frontend coarse group)
  -Phase 1..5              run scenarios of one or more phases (e.g. -Phase 1 or -Phase 1,5)
  -Module <name>           run scenarios of one or more modules (e.g. -Module sales or -Module sales,inventory)
  -Scenario QA-NNN         run specific scenarios (e.g. -Scenario QA-001 or -Scenario QA-001,QA-061)
Options:
  -ResultsJson <path>      machine-readable result path (default: disposable run folder under system temp)
  -OutputJson <path>       alias of -ResultsJson (Batch 2 runner compatibility)
  -NoBuild                 pass --no-build to dotnet test (requires a prior build)
  -TimeoutMinutes <n>      per-process timeout (default 45)
Selectors may be combined (e.g. -Phase 4 -Module purchasing). -All ignores other selectors.
Available modules: $((@($AllScenarios | ForEach-Object { $_.module } | Sort-Object -Unique)) -join ', ')
"@

# ---------------------------------------------------------------- selection

$SelectionMode = 'All'
$Selection = $null
if ($All) {
    $Selection = $AllScenarios
    $SelectionMode = 'All'
} else {
    $selected = $AllScenarios
    $anySelector = $false
    if ($Scenario -and $Scenario.Count -gt 0) {
        $anySelector = $true
        $SelectionMode = 'Scenario'
        foreach ($id in $Scenario) {
            if ($id -notmatch '^(?i)QA-\d{3,}$') {
                Write-Output "ERROR: scenario id must look like QA-NNN: $id"
                exit 3
            }
        }
        $missingIds = @()
        foreach ($id in $Scenario) {
            if (-not (@($AllScenarios | Where-Object { ([string]$_.id) -ieq ([string]$id) }).Count -gt 0)) { $missingIds += $id }
        }
        if ($missingIds.Count -gt 0) {
            Write-Output "ERROR: unknown scenario id(s): $($missingIds -join ', ') (valid ids are QA-001..QA-$($AllScenarios.Count.ToString('000')) in docs/qa/scenario-manifest.json)"
            Write-Output $Usage
            exit 3
        }
        $wanted = @{}
        foreach ($id in $Scenario) { $wanted[([string]$id).ToUpperInvariant()] = $true }
        $selected = @($selected | Where-Object { $wanted.ContainsKey(([string]$_.id).ToUpperInvariant()) })
    }
    if ($Phase -and $Phase.Count -gt 0) {
        $anySelector = $true
        $SelectionMode = 'Phase'
        foreach ($p in $Phase) {
            if ($p -lt 1 -or $p -gt 5) {
                Write-Output "ERROR: -Phase accepts values 1..5 only (received $p)."
                exit 3
            }
        }
        $phaseSet = @{}
        foreach ($p in $Phase) { $phaseSet[[int]$p] = $true }
        $selected = @($selected | Where-Object { $phaseSet.ContainsKey([int]$_.phase) })
    }
    if ($Module -and $Module.Count -gt 0) {
        $anySelector = $true
        if ($SelectionMode -eq 'Phase') { $SelectionMode = 'Phase+Module' } else { $SelectionMode = 'Module' }
        $knownModules = @($AllScenarios | ForEach-Object { [string]$_.module } | Sort-Object -Unique)
        foreach ($m in $Module) {
            if ($knownModules -notcontains ([string]$m)) {
                Write-Output "ERROR: unknown module '$m'. Available: $($knownModules -join ', ')"
                exit 3
            }
        }
        $moduleSet = @{}
        foreach ($m in $Module) { $moduleSet[[string]$m] = $true }
        $selected = @($selected | Where-Object { $moduleSet.ContainsKey([string]$_.module) })
    }
    if (-not $anySelector) {
        Write-Output "ERROR: choose one of -All, -Phase, -Module, or -Scenario."
        Write-Output $Usage
        exit 3
    }
    $Selection = $selected
}

if ($Selection.Count -eq 0) {
    Write-Output "ERROR: the requested selection matched no scenarios."
    exit 3
}

# ---------------------------------------------------------------- database isolation

$TempRoot = [System.IO.Path]::GetTempPath()
$RunToken = 'AlKhayal-QA-{0}-{1}' -f $PID, [Guid]::NewGuid().ToString('N')
$RunRoot = Join-Path $TempRoot $RunToken
$OverrideDb = Join-Path $RunRoot 'alkhayal-qa.db'

$IsolationFailure = $null
$DefaultAppDataDir = $null
$DefaultOperatorDb = $null
$SeededDb = Join-Path $RepoRoot 'seeded-database.db'

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    $IsolationFailure = 'LOCALAPPDATA is not set; the normal operator database path cannot be verified.'
} else {
    $DefaultAppDataDir = Join-Path $env:LOCALAPPDATA 'AlKhayal'
    $DefaultOperatorDb = Join-Path $DefaultAppDataDir 'alkhayal.db'
}

if ($null -eq $IsolationFailure) {
    $resolvedOverride = Resolve-FullPath $OverrideDb
    $resolvedDefault = Resolve-FullPath $DefaultOperatorDb
    if (Same-Path $resolvedOverride $resolvedDefault) {
        $IsolationFailure = "The resolved override path equals the normal operator database: $resolvedOverride"
    } elseif (StartsWith-Path $resolvedOverride (Resolve-FullPath $DefaultAppDataDir)) {
        $IsolationFailure = "The resolved override path lies inside the normal operator database folder: $resolvedOverride"
    } elseif (-not (StartsWith-Path $resolvedOverride $TempRoot)) {
        $IsolationFailure = "The resolved override path is not under the system temp directory: $resolvedOverride"
    } elseif (Same-Path $resolvedOverride (Resolve-FullPath $SeededDb)) {
        $IsolationFailure = "The resolved override path equals the repository seeded database: $resolvedOverride"
    } elseif (Test-Path -LiteralPath $resolvedOverride) {
        $IsolationFailure = "The disposable database path already exists and will not be deleted or reused: $resolvedOverride"
    }
}

if ($null -ne $IsolationFailure) {
    Write-Output "ISOLATION CHECK FAILED - refusing to run."
    Write-Output "Reason: $IsolationFailure"
    if ($DefaultOperatorDb) { Write-Output "Normal operator database (must never be targeted): $DefaultOperatorDb" }
    exit 3
}

[void](New-Item -ItemType Directory -Path $RunRoot -Force)
$DotnetResultsDir = Join-Path $RunRoot 'dotnet-results'
[void](New-Item -ItemType Directory -Path $DotnetResultsDir -Force)

$ChosenResultsJson = $ResultsJson
if ([string]::IsNullOrWhiteSpace($ChosenResultsJson) -and -not [string]::IsNullOrWhiteSpace($OutputJson)) {
    $ChosenResultsJson = $OutputJson
}
if ([string]::IsNullOrWhiteSpace($ChosenResultsJson)) {
    $ResultsJsonPath = Join-Path $RunRoot 'qa-acceptance-results.json'
} else {
    if ([System.IO.Path]::IsPathRooted($ChosenResultsJson)) {
        $ResultsJsonPath = $ChosenResultsJson
    } else {
        $ResultsJsonPath = Join-Path (Get-Location) $ChosenResultsJson
    }
    $parent = Split-Path -Parent $ResultsJsonPath
    if (-not (Test-Path -LiteralPath $parent)) { [void](New-Item -ItemType Directory -Path $parent -Force) }
}

$ChildEnvironment = @{ 'ALKHAYAL_DATABASE_PATH' = (Resolve-FullPath $OverrideDb) }

Write-Output "=================================================================="
Write-Output "AlKhayal POS acceptance-test runner"
Write-Output "Mode            : $SelectionMode"
Write-Output "Started (UTC)   : $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss'))"
Write-Output "Repository      : $RepoRoot"
Write-Output "------------------------------------------------------------------"
Write-Output "ISOLATION VERIFIED"
Write-Output "  Effective ALKHAYAL_DATABASE_PATH : $(Resolve-FullPath $OverrideDb)"
if ($DefaultOperatorDb) {
Write-Output "  Normal operator database (unused): $DefaultOperatorDb (exists: $(Test-Path -LiteralPath $DefaultOperatorDb))"
}
Write-Output "  Repository seeded database (unused): $SeededDb (never referenced)"
Write-Output "  ALKHAYAL_DATABASE_PATH is exported to every dotnet/node child process."
Write-Output "  No pre-existing path is deleted; disposable artifacts stay in the run folder."
Write-Output "------------------------------------------------------------------"
Write-Output "Selected scenarios : $($Selection.Count) of $($AllScenarios.Count)"
Write-Output "Run folder         : $RunRoot"
Write-Output "Results JSON       : $ResultsJsonPath"
Write-Output "=================================================================="

# ---------------------------------------------------------------- tooling

$DotnetExe = $null
$NodeExe = $null
try { $DotnetExe = (Get-Command dotnet -ErrorAction Stop).Source } catch { }
try { $NodeExe = (Get-Command node -ErrorAction Stop).Source } catch { }

# ---------------------------------------------------------------- partition

$DotnetScenarios = @($Selection | Where-Object { $_.tests -and @($_.tests).Count -gt 0 })
$FrontendScenarios = @($Selection | Where-Object { -not ($_.tests -and @($_.tests).Count -gt 0) -and [string]$_.group -eq 'frontend' })
$UnmappedScenarios = @($Selection | Where-Object { -not ($_.tests -and @($_.tests).Count -gt 0) -and [string]$_.group -ne 'frontend' })

# ---------------------------------------------------------------- dotnet group

$Commands = @()
$TrxResults = @()
$DotnetInfraBlock = $null
$DotnetExitCode = $null
$DotnetDurationMs = [int64]0
$DotnetFilterCount = 0

if ($DotnetScenarios.Count -gt 0) {
    if ($null -eq $DotnetExe) {
        $DotnetInfraBlock = 'dotnet executable not found on PATH.'
    } else {
        $filters = New-Object System.Collections.Generic.List[string]
        foreach ($s in $DotnetScenarios) {
            foreach ($t in @($s.tests)) {
                $expr = 'FullyQualifiedName=' + [string]$t.fullyQualifiedName
                if (-not $filters.Contains($expr)) { $filters.Add($expr) }
            }
        }
        $DotnetFilterCount = $filters.Count
        $argLine = "test `"$SolutionPath`" --no-restore"
        if ($NoBuild) { $argLine += ' --no-build' }
        $runFullSuite = $All
        if (-not $runFullSuite) {
            $filterExpr = ($filters -join '|')
            if ($filters.Count -gt 1) { $filterExpr = '(' + $filterExpr + ')' }
            $argLine += " --filter `"$filterExpr`""
        }
        $argLine += " --logger trx --results-directory `"$DotnetResultsDir`""
        $purpose = 'dotnet test'
        if ($runFullSuite) { $purpose = 'dotnet test (full suite, -All)' } else { $purpose = "dotnet test (filtered: $DotnetFilterCount mapped selector(s))" }
        Write-Output ""
        Write-Output "[$purpose]"
        Write-Output "  $DotnetExe $argLine"
        $timeoutMs = [int]([Math]::Max(1, $TimeoutMinutes)) * 60000
        $run = Invoke-LoggedProcess -FileName $DotnetExe -Arguments $argLine -WorkingDirectory $RepoRoot -Environment $ChildEnvironment -TimeoutMilliseconds $timeoutMs
        $DotnetExitCode = $run.ExitCode
        $DotnetDurationMs = $run.DurationMs
        $Commands += [pscustomobject]@{
            purpose = $purpose; command = ($DotnetExe + ' ' + $argLine); exitCode = $run.ExitCode
            durationMs = $run.DurationMs; timedOut = $run.TimedOut
        }
        if (-not $run.Started) {
            $DotnetInfraBlock = "dotnet test failed to start: $($run.ErrorText)"
        } elseif ($run.TimedOut) {
            $DotnetInfraBlock = "dotnet test exceeded the $TimeoutMinutes-minute timeout."
        } else {
            $trxFiles = @(Get-ChildItem -LiteralPath $DotnetResultsDir -Filter '*.trx' -File)
            foreach ($trx in $trxFiles) {
                $TrxResults += @(Parse-TrxFile $trx.FullName)
            }
            if ($trxFiles.Count -eq 0) {
                $detail = Clip-Text (($run.Output + ' ' + $run.ErrorText), 400)
                $DotnetInfraBlock = "dotnet test produced no TRX results (exit code $($run.ExitCode)). $detail"
            } else {
                $tail = ''
                try { $tail = (@($run.Output -split "`r?`n") | Where-Object { $_ -match '^(Passed|Failed|Skipped)!' } | Select-Object -Last 3) -join '; ' } catch { }
                if (-not [string]::IsNullOrWhiteSpace($tail)) { Write-Output "  $tail" }
                Write-Output "  exit code: $($run.ExitCode); TRX files: $($trxFiles.Count); wall time: $([Math]::Round($run.DurationMs / 1000.0, 1)) s"
            }
        }
    }
}

# ---------------------------------------------------------------- frontend group

$FrontendPassed = $null
$FrontendDurationMs = [int64]0
$FrontendFailure = $null

if ($FrontendScenarios.Count -gt 0) {
    if ($null -eq $NodeExe) {
        $FrontendFailure = 'node executable not found on PATH.'
    } else {
        $frontendArgs = "`"$FrontendTestPath`""
        Write-Output ""
        Write-Output "[frontend suite (single coarse group: registry/renderer/static-source/VM assertions only)]"
        Write-Output "  $NodeExe $frontendArgs"
        $run = Invoke-LoggedProcess -FileName $NodeExe -Arguments $frontendArgs -WorkingDirectory $RepoRoot -Environment $ChildEnvironment -TimeoutMilliseconds ([int]([Math]::Max(1, $TimeoutMinutes)) * 60000)
        $FrontendDurationMs = $run.DurationMs
        $Commands += [pscustomobject]@{
            purpose = 'frontend suite (coarse group)'; command = ($NodeExe + ' ' + $frontendArgs); exitCode = $run.ExitCode
            durationMs = $run.DurationMs; timedOut = $run.TimedOut
        }
        if (-not $run.Started) {
            $FrontendFailure = "frontend suite failed to start: $($run.ErrorText)"
        } elseif ($run.TimedOut) {
            $FrontendFailure = "frontend suite exceeded the $TimeoutMinutes-minute timeout."
        } elseif ($run.ExitCode -eq 0) {
            $FrontendPassed = $true
            Write-Output "  exit code: 0 (suite passed); wall time: $([Math]::Round($run.DurationMs / 1000.0, 1)) s"
        } else {
            $FrontendPassed = $false
            $FrontendFailure = Clip-Text (($run.ErrorText + ' ' + $run.Output), 500)
            Write-Output "  exit code: $($run.ExitCode) (suite FAILED): $FrontendFailure"
        }
    }
}

# ---------------------------------------------------------------- scenario evaluation

function Get-EvidenceMatches([string]$Fqn) {
    $exact = @($TrxResults | Where-Object { $_.Fqn -ieq $Fqn })
    if ($exact.Count -gt 0) { return $exact }
    return @($TrxResults | Where-Object { $_.Fqn -and $_.Fqn.StartsWith($Fqn, [System.StringComparison]::OrdinalIgnoreCase) })
}

$SelectedIds = @{}
foreach ($s in $Selection) { $SelectedIds[[string]$s.id] = $true }

$Results = @()
foreach ($s in $AllScenarios) {
    $id = [string]$s.id
    $inScope = $SelectedIds.ContainsKey($id)
    $status = $null
    $durationMs = [int64]0
    $detail = ''
    $mapped = @()
    $scope = [string]$s.evidenceScope
    $isPartial = [bool]$s.partial
    $hasTests = ($s.tests -and @($s.tests).Count -gt 0)
    $isFrontend = ((-not $hasTests) -and [string]$s.group -eq 'frontend')

    if (-not $inScope) {
        $status = 'SKIP'
        $detail = 'mapped scenario outside the requested run scope (not executed in this run)'
    } elseif ($hasTests) {
        if ($null -ne $DotnetInfraBlock) {
            $status = 'BLOCKED'
            $detail = $DotnetInfraBlock
        } else {
            $totalMatched = 0
            $failed = @()
            $notRun = @()
            $notExecuted = @()
            foreach ($t in @($s.tests)) {
                $fqn = [string]$t.fullyQualifiedName
                $matches = @(Get-EvidenceMatches $fqn)
                if ($matches.Count -eq 0) {
                    $notRun += $fqn
                    continue
                }
                foreach ($m in $matches) {
                    $totalMatched++
                    $durationMs += [int64]$m.DurationMs
                    if ($m.Outcome -eq 'Passed') {
                        $mapped += [pscustomobject]@{ fqn = $fqn; outcome = $m.Outcome }
                    } elseif ($m.Outcome -eq 'Failed') {
                        $failed += [pscustomobject]@{ fqn = $fqn; outcome = $m.Outcome; message = (Clip-Text $m.Message 400) }
                    } else {
                        $notExecuted += [pscustomobject]@{ fqn = $fqn; outcome = $m.Outcome; message = (Clip-Text $m.Message 300) }
                    }
                }
            }
            if ($totalMatched -eq 0) {
                $status = 'FAIL'
                $detail = 'Mapped test(s) did not appear in the run results (selector or discovery problem): ' + (($notRun | Select-Object -Unique) -join '; ')
            } elseif ($failed.Count -gt 0) {
                $status = 'FAIL'
                $msgs = @($failed | ForEach-Object { "$($_.fqn): $($_.message)" }) | Select-Object -Unique
                $detail = ($msgs -join ' || ')
            } elseif ($notExecuted.Count -gt 0) {
                $status = 'BLOCKED'
                $detail = 'Mapped test(s) were not executed: ' + ((@($notExecuted | ForEach-Object { "$($_.fqn) [$($_.outcome)]" }) | Select-Object -Unique) -join '; ')
            } elseif ($isPartial) {
                $status = 'PARTIAL'
                $detail = "all mapped assertion(s) passed ($totalMatched case(s)); manual remainder: $(Clip-Text ([string]$s.manualRemainder) 220)"
            } else {
                $status = 'PASS'
                $detail = "direct assertion(s) passed ($totalMatched case(s))"
            }
        }
    } elseif ($isFrontend) {
        if ($null -eq $FrontendPassed) {
            $status = 'BLOCKED'
            $detail = $FrontendFailure
        } elseif ($FrontendPassed) {
            $durationMs = $FrontendDurationMs
            $mapped += [pscustomobject]@{ fqn = 'frontend/tests/run-tests.js (coarse group)'; outcome = 'Passed' }
            if ($isPartial) {
                $status = 'PARTIAL'
                $detail = 'coarse frontend-group pass (registry/renderer/static-source/VM assertions only, never a transaction proof); manual remainder: ' + (Clip-Text ([string]$s.manualRemainder) 220)
            } else {
                $status = 'PASS'
                $detail = 'coarse frontend-group pass (registry/renderer/static-source/VM assertions only, never a transaction proof); scope: ' + $scope
            }
        } else {
            $status = 'FAIL'
            $durationMs = $FrontendDurationMs
            $detail = 'coarse frontend-group failure: ' + $FrontendFailure
            $mapped += [pscustomobject]@{ fqn = 'frontend/tests/run-tests.js (coarse group)'; outcome = 'Failed' }
        }
    } else {
        $status = 'BLOCKED'
        $reason = [string]$s.skipReason
        if ([string]::IsNullOrWhiteSpace($reason)) { $reason = 'no-automated-mapping' }
        $detail = 'not executable in this harness: ' + $reason
        if ($s.manualRemainder) { $detail += ' -- manual remainder: ' + (Clip-Text ([string]$s.manualRemainder) 180) }
        if ($s.missing) { $detail += ' -- ' + (Clip-Text ([string]$s.missing) 220) }
    }

    $Results += [pscustomobject]@{
        id = $id
        phase = [int]$s.phase
        module = [string]$s.module
        title = [string]$s.title
        level = [string]$s.level
        inScope = $inScope
        evidenceScope = $scope
        partial = $isPartial
        status = $status
        durationMs = $durationMs
        mappedTests = @($mapped)
        failureDetail = $detail
    }
}

# ---------------------------------------------------------------- reporting

$InScopeResults = @($Results | Where-Object { $_.inScope })
$Passed = @($InScopeResults | Where-Object { $_.status -eq 'PASS' })
$Partial = @($InScopeResults | Where-Object { $_.status -eq 'PARTIAL' })
$Failed = @($InScopeResults | Where-Object { $_.status -eq 'FAIL' })
$Skipped = @($Results | Where-Object { $_.status -eq 'SKIP' })
$Blocked = @($InScopeResults | Where-Object { $_.status -eq 'BLOCKED' })
$BlockedInfra = @($Blocked | Where-Object { $_.failureDetail -match 'dotnet|node|TRX|timeout|not found on PATH' })
$BlockedByDesign = @($Blocked | Where-Object { $_.failureDetail -notmatch 'dotnet|node|TRX|timeout|not found on PATH' })

Write-Output ""
Write-Output "=================================================================="
Write-Output "SCENARIO RESULTS (in scope: $($InScopeResults.Count))"
Write-Output ("{0,-7} {1,-5} {2,-14} {3,-8} {4,10}  {5}" -f 'ID', 'Phase', 'Module', 'Status', 'DurationMs', 'EvidenceScope')
foreach ($r in $InScopeResults) {
    Write-Output ("{0,-7} {1,-5} {2,-14} {3,-8} {4,10}  {5}" -f $r.id, $r.phase, $r.module, $r.status, $r.durationMs, $r.evidenceScope)
}
if ($Skipped.Count -gt 0) {
    Write-Output ""
    Write-Output "Out-of-scope scenarios (SKIP, not executed in this run): $(@($Skipped | ForEach-Object { $_.id }) -join ', ')"
}

if ($Failed.Count -gt 0) {
    Write-Output ""
    Write-Output "------------------------------------------------------------------"
    Write-Output "FAILURE SUMMARIES"
    foreach ($r in $Failed) {
        Write-Output "  [$($r.id)] $($r.title)"
        Write-Output "    $($r.failureDetail)"
    }
}
if ($Blocked.Count -gt 0) {
    Write-Output ""
    Write-Output "------------------------------------------------------------------"
    Write-Output "BLOCKED SUMMARIES"
    foreach ($r in $Blocked) {
        Write-Output "  [$($r.id)] $($r.title)"
        Write-Output "    $($r.failureDetail)"
    }
}
if ($Partial.Count -gt 0) {
    Write-Output ""
    Write-Output "------------------------------------------------------------------"
    Write-Output "PARTIAL (mapped assertions passed; documented manual remainder stays open)"
    foreach ($r in $Partial) {
        Write-Output "  [$($r.id)] $($r.failureDetail)"
    }
}

Write-Output ""
Write-Output "=================================================================="
Write-Output "SUMMARY"
Write-Output "  In scope        : $($InScopeResults.Count)"
Write-Output "  PASS            : $($Passed.Count)"
Write-Output "  PARTIAL         : $($Partial.Count)"
Write-Output "  FAIL            : $($Failed.Count)"
Write-Output "  SKIP (oos)      : $($Skipped.Count)"
Write-Output "  BLOCKED (design): $($BlockedByDesign.Count) (manual-only / not-implemented; not exit-affecting)"
Write-Output "  BLOCKED (infra) : $($BlockedInfra.Count) (mapped tests could not run)"

$ExitCode = 0
if ($Failed.Count -gt 0) { $ExitCode = 1 }
elseif ($BlockedInfra.Count -gt 0) { $ExitCode = 2 }

if ($ExitCode -eq 0) {
    Write-Output "  Result          : no FAIL - all executable mapped scenarios passed (PARTIAL keeps manual remainders visible)."
} elseif ($ExitCode -eq 1) {
    Write-Output "  Result          : FAILED - see failure summaries."
} else {
    Write-Output "  Result          : BLOCKED - mapped tests could not run (tooling/infrastructure)."
}

# ---------------------------------------------------------------- machine-readable output

$JsonRoot = [pscustomobject]@{
    runner = 'scripts/run-acceptance-tests.ps1'
    manifest = 'docs/qa/scenario-manifest.json'
    runId = $RunToken
    completedUtc = (Get-Date).ToUniversalTime().ToString('o')
    mode = $SelectionMode
    selection = [pscustomobject]@{
        all = [bool]$All
        phases = @($Phase)
        modules = @($Module)
        scenarios = @($Scenario)
        matched = @($InScopeResults | ForEach-Object { $_.id })
    }
    isolation = [pscustomobject]@{
        verified = $true
        effectiveDatabaseOverride = (Resolve-FullPath $OverrideDb)
        normalOperatorDatabase = $DefaultOperatorDb
        normalDatabaseUsed = $false
        seededDatabasePath = $SeededDb
        seededDatabaseUsed = $false
        tempRoot = $TempRoot
        note = 'ALKHAYAL_DATABASE_PATH was exported to every child test process; tests additionally use their own temp GUID fixtures.'
    }
    commands = @($Commands)
    summary = [pscustomobject]@{
        inScope = $InScopeResults.Count
        pass = $Passed.Count
        partial = $Partial.Count
        fail = $Failed.Count
        skipOutOfScope = $Skipped.Count
        blockedByDesign = $BlockedByDesign.Count
        blockedInfra = $BlockedInfra.Count
        exitCode = $ExitCode
    }
    results = @($InScopeResults)
}

$jsonText = ConvertTo-Json -InputObject $JsonRoot -Depth 8
[System.IO.File]::WriteAllText($ResultsJsonPath, $jsonText, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ""
Write-Output "Machine-readable results: $ResultsJsonPath"
Write-Output "Disposable run artifacts: $RunRoot (kept as evidence; nothing was deleted)"
Write-Output "Exit code: $ExitCode"
exit $ExitCode
