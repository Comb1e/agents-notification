using AgentNotifications.App.Domain;
using AgentNotifications.App.Infrastructure;

namespace AgentNotifications.Tests;

public sealed class StateMachineTests
{
    [Fact]
    public void Listener_accepts_expected_lifecycle()
    {
        var machine = new ListenerStateMachine();
        machine.MoveTo(ListenerState.AwaitingPermission);
        machine.MoveTo(ListenerState.Baselining);
        machine.MoveTo(ListenerState.Watching);
        machine.MoveTo(ListenerState.Retrying);
        machine.MoveTo(ListenerState.Watching);
        machine.MoveTo(ListenerState.Paused);
        Assert.Equal(ListenerState.Paused, machine.State);
    }

    [Fact]
    public void Listener_rejects_impossible_transition()
    {
        var machine = new ListenerStateMachine();
        Assert.Throws<InvalidOperationException>(() => machine.MoveTo(ListenerState.Watching));
    }

    [Fact]
    public void Delivery_accepts_new_revision_while_sending()
    {
        DeliveryStateMachine.Ensure(DeliveryState.Sending, DeliveryState.Debouncing);
        DeliveryStateMachine.Ensure(DeliveryState.Delivered, DeliveryState.Debouncing);
    }

    [Fact]
    public void Delivery_rejects_direct_observed_to_delivered()
    {
        Assert.Throws<InvalidOperationException>(() => DeliveryStateMachine.Ensure(DeliveryState.Observed, DeliveryState.Delivered));
    }
}
