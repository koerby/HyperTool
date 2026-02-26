using HyperTool.Models;
using Serilog;
using System.ComponentModel;
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
    private Func<bool>? _isTrayMenuEnabled;
    private Func<IReadOnlyList<VmDefinition>>? _getVms;
    private Func<IReadOnlyList<HyperVSwitchInfo>>? _getSwitches;
    private Func<Task>? _refreshTrayDataAction;
    private Func<string, Task>? _startVmAction;
    private Func<string, Task>? _stopVmAction;
    private Func<string, Task>? _openConsoleAction;
    private Func<string, Task>? _createSnapshotAction;
    private Func<string, string, Task>? _connectVmToSwitchAction;
    private Action? _exitAction;
    private EventHandler? _trayStateChangedHandler;
    private Action<EventHandler>? _unsubscribeTrayStateChanged;
    private bool _isContextMenuOpen;
    private bool _hasPendingMenuRefresh;

    public void Initialize(
        Action showAction,
        Action hideAction,
        Func<bool> isTrayMenuEnabled,
        Func<IReadOnlyList<VmDefinition>> getVms,
        Func<IReadOnlyList<HyperVSwitchInfo>> getSwitches,
        Func<Task> refreshTrayDataAction,
        Action<EventHandler> subscribeTrayStateChanged,
        Action<EventHandler> unsubscribeTrayStateChanged,
        Func<string, Task> startVmAction,
        Func<string, Task> stopVmAction,
        Func<string, Task> openConsoleAction,
        Func<string, Task> createSnapshotAction,
        Func<string, string, Task> connectVmToSwitchAction,
        Action exitAction)
    {
        _showAction = showAction;
        _hideAction = hideAction;
        _isTrayMenuEnabled = isTrayMenuEnabled;
        _getVms = getVms;
        _getSwitches = getSwitches;
        _refreshTrayDataAction = refreshTrayDataAction;
        _startVmAction = startVmAction;
        _stopVmAction = stopVmAction;
        _openConsoleAction = openConsoleAction;
        _createSnapshotAction = createSnapshotAction;
        _connectVmToSwitchAction = connectVmToSwitchAction;
        _exitAction = exitAction;
        _unsubscribeTrayStateChanged = unsubscribeTrayStateChanged;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Opening += (_, e) =>
        {
            if (!IsTrayMenuEnabled())
            {
                e.Cancel = true;
                return;
            }

            _isContextMenuOpen = true;
            UpdateTrayMenu();
        };
        _contextMenu.Closed += (_, _) =>
        {
            _isContextMenuOpen = false;
            if (_hasPendingMenuRefresh)
            {
                _hasPendingMenuRefresh = false;
                UpdateTrayMenuThreadSafe();
            }
        };

        _trayStateChangedHandler = (_, _) => UpdateTrayMenuThreadSafe();
        subscribeTrayStateChanged(_trayStateChangedHandler);

        UpdateTrayMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = ResolveTrayIcon(),
            Text = "HyperTool",
            ContextMenuStrip = null,
            Visible = true
        };

        ApplyTrayMenuVisibility();

        _notifyIcon.DoubleClick += (_, _) => _showAction?.Invoke();
        Log.Information("Tray icon initialized.");
    }

    public void UpdateTrayMenu()
    {
        ApplyTrayMenuVisibility();

        if (!IsTrayMenuEnabled())
        {
            return;
        }

        if (_contextMenu is null
            || _showAction is null
            || _hideAction is null
            || _getVms is null
            || _getSwitches is null
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

        var vms = _getVms();
        var switches = _getSwitches();

        _contextMenu.Items.Add(BuildVmActionsMenu(vms));
        _contextMenu.Items.Add(BuildSwitchActionMenu(vms, switches));

        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Aktualisieren", null, (_, _) => ExecuteMenuAction(async () =>
        {
            if (_refreshTrayDataAction is not null)
            {
                await _refreshTrayDataAction();
            }

            UpdateTrayMenuThreadSafe();
        }, "refresh"));

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
        IReadOnlyList<HyperVSwitchInfo> switches)
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
            var vmCurrentSwitch = NormalizeSwitchDisplayName(vm.RuntimeSwitchName);
            var isNotConnected = IsNotConnectedSwitchDisplay(vmCurrentSwitch);
            var currentSwitchInList = switches.Any(vmSwitch =>
                string.Equals(vmSwitch.Name, vmCurrentSwitch, StringComparison.OrdinalIgnoreCase));

            foreach (var vmSwitch in switches)
            {
                var switchName = vmSwitch.Name;
                var item = new ToolStripMenuItem(switchName)
                {
                    CheckOnClick = true,
                    Checked = !isNotConnected
                              && string.Equals(vmCurrentSwitch, switchName, StringComparison.OrdinalIgnoreCase)
                };

                item.Click += (_, _) => ExecuteMenuAction(async () =>
                {
                    await _connectVmToSwitchAction!(vmName, switchName);
                    if (_refreshTrayDataAction is not null)
                    {
                        await _refreshTrayDataAction();
                    }

                    UpdateTrayMenu();
                }, $"connect-{vmName}-{switchName}");

                vmMenu.DropDownItems.Add(item);
            }

            if (isNotConnected)
            {
                vmMenu.DropDownItems.Add(new ToolStripSeparator());
                vmMenu.DropDownItems.Add(new ToolStripMenuItem("Nicht verbunden")
                {
                    Enabled = false,
                    CheckOnClick = true,
                    Checked = true
                });
            }
            else if (!currentSwitchInList)
            {
                vmMenu.DropDownItems.Add(new ToolStripSeparator());
                vmMenu.DropDownItems.Add(new ToolStripMenuItem($"Aktiver Switch fehlt: {vmCurrentSwitch}")
                {
                    Enabled = false,
                    CheckOnClick = true,
                    Checked = true
                });
            }

            menu.DropDownItems.Add(vmMenu);
        }

        return menu;
    }

    private static string NormalizeSwitchDisplayName(string? switchName)
    {
        return string.IsNullOrWhiteSpace(switchName) ? "Nicht verbunden" : switchName.Trim();
    }

    private static bool IsNotConnectedSwitchDisplay(string? switchName)
    {
        return string.IsNullOrWhiteSpace(switchName)
               || string.Equals(switchName, "-", StringComparison.Ordinal)
               || string.Equals(switchName, "Nicht verbunden", StringComparison.OrdinalIgnoreCase);
    }

    private async void ExecuteMenuAction(Func<Task> action, string actionName)
    {
        try
        {
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

        if (_isContextMenuOpen)
        {
            _hasPendingMenuRefresh = true;
            return;
        }

        if (_contextMenu.IsHandleCreated)
        {
            _contextMenu.BeginInvoke(new Action(() =>
            {
                ApplyTrayMenuVisibility();
                UpdateTrayMenu();
            }));
            return;
        }

        ApplyTrayMenuVisibility();
        UpdateTrayMenu();
    }

    private bool IsTrayMenuEnabled()
    {
        return _isTrayMenuEnabled?.Invoke() ?? true;
    }

    private void ApplyTrayMenuVisibility()
    {
        if (_notifyIcon is null || _contextMenu is null)
        {
            return;
        }

        if (IsTrayMenuEnabled())
        {
            if (!ReferenceEquals(_notifyIcon.ContextMenuStrip, _contextMenu))
            {
                _notifyIcon.ContextMenuStrip = _contextMenu;
            }

            return;
        }

        _contextMenu.Items.Clear();
        if (_notifyIcon.ContextMenuStrip is not null)
        {
            _notifyIcon.ContextMenuStrip = null;
        }
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
