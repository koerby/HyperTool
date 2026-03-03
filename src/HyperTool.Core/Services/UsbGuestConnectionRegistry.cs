using System.Collections.Concurrent;

namespace HyperTool.Services;

public static class UsbGuestConnectionRegistry
{
    private static readonly ConcurrentDictionary<string, string> ConnectedGuestsByBusId = new(StringComparer.OrdinalIgnoreCase);

    public static void UpdateFromDiagnosticsAck(HyperVSocketDiagnosticsAck ack)
    {
        if (ack is null)
        {
            return;
        }

        var busId = (ack.BusId ?? string.Empty).Trim();
        var eventType = (ack.EventType ?? string.Empty).Trim();
        var guestComputerName = (ack.GuestComputerName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(busId))
        {
            return;
        }

        if (string.Equals(eventType, "usb-disconnected", StringComparison.OrdinalIgnoreCase))
        {
            ConnectedGuestsByBusId.TryRemove(busId, out _);
            return;
        }

        if (string.Equals(eventType, "usb-connected", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(guestComputerName))
        {
            ConnectedGuestsByBusId[busId] = guestComputerName;
        }
    }

    public static bool TryGetGuestComputerName(string? busId, out string guestComputerName)
    {
        guestComputerName = string.Empty;

        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        return ConnectedGuestsByBusId.TryGetValue(busId.Trim(), out guestComputerName!);
    }
}
