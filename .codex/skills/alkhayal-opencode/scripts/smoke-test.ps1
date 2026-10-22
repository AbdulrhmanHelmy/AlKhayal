param(
    [Parameter(Mandatory = $true)][ValidatePattern('^opencode-go/[a-z0-9.-]+$')][string]$Model,
    [string]$Task = 'skill validation',
    [int]$Attempt = 1
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$log = Join-Path $root '.codex\opencode-log.md'
$prompt = 'Read-only availability check. Reply with exactly OK. Do not use tools or modify any files.'
Push-Location $root
try {
    $output = @(& opencode run --standalone --model $Model -- $prompt 2>&1)
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Output $_ }
    $lastLine = @($output | ForEach-Object { [string]$_ } | Where-Object { $_.Trim() })[-1].Trim()
    if ($exitCode -eq 0 -and $lastLine -eq 'OK') {
        $result = 'success'
    } else {
        $result = 'fail'
        if ($exitCode -eq 0) { $exitCode = 1 }
    }
} finally {
    Pop-Location
}
$date = (Get-Date).ToString('yyyy-MM-dd')
Add-Content -LiteralPath $log -Value "$date | $Task | smoke-test | $Model | $result | $Attempt"
exit $exitCode
