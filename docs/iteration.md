# Iteration History

## 0.1.3 - 2026-09-19

### Previous Version Issue

- The package and tray used generated placeholder icons instead of the supplied application artwork.

### Root Cause

- The asset generator drew a built-in bell and the tray explicitly used `SystemIcons.Information`.

### Improvement

- Added `icon/icon.png` as the source artwork.
- Generate 44, 50, 150, and 256 pixel PNG assets with alpha and high-quality bicubic scaling.
- Use the supplied artwork for the MSIX package, WPF window, and system tray.
- Bumped the application and MSIX version to `0.1.3` / `0.1.3.0`.

### Verification

- Generated assets are 44, 50, 150, and 256 pixels square and retain PNG alpha.
- `dotnet format AgentNotifications.sln --verify-no-changes --no-restore`: passed.
- `dotnet build AgentNotifications.sln -c Release`: passed with zero warnings and zero errors.
- `dotnet test AgentNotifications.sln -c Release --no-build --no-restore`: 45 passed, 0 failed.
- Signed x64 MSIX `0.1.3.0` was created with a valid signature.
- The installed package upgraded in place from `0.1.2.0` to `0.1.3.0` and reported status `Ok`.
- Five package launch attempts produced exactly one running `AgentNotifications.App` process.

### Remaining Issues

- Windows can retain cached Start menu artwork briefly after an upgrade; signing out or rebuilding the icon cache refreshes stale shell thumbnails.

## 0.1.2 - 2026-09-19

### Previous Version Issue

- Every Start menu or package launch created another hidden tray process.
- Concurrent instances opened the same SQLite database, registered duplicate notification listeners, and could forward the same toast more than once.

### Root Cause

- The WPF entry point had no per-user process ownership or activation forwarding.
- A configured primary instance intentionally hides its Settings window, so a second launch appeared to do nothing while still creating another process.

### Improvement

- Added a per-user named mutex that permits one primary process.
- Added a per-user named pipe so a secondary launch asks the primary process to open Settings, then exits.
- Bumped the application and MSIX version to `0.1.2` / `0.1.2.0`.

### Verification

- Added a regression test that creates primary and secondary coordinators and verifies activation reaches only the primary instance.
- `dotnet build AgentNotifications.sln -c Release`: passed with zero warnings and zero errors.
- `dotnet test AgentNotifications.sln -c Release --no-build --no-restore`: 45 passed, 0 failed.
- Signed x64 MSIX `0.1.2.0` was created with a valid signature and upgraded in place with package status `Ok`.
- Five Start menu launch attempts produced exactly one running `AgentNotifications.App` process.

### Remaining Issues

- A forced process termination can interrupt an in-flight Feishu request; SQLite restart recovery returns that row to the retry queue.

## 0.1.1 - 2026-09-19

### Previous Version Issue

- Feishu rejected test delivery with error `300302` because the interactive card declared `config.update_multi` as `false`.

### Root Cause

- The card schema disabled multi-update behavior even though Agent Notifications updates a previously delivered card when the Windows toast content changes.

### Improvement

- Changed every generated card to declare `config.update_multi` as `true`.
- Added a regression assertion that reads the serialized card JSON and requires the setting to remain enabled.
- Bumped the application and MSIX version to `0.1.1` / `0.1.1.0`.

### Verification

- Automated card tests verify update support, markdown escaping, and Unicode-safe limits.
- `dotnet build AgentNotifications.sln -c Release`: passed with zero warnings and zero errors.
- `dotnet test AgentNotifications.sln -c Release --no-build --no-restore`: 44 passed, 0 failed.
- Signed x64 MSIX `0.1.1.0` was created with a valid signature.
- The installed package upgraded in place from `0.1.0.0` to `0.1.1.0`, reported status `Ok`, and was relaunched.
- A live Feishu test delivery succeeded after the card update setting was corrected.

### Remaining Issues

- Real ChatGPT, Qoder, and ZCode delivery checks still require the installed package and Windows notification permission.

## 0.1.0 - 2026-09-19

### Previous State And Issues

- No independent Windows application existed for forwarding coding-agent desktop notifications to Feishu.
- Existing Action Center notifications could be mistaken for new work unless startup established a baseline.
- Event-only listening could miss a change; polling-only listening would add delay and repeated reads.
- Network failures, application restarts, and changed toasts required durable state instead of an in-memory queue.
- Notification bodies and the Feishu App Secret needed different storage and retention rules.

