using System.Security.Principal;

namespace AgentNotifications.App.Infrastructure;

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly FileStream? instanceLock;
    private readonly FileStream? activationReader;
    private readonly string activationPath;
    private readonly CancellationTokenSource cancellation = new();
    private Task? listener;
    private long activationOffset;

    public SingleInstanceCoordinator(string applicationId = "Comb1e.AgentNotifications", string? sharedDirectory = null)
    {
        var userId = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var suffix = TextTools.Sha256($"{applicationId}\u001f{userId}")[..24];
        var directory = sharedDirectory ?? Path.Combine(Path.GetTempPath(), "AgentNotifications", suffix);
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, $".{suffix}.instance.lock");
        activationPath = Path.Combine(directory, $".{suffix}.activation");

        FileStream? reader = null;
        try
        {
            reader = new FileStream(
                activationPath,
                FileMode.OpenOrCreate,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.Asynchronous);
            activationOffset = reader.Length;
            instanceLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            activationReader = reader;
            IsPrimary = true;
        }
        catch (IOException)
        {
            reader?.Dispose();
            instanceLock?.Dispose();
            IsPrimary = false;
        }
    }

    public bool IsPrimary { get; }

    public void Start(Action onActivation)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance can listen for activation");
        ArgumentNullException.ThrowIfNull(onActivation);
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
                await using var stream = new FileStream(
                    activationPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64,
                    FileOptions.Asynchronous);
                var signal = Guid.NewGuid().ToByteArray();
                await stream.WriteAsync(signal, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                return true;
            }
            catch (IOException)
            {
            }

            await Task.Delay(100, cancellationToken);
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
        activationReader?.Dispose();
        instanceLock?.Dispose();
    }

    private async Task ListenAsync(Action onActivation, CancellationToken cancellationToken)
    {
        if (activationReader is null) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var length = activationReader.Length;
                if (length <= activationOffset) continue;
                activationReader.Position = activationOffset;
                var buffer = new byte[Math.Min(4096, length - activationOffset)];
                var read = await activationReader.ReadAsync(buffer, cancellationToken);
                if (read <= 0) continue;
                activationOffset += read;
                onActivation();
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
