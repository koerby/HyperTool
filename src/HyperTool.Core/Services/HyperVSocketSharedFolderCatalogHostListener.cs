using HyperTool.Models;
using Microsoft.Win32;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketSharedFolderCatalogHostListener : IDisposable
{
    private readonly Guid _serviceId;
    private readonly Func<IReadOnlyList<HostSharedFolderDefinition>> _catalogProvider;
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    private static readonly JsonSerializerOptions CatalogSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HyperVSocketSharedFolderCatalogHostListener(
        Func<IReadOnlyList<HostSharedFolderDefinition>> catalogProvider,
        Guid? serviceId = null)
    {
        _catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.SharedFolderCatalogServiceId;
    }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (!EnsureServiceGuidRegistration())
        {
            throw new InvalidOperationException(
                "Hyper-V Socket Shared-Folder-Dienst ist nicht registriert. Starte HyperTool Host als Administrator, um den Dienst einmalig zu registrieren.");
        }

        var listener = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        listener.Bind(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdWildcard, _serviceId));
        listener.Listen(16);

        _listener = listener;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
    }

    private bool EnsureServiceGuidRegistration()
    {
        if (IsServiceGuidRegistered())
        {
            return true;
        }

        if (TryRegisterServiceGuid())
        {
            return true;
        }

        return IsServiceGuidRegistered();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? socket = null;
            try
            {
                if (_listener is null)
                {
                    break;
                }

                socket = await _listener.AcceptAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(socket, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                socket?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(Socket socket, CancellationToken cancellationToken)
    {
        await using var stream = new NetworkStream(socket, ownsSocket: true);

        IReadOnlyList<HostSharedFolderDefinition> catalog;
        try
        {
            catalog = _catalogProvider() ?? [];
        }
        catch
        {
            catalog = [];
        }

        var payload = JsonSerializer.Serialize(catalog.Select(item => new HostSharedFolderDefinition
        {
            Id = item.Id,
            Label = item.Label,
            LocalPath = item.LocalPath,
            ShareName = item.ShareName,
            Enabled = item.Enabled,
            ReadOnly = item.ReadOnly
        }).ToList(), CatalogSerializerOptions);

        await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: false)
        {
            NewLine = "\n"
        };

        await writer.WriteLineAsync(payload.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private bool TryRegisterServiceGuid()
    {
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";

            using var rootKey = Registry.LocalMachine.CreateSubKey(rootPath, writable: true);
            if (rootKey is null)
            {
                return false;
            }

            using var serviceKey = rootKey.CreateSubKey(_serviceId.ToString("D"), writable: true);
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket Shared Folder Catalog", RegistryValueKind.String);
            return serviceKey is not null;
        }
        catch
        {
            return false;
        }
    }

    private bool IsServiceGuidRegistered()
    {
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";

            using var rootKey = Registry.LocalMachine.OpenSubKey(rootPath, writable: false);
            if (rootKey is null)
            {
                return false;
            }

            using var serviceKey = rootKey.OpenSubKey(_serviceId.ToString("D"), writable: false);
            return serviceKey is not null;
        }
        catch
        {
            return false;
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
