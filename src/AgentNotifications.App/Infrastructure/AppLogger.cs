using System.Text.Json;
using AgentNotifications.App.Configuration;

namespace AgentNotifications.App.Infrastructure;

public sealed class AppLogger
{
    private readonly AppPaths paths;
    private readonly object gate = new();
    private RuntimeSettings settings;

    public AppLogger(AppPaths paths, RuntimeSettings settings)
    {
        this.paths = paths;
        this.settings = settings;
        Directory.CreateDirectory(paths.LogDirectory);
        Retain();
    }

    public void Info(string eventName, object? fields = null) => Write("info", eventName, fields);

    public void Warn(string eventName, object? fields = null) => Write("warn", eventName, fields);

    public void Error(string eventName, object? fields = null) => Write("error", eventName, fields);

    public void UpdateSettings(RuntimeSettings value)
    {
        lock (gate) settings = value;
    }

    public void Retain()
    {
        lock (gate)
        {
            Directory.CreateDirectory(paths.LogDirectory);
            var configuration = settings;
            var cutoff = DateTime.UtcNow.Date.AddDays(-configuration.LogRetentionDays);
            var files = new DirectoryInfo(paths.LogDirectory)
                .EnumerateFiles("*.jsonl")
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToList();
            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff).ToArray())
            {
                TryDelete(file);
                files.Remove(file);
            }

            var total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= configuration.LogMaximumBytes) break;
                if (file.Name.StartsWith(DateTimeOffset.Now.ToString("yyyy-MM-dd"), StringComparison.Ordinal)) continue;
                total -= file.Length;
                TryDelete(file);
            }
        }
    }

    private void Write(string level, string eventName, object? fields)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(paths.LogDirectory);
                var path = CurrentPath();
                var line = JsonSerializer.Serialize(new
                {
                    timestamp = DateTimeOffset.UtcNow,
                    level,
                    eventName,
                    fields,
                });
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    private string CurrentPath()
    {
        var date = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        for (var index = 0; index < 1000; index++)
        {
            var suffix = index == 0 ? string.Empty : $"-{index:000}";
            var path = Path.Combine(paths.LogDirectory, $"{date}{suffix}.jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length < 10_000_000) return path;
        }

        return Path.Combine(paths.LogDirectory, $"{date}-overflow.jsonl");
    }

    private static void TryDelete(FileInfo file)
    {
        try { file.Delete(); }
        catch { }
    }
}
