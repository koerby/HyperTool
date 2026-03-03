using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public static class UsbHostDiscoveryService
{
    public const int DiscoveryPort = 32491;
    private const string RequestType = "hypertool-usb-host-discovery-request-v1";
    private const string ResponseType = "hypertool-usb-host-discovery-response-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public sealed class UsbHostDiscoveryResult
    {
        public string HostAddress { get; init; } = string.Empty;
        public string HostComputerName { get; init; } = string.Empty;
    }

    public static async Task<string?> DiscoverHostAddressAsync(string requesterComputerName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await DiscoverHostAsync(requesterComputerName, timeout, cancellationToken);
        var address = (result?.HostAddress ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(address) ? null : address;
    }

    public static async Task<UsbHostDiscoveryResult?> DiscoverHostAsync(string requesterComputerName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true
        };

        var request = new UsbHostDiscoveryRequest
        {
            Type = RequestType,
            RequesterComputerName = string.IsNullOrWhiteSpace(requesterComputerName) ? Environment.MachineName : requesterComputerName.Trim(),
            SentAtUtc = DateTime.UtcNow.ToString("O")
        };

        var payload = JsonSerializer.Serialize(request, JsonOptions);
        var data = Encoding.UTF8.GetBytes(payload);

        await udpClient.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        while (!timeoutCts.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync(timeoutCts.Token);
                var responsePayload = Encoding.UTF8.GetString(result.Buffer);
                if (!TryParseResponse(responsePayload, out var response))
                {
                    continue;
                }

                var hostAddress = (response.HostAddress ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(hostAddress))
                {
                    hostAddress = response.HostAddresses
                    .Select(entry => (entry ?? string.Empty).Trim())
                    .FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry)) ?? string.Empty;
                }

                var hostComputerName = (response.HostComputerName ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(hostAddress) || !string.IsNullOrWhiteSpace(hostComputerName))
                {
                    return new UsbHostDiscoveryResult
                    {
                        HostAddress = hostAddress,
                        HostComputerName = hostComputerName
                    };
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }

        return null;
    }

    public static async Task RunHostResponderAsync(string hostComputerName, Func<IReadOnlyList<string>> getHostAddresses, CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync(cancellationToken);
                var payload = Encoding.UTF8.GetString(result.Buffer);
                if (!TryParseRequest(payload, out _))
                {
                    continue;
                }

                var addresses = getHostAddresses()
                    .Where(entry => !string.IsNullOrWhiteSpace(entry))
                    .Select(entry => entry.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var primaryAddress = SelectBestAddressForRequester(addresses, result.RemoteEndPoint.Address)
                    ?? addresses.FirstOrDefault()
                    ?? string.Empty;

                var response = new UsbHostDiscoveryResponse
                {
                    Type = ResponseType,
                    HostComputerName = string.IsNullOrWhiteSpace(hostComputerName) ? Environment.MachineName : hostComputerName.Trim(),
                    HostAddress = primaryAddress,
                    HostAddresses = addresses,
                    SentAtUtc = DateTime.UtcNow.ToString("O")
                };

                var responseJson = JsonSerializer.Serialize(response, JsonOptions);
                var responseData = Encoding.UTF8.GetBytes(responseJson);
                await udpClient.SendAsync(responseData, responseData.Length, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    public static IReadOnlyList<string> GetLocalIpv4Addresses()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool TryParseRequest(string payload, out UsbHostDiscoveryRequest request)
    {
        request = new UsbHostDiscoveryRequest();

        try
        {
            var parsed = JsonSerializer.Deserialize<UsbHostDiscoveryRequest>(payload, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            if (!string.Equals(parsed.Type, RequestType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            request = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseResponse(string payload, out UsbHostDiscoveryResponse response)
    {
        response = new UsbHostDiscoveryResponse();

        try
        {
            var parsed = JsonSerializer.Deserialize<UsbHostDiscoveryResponse>(payload, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            if (!string.Equals(parsed.Type, ResponseType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            response = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? SelectBestAddressForRequester(IReadOnlyList<string> hostAddresses, IPAddress requesterAddress)
    {
        if (hostAddresses.Count == 0)
        {
            return null;
        }

        if (requesterAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return hostAddresses[0];
        }

        foreach (var candidate in hostAddresses)
        {
            if (!IPAddress.TryParse(candidate, out var hostAddress) || hostAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            if (IsSame24Subnet(hostAddress, requesterAddress))
            {
                return candidate;
            }
        }

        return hostAddresses[0];
    }

    private static bool IsSame24Subnet(IPAddress left, IPAddress right)
    {
        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        if (leftBytes.Length != 4 || rightBytes.Length != 4)
        {
            return false;
        }

        return leftBytes[0] == rightBytes[0]
            && leftBytes[1] == rightBytes[1]
            && leftBytes[2] == rightBytes[2];
    }
}

public sealed class UsbHostDiscoveryRequest
{
    public string Type { get; set; } = string.Empty;

    public string RequesterComputerName { get; set; } = string.Empty;

    public string SentAtUtc { get; set; } = string.Empty;
}

public sealed class UsbHostDiscoveryResponse
{
    public string Type { get; set; } = string.Empty;

    public string HostComputerName { get; set; } = string.Empty;

    public string HostAddress { get; set; } = string.Empty;

    public List<string> HostAddresses { get; set; } = [];

    public string SentAtUtc { get; set; } = string.Empty;
}
