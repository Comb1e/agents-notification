using AgentNotifications.App.Domain;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace AgentNotifications.App.Notifications;

public sealed class WindowsNotificationSource : IWindowsNotificationSource, IDisposable
{
    private readonly UserNotificationListener listener = UserNotificationListener.Current;

    public WindowsNotificationSource()
    {
        listener.NotificationChanged += OnNotificationChanged;
    }

    public event EventHandler? Changed;

    public NotificationAccess GetAccessStatus() => Map(listener.GetAccessStatus());

    public async Task<NotificationAccess> RequestAccessAsync() => Map(await listener.RequestAccessAsync());

    public async Task<IReadOnlyList<WindowsToast>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellationToken);
        var result = new List<WindowsToast>();
        foreach (var notification in notifications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                var parts = binding?.GetTextElements()
                    .Select(element => element.Text.Trim())
                    .Where(text => text.Length > 0)
                    .ToArray() ?? [];
                var appUserModelId = notification.AppInfo.AppUserModelId;
                if (string.IsNullOrWhiteSpace(appUserModelId)) continue;
                result.Add(new WindowsToast(
                    appUserModelId,
                    notification.AppInfo.DisplayInfo.DisplayName,
                    notification.Id,
                    notification.CreationTime,
                    parts.ElementAtOrDefault(0) ?? string.Empty,
                    string.Join(Environment.NewLine, parts.Skip(1))));
            }
            catch
            {
                // A malformed notification must not prevent the rest of the snapshot.
            }
        }

        return result;
    }

    public void Dispose() => listener.NotificationChanged -= OnNotificationChanged;

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    private static NotificationAccess Map(UserNotificationListenerAccessStatus status) => status switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccess.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccess.Denied,
        UserNotificationListenerAccessStatus.Unspecified => NotificationAccess.Unspecified,
        _ => NotificationAccess.Unsupported,
    };
}
