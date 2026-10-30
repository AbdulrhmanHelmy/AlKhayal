# Owner-only desktop deployment. Copies an explicit allowlist; never touches POS data or signing keys.
param([string]$NodeExecutable='')
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath($PSScriptRoot)
$repo=[IO.Path]::GetFullPath((Join-Path $source '..\..'))
if(-not $NodeExecutable){$version=(Get-Content -LiteralPath (Join-Path $repo 'artifacts\license-manager-runtime\version.txt') -Raw).Trim();$NodeExecutable=Join-Path $repo "artifacts\license-manager-runtime\node-$version-win-x64\node.exe"}
$NodeExecutable=[IO.Path]::GetFullPath($NodeExecutable)
if(-not (Test-Path -LiteralPath $NodeExecutable -PathType Leaf)){throw 'Verified bundled Node executable is required.'}
$version=& $NodeExecutable --version
if($version -notmatch '^v24\.'){throw 'Use the supported bundled Node 24 LTS runtime.'}
& $NodeExecutable -e "const {DatabaseSync}=require('node:sqlite');const d=new DatabaseSync(':memory:');d.exec('CREATE TABLE probe(id INTEGER)');d.close();"
if($LASTEXITCODE){throw 'Bundled SQLite runtime probe failed.'}
$desktop=[Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
if(-not $desktop){throw 'Windows Desktop could not be resolved.'}
$target=[IO.Path]::GetFullPath((Join-Path $desktop 'AlKhayal License Manager'))
if(-not $target.StartsWith([IO.Path]::GetFullPath($desktop).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected deployment target.'}
New-Item -ItemType Directory -Force -Path $target,(Join-Path $target 'public'),(Join-Path $target 'runtime') | Out-Null
$files=@('protocol.cjs','registry.cjs','server.cjs','launch.cjs','secure-storage.ps1','open-browser.vbs','error.vbs','AlKhayal License Manager.vbs','package.json')
foreach($file in $files){Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $target $file) -Force}
foreach($file in @('index.html','style.css','app.js')){Copy-Item -LiteralPath (Join-Path $source ('public\'+$file)) -Destination (Join-Path $target ('public\'+$file)) -Force}
$runtimeTarget=Join-Path $target 'runtime\node.exe'
if(-not (Test-Path -LiteralPath $runtimeTarget) -or (Get-FileHash -LiteralPath $NodeExecutable -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $runtimeTarget -Algorithm SHA256).Hash){
    Copy-Item -LiteralPath $NodeExecutable -Destination $runtimeTarget -Force
}
$license=Join-Path ([IO.Path]::GetDirectoryName($NodeExecutable)) 'LICENSE'
if(Test-Path -LiteralPath $license){Copy-Item -LiteralPath $license -Destination (Join-Path $target 'runtime\LICENSE') -Force}
$launcher=Join-Path $desktop 'AlKhayal License Manager.lnk'
$shell=New-Object -ComObject WScript.Shell
$link=$shell.CreateShortcut($launcher)
$link.TargetPath=Join-Path $env:SystemRoot 'System32\wscript.exe'
$link.Arguments='"'+(Join-Path $target 'AlKhayal License Manager.vbs')+'"'
$link.WorkingDirectory=$target
$link.Description='AlKhayal owner-only offline license registry'
$link.Save()
Write-Output ('Desktop tool: '+$target)
Write-Output ('Desktop launcher: '+$launcher)
Write-Output ('Owner data: '+(Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AlKhayalLicenseManager'))
