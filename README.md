# Agent Notifications

Agent Notifications is a Windows tray application that mirrors new Windows toasts from ChatGPT, Qoder, and ZCode to one Feishu or Lark destination. Grok Build CLI completions produce both a Windows notification and a Feishu card. It also synchronizes a shared Markdown instruction file to existing coding-agent instruction files. Existing desktop toasts are left unchanged and are never replayed at startup.

## Scope

- Windows 10 version 1809 or later, one signed-in user, x64.
- Packaged .NET 8 WPF application with `userNotificationListener` and `runFullTrust` capabilities.
- ChatGPT, Qoder, and ZCode desktop applications, plus automatic Grok Build CLI completion detection.
- One Feishu or Lark destination configured by `email`, `open_id`, `user_id`, `union_id`, or `chat_id`.
- Full title and body forwarding after configured regex redaction.
- Durable SQLite delivery, one-second update coalescing, retries, and in-place Feishu card updates.

Historical replay, remote control, notification actions, notification images, Windows services, and inbound Feishu events are outside v0.3.0.

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

Install MSIX 0.2.1.2 or later, configure Feishu, and keep Agent Notifications running in the tray. **Sources → Grok Build CLI (Windows + Feishu)** enables automatic detection and displays the number of open Grok sessions and processes. Already-running sessions are discovered automatically; no Grok restart or hook installation is needed.

Each new successful main-agent `turn_completed` record produces **Grok Build finished** in Windows and a Feishu card with the project name, session ID, and a fixed completion message. Earlier history, cancellations, errors, and subagent sessions are excluded. **Pause forwarding** suppresses new completions on both channels. Repeated records are deduplicated and Feishu delivery retains the existing retry behavior.

The app reads Grok's local session store in `%USERPROFILE%\.grok`, the app's `GROK_HOME` environment override, and any absolute paths in `grokHomeDirectories` in `config.json`. Windows notification settings and Do not disturb can suppress banners. Upgrading removes the app's former hook registration; already-loaded legacy callbacks remain compatible until those Grok sessions exit.

## Shared Agent Instructions

1. Keep the shared instructions in this project's `agents\AGENTS.md`.
2. Open **Settings → Agent files**, select that file with **Browse**, enable automatic sync, and **Save**.
3. Keep the tray app running. The tab displays detected files, sync status, and the last successful sync.

The app checks the source every second, waits for its contents to settle, and copies its exact bytes to existing instruction files in agent folders such as `C:\Users\<user>\.codex`, `.grok`, `.claude`, `.qoder`, and `.zcode`. It scans accessible user profiles and `.config\<agent>` folders. Detection uses existing filenames: `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `GROK.md`, `QODER.md`, `ZCODE.md`, and `copilot-instructions.md`, ignoring case. Both files are updated if a folder contains more than one matching name. Original filenames and casing stay unchanged; no instruction files are created in folders that lack them, and project/skill subdirectories are excluded.

Sync runs on app startup, source content changes, and discovery of a new target. Files already matching the source are left untouched. Locked or inaccessible targets retry independently; copying waits for stable source contents. Missing sources retain the current target contents. Symbolic links and junctions are skipped. Sources up to 1 MiB are supported. Disable sync with its own checkbox; notification forwarding and Feishu configuration are independent of this feature.

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
- [Microsoft file-system change behavior](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher), used to choose stable content polling across editor save/rename operations, and [atomic file replacement](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace), used to retain existing destination filenames and access controls.
- [Grok Build session storage](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/17-sessions.md), used for automatic session discovery and incremental completion detection; local completion records were checked against Grok Build 1.0.77.
- [Microsoft app capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- [Microsoft.Windows.SDK.BuildTools.MSIX 1.7.260903100](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.MSIX/1.7.260903100)
- [WinToastRelay](https://github.com/RavelloH/WinToastRelay), used as a reference for packaged listener, baseline, tray, and durable relay concepts; no code was copied.
- [NotifAI](https://github.com/lc700x/NotifAI), used as a reference for app allow-list and notification mirror behavior.
- `E:\Projects\PulseCortex`, specifically `packages\feishu-adapter\src\cards.ts`, `packages\feishu-adapter\src\adapter.ts`, `packages\domain\src\text.ts`, `docs\operations.md`, and `docs\feishu-setup.md`, used for Feishu card escaping, redaction, bounded retry, and operational documentation patterns.

No academic paper was needed for this platform integration; the implementation is based on operating-system/API documentation and the projects above.
