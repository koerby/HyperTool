using HyperTool.Models;

namespace HyperTool.Services;

public interface IHostSharedFolderService
{
    Task EnsureShareAsync(HostSharedFolderDefinition definition, CancellationToken cancellationToken);

    Task RemoveShareAsync(string shareName, CancellationToken cancellationToken);

    Task<bool> ShareExistsAsync(string shareName, CancellationToken cancellationToken);
}
