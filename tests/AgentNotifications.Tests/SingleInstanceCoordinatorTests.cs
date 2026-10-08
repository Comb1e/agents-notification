using AgentNotifications.App.Infrastructure;

namespace AgentNotifications.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task Secondary_instance_signals_primary_and_exits()
    {
        var applicationId = $"AgentNotifications.Tests.{Guid.NewGuid():N}";
        var directory = Path.Combine(Path.GetTempPath(), "agent-notifications-instance-tests", Guid.NewGuid().ToString("N"));
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primary = new SingleInstanceCoordinator(applicationId, directory);
        var secondary = new SingleInstanceCoordinator(applicationId, directory);
        try
        {
            Assert.True(primary.IsPrimary);
            primary.Start(() => activated.TrySetResult());

            Assert.False(secondary.IsPrimary);
            Assert.True(await secondary.SignalPrimaryAsync());
            await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            await secondary.DisposeAsync();
            await primary.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
