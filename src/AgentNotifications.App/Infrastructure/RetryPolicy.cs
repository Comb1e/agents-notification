using AgentNotifications.App.Configuration;

namespace AgentNotifications.App.Infrastructure;

public interface IRandomSource
{
    double NextDouble();
}

public sealed class SystemRandomSource : IRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble();
}

public sealed class RetryPolicy(RuntimeSettings settings, IRandomSource random)
{
    public TimeSpan DelayForAttempt(int attempt)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        var ceilingMs = Math.Min(
            settings.RetryMaximumMinutes * 60_000d,
            settings.RetryBaseMilliseconds * Math.Pow(2, exponent));
        var jitteredMs = (ceilingMs / 2d) + (random.NextDouble() * ceilingMs / 2d);
        return TimeSpan.FromMilliseconds(jitteredMs);
    }
}
