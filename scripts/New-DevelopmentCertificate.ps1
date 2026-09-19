param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\development-certificate'),
    [string]$Password = ''
)

$ErrorActionPreference = 'Stop'
$subject = 'CN=Agent Notifications Development'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $subject `
        -FriendlyName 'Agent Notifications Development' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -TextExtension @('2.5.29.19={text}', '2.5.29.37={text}1.3.6.1.5.5.7.3.3') `
        -CertStoreLocation Cert:\CurrentUser\My
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    $passwordBytes = New-Object byte[] 24
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($passwordBytes) }
    finally { $generator.Dispose() }
    $Password = [Convert]::ToBase64String($passwordBytes)
    [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
}

$passwordValue = ConvertTo-SecureString -String $Password -AsPlainText -Force
$pfxPath = Join-Path $resolvedOutput 'AgentNotifications.Development.pfx'
$cerPath = Join-Path $resolvedOutput 'AgentNotifications.Development.cer'
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $passwordValue -Force | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath -Force | Out-Null

[pscustomobject]@{
    PfxPath = $pfxPath
    CertificatePath = $cerPath
    Password = $Password
    Thumbprint = $certificate.Thumbprint
    Expires = $certificate.NotAfter
}
