using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketHostIdentityGuestClient
{
    private readonly Guid _serviceId;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class HostIdentityPayload
    {
        public string HostName { get; set; } = string.Empty;
        public string Fqdn { get; set; } = string.Empty;
    }

    public HyperVSocketHostIdentityGuestClient(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.HostIdentityServiceId;
    }

    public async Task<string?> FetchHostNameAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(2000));

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

        var payload = JsonSerializer.Deserialize<HostIdentityPayload>(payloadText, SerializerOptions);
        if (!string.IsNullOrWhiteSpace(payload?.HostName))
        {
            return payload.HostName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(payload?.Fqdn))
        {
            return payload.Fqdn.Trim();
        }

        return null;
    }
}
