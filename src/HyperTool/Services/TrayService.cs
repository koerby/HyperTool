using Serilog;
using System.Drawing;
using System.Windows.Forms;

namespace HyperTool.Services;

public sealed class TrayService : ITrayService
{
    private NotifyIcon? _notifyIcon;

    public void Initialize(
        Action showAction,
        Action hideAction,
        Action startDefaultVmAction,
        Action stopDefaultVmAction,
        Action connectDefaultVmAction,
        Action createCheckpointAction,
        Action exitAction)
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (_, _) => showAction());
        contextMenu.Items.Add("Hide", null, (_, _) => hideAction());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Start Default VM", null, (_, _) => startDefaultVmAction());
        contextMenu.Items.Add("Stop Default VM", null, (_, _) => stopDefaultVmAction());
        contextMenu.Items.Add("Connect Default VM to Default Switch", null, (_, _) => connectDefaultVmAction());
        contextMenu.Items.Add("Create Checkpoint (Selected/Default)", null, (_, _) => createCheckpointAction());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => exitAction());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "HyperTool",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => showAction();
        Log.Information("Tray icon initialized.");
    }

    public void Dispose()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }
}