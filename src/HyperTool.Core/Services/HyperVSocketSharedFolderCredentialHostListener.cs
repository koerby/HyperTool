using HyperTool.Models;
using Microsoft.Win32;
using Serilog;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketSharedFolderCredentialHostListener : IDisposable
{
    private readonly Guid _serviceId;
    private readonly Action<DateTimeOffset>? _onCredentialServed;
    private readonly HostSharedFolderCredentialProvisioningService _credentialProvisioningService = new();
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HyperVSocketSharedFolderCredentialHostListener(Guid? serviceId = null, Action<DateTimeOffset>? onCredentialServed = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.SharedFolderCredentialServiceId;
        _onCredentialServed = onCredentialServed;
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
                "Hyper-V Socket SharedFolder-Credential-Dienst ist nicht registriert. Starte HyperTool Host als Administrator, um den Dienst einmalig zu registrieren.");
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

        _credentialProvisioningService.TryGetCredential(out var credential);
        credential ??= new HostSharedFolderGuestCredential();

        if (!credential.Available
            || string.IsNullOrWhiteSpace(credential.Username)
            || string.IsNullOrWhiteSpace(credential.Password))
        {
            try
            {
                credential = await _credentialProvisioningService.EnsureProvisionedAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Shared-folder credential socket request received, but host credential provisioning is unavailable.");
                credential = new HostSharedFolderGuestCredential
                {
                    Available = false,
                    Source = "host-provisioning-unavailable"
                };
            }
        }

        credential.HostName = string.IsNullOrWhiteSpace(credential.HostName)
            ? Environment.MachineName
            : credential.HostName;

        var payload = JsonSerializer.Serialize(credential, SerializerOptions);

        await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: false)
        {
            NewLine = "\n"
        };

        await writer.WriteLineAsync(payload.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);

        var hasValidCredential = credential.Available
                                 && !string.IsNullOrWhiteSpace(credential.Username)
                                 && !string.IsNullOrWhiteSpace(credential.Password);

        try
        {
            if (hasValidCredential)
            {
                _onCredentialServed?.Invoke(DateTimeOffset.UtcNow);
            }
        }
        catch
        {
        }

        if (!hasValidCredential)
        {
            Log.Warning("Shared-folder credential socket served without valid credential payload.");
        }
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
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket Shared Folder Credential", RegistryValueKind.String);
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
