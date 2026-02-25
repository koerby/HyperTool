using HyperTool.Models;
using Serilog;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HyperTool.Services;

public sealed class TrayService : ITrayService
{
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;

    private Action? _showAction;
    private Action? _hideAction;
    private Func<IReadOnlyList<VmDefinition>>? _getVms;
    private Func<IReadOnlyList<HyperVSwitchInfo>>? _getSwitches;
    private Func<(string VmName, string CurrentSwitchName, bool IsConnected)>? _getSwitchTargetContext;
    private Func<Task>? _refreshTrayDataAction;
    private Func<Task>? _reloadConfigAction;
    private Func<string, Task>? _startVmAction;
    private Func<string, Task>? _stopVmAction;
    private Func<string, Task>? _openConsoleAction;
    private Func<string, Task>? _createSnapshotAction;
    private Func<string, string, Task>? _connectVmToSwitchAction;
    private Action? _exitAction;
    private EventHandler? _trayStateChangedHandler;
    private Action<EventHandler>? _unsubscribeTrayStateChanged;

    public void Initialize(
        Action showAction,
        Action hideAction,
        Func<IReadOnlyList<VmDefinition>> getVms,
        Func<IReadOnlyList<HyperVSwitchInfo>> getSwitches,
        Func<(string VmName, string CurrentSwitchName, bool IsConnected)> getSwitchTargetContext,
        Func<Task> refreshTrayDataAction,
        Action<EventHandler> subscribeTrayStateChanged,
        Action<EventHandler> unsubscribeTrayStateChanged,
        Func<Task> reloadConfigAction,
        Func<string, Task> startVmAction,
        Func<string, Task> stopVmAction,
        Func<string, Task> openConsoleAction,
        Func<string, Task> createSnapshotAction,
        Func<string, string, Task> connectVmToSwitchAction,
        Action exitAction)
    {
        _showAction = showAction;
        _hideAction = hideAction;
        _getVms = getVms;
        _getSwitches = getSwitches;
        _getSwitchTargetContext = getSwitchTargetContext;
        _refreshTrayDataAction = refreshTrayDataAction;
        _reloadConfigAction = reloadConfigAction;
        _startVmAction = startVmAction;
        _stopVmAction = stopVmAction;
        _openConsoleAction = openConsoleAction;
        _createSnapshotAction = createSnapshotAction;
        _connectVmToSwitchAction = connectVmToSwitchAction;
        _exitAction = exitAction;
        _unsubscribeTrayStateChanged = unsubscribeTrayStateChanged;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Opening += async (_, _) =>
        {
            try
            {
                if (_refreshTrayDataAction is not null)
                {
                    await _refreshTrayDataAction();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Tray data refresh failed.");
            }

            UpdateTrayMenu();
        };

        _trayStateChangedHandler = (_, _) => UpdateTrayMenuThreadSafe();
        subscribeTrayStateChanged(_trayStateChangedHandler);

        UpdateTrayMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = ResolveTrayIcon(),
            Text = "HyperTool",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => _showAction?.Invoke();
        Log.Information("Tray icon initialized.");
    }

    public void UpdateTrayMenu()
    {
        if (_contextMenu is null
            || _showAction is null
            || _hideAction is null
            || _getVms is null
            || _getSwitches is null
            || _getSwitchTargetContext is null
            || _reloadConfigAction is null
            || _startVmAction is null
            || _stopVmAction is null
            || _openConsoleAction is null
            || _createSnapshotAction is null
            || _connectVmToSwitchAction is null
            || _exitAction is null)
        {
            return;
        }

        _contextMenu.Items.Clear();

        _contextMenu.Items.Add("Show", null, (_, _) => ExecuteMenuAction(() =>
        {
            _showAction();
            return Task.CompletedTask;
        }, "show"));

        _contextMenu.Items.Add("Hide", null, (_, _) => ExecuteMenuAction(() =>
        {
            _hideAction();
            return Task.CompletedTask;
        }, "hide"));

        _contextMenu.Items.Add(new ToolStripSeparator());

        var targetContext = _getSwitchTargetContext();
        var statusText = $"VM: {targetContext.VmName} | Switch: {targetContext.CurrentSwitchName}";
        _contextMenu.Items.Add(new ToolStripMenuItem(statusText)
        {
            Enabled = false
        });

        var vms = _getVms();
        var switches = _getSwitches();

        _contextMenu.Items.Add(BuildVmActionsMenu(vms));
        _contextMenu.Items.Add(BuildSwitchActionMenu(vms, switches, targetContext));

        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Aktualisieren", null, (_, _) => ExecuteMenuAction(_reloadConfigAction, "reload"));

        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, (_, _) => ExecuteMenuAction(() =>
        {
            _exitAction();
            return Task.CompletedTask;
        }, "exit"));
    }

