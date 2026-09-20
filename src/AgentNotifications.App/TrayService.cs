using AgentNotifications.App.Domain;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgentNotifications.App;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly Icon? customIcon;
    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem startupItem;

    public TrayService()
    {
        pauseItem = new ToolStripMenuItem("Pause forwarding");
        startupItem = new ToolStripMenuItem("Start with Windows");
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open settings", null, (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(pauseItem);
        menu.Items.Add(new ToolStripMenuItem("Send test", null, (_, _) => SendTestRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));
        pauseItem.Click += (_, _) => TogglePauseRequested?.Invoke(this, EventArgs.Empty);
        startupItem.Click += (_, _) => ToggleStartupRequested?.Invoke(this, EventArgs.Empty);
        customIcon = LoadIcon();
        icon = new NotifyIcon
        {
            Text = "Agent Notifications",
            Icon = customIcon ?? SystemIcons.Information,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenSettingsRequested;

    public event EventHandler? TogglePauseRequested;

    public event EventHandler? SendTestRequested;

    public event EventHandler? ToggleStartupRequested;

    public event EventHandler? ExitRequested;

    public void Update(ListenerState state, bool startWithWindows, DeliveryCounts counts)
    {
        pauseItem.Text = state == ListenerState.Paused ? "Resume forwarding" : "Pause forwarding";
        startupItem.Checked = startWithWindows;
        icon.Text = counts.Failed > 0
            ? $"Agent Notifications - {counts.Failed} failed"
            : $"Agent Notifications - {state}";
    }

    public void Notify(string title, string message, ToolTipIcon kind = ToolTipIcon.Info)
    {
        icon.BalloonTipTitle = title;
        icon.BalloonTipText = message;
        icon.BalloonTipIcon = kind;
        icon.ShowBalloonTip(4_000);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        customIcon?.Dispose();
    }

    private static Icon? LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.png");
        if (!File.Exists(path)) return null;
        try
        {
            using var bitmap = new Bitmap(path);
            var handle = bitmap.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { _ = DestroyIcon(handle); }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
