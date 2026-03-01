using HyperTool.Models;

namespace HyperTool.WinUI.Services;

internal interface ITrayControlCenterService : IDisposable
{
    void Initialize(
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
        Action exitAction);

    void ToggleFull();

    void ToggleCompact();

    void Hide();
}
