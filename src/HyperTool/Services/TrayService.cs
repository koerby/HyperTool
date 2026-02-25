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
        Func<Task> reloadConfigAction,
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
                reloadConfigAction,
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
            reloadConfigAction,
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
        Func<Task> reloadConfigAction,
        Action exitAction)
    {
        contextMenu.Items.Clear();
        contextMenu.Items.Add("Show", null, (_, _) => ExecuteMenuAction(contextMenu, () =>
        {
            showAction();
            return Task.CompletedTask;
        }, "show"));
        contextMenu.Items.Add("Hide", null, (_, _) => ExecuteMenuAction(contextMenu, () =>
        {
            hideAction();
            return Task.CompletedTask;
        }, "hide"));
        contextMenu.Items.Add(new ToolStripSeparator());

        var vms = getVms();
        var switches = getSwitches();

        contextMenu.Items.Add(BuildVmActionsMenu(contextMenu, vms, startVmAction, stopVmAction, openConsoleAction, createSnapshotAction));
        contextMenu.Items.Add(BuildSwitchActionMenu(contextMenu, vms, switches, connectVmToSwitchAction));

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Aktualisieren", null, (_, _) => ExecuteMenuAction(contextMenu, reloadConfigAction, "reload"));

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExecuteMenuAction(contextMenu, () =>
        {
            exitAction();
            return Task.CompletedTask;
        }, "exit"));
    }

    private static ToolStripMenuItem BuildVmActionsMenu(
        ContextMenuStrip contextMenu,
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

            vmMenu.DropDownItems.Add("Start", null, (_, _) => ExecuteMenuAction(contextMenu, () => startAction(vmName), $"start-{vmName}"));
            vmMenu.DropDownItems.Add("Stop", null, (_, _) => ExecuteMenuAction(contextMenu, () => stopAction(vmName), $"stop-{vmName}"));
            vmMenu.DropDownItems.Add("Konsole öffnen", null, (_, _) => ExecuteMenuAction(contextMenu, () => openConsoleAction(vmName), $"console-{vmName}"));
            vmMenu.DropDownItems.Add("Snapshot erstellen", null, (_, _) => ExecuteMenuAction(contextMenu, () => snapshotAction(vmName), $"snapshot-{vmName}"));

            menu.DropDownItems.Add(vmMenu);
        }

        return menu;
    }

    private static ToolStripMenuItem BuildSwitchActionMenu(
        ContextMenuStrip contextMenu,
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
                vmMenu.DropDownItems.Add(switchName, null, (_, _) => ExecuteMenuAction(contextMenu, () => connectAction(vmName, switchName), $"connect-{vmName}-{switchName}"));
            }

            menu.DropDownItems.Add(vmMenu);
        }

        return menu;
    }

    private static async void ExecuteMenuAction(ContextMenuStrip contextMenu, Func<Task> action, string actionName)
    {
        try
        {
            contextMenu.Close();
            await action();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tray menu action failed: {ActionName}", actionName);
        }
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