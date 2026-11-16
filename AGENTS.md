# Code Structure

- `src/AgentNotifications.App`: packaged .NET 8 WPF tray application.
- `src/AgentNotifications.App/Configuration`: JSON settings, built-in source rules, LocalState paths, and DPAPI secret storage.
- `src/AgentNotifications.App/Notifications`: Windows notification-listener adapter, native toast presenter, CLI event inbox, and listener state machine orchestration.
- `src/AgentNotifications.App/Persistence`: SQLite delivery queue and retained metadata.
- `src/AgentNotifications.App/Feishu`: Feishu/Lark HTTP client and interactive-card construction.
- `src/AgentNotifications.App/Infrastructure`: logging, retry, text, source matching, state machines, and single-instance coordination.
- `src/AgentNotifications.App/Services`: application composition, delivery processing, CLI completion ingestion, and Windows startup-task management.
- `tests/AgentNotifications.Tests`: focused xUnit coverage using fake notification and Feishu boundaries.
- `scripts`: package asset, certificate, MSIX build, install, uninstall, and Grok completion-hook workflows.
- `docs/architecture.md`: current responsibilities, data flow, storage, states, decisions, constraints, and failure paths.
- `docs/iteration.md`: version history, verification, remaining risks, and references actually used.
