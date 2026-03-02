using System.Net;
using System.Net.Sockets;

namespace HyperTool.Services;

public sealed class HyperVSocketEndPoint(Guid vmId, Guid serviceId) : EndPoint
{
    private const int SockAddrLength = 36;
    private const int FamilyOffset = 0;
    private const int ReservedOffset = 2;
    private const int VmIdOffset = 4;
    private const int ServiceIdOffset = 20;
    private static readonly AddressFamily HyperVAddressFamily = (AddressFamily)34;

    public Guid VmId { get; } = vmId;
    public Guid ServiceId { get; } = serviceId;

    public override AddressFamily AddressFamily => HyperVAddressFamily;

    public override SocketAddress Serialize()
    {
        var socketAddress = new SocketAddress(HyperVAddressFamily, SockAddrLength);
        WriteUInt16(socketAddress, FamilyOffset, (ushort)HyperVAddressFamily);
        WriteUInt16(socketAddress, ReservedOffset, 0);

        var vmIdBytes = VmId.ToByteArray();
        for (var index = 0; index < vmIdBytes.Length; index++)
        {
            socketAddress[VmIdOffset + index] = vmIdBytes[index];
        }

        var serviceIdBytes = ServiceId.ToByteArray();
        for (var index = 0; index < serviceIdBytes.Length; index++)
        {
            socketAddress[ServiceIdOffset + index] = serviceIdBytes[index];
        }

        return socketAddress;
    }

    public override EndPoint Create(SocketAddress socketAddress)
    {
        if (socketAddress.Size < SockAddrLength)
        {
            throw new ArgumentOutOfRangeException(nameof(socketAddress), "SocketAddress ist zu kurz für SOCKADDR_HV.");
        }

        var vmIdBytes = new byte[16];
        for (var index = 0; index < vmIdBytes.Length; index++)
        {
            vmIdBytes[index] = socketAddress[VmIdOffset + index];
        }

        var serviceIdBytes = new byte[16];
        for (var index = 0; index < serviceIdBytes.Length; index++)
        {
            serviceIdBytes[index] = socketAddress[ServiceIdOffset + index];
        }

        return new HyperVSocketEndPoint(new Guid(vmIdBytes), new Guid(serviceIdBytes));
    }

    private static void WriteUInt16(SocketAddress address, int offset, ushort value)
    {
        address[offset] = (byte)(value & 0xFF);
        address[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}