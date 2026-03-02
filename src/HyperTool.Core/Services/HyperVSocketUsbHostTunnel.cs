using Microsoft.Win32;
using System.Net;
using System.Net.Sockets;

namespace HyperTool.Services;

public sealed class HyperVSocketUsbHostTunnel : IDisposable
{
    private readonly Guid _serviceId;
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public HyperVSocketUsbHostTunnel(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.ServiceId;
    }

    public bool IsRunning { get; private set; }

    public static bool IsServiceRegistered(Guid? serviceId = null)
    {
        var id = serviceId ?? HyperVSocketUsbTunnelDefaults.ServiceId;
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";
            using var rootKey = Registry.LocalMachine.OpenSubKey(rootPath, writable: false);
            if (rootKey is null)
            {
                return false;
            }

            using var serviceKey = rootKey.OpenSubKey(id.ToString("D"), writable: false);
            if (serviceKey is null)
            {
                return false;
            }

            var elementName = serviceKey.GetValue("ElementName") as string;
            return !string.IsNullOrWhiteSpace(elementName);
        }
        catch
        {
            return false;
        }
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        TryRegisterServiceGuid();

        var listener = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        listener.Bind(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdWildcard, _serviceId));
        listener.Listen(64);

        _listener = listener;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? hyperVClient = null;
            try
            {
                if (_listener is null)
                {
                    break;
                }

                hyperVClient = await _listener.AcceptAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(hyperVClient, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                hyperVClient?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private static async Task HandleClientAsync(Socket hyperVClient, CancellationToken cancellationToken)
    {
        await using var hyperVStream = new NetworkStream(hyperVClient, ownsSocket: true);
        using var tcpClient = new TcpClient();

        await tcpClient.ConnectAsync(IPAddress.Loopback, HyperVSocketUsbTunnelDefaults.UsbIpTcpPort, cancellationToken);
        await using var tcpStream = tcpClient.GetStream();

        var toTcpTask = hyperVStream.CopyToAsync(tcpStream, cancellationToken);
        var fromTcpTask = tcpStream.CopyToAsync(hyperVStream, cancellationToken);

        await Task.WhenAll(toTcpTask, fromTcpTask);
    }

    private void TryRegisterServiceGuid()
    {
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";

            using var rootKey = Registry.LocalMachine.CreateSubKey(rootPath, writable: true);
            if (rootKey is null)
            {
                return;
            }

            using var serviceKey = rootKey.CreateSubKey(_serviceId.ToString("D"), writable: true);
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket USB Tunnel", RegistryValueKind.String);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;

        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _listener?.Dispose();
        }
        catch
        {
        }

        try
        {
            _acceptLoopTask?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch
        {
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _acceptLoopTask = null;
    }
}