### Root Causes And Method

- Windows notification access is a permissioned packaged-app API, so the project uses an MSIX WPF application with `userNotificationListener` and full-trust execution.
- `NotificationChanged` does not carry the complete changed notification, so every event triggers a snapshot delta and a 15-second reconciliation recovers missed events.
- A toast can change while an earlier revision is being sent, so SQLite updates are guarded by both toast key and content hash.
- Feishu access tokens expire and HTTP failures have different recovery rules, so the client classifies authentication, rate-limit, missing-message, transient, and permanent failures.
- Complex listener and delivery transitions are encoded as explicit state machines and tested against invalid transitions.

### Improvements In This Version

- Added the .NET 8 WPF tray app, xUnit project, MSIX manifest, package assets, and build/install scripts.
- Added first-run Settings for notification access, Feishu/Lark credentials, recipient type/value, source rules, redaction, test delivery, and startup.
- Added ChatGPT, Qoder, and ZCode matchers and excluded ChatGPT `CodexCoreCommandRunner` notifications.
- Added startup baseline suppression, event reconciliation, duplicate suppression, one-second coalescing, and revocation handling.
- Added DPAPI App Secret storage, JSON settings, SQLite WAL delivery state, seven-day metadata retention, and metadata-only logs.
- Added Feishu/Lark tenant-token caching, interactive cards, Unicode-safe limits, card patching, replacement sends, and typed retry behavior.
- Added a direct tray startup toggle and settings health status.

### Verification

- `dotnet build AgentNotifications.sln -c Debug`: passed with zero warnings and zero errors.
- `dotnet test AgentNotifications.sln -c Debug --no-restore`: 44 passed, 0 failed.
- Automated coverage includes state transitions, source matching, baseline suppression, remove/reappear ordering, duplicate IDs across apps, empty toast content, permission denial/revocation, deduplication, coalescing, restart recovery, redaction, Unicode limits, deterministic retry bounds, token refresh, invalid credentials, timeouts, 4xx/5xx classification, rate limits, card patching, missing-message replacement, expiry, and secret-free delivery logs.
- Release MSIX creation passed with `Microsoft.Windows.SDK.BuildTools.MSIX` 1.7.260903100.
- The development MSIX is signed. Installation, startup-task, upgrade, uninstall, and visual checks require the generated certificate to be trusted in `LocalMachine\TrustedPeople` first.

### Remaining Risks

- Live Feishu delivery was not run because no App ID, App Secret, or recipient was supplied.
- Real ChatGPT, Qoder, and ZCode toast checks still require the installed package, user-approved notification permission, and notifications from those applications.
- Vendor AppUserModelIDs can change; custom exact/regex rules are available for that case.
- The development certificate is for local sideloading only. A production release needs a production signing identity or Microsoft Store signing.
- Regex redaction reduces accidental disclosure but cannot identify every secret format.

### References Used

- Microsoft: [Notification listener](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener) and [app capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations).
- Microsoft package tool: [Microsoft.Windows.SDK.BuildTools.MSIX 1.7.260903100](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.MSIX/1.7.260903100).
- Feishu: [tenant access token](https://open.feishu.cn/document/server-docs/authentication-management/access-token/tenant_access_token_internal), [send message](https://open.feishu.cn/document/server-docs/im-v1/message/create), and [update message](https://open.feishu.cn/document/server-docs/im-v1/message/patch).
- [WinToastRelay](https://github.com/RavelloH/WinToastRelay), used for notification-listener, baseline, tray, startup, and durable relay design comparison. No code copied.
- [NotifAI](https://github.com/lc700x/NotifAI), used for notification mirroring and source allow-list comparison. Its CLI hook path is outside this project. No code copied.
- Local `E:\Projects\PulseCortex`: `packages\feishu-adapter\src\cards.ts`, `packages\feishu-adapter\src\adapter.ts`, `packages\domain\src\text.ts`, `docs\operations.md`, and `docs\feishu-setup.md`. These informed card escaping, redaction order, jittered retry, log retention, and operator setup wording. No code copied.
- No academic paper was used; primary platform/API documentation and working open-source projects were the relevant sources.
