using HyperTool.Models;

namespace HyperTool.Services;

public interface IHyperVService
{
    Task<IReadOnlyList<HyperVVmInfo>> GetVmsAsync(CancellationToken cancellationToken);

    Task StartVmAsync(string vmName, CancellationToken cancellationToken);

    Task StopVmGracefulAsync(string vmName, CancellationToken cancellationToken);

    Task TurnOffVmAsync(string vmName, CancellationToken cancellationToken);

    Task RestartVmAsync(string vmName, CancellationToken cancellationToken);

    Task<IReadOnlyList<HyperVSwitchInfo>> GetVmSwitchesAsync(CancellationToken cancellationToken);

    Task ConnectVmNetworkAdapterAsync(string vmName, string switchName, CancellationToken cancellationToken);

    Task DisconnectVmNetworkAdapterAsync(string vmName, CancellationToken cancellationToken);

    Task<IReadOnlyList<HyperVCheckpointInfo>> GetCheckpointsAsync(string vmName, CancellationToken cancellationToken);

    Task CreateCheckpointAsync(string vmName, string checkpointName, string? description, CancellationToken cancellationToken);

    Task ApplyCheckpointAsync(string vmName, string checkpointName, CancellationToken cancellationToken);

    Task RemoveCheckpointAsync(string vmName, string checkpointName, CancellationToken cancellationToken);

    Task OpenVmConnectAsync(string vmName, string computerName, CancellationToken cancellationToken);
}