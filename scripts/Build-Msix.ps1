param(
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release',
    [string]$CertificatePath = '',
    [string]$CertificatePassword = '',
    [string]$CertificateThumbprint = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\AgentNotifications.App\AgentNotifications.App.csproj'
$output = Join-Path $root 'artifacts\msix'

& (Join-Path $PSScriptRoot 'Generate-PackageAssets.ps1')

$arguments = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'false',
    '-p:GenerateAppxPackageOnBuild=true',
    '-p:AppxBundle=Never',
    "-p:AppxPackageDir=$output\"
)

if ($CertificateThumbprint) {
    $arguments += '-p:AppxPackageSigningEnabled=true'
    $arguments += "-p:PackageCertificateThumbprint=$CertificateThumbprint"
}
elseif ($CertificatePath) {
    $arguments += '-p:AppxPackageSigningEnabled=true'
    $arguments += "-p:PackageCertificateKeyFile=$CertificatePath"
    if ($CertificatePassword) { $arguments += "-p:PackageCertificatePassword=$CertificatePassword" }
}
else {
    $arguments += '-p:AppxPackageSigningEnabled=false'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "MSIX build failed with exit code $LASTEXITCODE" }
