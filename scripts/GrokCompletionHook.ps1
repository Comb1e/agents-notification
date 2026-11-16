$ErrorActionPreference = 'Stop'
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
if ($payload.hookEventName -ne 'stop' -or $payload.reason -ne 'end_turn' -or $payload.subagentType) { exit 0 }
if ([string]::IsNullOrWhiteSpace($payload.sessionId) -or [string]::IsNullOrWhiteSpace($payload.promptId)) {
    throw 'Grok did not provide session and prompt IDs.'
}
$env:GROK_EVENT = 'turn_complete'
$env:GROK_SESSION_ID = $payload.sessionId
$env:GROK_PROMPT_ID = $payload.promptId
$env:GROK_COMPLETED_AT = $payload.timestamp
$env:GROK_WORKSPACE_ROOT = if ($payload.workspaceRoot) { $payload.workspaceRoot } else { $payload.cwd }
$env:GROK_MESSAGE = 'Grok has finished its turn.'
$package = Get-AppxPackage -Name 'Comb1e.AgentNotifications' |
    Sort-Object Version -Descending | Select-Object -First 1
if (-not $package) { throw 'Install the Agent Notifications MSIX before enabling the Grok hook.' }
if ([version]$package.Version -lt [version]'0.2.0.0') { throw 'Grok notifications require Agent Notifications 0.2.0.0 or later.' }
$executable = Join-Path $package.InstallLocation 'AgentNotifications.App.exe'
# Keep the Grok hook short. The tray process persists and delivers the completion asynchronously.
Start-Process -FilePath $executable -ArgumentList '--grok-completed' -WindowStyle Hidden | Out-Null
