using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Feishu;
using AgentNotifications.App.Infrastructure;
using AgentNotifications.App.Notifications;
using AgentNotifications.App.Persistence;

namespace AgentNotifications.App.Services;

public sealed class AppController : IAsyncDisposable
{
    private readonly AppPaths paths;
    private readonly SettingsStore settingsStore;
    private readonly ISecretStore secretStore;
    private readonly DeliveryStore deliveryStore;
    private readonly AppLogger logger;
    private readonly HttpClient httpClient;
    private readonly IClock clock;
    private readonly StartupManager startupManager;
    private readonly DeliveryQueue deliveryQueue;
    private readonly NotificationMonitor monitor;
    private readonly IWindowsNotificationSource notificationSource;
    private readonly CliCompletionService cliCompletion;
    private AppSettings currentSettings;
    private string currentSecret;
    private DeliveryCounts counts = new(0, 0);

    private AppController(
        AppPaths paths,
        SettingsStore settingsStore,
        ISecretStore secretStore,
        DeliveryStore deliveryStore,
        AppLogger logger,
        HttpClient httpClient,
        IClock clock,
        StartupManager startupManager,
        DeliveryQueue deliveryQueue,
        NotificationMonitor monitor,
        IWindowsNotificationSource notificationSource,
        AppSettings settings,
        string secret)
    {
        this.paths = paths;
        this.settingsStore = settingsStore;
        this.secretStore = secretStore;
        this.deliveryStore = deliveryStore;
        this.logger = logger;
        this.httpClient = httpClient;
        this.clock = clock;
        this.startupManager = startupManager;
        this.deliveryQueue = deliveryQueue;
        this.monitor = monitor;
        this.notificationSource = notificationSource;
        currentSettings = settings;
        currentSecret = secret;
        cliCompletion = new CliCompletionService(paths, deliveryStore, () => currentSettings, clock,
            new WindowsToastPresenter().Show, deliveryQueue.Wake, logger);
        logger.UpdateSettings(settings.Runtime);
        monitor.StateChanged += (_, state) => StateChanged?.Invoke(this, state);
        monitor.QueueChanged += (_, _) => deliveryQueue.Wake();
        deliveryQueue.CountsChanged += (_, value) =>
        {
            counts = value;
            CountsChanged?.Invoke(this, value);
        };
        deliveryQueue.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
    }

    public event EventHandler<ListenerState>? StateChanged;

    public event EventHandler<DeliveryCounts>? CountsChanged;

    public event EventHandler<string>? StatusChanged;

    public AppSettings Settings => currentSettings;

    public string Secret => currentSecret;

    public ListenerState ListenerState => monitor.State;

    public NotificationAccess NotificationAccess => monitor.AccessStatus;

    public DeliveryCounts Counts => counts;

