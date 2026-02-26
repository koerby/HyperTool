using HyperTool.Models;

namespace HyperTool.Services;

public interface ITrayService : IDisposable
{
    void Initialize(
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
        Action exitAction);

    void UpdateTrayMenu();
}