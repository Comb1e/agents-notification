using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Services;
using System.Windows;
using System.Windows.Controls;

namespace AgentNotifications.App;

public partial class MainWindow : Window
{
    private readonly AppController controller;
    private bool allowClose;

    public MainWindow(AppController controller)
    {
        this.controller = controller;
        InitializeComponent();
        LoadSettings();
        controller.StateChanged += (_, _) => Dispatcher.Invoke(RefreshStatus);
        controller.CountsChanged += (_, _) => Dispatcher.Invoke(RefreshStatus);
        controller.StatusChanged += (_, status) => Dispatcher.Invoke(() => FooterStatusText.Text = status);
        Closing += OnClosing;
    }

    public void ShowSettings()
    {
        LoadSettings();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void PermitClose() => allowClose = true;

    public void RefreshStatus()
    {
        StatusText.Text = controller.ListenerState switch
        {
            ListenerState.Watching => "Forwarding notifications",
            ListenerState.Paused => "Forwarding paused",
            ListenerState.PermissionDenied => "Notification access denied",
            ListenerState.AwaitingPermission => "Notification access required",
            ListenerState.Retrying => "Listener retrying",
            ListenerState.Faulted => "Listener faulted",
            _ => controller.ListenerState.ToString(),
        };
        CountsText.Text = $"Pending {controller.Counts.Pending}  |  Failed {controller.Counts.Failed}";
        AccessStatusText.Text = $"Notification access: {controller.NotificationAccess}";
    }

    private void LoadSettings()
    {
        var settings = controller.Settings;
        SelectByTag(DomainCombo, settings.Feishu.Domain);
        AppIdText.Text = settings.Feishu.AppId;
        AppSecretBox.Password = controller.Secret;
        SelectByTag(RecipientTypeCombo, settings.Feishu.RecipientType);
        RecipientText.Text = settings.Feishu.Recipient;
        FeishuHealthText.Text = settings.Feishu.AppId.Length > 0 && settings.Feishu.Recipient.Length > 0
            ? "Configured - not tested"
            : "Not configured";
        ChatGPTCheck.IsChecked = IsSourceEnabled(settings, BuiltInSourceRules.ChatGptName);
        QoderCheck.IsChecked = IsSourceEnabled(settings, BuiltInSourceRules.QoderName);
        ZCodeCheck.IsChecked = IsSourceEnabled(settings, BuiltInSourceRules.ZCodeName);
        CustomRulesText.Text = string.Join(Environment.NewLine, settings.SourceRules
            .Where(rule => !BuiltInSourceRules.IsBuiltInName(rule.Name))
            .Select(rule => $"{rule.Name}|{rule.MatchMode}|{rule.Pattern}"));
        RedactionText.Text = string.Join(Environment.NewLine, settings.RedactionPatterns);
        MonitoringCheck.IsChecked = settings.MonitoringEnabled;
        StartupCheck.IsChecked = settings.StartWithWindows;
        DataDirectoryText.Text = controller.DataDirectory;
        RefreshStatus();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            await controller.SaveAsync(BuildSettings(), AppSecretBox.Password);
            LoadSettings();
        }, "Settings saved");
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        FeishuHealthText.Text = "Testing...";
        var succeeded = await RunUiActionAsync(async () =>
        {
            var messageId = await controller.TestFeishuAsync(BuildSettings(), AppSecretBox.Password);
            FooterStatusText.Text = $"Test delivered: {messageId}";
        }, null);
        FeishuHealthText.Text = succeeded ? "Healthy" : "Connection failed";
    }

    private async void GrantAccessButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            var access = await controller.RequestNotificationAccessAsync();
            FooterStatusText.Text = $"Notification access: {access}";
            RefreshStatus();
        }, null);
    }

    private void RetryFailedButton_Click(object sender, RoutedEventArgs e)
    {
        var count = controller.RetryFailed();
        FooterStatusText.Text = $"Queued {count} failed deliveries";
    }

    private AppSettings BuildSettings()
    {
        var current = controller.Settings;
        return new AppSettings
        {
            Version = 1,
            MonitoringEnabled = MonitoringCheck.IsChecked == true,
            StartWithWindows = StartupCheck.IsChecked == true,
            Feishu = new FeishuSettings
            {
                Domain = SelectedTag(DomainCombo),
                AppId = AppIdText.Text.Trim(),
                RecipientType = SelectedTag(RecipientTypeCombo),
                Recipient = RecipientText.Text.Trim(),
            },
            SourceRules = BuildSourceRules(),
            RedactionPatterns = Lines(RedactionText.Text).ToList(),
            Runtime = current.Runtime,
        };
    }

    private List<SourceRuleSettings> BuildSourceRules()
    {
        var rules = new List<SourceRuleSettings>
        {
            BuiltInSourceRules.CreateChatGpt(ChatGPTCheck.IsChecked == true),
            BuiltInSourceRules.CreateQoder(QoderCheck.IsChecked == true),
            BuiltInSourceRules.CreateZCode(ZCodeCheck.IsChecked == true),
        };
        foreach (var line in Lines(CustomRulesText.Text))
        {
            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || parts[1] is not ("exact" or "regex"))
                throw new InvalidDataException($"Invalid source rule: {line}");
            rules.Add(new SourceRuleSettings { Name = parts[0], MatchMode = parts[1], Pattern = parts[2], Enabled = true });
        }

        return rules;
    }

    private async Task<bool> RunUiActionAsync(Func<Task> action, string? success)
    {
        SaveButton.IsEnabled = false;
        TestButton.IsEnabled = false;
        try
        {
            await action();
            if (success is not null) FooterStatusText.Text = success;
            return true;
        }
        catch (Exception exception)
        {
            FooterStatusText.Text = exception.Message;
            System.Windows.MessageBox.Show(this, exception.Message, "Agent Notifications", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            SaveButton.IsEnabled = true;
            TestButton.IsEnabled = true;
            RefreshStatus();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        Hide();
    }

    private static bool IsSourceEnabled(AppSettings settings, string name) => settings.SourceRules
        .FirstOrDefault(rule => rule.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Enabled == true;

    private static IEnumerable<string> Lines(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => !line.StartsWith('#'));

    private static string SelectedTag(System.Windows.Controls.ComboBox comboBox) => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

    private static void SelectByTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? comboBox.Items[0];
    }
}
