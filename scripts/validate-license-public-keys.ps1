# Build guard: only an RSA-3072 public key may enter customer resources.
param([Parameter(Mandatory=$true)][string]$KeyDirectory)
$ErrorActionPreference='Stop'
if (-not (Test-Path -LiteralPath $KeyDirectory)) { exit 0 }
foreach($file in Get-ChildItem -LiteralPath $KeyDirectory -Filter '*.xml' -File) {
    if($file.Length -gt 8192 -or $file.BaseName -notmatch '^[a-zA-Z0-9_-]{1,64}$') { throw 'Invalid trusted public-key resource.' }
    $settings=New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing=[System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver=$null
    $reader=[System.Xml.XmlReader]::Create($file.FullName,$settings)
    try { $doc=New-Object System.Xml.XmlDocument; $doc.XmlResolver=$null; $doc.Load($reader) } finally { $reader.Dispose() }
    $names=@($doc.DocumentElement.ChildNodes | ForEach-Object { $_.Name })
    if($doc.DocumentElement.Name -ne 'RSAKeyValue' -or $names.Count -ne 2 -or $names[0] -ne 'Modulus' -or $names[1] -ne 'Exponent') { throw 'Only public RSA modulus/exponent resources are permitted. Private material is forbidden.' }
    $csp=New-Object System.Security.Cryptography.CspParameters(24)
    $csp.Flags=[System.Security.Cryptography.CspProviderFlags]::CreateEphemeralKey
    $rsa=New-Object System.Security.Cryptography.RSACryptoServiceProvider($csp)
    try {
        $rsa.PersistKeyInCsp=$false
        $rsa.FromXmlString($doc.OuterXml)
        if(-not $rsa.PublicOnly -or $rsa.KeySize -ne 3072) { throw 'Trusted key must be public RSA-3072.' }
    } finally { $rsa.Dispose() }
}
