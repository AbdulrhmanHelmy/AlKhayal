$ErrorActionPreference = 'Stop'
$root = 'D:\projects\AlKhayal\test-artifacts\phase-1-to-4-acceptance-2026-10-24\winforms-run-001'
$env:ALKHAYAL_DATABASE_PATH = Join-Path $root 'database\alkhayal-ui-acceptance.db'
$env:ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER = Join-Path $root 'webview2-profile'
$env:ALKHAYAL_FRONTEND_PATH = 'D:\projects\AlKhayal\frontend'
$env:ALKHAYAL_SERVER_URL = ''
$password = [guid]::NewGuid().ToString('N') + 'R!'
$username = 'acceptance-repro-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$hostPath = 'D:\projects\AlKhayal\src\AlKhayal.Server.Host\bin\Debug\net48\AlKhayal.Server.Host.exe'
$psi = [Diagnostics.ProcessStartInfo]::new($hostPath, "--provision-user $username")
$psi.WorkingDirectory = Split-Path $hostPath
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
$provision = [Diagnostics.Process]::Start($psi)
$provision.StandardInput.WriteLine($password); $provision.StandardInput.Close(); $provision.WaitForExit()
if ($provision.ExitCode -ne 0) { throw 'Could not provision synthetic restart-check account.' }
$uiPath = 'D:\projects\AlKhayal\src\AlKhayal.UI\bin\Debug\net48\AlKhayal.UI.exe'
$start = [Diagnostics.ProcessStartInfo]::new($uiPath)
$start.WorkingDirectory = Split-Path $uiPath; $start.UseShellExecute = $false
foreach ($name in @('ALKHAYAL_DATABASE_PATH','ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER','ALKHAYAL_FRONTEND_PATH')) { $start.EnvironmentVariables[$name] = [Environment]::GetEnvironmentVariable($name) }
$ui = [Diagnostics.Process]::Start($start)
$handler = [Net.Http.HttpClientHandler]::new(); $handler.UseCookies = $true; $handler.CookieContainer = [Net.CookieContainer]::new()
$client = [Net.Http.HttpClient]::new($handler); $client.BaseAddress = [uri]'http://127.0.0.1:5137/'
$healthy = $false
for ($i=0; $i -lt 20; $i++) { try { $response = $client.GetAsync('api/system/health').GetAwaiter().GetResult(); if ($response.IsSuccessStatusCode) { $healthy=$true; break } } catch {}; Start-Sleep -Milliseconds 500 }
if (-not $healthy) { throw 'Restarted MainForm API did not become healthy.' }
$body = @{username=$username;password=$password} | ConvertTo-Json -Compress
$content = [Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json')
$login = $client.PostAsync('api/auth/login',$content).GetAwaiter().GetResult()
if (-not $login.IsSuccessStatusCode) { throw 'Synthetic restart-check login failed.' }
$me = $client.GetAsync('api/auth/me').GetAwaiter().GetResult()
$supplierResponse = $client.GetAsync('api/suppliers').GetAwaiter().GetResult()
$bodyText = $supplierResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
$supplierBody = $null; try { $supplierBody = $bodyText | ConvertFrom-Json } catch {}
$diagnostics = [ordered]@{ processId=$ui.Id; health=$true; loginStatus=[int]$login.StatusCode; authMeStatus=[int]$me.StatusCode; suppliersStatus=[int]$supplierResponse.StatusCode; supplierResponse=$supplierBody; cookies=$handler.CookieContainer.GetCookies([uri]'http://127.0.0.1:5137/').Count; credentialsPersisted=$false }
$diagnostics | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $root 'restart-http-diagnostics.json') -Encoding UTF8
$client.Dispose(); $handler.Dispose(); $password=$null
$diagnostics | ConvertTo-Json -Depth 15
