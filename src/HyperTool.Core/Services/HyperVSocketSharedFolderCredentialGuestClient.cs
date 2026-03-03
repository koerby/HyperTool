using HyperTool.Models;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketSharedFolderCredentialGuestClient
{
    private readonly Guid _serviceId;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HyperVSocketSharedFolderCredentialGuestClient(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.SharedFolderCredentialServiceId;
    }

    public async Task<HostSharedFolderGuestCredential?> FetchCredentialAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(6000));

        using var socket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        linkedCts.Token.ThrowIfCancellationRequested();
        socket.Connect(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId));

        await using var stream = new NetworkStream(socket, ownsSocket: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);

        var payloadText = await reader.ReadLineAsync(linkedCts.Token);
        if (string.IsNullOrWhiteSpace(payloadText))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<HostSharedFolderGuestCredential>(payloadText, SerializerOptions);
        if (payload is null || !payload.Available)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(payload.Username)
            || string.IsNullOrWhiteSpace(payload.Password))
        {
            return null;
        }

        return new HostSharedFolderGuestCredential
        {
            Available = true,
            Username = payload.Username.Trim(),
            Password = payload.Password,
            GroupName = (payload.GroupName ?? string.Empty).Trim(),
            GroupPrincipal = (payload.GroupPrincipal ?? string.Empty).Trim(),
            HostName = (payload.HostName ?? string.Empty).Trim(),
            Source = string.IsNullOrWhiteSpace(payload.Source) ? "hyperv-socket" : payload.Source.Trim()
        };
    }
}
