param(
    [string]$CertificatePath = (Join-Path $PSScriptRoot '..\artifacts\development-certificate\AgentNotifications.Development.cer')
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an Administrator PowerShell window.'
}

$resolvedCertificate = (Resolve-Path $CertificatePath).Path
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($resolvedCertificate)
if ($certificate.Subject -ne 'CN=Agent Notifications Development') {
    throw "Unexpected certificate subject: $($certificate.Subject)"
}
$existing = Get-ChildItem Cert:\LocalMachine\TrustedPeople |
    Where-Object Thumbprint -eq $certificate.Thumbprint |
    Select-Object -First 1

if (-not $existing) {
    Import-Certificate -FilePath $resolvedCertificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
}

[pscustomobject]@{
    Subject = $certificate.Subject
    Thumbprint = $certificate.Thumbprint
    Store = 'LocalMachine\TrustedPeople'
}
