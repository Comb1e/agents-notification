# Agent Notifications

Agent Notifications is a Windows tray application that mirrors new Windows toasts from ChatGPT, Qoder, and ZCode to one Feishu or Lark destination. Grok Build CLI completions produce both a Windows notification and a Feishu card. Existing desktop toasts are left unchanged and are never replayed at startup.

## Scope

- Windows 10 version 1809 or later, one signed-in user, x64.
- Packaged .NET 8 WPF application with `userNotificationListener` and `runFullTrust` capabilities.
- ChatGPT, Qoder, and ZCode desktop applications, plus Grok Build CLI turn-completion hooks.
- One Feishu or Lark destination configured by `email`, `open_id`, `user_id`, `union_id`, or `chat_id`.
- Full title and body forwarding after configured regex redaction.
- Durable SQLite delivery, one-second update coalescing, retries, and in-place Feishu card updates.

Historical replay, remote control, notification actions, notification images, Windows services, and inbound Feishu events are outside v0.2.0.

## Build And Test

Requirements:

- Windows 10/11 x64
- .NET 8 SDK and Desktop Runtime
- PowerShell 7 or Windows PowerShell 5.1

```powershell
dotnet build .\AgentNotifications.sln -c Debug
dotnet test .\AgentNotifications.sln -c Debug
```

Create the signed development MSIX:

```powershell
.\scripts\Build-DevelopmentMsix.ps1 -Configuration Release
```

