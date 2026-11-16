using AgentNotifications.App.Configuration;

namespace AgentNotifications.App.Infrastructure;

public sealed class JsonLineTailer
{
    private enum LineState { Reading, Skipping }
    private LineState state;
    private byte[] guard = [];

    public long Offset { get; private set; }

    public async Task ReadAsync(string path, Func<ReadOnlyMemory<byte>, Task> onLine,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length < Offset || !ReadGuard(stream, Offset).AsSpan().SequenceEqual(guard))
        {
            // Rewinds and atomic replacements can rewrite a log without making it shorter.
            Offset = 0;
            guard = [];
            state = LineState.Reading;
        }

        stream.Position = Offset;
        using var line = new MemoryStream();
        var buffer = new byte[65_536];
        var consumed = 0;
        var position = Offset;
        while (consumed < CliIntegrationDefaults.GrokReadBatchBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, CliIntegrationDefaults.GrokReadBatchBytes - consumed)), cancellationToken);
            if (read == 0) break;
            consumed += read;
            for (var index = 0; index < read; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = buffer[index];
                position++;
                if (value == '\n')
                {
                    if (state == LineState.Reading && line.Length > 0) await onLine(line.ToArray());
                    line.SetLength(0);
                    state = LineState.Reading;
                    Offset = position;
                    guard = ReadGuard(stream, Offset);
                }
                else if (state == LineState.Skipping)
                {
                    Offset = position;
                }
                else if (line.Length >= CliIntegrationDefaults.GrokMaximumLineBytes)
                {
                    line.SetLength(0);
                    state = LineState.Skipping;
                    Offset = position;
                }
                else line.WriteByte(value);
            }
        }
        guard = ReadGuard(stream, Offset);
        // An incomplete line is re-read from its beginning on the next poll.
    }

    private static byte[] ReadGuard(FileStream stream, long offset)
    {
        if (offset == 0 || offset > stream.Length) return [];
        var previousPosition = stream.Position;
        var bytes = new byte[(int)Math.Min(64, offset)];
        stream.Position = offset - bytes.Length;
        var read = stream.Read(bytes, 0, bytes.Length);
        stream.Position = previousPosition;
        return bytes[..read];
    }
}
