using System.Net;
using System.Net.Sockets;

namespace HyperTool.Services;

public sealed class HyperVSocketUsbGuestProxy : IDisposable
{
    private readonly Guid _serviceId;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public HyperVSocketUsbGuestProxy(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.ServiceId;
    }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        var listener = new TcpListener(IPAddress.Loopback, HyperVSocketUsbTunnelDefaults.UsbIpTcpPort)
        {
            Server =
            {
                ExclusiveAddressUse = true
            }
        };

        listener.Start(64);

        _listener = listener;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? tcpClient = null;
            try
            {
                if (_listener is null)
                {
                    break;
                }

                tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                tcpClient?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        await using var guestTcpStream = tcpClient.GetStream();
        using var hyperVSocket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);

        cancellationToken.ThrowIfCancellationRequested();
        hyperVSocket.Connect(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId));

        await using var hyperVStream = new NetworkStream(hyperVSocket, ownsSocket: true);

        var toHostTask = guestTcpStream.CopyToAsync(hyperVStream, cancellationToken);
        var fromHostTask = hyperVStream.CopyToAsync(guestTcpStream, cancellationToken);

        await Task.WhenAll(toHostTask, fromHostTask);
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
            _listener?.Stop();
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