The package and public certificate are written below `artifacts\`. The PFX password and certificate are for local development only and are excluded from Git.

## Install The MSIX

Windows requires a trusted signature for an MSIX that activates a full-trust desktop executable. For the local development certificate:

1. Open PowerShell as Administrator.
2. Trust the generated public certificate:

```powershell
.\scripts\Trust-DevelopmentCertificate.ps1
```

3. Return to a normal PowerShell window and install the newest package:

```powershell
$package = Get-ChildItem .\artifacts\msix -Recurse -Filter *.msix |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
.\scripts\Install-Msix.ps1 -PackagePath $package.FullName
```

Uninstall without deleting the source tree:

```powershell
.\scripts\Uninstall-Msix.ps1
```

For a production release, replace the development certificate and manifest publisher with the organization or Microsoft Store identity.

## Create The Feishu App

1. In the Feishu or Lark developer console, create an enterprise self-built app.
2. Enable the bot capability.
3. Grant only the bot message-send permission: `im:message:send_as_bot`.
4. Do not configure events, callbacks, long connections, or pairing.
5. Publish an app version and make it available to the intended recipient.
6. Copy the App ID and App Secret from the app credentials page.

The app obtains an internal tenant access token, sends interactive cards, and patches an existing card when a Windows toast changes. Official API references:

- [Get custom app tenant access token](https://open.feishu.cn/document/server-docs/authentication-management/access-token/tenant_access_token_internal)
- [Send message](https://open.feishu.cn/document/server-docs/im-v1/message/create)
- [Update message](https://open.feishu.cn/document/server-docs/im-v1/message/patch)

## First Run

1. Open Agent Notifications from the Start menu.
2. Enter the Feishu/Lark domain, App ID, App Secret, recipient type, and recipient value.
3. Choose the monitored applications. The defaults are ChatGPT, Qoder, and ZCode.
4. Add optional regex redaction rules, one per line.
5. Select **Send test** and verify that the destination receives the card.
6. Select **Grant notification access** and approve the Windows prompt.
7. Save. Closing the window leaves the tray application running.

The tray menu provides Settings, Pause/Resume, Send test, Start with Windows, and Exit. Launching Agent Notifications again opens Settings in the existing tray process instead of starting another listener.

## Grok Build CLI

After installing MSIX 0.2.0.0 or later and configuring Feishu, register the completion hook:

```powershell
.\scripts\Install-GrokHook.ps1
```

Run `/hooks` and Reload in existing Grok sessions, or restart them. A main-agent turn completion shows **Grok Build finished** in Windows and sends a Feishu card. Notifications contain the project name, session ID, and a fixed completion message. Session shutdowns, interruptions, errors, and subagent completions do not trigger this hook.

**Sources → Grok Build CLI (Windows + Feishu)** enables the integration. **Pause forwarding** suppresses both channels. Unprocessed events expire after the configured delivery lifetime; Feishu uses the existing durable retry queue. The hook can start the tray app when it is closed, and finds the installed package again after upgrades. Re-running the installer replaces only its own hook and callback, leaving other Grok settings unchanged.

Windows notification settings and Do not disturb can suppress banners. The hook uses Grok's `Stop` event; another `Stop` hook that blocks completion can keep Grok working after this event. Session/prompt IDs suppress repeated notifications for the same turn.

## Default Source Rules

| Source | AppUserModelID rule |
| --- | --- |
| ChatGPT | Regex `^OpenAI\.Codex_[^!]+!App$` |
| Qoder | Exact `com.qoder.app` |
| ZCode | Regex `^(?:electron\.app\.ZCode|dev\.zcode\.app)$` |

ChatGPT identities containing `!CodexCoreCommandRunner` are always excluded. Additional exact or regex rules can be entered as `Name|exact|AppUserModelID` or `Name|regex|pattern`.

## Storage And Privacy

The Settings window displays the packaged local data directory. It contains:

- `config.json`: non-secret settings.
- `secrets.dat`: App Secret encrypted with per-user Windows DPAPI.
- `deliveries.db`: queue state, hashes, message IDs, timestamps, and outcomes.
- `logs\*.jsonl`: metadata-only operational logs.
- `cli-inbox\*.json`: bounded pending Grok completion events, removed after ingestion; abandoned temporary files expire with the delivery lifetime.

Notification title and body are persisted only while delivery is pending. They are cleared after successful delivery. Delivered/dead-letter metadata is retained for seven days. Logs never intentionally contain notification text, access tokens, or the App Secret and are retained for seven days within the configured 50 MB total budget.

## Troubleshooting

**Startup reports `Collection was modified; enumeration operation may not execute`**

Install MSIX version 0.1.5.2 or later using the steps above. This fixes startup log cleanup when logs exceed the retention period. Your configuration, encrypted App Secret, and delivery queue are preserved; deleting app data is unnecessary.

**Install fails with `0x800B0109`**

The package-signing certificate is not trusted. Rebuild the development MSIX, run `Trust-DevelopmentCertificate.ps1` as Administrator, and install the package generated by the same build.

**Notification access is denied**

Open Windows Settings, find notification access/privacy controls for Agent Notifications, allow access, then reopen Settings in the tray and select **Grant notification access**. Windows can revoke this permission at any time.

**No ChatGPT, Qoder, or ZCode messages arrive**

- Confirm the desktop app produced a normal Windows toast.
- Confirm forwarding is not paused and the source is enabled.
- Check the AppUserModelID against the rules above and add a custom rule if the vendor identity changed.
- Existing Action Center notifications are intentionally baselined and not forwarded.

**Feishu test fails**

- Verify the correct Feishu/Lark domain, App ID, and App Secret.
- Verify `im:message:send_as_bot` is approved and the app version is published.
- Verify the selected recipient type matches the supplied value.
- Check metadata-only logs in the data directory for the classified failure.

**Startup is unavailable**

Windows can disable a startup task after the user rejects it. Re-enable Agent Notifications in Windows startup-app settings, then toggle **Start with Windows** again.

At sign-in, Windows notification services can take time to become ready. Agent Notifications remains in the tray as `Retrying`, limits each notification read to ten seconds, and resumes watching without replaying notifications that predate app startup. If the process is absent, confirm the startup task is enabled and reinstall the latest MSIX.

## Design References

- [Microsoft notification listener](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener)
- [Grok Build hooks](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/10-hooks.md), used for native Windows command-hook registration, completion payloads, and turn/session filtering.
- [Microsoft app capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- [Microsoft.Windows.SDK.BuildTools.MSIX 1.7.260903100](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.MSIX/1.7.260903100)
- [WinToastRelay](https://github.com/RavelloH/WinToastRelay), used as a reference for packaged listener, baseline, tray, and durable relay concepts; no code was copied.
- [NotifAI](https://github.com/lc700x/NotifAI), used as a reference for app allow-list and notification mirror behavior.
- `E:\Projects\PulseCortex`, specifically `packages\feishu-adapter\src\cards.ts`, `packages\feishu-adapter\src\adapter.ts`, `packages\domain\src\text.ts`, `docs\operations.md`, and `docs\feishu-setup.md`, used for Feishu card escaping, redaction, bounded retry, and operational documentation patterns.

No academic paper was needed for this platform integration; the implementation is based on operating-system/API documentation and the projects above.
