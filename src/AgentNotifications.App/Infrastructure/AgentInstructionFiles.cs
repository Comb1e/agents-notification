using AgentNotifications.App.Configuration;

namespace AgentNotifications.App.Infrastructure;

public static class AgentInstructionFiles
{
    private static EnumerationOptions DirectEntries => new()
    {
        IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    public static IReadOnlyList<string> Discover(string profilesDirectory, string sourcePath)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Directories(profilesDirectory, "*"))
        foreach (var directory in Directories(profile, ".*"))
        {
            AddFiles(directory);
            // Some agents keep their global configuration in ~/.config/<agent>.
            if (Path.GetFileName(directory).Equals(".config", StringComparison.OrdinalIgnoreCase))
                foreach (var nested in Directories(directory, "*")) AddFiles(nested);
        }
        return targets.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        void AddFiles(string directory)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*", DirectEntries))
                    if (AgentInstructionDefaults.InstructionFileNames.Contains(Path.GetFileName(path))
                        && !path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase)) targets.Add(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string[] Directories(string path, string pattern)
    {
        try { return Directory.GetDirectories(path, pattern, DirectEntries); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }
}
