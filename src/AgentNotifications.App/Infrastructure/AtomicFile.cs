namespace AgentNotifications.App.Infrastructure;

public static class AtomicFile
{
    public static async Task ReplaceExistingAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("The destination is not a regular file");
        var temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".agent-notifications-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4_096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Replace requires an existing destination and preserves its filename and Windows ACL.
            File.Replace(temporaryPath, path, destinationBackupFileName: null);
        }
        finally { File.Delete(temporaryPath); }
    }
}
