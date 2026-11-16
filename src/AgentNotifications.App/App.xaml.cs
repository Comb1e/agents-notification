using AgentNotifications.App.Configuration;
using AgentNotifications.App.Infrastructure;
using AgentNotifications.App.Notifications;
using AgentNotifications.App.Services;
using System.Windows;
using System.Windows.Threading;

namespace AgentNotifications.App;

public partial class App : System.Windows.Application
{
    private AppController? controller;
    private TrayService? tray;
    private MainWindow? settingsWindow;
    private SingleInstanceCoordinator? singleInstance;
    private bool exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        var grokCompletion = e.Args.Contains(CliIntegrationDefaults.GrokCompletionArgument, StringComparer.Ordinal);
        try
        {
            var openSettingsRequested = false;
            var paths = AppPaths.Resolve();
            if (grokCompletion)
                await new CliCompletionInbox(paths).WriteAsync(CliCompletionInbox.CaptureGrokEnvironment());
            singleInstance = new SingleInstanceCoordinator(sharedDirectory: paths.DataDirectory);
            if (!singleInstance.IsPrimary)
            {
                var signaled = grokCompletion || await singleInstance.SignalPrimaryAsync();
                await singleInstance.DisposeAsync();
                singleInstance = null;
                if (!signaled)
                    System.Windows.MessageBox.Show("Agent Notifications is already running but could not open Settings.", "Agent Notifications", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(signaled ? 0 : 1);
                return;
            }

            singleInstance.Start(() => Dispatcher.BeginInvoke(() =>
            {
                if (settingsWindow is null) openSettingsRequested = true;
                else settingsWindow.ShowSettings();
            }));
            controller = await AppController.CreateAsync();
            settingsWindow = new MainWindow(controller);
            MainWindow = settingsWindow;
            tray = new TrayService();
            tray.OpenSettingsRequested += (_, _) => Dispatcher.Invoke(() => settingsWindow.ShowSettings());
            tray.TogglePauseRequested += async (_, _) => await ToggleMonitoringAsync();
            tray.SendTestRequested += async (_, _) => await SendTestAsync();
            tray.ToggleStartupRequested += async (_, _) => await ToggleStartupAsync();
            tray.ExitRequested += async (_, _) => await ExitAsync();
            controller.StateChanged += (_, _) => Dispatcher.Invoke(UpdateTray);
            controller.CountsChanged += (_, _) => Dispatcher.Invoke(UpdateTray);
            await controller.InitializeAsync();
            UpdateTray();
            if (openSettingsRequested || !controller.Settings.IsConfigured || string.IsNullOrWhiteSpace(controller.Secret))
                settingsWindow.ShowSettings();
        }
        catch (Exception exception)
        {
            if (!grokCompletion)
                System.Windows.MessageBox.Show(exception.Message, "Agent Notifications", MessageBoxButton.OK, MessageBoxImage.Error);
            if (singleInstance is not null) await singleInstance.DisposeAsync();
            singleInstance = null;
            Shutdown(1);
        }
    }

    private async Task ToggleMonitoringAsync()
    {
        if (controller is null) return;
        try
        {
            var enabled = controller.ListenerState == Domain.ListenerState.Paused;
            await controller.SetMonitoringEnabledAsync(enabled);
            UpdateTray();
        }
        catch (Exception exception)
        {
            tray?.Notify("Agent Notifications", exception.Message, System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private async Task SendTestAsync()
    {
        if (controller is null) return;
        try
        {
            await controller.TestFeishuAsync(controller.Settings, controller.Secret);
            tray?.Notify("Agent Notifications", "Test notification delivered");
        }
        catch (Exception exception)
        {
            tray?.Notify("Agent Notifications", exception.Message, System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private async Task ToggleStartupAsync()
    {
        if (controller is null) return;
        try
        {
            var status = await controller.SetStartWithWindowsAsync(!controller.Settings.StartWithWindows);
            UpdateTray();
            tray?.Notify("Agent Notifications", $"Start with Windows: {status}");
        }
        catch (Exception exception)
        {
            tray?.Notify("Agent Notifications", exception.Message, System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        settingsWindow?.PermitClose();
        tray?.Dispose();
        tray = null;
        if (controller is not null) await controller.DisposeAsync();
        controller = null;
        if (singleInstance is not null) await singleInstance.DisposeAsync();
        singleInstance = null;
        settingsWindow?.Close();
        Shutdown();
    }

    private void UpdateTray()
    {
        if (controller is null || tray is null) return;
        tray.Update(controller.ListenerState, controller.Settings.StartWithWindows, controller.Counts);
        settingsWindow?.RefreshStatus();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        tray?.Notify("Agent Notifications", "Unexpected application error", System.Windows.Forms.ToolTipIcon.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        tray?.Notify("Agent Notifications", "Unexpected application error", System.Windows.Forms.ToolTipIcon.Error);
    }
}
