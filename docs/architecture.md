# Architecture

## Main Parts

```mermaid
flowchart LR
    W[Windows notification center] --> S[WindowsNotificationSource]
    S --> M[NotificationMonitor]
    C[JSON settings] --> M
    M --> R[Match and redact]
    R --> D[(SQLite delivery store)]
    D --> Q[DeliveryQueue]
    Q --> F[FeishuClient]
    F --> A[Feishu or Lark]
    P[DPAPI secret store] --> F
    T[Tray and Settings] --> C
    T --> M
    T --> Q
```

- `WindowsNotificationSource` is the only code that calls `UserNotificationListener`.
- `NotificationMonitor` owns listener state, startup baseline, source matching, redaction, deduplication, and reconciliation.
- `DeliveryStore` persists work before network delivery and keeps restart state.
- `DeliveryQueue` owns expiry, retry, send, update, replacement, and retention behavior.
- `FeishuClient` owns tenant tokens and typed HTTP errors.
- `AppController` connects the tray, Settings window, listener, queue, and storage.

## Data Flow

```mermaid
flowchart TD
    A[Read current toast snapshot] --> B{Startup baseline?}
    B -- yes --> C[Remember toast and suppress old content]
    B -- no --> D{Source rule matches?}
    D -- no --> E[Ignore]
    D -- yes --> F[Redact title and body]
    F --> G[Build key and content hash]
    G --> H[Upsert debounced row in SQLite]
    H --> I{Due and not expired?}
    I -- no --> J[Wait or dead-letter]
    I -- yes --> K{Existing Feishu message ID?}
    K -- no --> L[Send card]
    K -- yes --> M[Patch card]
    M --> N{Message missing?}
    N -- yes --> L
    N -- no --> O[Mark delivered]
    L --> O
    O --> P[Clear title and body; keep metadata]
```

Toast identity is `source AppUserModelID + Windows notification ID + creation time`. The content hash detects repeated snapshots and changed content. A one-second debounce keeps rapid changes in one row.

## Listener State

```mermaid
stateDiagram-v2
    [*] --> Starting
    Starting --> AwaitingPermission
    Starting --> Baselining
    Starting --> Paused
    AwaitingPermission --> PermissionDenied
    AwaitingPermission --> Baselining
    PermissionDenied --> AwaitingPermission
    PermissionDenied --> Baselining
    Baselining --> Watching
    Baselining --> Retrying
    Watching --> Retrying
    Watching --> PermissionDenied
    Watching --> Paused
    Retrying --> Watching
    Retrying --> PermissionDenied
    Retrying --> Paused
    Paused --> AwaitingPermission
    Paused --> Baselining
    Starting --> Faulted
    Baselining --> Faulted
    Watching --> Faulted
```

- `Baselining` records notifications already present at startup and never delivers them.
- `Watching` handles change events and also reconciles the full snapshot every 15 seconds.
- `PermissionDenied` is entered when access is missing or revoked.
- `Retrying` keeps the app alive after a temporary listener read failure.

## Delivery State

```mermaid
stateDiagram-v2
    [*] --> Observed
    Observed --> Debouncing
    Debouncing --> Queued
    Queued --> Sending
    Queued --> Updating
    Sending --> Delivered
    Updating --> Delivered
    Updating --> Sending: old message missing
    Sending --> RetryScheduled
    Updating --> RetryScheduled
    RetryScheduled --> Queued
    Sending --> DeadLetter
    Updating --> DeadLetter
    RetryScheduled --> DeadLetter
    Delivered --> Debouncing: toast content changed
    DeadLetter --> Debouncing: manual retry or new content
```

The `Queued` transition is validated before a row moves atomically to `Sending` or `Updating`. A newer toast revision can move an in-flight row back to `Debouncing`; completion of the older revision cannot erase the newer title/body.

## Files And Secrets

```mermaid
flowchart LR
    U[Settings UI] --> J[config.json]
    U --> X[DPAPI encrypt]
    X --> S[secrets.dat]
    N[Redacted notification] --> Q[(deliveries.db)]
    Q --> H[Hash, message ID, outcome]
    Q --> V[Pending title and body]
    O[Successful delivery] --> Z[Delete title and body]
    L[Runtime events] --> G[Metadata-only JSONL logs]
```

The App Secret is encrypted for the signed-in Windows user. Regex redaction runs before notification content enters SQLite, logs, or Feishu. Logs record keys, hashes, source names, outcomes, and message IDs, not notification bodies or credentials.

## Boundaries

- One Windows user and one Feishu/Lark destination.
- No notification deletion or modification.
- No direct Windows notification database access.
- No process scraping and no CLI-agent hooks.
- No inbound network listener, Feishu event subscription, or remote command path.
