using System.IO.Pipes;
using System.Security.Principal;

namespace AgentNotifications.App.Infrastructure;

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource cancellation = new();
    private Task? listener;

    public SingleInstanceCoordinator(string applicationId = "Comb1e.AgentNotifications")
    {
        var userId = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var suffix = TextTools.Sha256($"{applicationId}\u001f{userId}")[..24];
        pipeName = $"{applicationId}.{suffix}.Activation";
        mutex = new Mutex(false, $"Local\\{applicationId}.{suffix}.Mutex", out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void Start(Action onActivation)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance can listen for activation");
        listener ??= ListenAsync(onActivation, cancellation.Token);
    }

    public async Task<bool> SignalPrimaryAsync(CancellationToken cancellationToken = default)
    {
        if (IsPrimary) return false;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync(250, cancellationToken);
                await client.WriteAsync(new byte[] { 1 }, cancellationToken);
                await client.FlushAsync(cancellationToken);
                return true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            await Task.Delay(50, cancellationToken);
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        if (listener is not null)
        {
            try { await listener; }
            catch (OperationCanceledException) { }
        }

        cancellation.Dispose();
        mutex.Dispose();
    }

    private async Task ListenAsync(Action onActivation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                var buffer = new byte[1];
                if (await server.ReadAsync(buffer, cancellationToken) > 0) onActivation();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}
