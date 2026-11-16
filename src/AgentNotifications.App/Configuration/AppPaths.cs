using Windows.Storage;

namespace AgentNotifications.App.Configuration;

public sealed record AppPaths(string DataDirectory, string SettingsPath, string SecretsPath, string DatabasePath, string LogDirectory)
{
    public string CliInboxDirectory => Path.Combine(DataDirectory, CliIntegrationDefaults.InboxFolder);

    public static AppPaths Resolve()
    {
        string root;
        try
        {
            root = ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentNotifications");
        }

        return new AppPaths(
            root,
            Path.Combine(root, "config.json"),
            Path.Combine(root, "secrets.dat"),
            Path.Combine(root, "deliveries.db"),
            Path.Combine(root, "logs"));
    }
}
