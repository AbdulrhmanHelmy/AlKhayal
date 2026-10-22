$ErrorActionPreference = 'Stop'
$models = @(opencode models)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$models | Where-Object { $_ -like 'opencode-go/*' }
