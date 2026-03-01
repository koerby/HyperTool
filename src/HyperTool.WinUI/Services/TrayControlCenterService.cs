using HyperTool.Models;
using HyperTool.WinUI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Serilog;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace HyperTool.WinUI.Services;

internal sealed class TrayControlCenterService : ITrayControlCenterService
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly object _syncLock = new();

    private TrayControlCenterWindow? _window;
    private Action? _showMainWindowAction;
    private Action? _hideMainWindowAction;
    private Func<bool>? _isMainWindowVisible;
    private Func<string>? _getUiTheme;
    private Func<IReadOnlyList<VmDefinition>>? _getVms;
    private Func<IReadOnlyList<HyperVSwitchInfo>>? _getSwitches;
    private Func<bool>? _isTrayMenuEnabled;
    private Func<Task>? _refreshTrayDataAction;
    private Func<string, Task>? _startVmAction;
    private Func<string, Task>? _stopVmAction;
    private Func<string, Task>? _restartVmAction;
    private Func<string, Task>? _openConsoleAction;
    private Func<string, Task>? _createSnapshotAction;
    private Func<string, string, Task>? _connectVmToSwitchAction;
    private Action? _exitAction;

    private readonly List<VmDefinition> _vms = [];
    private readonly List<HyperVSwitchInfo> _switches = [];
    private int _selectedVmIndex = -1;
    private string? _selectedSwitchName;
    private bool _isInitialized;
    private bool _isBusy;
    private TrayControlCenterMode _mode = TrayControlCenterMode.Full;

    public TrayControlCenterService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public void Initialize(
        Action showMainWindowAction,
        Action hideMainWindowAction,
        Func<bool> isMainWindowVisible,
        Func<string> getUiTheme,
        Func<IReadOnlyList<VmDefinition>> getVms,
        Func<IReadOnlyList<HyperVSwitchInfo>> getSwitches,
        Func<bool> isTrayMenuEnabled,
        Func<Task> refreshTrayDataAction,
        Func<string, Task> startVmAction,
        Func<string, Task> stopVmAction,
        Func<string, Task> restartVmAction,
        Func<string, Task> openConsoleAction,
        Func<string, Task> createSnapshotAction,
        Func<string, string, Task> connectVmToSwitchAction,
        Action exitAction)
    {
        _showMainWindowAction = showMainWindowAction;
        _hideMainWindowAction = hideMainWindowAction;
        _isMainWindowVisible = isMainWindowVisible;
        _getUiTheme = getUiTheme;
        _getVms = getVms;
        _getSwitches = getSwitches;
        _isTrayMenuEnabled = isTrayMenuEnabled;
        _refreshTrayDataAction = refreshTrayDataAction;
        _startVmAction = startVmAction;
        _stopVmAction = stopVmAction;
        _restartVmAction = restartVmAction;
        _openConsoleAction = openConsoleAction;
        _createSnapshotAction = createSnapshotAction;
        _connectVmToSwitchAction = connectVmToSwitchAction;
        _exitAction = exitAction;
        _isInitialized = true;
    }

    public void ToggleFull()
    {
        if (!_isInitialized)
        {
            return;
        }

        Enqueue(() =>
        {
            _ = ToggleInternalAsync(TrayControlCenterMode.Full);
        });
    }

    public void ToggleCompact()
    {
        if (!_isInitialized)
        {
            return;
        }

        Enqueue(() =>
        {
            _ = ToggleInternalAsync(TrayControlCenterMode.Compact);
        });
    }

    private async Task ToggleInternalAsync(TrayControlCenterMode mode)
    {
        try
        {
            EnsureWindow();
            if (_window is null)
            {
                return;
            }

            if (_window.AppWindow.IsVisible && _mode == mode)
            {
                _window.AppWindow.Hide();
                return;
            }

            _mode = mode;

            await RefreshDataAsync();
            UpdateWindowTheme();
            UpdateWindowView();
            PositionWindowNearTray();

            _window.AppWindow.Show();
            _window.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Tray control center toggle failed.");

            try
            {
                _window?.AppWindow.Hide();
            }
            catch
            {
            }
        }
    }

    public void Hide()
    {
        Enqueue(() =>
        {
            if (_window?.AppWindow.IsVisible == true)
            {
                _window.AppWindow.Hide();
            }
        });
    }

    public void Dispose()
    {
        Enqueue(() =>
        {
            if (_window is not null)
            {
                _window.Close();
                _window = null;
            }
        });
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        var window = new TrayControlCenterWindow();
        _window = window;

        window.CloseRequested += () =>
        {
            try
            {
                if (window.AppWindow.IsVisible)
                {
                    window.AppWindow.Hide();
                }
            }
            catch
            {
            }
        };

        window.PreviousVmRequested += OnPreviousVmRequested;
        window.NextVmRequested += OnNextVmRequested;
        window.StartRequested += () => ExecuteVmAction(_startVmAction, "tray-start");
        window.StopRequested += () => ExecuteVmAction(_stopVmAction, "tray-stop");
        window.RestartRequested += () => ExecuteVmAction(_restartVmAction, "tray-restart");
        window.OpenConsoleRequested += () => ExecuteVmAction(_openConsoleAction, "tray-console");
        window.SnapshotRequested += () => ExecuteVmAction(_createSnapshotAction, "tray-snapshot");
        window.SwitchSelected += OnSwitchSelected;
        window.ToggleVisibilityRequested += OnToggleVisibilityRequested;
        window.ExitRequested += () => _exitAction?.Invoke();
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
    }

    private void OnToggleVisibilityRequested()
    {
        try
        {
            var isVisible = _isMainWindowVisible?.Invoke() ?? true;
            if (isVisible)
            {
                _hideMainWindowAction?.Invoke();
            }
            else
            {
                _showMainWindowAction?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tray compact visibility toggle failed.");
        }

        UpdateWindowView();
    }

    private void OnPreviousVmRequested()
    {
        if (_vms.Count <= 1)
        {
            return;
        }

        _selectedVmIndex = (_selectedVmIndex - 1 + _vms.Count) % _vms.Count;
        SyncSelectedSwitchWithVm();
        UpdateWindowView();
    }

    private void OnNextVmRequested()
    {
        if (_vms.Count <= 1)
        {
            return;
        }

        _selectedVmIndex = (_selectedVmIndex + 1) % _vms.Count;
        SyncSelectedSwitchWithVm();
        UpdateWindowView();
    }

    private void OnSwitchSelected(string switchName)
    {
        _selectedSwitchName = string.IsNullOrWhiteSpace(switchName) ? null : switchName;
        UpdateWindowView();

        if (_isBusy || string.IsNullOrWhiteSpace(_selectedSwitchName) || _connectVmToSwitchAction is null)
        {
            return;
        }

        var vm = GetSelectedVm();
        if (vm is null)
        {
            return;
        }

        var currentSwitch = NormalizeSwitchDisplayName(vm.RuntimeSwitchName);
        if (string.Equals(currentSwitch, _selectedSwitchName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var selectedSwitch = _selectedSwitchName;
        ExecuteAction(async () =>
        {
            await _connectVmToSwitchAction(vm.Name, selectedSwitch);
            await RefreshDataAsync();
            UpdateWindowView();
        }, "tray-switch-select-connect");
    }

    private void ExecuteVmAction(Func<string, Task>? action, string actionName)
    {
        var vm = GetSelectedVm();
        if (vm is null || action is null)
        {
            return;
        }

        ExecuteAction(async () =>
        {
            await action(vm.Name);
            await RefreshDataAsync();
            UpdateWindowView();
        }, actionName);
    }

    private void ExecuteAction(Func<Task> action, string actionName)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        UpdateWindowView();

        _ = ExecuteActionAsync(action, actionName);
    }

    private async Task ExecuteActionAsync(Func<Task> action, string actionName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tray control center action failed: {ActionName}", actionName);
        }
        finally
        {
            Enqueue(() =>
            {
                _isBusy = false;
                UpdateWindowView();
            });
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_refreshTrayDataAction is not null)
        {
            try
            {
                await _refreshTrayDataAction();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Tray control center data refresh failed.");
            }
        }

        var previousVmName = GetSelectedVm()?.Name;

        _vms.Clear();
        _vms.AddRange(_getVms?.Invoke() ?? []);

        _switches.Clear();
        _switches.AddRange((_getSwitches?.Invoke() ?? []).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));

        if (_vms.Count == 0)
        {
            _selectedVmIndex = -1;
            _selectedSwitchName = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousVmName))
        {
            var idx = _vms.FindIndex(vm => string.Equals(vm.Name, previousVmName, StringComparison.OrdinalIgnoreCase));
            _selectedVmIndex = idx >= 0 ? idx : 0;
        }
        else if (_selectedVmIndex < 0 || _selectedVmIndex >= _vms.Count)
        {
            _selectedVmIndex = 0;
        }

        SyncSelectedSwitchWithVm();
    }

    private void SyncSelectedSwitchWithVm()
    {
        var vm = GetSelectedVm();
        if (vm is null)
        {
            _selectedSwitchName = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_selectedSwitchName)
            && _switches.Any(vmSwitch => string.Equals(vmSwitch.Name, _selectedSwitchName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var runtimeSwitch = NormalizeSwitchDisplayName(vm.RuntimeSwitchName);
        var runtimeMatch = _switches.FirstOrDefault(vmSwitch => string.Equals(vmSwitch.Name, runtimeSwitch, StringComparison.OrdinalIgnoreCase));
        _selectedSwitchName = runtimeMatch?.Name ?? _switches.FirstOrDefault()?.Name;
    }

    private void UpdateWindowTheme()
    {
        if (_window is null)
        {
            return;
        }

        var isDark = string.Equals(_getUiTheme?.Invoke(), "Dark", StringComparison.OrdinalIgnoreCase);
        _window.ApplyTheme(isDark);
    }

    private void UpdateWindowView()
    {
        if (_window is null)
        {
            return;
        }

        var trayEnabled = _isTrayMenuEnabled?.Invoke() ?? true;
        var vm = GetSelectedVm();
        var hasVm = vm is not null;
        var runtimeState = vm?.RuntimeState?.Trim() ?? "Unbekannt";
        var runtimeSwitch = NormalizeSwitchDisplayName(vm?.RuntimeSwitchName);

        var state = new TrayControlCenterViewState
        {
            IsCompactMode = _mode == TrayControlCenterMode.Compact,
            HasVm = hasVm,
            CanMoveVm = trayEnabled && _vms.Count > 1,
            CanStart = trayEnabled && hasVm && !IsVmRunning(runtimeState),
            CanStop = trayEnabled && hasVm && IsVmRunning(runtimeState),
            CanRestart = trayEnabled && hasVm,
            SelectedVmDisplay = hasVm ? vm!.DisplayLabel : "Keine VM ausgewählt",
            SelectedVmMeta = hasVm ? $"{runtimeState} · {runtimeSwitch}" : "-",
            ActiveSwitchDisplay = $"Aktiv: {runtimeSwitch}",
            VmIndexDisplay = _vms.Count == 0 ? "0 / 0" : $"{_selectedVmIndex + 1} / {_vms.Count}",
            VisibilityButtonText = (_isMainWindowVisible?.Invoke() ?? true) ? "⌂  Ausblenden" : "⌂  Anzeigen"
        };

        foreach (var vmSwitch in _switches)
        {
            state.Switches.Add(new TraySwitchItem(
                vmSwitch.Name,
                vmSwitch.Name,
                string.Equals(vmSwitch.Name, _selectedSwitchName, StringComparison.OrdinalIgnoreCase),
                trayEnabled && hasVm));
        }

        if (_isBusy)
        {
            state.CanMoveVm = false;
            state.CanStart = false;
            state.CanStop = false;
            state.CanRestart = false;
        }

        _window.UpdateView(state);
    }

    private void PositionWindowNearTray()
    {
        if (_window is null)
        {
            return;
        }

        var popupWidth = GetPopupWidth(_mode);
        var popupHeight = GetPopupHeight(_mode);
        _window.SetPanelSize(popupWidth, popupHeight);

        if (!GetCursorPos(out var cursorPos))
        {
            cursorPos = new NativePoint { X = 0, Y = 0 };
        }

        var displayArea = DisplayArea.GetFromPoint(new PointInt32(cursorPos.X, cursorPos.Y), DisplayAreaFallback.Primary);
        var work = displayArea.WorkArea;
        var bounds = displayArea.OuterBounds;

        var x = cursorPos.X - popupWidth + 24;
        var y = work.Y + work.Height - popupHeight - 8;

        var taskbarAtBottom = work.Y + work.Height < bounds.Y + bounds.Height;
        var taskbarAtTop = work.Y > bounds.Y;
        var taskbarAtLeft = work.X > bounds.X;
        var taskbarAtRight = work.X + work.Width < bounds.X + bounds.Width;

        if (taskbarAtTop)
        {
            y = work.Y + 8;
        }
        else if (taskbarAtBottom)
        {
            y = work.Y + work.Height - popupHeight - 8;
        }

        if (taskbarAtLeft)
        {
            x = work.X + 8;
        }
        else if (taskbarAtRight)
        {
            x = work.X + work.Width - popupWidth - 8;
        }

        x = Math.Clamp(x, work.X + 8, work.X + work.Width - popupWidth - 8);
        y = Math.Clamp(y, work.Y + 8, work.Y + work.Height - popupHeight - 8);

        _window.SetPosition(x, y);
    }

    private VmDefinition? GetSelectedVm()
    {
        if (_selectedVmIndex < 0 || _selectedVmIndex >= _vms.Count)
        {
            return null;
        }

        return _vms[_selectedVmIndex];
    }

    private static bool IsVmRunning(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        return state.Contains("Running", StringComparison.OrdinalIgnoreCase)
               || state.Contains("Wird ausgeführt", StringComparison.OrdinalIgnoreCase)
               || state.Contains("Ausgeführt", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSwitchDisplayName(string? switchName)
    {
        return string.IsNullOrWhiteSpace(switchName)
            ? "Nicht verbunden"
            : switchName.Trim();
    }

    private void Enqueue(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            lock (_syncLock)
            {
                action();
            }
        });
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum TrayControlCenterMode
    {
        Full,
        Compact
    }

    private static int GetPopupWidth(TrayControlCenterMode mode)
    {
        return mode == TrayControlCenterMode.Compact ? 228 : 404;
    }

    private static int GetPopupHeight(TrayControlCenterMode mode)
    {
        return mode == TrayControlCenterMode.Compact ? 196 : 600;
    }
}
