using HyperTool.Models;

namespace HyperTool.Services;

public interface ITrayService : IDisposable
{
    void Initialize(
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
        Action exitAction);

    void UpdateTrayMenu();
}