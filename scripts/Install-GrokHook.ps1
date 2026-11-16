param(
    [string]$GrokHome = $(if ($env:GROK_HOME) { $env:GROK_HOME } else { Join-Path $env:USERPROFILE '.grok' })
)

$ErrorActionPreference = 'Stop'
$package = Get-AppxPackage -Name 'Comb1e.AgentNotifications' | Select-Object -First 1
if (-not $package -or [version]$package.Version -lt [version]'0.2.0.0') {
    throw 'Install Agent Notifications MSIX version 0.2.0.0 or later first.'
}
$integrationDirectory = Join-Path $env:LOCALAPPDATA 'AgentNotifications\integrations\grok'
New-Item -ItemType Directory -Path $integrationDirectory -Force | Out-Null
$hookPath = Join-Path $integrationDirectory 'GrokCompletionHook.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GrokCompletionHook.ps1') -Destination $hookPath -Force
$hooksDirectory = Join-Path $GrokHome 'hooks'
New-Item -ItemType Directory -Path $hooksDirectory -Force | Out-Null
$configPath = Join-Path $hooksDirectory 'agent-notifications.json'
$powershellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$configuration = @{
    hooks = @{
        Stop = @(@{
            hooks = @(@{
                type = 'command'
                command = $powershellPath
                args = @('-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden', '-File', $hookPath)
                timeout = 10
            })
        })
    }
}
$encoding = New-Object System.Text.UTF8Encoding($false)
$temporaryPath = $configPath + '.tmp'
try {
    [IO.File]::WriteAllText($temporaryPath, ($configuration | ConvertTo-Json -Depth 8) + "`n", $encoding)
    if (Test-Path -LiteralPath $configPath) { [IO.File]::Replace($temporaryPath, $configPath, [NullString]::Value) }
    else { [IO.File]::Move($temporaryPath, $configPath) }
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath }
}
Write-Output 'Grok completion notifications enabled. Run /hooks and Reload, or restart existing Grok CLI sessions.'
