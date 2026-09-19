using AgentNotifications.App.Domain;

namespace AgentNotifications.App.Infrastructure;

public sealed class ListenerStateMachine
{
    private static readonly IReadOnlyDictionary<ListenerState, ListenerState[]> Allowed = new Dictionary<ListenerState, ListenerState[]>
    {
        [ListenerState.Starting] = [ListenerState.AwaitingPermission, ListenerState.Baselining, ListenerState.Paused, ListenerState.PermissionDenied, ListenerState.Faulted],
        [ListenerState.AwaitingPermission] = [ListenerState.Baselining, ListenerState.PermissionDenied, ListenerState.Paused, ListenerState.Faulted],
        [ListenerState.Baselining] = [ListenerState.Watching, ListenerState.PermissionDenied, ListenerState.Retrying, ListenerState.Paused, ListenerState.Faulted],
        [ListenerState.Watching] = [ListenerState.Paused, ListenerState.PermissionDenied, ListenerState.Retrying, ListenerState.Faulted],
        [ListenerState.Paused] = [ListenerState.AwaitingPermission, ListenerState.Baselining, ListenerState.Faulted],
        [ListenerState.PermissionDenied] = [ListenerState.AwaitingPermission, ListenerState.Baselining, ListenerState.Paused],
        [ListenerState.Retrying] = [ListenerState.Baselining, ListenerState.Watching, ListenerState.PermissionDenied, ListenerState.Paused, ListenerState.Faulted],
        [ListenerState.Faulted] = [ListenerState.AwaitingPermission, ListenerState.Baselining, ListenerState.Paused],
    };

    public ListenerStateMachine(ListenerState initial = ListenerState.Starting) => State = initial;

    public ListenerState State { get; private set; }

    public void MoveTo(ListenerState next)
    {
        if (next == State) return;
        if (!Allowed[State].Contains(next)) throw new InvalidOperationException($"Invalid listener transition {State} -> {next}");
        State = next;
    }
}

public static class DeliveryStateMachine
{
    private static readonly IReadOnlyDictionary<DeliveryState, DeliveryState[]> Allowed = new Dictionary<DeliveryState, DeliveryState[]>
    {
        [DeliveryState.Observed] = [DeliveryState.Debouncing],
        [DeliveryState.Debouncing] = [DeliveryState.Debouncing, DeliveryState.Queued, DeliveryState.DeadLetter],
        [DeliveryState.Queued] = [DeliveryState.Sending, DeliveryState.Updating, DeliveryState.DeadLetter],
        [DeliveryState.Sending] = [DeliveryState.Debouncing, DeliveryState.Delivered, DeliveryState.RetryScheduled, DeliveryState.DeadLetter],
        [DeliveryState.Updating] = [DeliveryState.Debouncing, DeliveryState.Delivered, DeliveryState.RetryScheduled, DeliveryState.DeadLetter],
        [DeliveryState.Delivered] = [DeliveryState.Debouncing],
        [DeliveryState.RetryScheduled] = [DeliveryState.Debouncing, DeliveryState.Queued, DeliveryState.DeadLetter],
        [DeliveryState.DeadLetter] = [DeliveryState.Debouncing],
    };

    public static void Ensure(DeliveryState current, DeliveryState next)
    {
        if (next == current) return;
        if (!Allowed[current].Contains(next)) throw new InvalidOperationException($"Invalid delivery transition {current} -> {next}");
    }
}
