using HyperTool.Models;

namespace HyperTool.Services;

public interface ITrayService : IDisposable
{
    void Initialize(
        Action showAction,
        Action hideAction,
    Func<IReadOnlyList<VmDefinition>> getVms,
    Func<IReadOnlyList<HyperVSwitchInfo>> getSwitches,
    Func<Task> refreshTrayDataAction,
    Func<string, Task> startVmAction,
    Func<string, Task> stopVmAction,
    Func<string, Task> createSnapshotAction,
    Func<string, string, Task> connectVmToSwitchAction,
        Action exitAction);
}