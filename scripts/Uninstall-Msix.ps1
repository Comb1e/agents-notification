$ErrorActionPreference = 'Stop'
$package = Get-AppxPackage -Name 'Comb1e.AgentNotifications' -ErrorAction SilentlyContinue
if ($package) { Remove-AppxPackage -Package $package.PackageFullName }
