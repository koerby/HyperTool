using HyperTool.Models;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketSharedFolderCatalogGuestClient
{
    private readonly Guid _serviceId;

    private static readonly JsonSerializerOptions CatalogSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HyperVSocketSharedFolderCatalogGuestClient(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.SharedFolderCatalogServiceId;
    }

    public async Task<IReadOnlyList<HostSharedFolderDefinition>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(2000));

        using var socket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        linkedCts.Token.ThrowIfCancellationRequested();
        socket.Connect(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId));

        await using var stream = new NetworkStream(socket, ownsSocket: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);

        var payload = await reader.ReadLineAsync(linkedCts.Token);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var parsed = JsonSerializer.Deserialize<List<HostSharedFolderDefinition>>(payload, CatalogSerializerOptions) ?? [];

        return parsed
            .Where(static item => item is not null
                                  && !string.IsNullOrWhiteSpace(item.ShareName)
                                  && !string.IsNullOrWhiteSpace(item.Id))
            .Select(static item => new HostSharedFolderDefinition
            {
                Id = item.Id,
                Label = item.Label,
                LocalPath = item.LocalPath,
                ShareName = item.ShareName,
                Enabled = item.Enabled,
                ReadOnly = item.ReadOnly
            })
            .ToList();
    }
}
