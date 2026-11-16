param(
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$certificate = & (Join-Path $PSScriptRoot 'New-DevelopmentCertificate.ps1')
& (Join-Path $PSScriptRoot 'Build-Msix.ps1') `
    -Configuration $Configuration `
    -CertificateThumbprint $certificate.Thumbprint

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$package = Get-ChildItem (Join-Path $root 'artifacts\msix') -Recurse -Filter '*.msix' |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if (-not $package) {
    throw 'The MSIX build completed without producing a package.'
}

$signature = Get-AuthenticodeSignature -FilePath $package.FullName
if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or
    $signature.Status -notin @('Valid', 'UnknownError')) {
    throw "MSIX signature validation failed: $($signature.Status) - $($signature.StatusMessage)"
}

[pscustomobject]@{
    PackagePath = $package.FullName
    CertificatePath = $certificate.CertificatePath
    SignatureStatus = $signature.Status
    SignatureMessage = $signature.StatusMessage
    CertificateThumbprint = $certificate.Thumbprint
}
