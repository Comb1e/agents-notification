using AgentNotifications.App.Domain;

namespace AgentNotifications.App.Notifications;

public interface IWindowsNotificationSource
{
    event EventHandler? Changed;

    NotificationAccess GetAccessStatus();

    Task<NotificationAccess> RequestAccessAsync();

    Task<IReadOnlyList<WindowsToast>> GetSnapshotAsync(CancellationToken cancellationToken);
}
