param([Parameter(Mandatory=$true)][string]$DataDirectory)
$ErrorActionPreference='Stop'
$resolved=[IO.Path]::GetFullPath($DataDirectory)
New-Item -ItemType Directory -Path $resolved -Force | Out-Null
$acl=New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
$sid=[System.Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
[IO.Directory]::SetAccessControl($resolved,$acl)
# Existing files keep only this owner. No paths are moved/deleted.
Get-ChildItem -LiteralPath $resolved -File -Recurse | ForEach-Object {
    $fileAcl=New-Object System.Security.AccessControl.FileSecurity
    $fileAcl.SetAccessRuleProtection($true,$false)
    $fileAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))
    [IO.File]::SetAccessControl($_.FullName,$fileAcl)
}