    private ToolStripMenuItem BuildVmActionsMenu(IReadOnlyList<VmDefinition> vms)
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

            vmMenu.DropDownItems.Add("Start", null, (_, _) => ExecuteMenuAction(() => _startVmAction!(vmName), $"start-{vmName}"));
            vmMenu.DropDownItems.Add("Stop", null, (_, _) => ExecuteMenuAction(() => _stopVmAction!(vmName), $"stop-{vmName}"));
            vmMenu.DropDownItems.Add("Konsole öffnen", null, (_, _) => ExecuteMenuAction(() => _openConsoleAction!(vmName), $"console-{vmName}"));
            vmMenu.DropDownItems.Add("Snapshot erstellen", null, (_, _) => ExecuteMenuAction(() => _createSnapshotAction!(vmName), $"snapshot-{vmName}"));

            menu.DropDownItems.Add(vmMenu);
        }

        return menu;
    }

    private ToolStripMenuItem BuildSwitchActionMenu(
        IReadOnlyList<VmDefinition> vms,
        IReadOnlyList<HyperVSwitchInfo> switches,
        (string VmName, string CurrentSwitchName, bool IsConnected) targetContext)
    {
        var menu = new ToolStripMenuItem("Switch umstellen");
        menu.DropDownItems.Add(new ToolStripMenuItem("Steuerung: Default VM aus Config") { Enabled = false });
        menu.DropDownItems.Add(new ToolStripSeparator());

        if (vms.Count == 0 || string.IsNullOrWhiteSpace(targetContext.VmName) || targetContext.VmName == "-")
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("Keine VM verfügbar") { Enabled = false });
            return menu;
        }

        if (switches.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("Keine Switches verfügbar") { Enabled = false });
            return menu;
        }

        var currentSwitchInList = switches.Any(vmSwitch =>
            string.Equals(vmSwitch.Name, targetContext.CurrentSwitchName, StringComparison.OrdinalIgnoreCase));

        foreach (var vmSwitch in switches)
        {
            var switchName = vmSwitch.Name;
            var item = new ToolStripMenuItem(switchName)
            {
                CheckOnClick = true,
                Checked = targetContext.IsConnected
                          && string.Equals(targetContext.CurrentSwitchName, switchName, StringComparison.OrdinalIgnoreCase)
            };

            item.Click += (_, _) => ExecuteMenuAction(async () =>
            {
                await _connectVmToSwitchAction!(targetContext.VmName, switchName);
                if (_refreshTrayDataAction is not null)
                {
                    await _refreshTrayDataAction();
                }

                UpdateTrayMenu();
            }, $"connect-{targetContext.VmName}-{switchName}");

            menu.DropDownItems.Add(item);
        }

        if (!targetContext.IsConnected)
        {
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(new ToolStripMenuItem("Nicht verbunden")
            {
                Enabled = false,
                CheckOnClick = true,
                Checked = true
            });
        }
        else if (!currentSwitchInList)
        {
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(new ToolStripMenuItem($"Aktiver Switch fehlt: {targetContext.CurrentSwitchName}")
            {
                Enabled = false,
                CheckOnClick = true,
                Checked = true
            });
        }

        return menu;
    }

    private async void ExecuteMenuAction(Func<Task> action, string actionName)
    {
        if (_contextMenu is null)
        {
            return;
        }

        try
        {
            _contextMenu.Close();
            await action();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tray menu action failed: {ActionName}", actionName);
        }
    }

    public void Dispose()
    {
        if (_trayStateChangedHandler is not null)
        {
            _unsubscribeTrayStateChanged?.Invoke(_trayStateChangedHandler);
            _trayStateChangedHandler = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_contextMenu is not null)
        {
            _contextMenu.Dispose();
            _contextMenu = null;
        }
    }

    private void UpdateTrayMenuThreadSafe()
    {
        if (_contextMenu is null)
        {
            return;
        }

        if (_contextMenu.IsHandleCreated)
        {
            _contextMenu.BeginInvoke(new Action(UpdateTrayMenu));
            return;
        }

        UpdateTrayMenu();
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
