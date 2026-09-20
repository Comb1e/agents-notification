using AgentNotifications.App.Infrastructure;

namespace AgentNotifications.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task Secondary_instance_signals_primary_and_exits()
    {
        var applicationId = $"AgentNotifications.Tests.{Guid.NewGuid():N}";
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var primary = new SingleInstanceCoordinator(applicationId);
        Assert.True(primary.IsPrimary);
        primary.Start(() => activated.TrySetResult());

        await using var secondary = new SingleInstanceCoordinator(applicationId);
        Assert.False(secondary.IsPrimary);
        Assert.True(await secondary.SignalPrimaryAsync());
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
