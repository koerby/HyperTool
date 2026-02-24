using Serilog;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HyperTool.Models;

namespace HyperTool.Services;

public sealed class TrayService : ITrayService
{
    private NotifyIcon? _notifyIcon;

    public void Initialize(
        Action showAction,
        Action hideAction,
        Func<IReadOnlyList<VmDefinition>> getVms,
        Func<IReadOnlyList<HyperVSwitchInfo>> getSwitches,
        Func<Task> refreshTrayDataAction,
        Func<string, Task> startVmAction,
        Func<string, Task> stopVmAction,
        Func<string, Task> openConsoleAction,
        Func<string, Task> createSnapshotAction,
        Func<string, string, Task> connectVmToSwitchAction,
        Action exitAction)
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Opening += async (_, _) =>
        {
            try
            {
                await refreshTrayDataAction();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Tray data refresh failed.");
            }

            BuildMenu(
                contextMenu,
                showAction,
                hideAction,
                getVms,
                getSwitches,
                startVmAction,
                stopVmAction,
                openConsoleAction,
                createSnapshotAction,
                connectVmToSwitchAction,
                refreshTrayDataAction,
                exitAction);
        };

        BuildMenu(
            contextMenu,
            showAction,
            hideAction,
            getVms,
            getSwitches,
            startVmAction,
            stopVmAction,
            openConsoleAction,
            createSnapshotAction,
            connectVmToSwitchAction,
            refreshTrayDataAction,
            exitAction);

        _notifyIcon = new NotifyIcon
        {
            Icon = ResolveTrayIcon(),
            Text = "HyperTool",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => showAction();
        Log.Information("Tray icon initialized.");
    }

    private static void BuildMenu(
        ContextMenuStrip contextMenu,
        Action showAction,
        Action hideAction,
        Func<IReadOnlyList<VmDefinition>> getVms,
        Func<IReadOnlyList<HyperVSwitchInfo>> getSwitches,
        Func<string, Task> startVmAction,
        Func<string, Task> stopVmAction,
        Func<string, Task> openConsoleAction,
        Func<string, Task> createSnapshotAction,
        Func<string, string, Task> connectVmToSwitchAction,
        Func<Task> refreshTrayDataAction,
        Action exitAction)
    {
        contextMenu.Items.Clear();
        contextMenu.Items.Add("Show", null, (_, _) => showAction());
        contextMenu.Items.Add("Hide", null, (_, _) => hideAction());
        contextMenu.Items.Add(new ToolStripSeparator());

        var vms = getVms();
        var switches = getSwitches();

        contextMenu.Items.Add(BuildVmActionsMenu(vms, startVmAction, stopVmAction, openConsoleAction, createSnapshotAction));
        contextMenu.Items.Add(BuildSwitchActionMenu(vms, switches, connectVmToSwitchAction));

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Daten aktualisieren", null, async (_, _) =>
        {
            try
            {
                await refreshTrayDataAction();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Manual tray data refresh failed.");
            }
        });

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => exitAction());
    }

    private static ToolStripMenuItem BuildVmActionsMenu(
        IReadOnlyList<VmDefinition> vms,
        Func<string, Task> startAction,
        Func<string, Task> stopAction,
        Func<string, Task> openConsoleAction,
        Func<string, Task> snapshotAction)
    {
        var menu = new ToolStripMenuItem("VM Aktionen");

        if (vms.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("Keine VM verfügbar") { Enabled = false });
            return menu;
        }

        foreach (var vm in vms)
        {
            var vmName = vm.Name;
            var vmLabel = string.IsNullOrWhiteSpace(vm.Label) ? vmName : vm.Label;
            var vmMenu = new ToolStripMenuItem(vmLabel);

            vmMenu.DropDownItems.Add("Start", null, async (_, _) => await startAction(vmName));
            vmMenu.DropDownItems.Add("Stop", null, async (_, _) => await stopAction(vmName));
            vmMenu.DropDownItems.Add("Konsole öffnen", null, async (_, _) => await openConsoleAction(vmName));
            vmMenu.DropDownItems.Add("Snapshot erstellen", null, async (_, _) => await snapshotAction(vmName));

            menu.DropDownItems.Add(vmMenu);
        }

        return menu;
    }

    private static ToolStripMenuItem BuildSwitchActionMenu(
        IReadOnlyList<VmDefinition> vms,
        IReadOnlyList<HyperVSwitchInfo> switches,
        Func<string, string, Task> connectAction)
    {
        var menu = new ToolStripMenuItem("Switch umstellen");

        if (vms.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("Keine VM verfügbar") { Enabled = false });
            return menu;
        }

        if (switches.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("Keine Switches verfügbar") { Enabled = false });
            return menu;
        }

        foreach (var vm in vms)
        {
            var vmName = vm.Name;
            var vmLabel = string.IsNullOrWhiteSpace(vm.Label) ? vmName : vm.Label;
            var vmMenu = new ToolStripMenuItem(vmLabel);

            foreach (var vmSwitch in switches)
            {
                var switchName = vmSwitch.Name;
                vmMenu.DropDownItems.Add(switchName, null, async (_, _) => await connectAction(vmName, switchName));
            }

            menu.DropDownItems.Add(vmMenu);
        }

        return menu;
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

    private static Icon ResolveTrayIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var extracted = Icon.ExtractAssociatedIcon(processPath);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
        }

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HyperTool.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }
}