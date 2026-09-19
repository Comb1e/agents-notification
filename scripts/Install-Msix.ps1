param(
    [Parameter(Mandatory)] [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$resolvedPackage = (Resolve-Path $PackagePath).Path
$signature = Get-AuthenticodeSignature -FilePath $resolvedPackage
if ($signature.Status -eq 'NotSigned' -or -not $signature.SignerCertificate) {
    throw 'The package is unsigned. Build it with scripts\Build-DevelopmentMsix.ps1.'
}

$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople |
    Where-Object Thumbprint -eq $signature.SignerCertificate.Thumbprint |
    Select-Object -First 1
if (-not $trusted) {
    throw 'The signing certificate is not trusted. Run scripts\Trust-DevelopmentCertificate.ps1 as Administrator.'
}

Add-AppxPackage -Path $resolvedPackage -ForceApplicationShutdown