    public string DataDirectory => paths.DataDirectory;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        deliveryQueue.Start();
        cliCompletion.Start();
        if (currentSettings.IsConfigured && !string.IsNullOrWhiteSpace(currentSecret))
            await monitor.StartAsync(false, cancellationToken);
        else
            StatusChanged?.Invoke(this, "Configuration required");
        counts = deliveryStore.GetCounts();
        CountsChanged?.Invoke(this, counts);
        logger.Info("application.started", new { paths.DataDirectory, configured = currentSettings.IsConfigured, monitorState = monitor.State.ToString() });
    }

    public async Task SaveAsync(AppSettings settings, string secret, CancellationToken cancellationToken = default)
    {
        SettingsStore.Validate(settings);
        EnsureDestinationWhenEnabled(settings, secret);
        var startupStatus = await startupManager.SetEnabledAsync(settings.StartWithWindows);
        settings.StartWithWindows = startupStatus == StartupAvailability.Enabled;
        await settingsStore.SaveAsync(settings, cancellationToken);
        await secretStore.SaveAsync(secret, cancellationToken);
        currentSettings = settings;
        currentSecret = secret;
        if (settings.IsConfigured && !string.IsNullOrWhiteSpace(secret))
            await monitor.ReconfigureAsync(cancellationToken);
        else
            await monitor.PauseAsync();
        deliveryQueue.RetryFailed();
        logger.Info("settings.saved", new
        {
            settings.Feishu.Domain,
            settings.Feishu.RecipientType,
            sourceCount = settings.SourceRules.Count(rule => rule.Enabled),
            redactionPatternCount = settings.RedactionPatterns.Count,
            settings.MonitoringEnabled,
            settings.StartWithWindows,
            settings.GrokNotificationsEnabled,
        });
        StatusChanged?.Invoke(this, "Settings saved");
    }

    public async Task<NotificationAccess> RequestNotificationAccessAsync()
    {
        var result = await monitor.RequestAccessAsync();
        if (result == NotificationAccess.Allowed && currentSettings.IsConfigured && !string.IsNullOrWhiteSpace(currentSecret))
            await monitor.StartAsync(false);
        return result;
    }

    public async Task SetMonitoringEnabledAsync(bool enabled)
    {
        if (enabled && (!currentSettings.IsConfigured || string.IsNullOrWhiteSpace(currentSecret)))
            throw new InvalidOperationException("Feishu App ID, App Secret, recipient, and at least one monitored app are required");
        currentSettings.MonitoringEnabled = enabled;
        await settingsStore.SaveAsync(currentSettings);
        if (enabled) await monitor.StartAsync(false);
        else await monitor.PauseAsync();
        logger.Info("monitoring.changed", new { enabled });
    }

    public async Task<StartupAvailability> SetStartWithWindowsAsync(bool enabled)
    {
        var status = await startupManager.SetEnabledAsync(enabled);
        currentSettings.StartWithWindows = status == StartupAvailability.Enabled;
        await settingsStore.SaveAsync(currentSettings);
        logger.Info("startup.changed", new { requested = enabled, status = status.ToString() });
        return status;
    }

    public async Task<string> TestFeishuAsync(AppSettings settings, string secret, CancellationToken cancellationToken = default)
    {
        SettingsStore.Validate(settings);
        if (string.IsNullOrWhiteSpace(settings.Feishu.AppId) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(settings.Feishu.Recipient))
            throw new InvalidOperationException("Feishu App ID, App Secret, and recipient are required");
        var cardBuilder = new FeishuCardBuilder(settings.Runtime);
        var client = new FeishuClient(httpClient, cardBuilder, clock);
        var now = clock.UtcNow;
        var record = new DeliveryRecord(
            "test", "AgentNotifications", "Agent Notifications", 0, now, now,
            "Delivery test", "Agent Notifications can deliver Windows notifications to this destination.",
            "test", null, DeliveryState.Sending, 1, now, now.AddMinutes(5), null, now);
        var message = await client.SendAsync(ToDestination(settings, secret), record, cancellationToken);
        logger.Info("feishu.test_succeeded", new { settings.Feishu.Domain, settings.Feishu.RecipientType, message.MessageId });
        return message.MessageId;
    }

    public int RetryFailed() => deliveryQueue.RetryFailed();

    public Task<StartupAvailability> GetStartupStatusAsync() => startupManager.GetStatusAsync();

    public async ValueTask DisposeAsync()
    {
        logger.Info("application.stopping", new { monitorState = monitor.State.ToString() });
        await cliCompletion.DisposeAsync();
        await monitor.DisposeAsync();
        await deliveryQueue.DisposeAsync();
        if (notificationSource is IDisposable disposable) disposable.Dispose();
        deliveryStore.Dispose();
        httpClient.Dispose();
        logger.Retain();
    }

    public static async Task<AppController> CreateAsync()
    {
        var paths = AppPaths.Resolve();
        Directory.CreateDirectory(paths.DataDirectory);
        var settingsStore = new SettingsStore(paths);
        var secretStore = new DpapiSecretStore(paths);
        var settings = await settingsStore.LoadAsync();
        var secret = await secretStore.LoadAsync();
        var clock = new SystemClock();
        var logger = new AppLogger(paths, settings.Runtime);
        var store = new DeliveryStore(paths.DatabasePath);
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var feishu = new FeishuClient(httpClient, new FeishuCardBuilder(settings.Runtime), clock);
        var source = new WindowsNotificationSource();
        AppController? controller = null;
        var queue = new DeliveryQueue(
            store,
            feishu,
            () => controller?.Destination,
            () => controller?.currentSettings ?? settings,
            clock,
            new SystemRandomSource(),
            logger);
        var monitor = new NotificationMonitor(source, store, () => controller?.currentSettings ?? settings, clock);
        controller = new AppController(
            paths, settingsStore, secretStore, store, logger, httpClient, clock, new StartupManager(), queue, monitor, source, settings, secret);
        return controller;
    }

    private FeishuDestination? Destination => currentSettings.IsConfigured && !string.IsNullOrWhiteSpace(currentSecret)
        ? ToDestination(currentSettings, currentSecret)
        : null;

    private static FeishuDestination ToDestination(AppSettings settings, string secret) => new(
        settings.Feishu.Domain,
        settings.Feishu.AppId.Trim(),
        secret,
        settings.Feishu.RecipientType,
        settings.Feishu.Recipient.Trim());

    private static void EnsureDestinationWhenEnabled(AppSettings settings, string secret)
    {
        if (!settings.MonitoringEnabled) return;
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Feishu App ID, App Secret, recipient, and at least one monitored app are required");
    }
}
