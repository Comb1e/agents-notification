using Windows.ApplicationModel;

namespace AgentNotifications.App.Services;

public enum StartupAvailability
{
    Enabled,
    Disabled,
    DisabledByUser,
    Unavailable,
}

public sealed class StartupManager
{
    private const string TaskId = "AgentNotificationsStartup";

    public async Task<StartupAvailability> GetStatusAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State switch
            {
                StartupTaskState.Enabled => StartupAvailability.Enabled,
                StartupTaskState.DisabledByUser => StartupAvailability.DisabledByUser,
                StartupTaskState.Disabled => StartupAvailability.Disabled,
                _ => StartupAvailability.Unavailable,
            };
        }
        catch
        {
            return StartupAvailability.Unavailable;
        }
    }

    public async Task<StartupAvailability> SetEnabledAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (!enabled)
            {
                task.Disable();
                return StartupAvailability.Disabled;
            }

            var state = await task.RequestEnableAsync();
            return state switch
            {
                StartupTaskState.Enabled => StartupAvailability.Enabled,
                StartupTaskState.DisabledByUser => StartupAvailability.DisabledByUser,
                StartupTaskState.Disabled => StartupAvailability.Disabled,
                _ => StartupAvailability.Unavailable,
            };
        }
        catch
        {
            return StartupAvailability.Unavailable;
        }
    }
}
