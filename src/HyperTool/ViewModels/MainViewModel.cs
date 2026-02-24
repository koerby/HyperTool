using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTool.Models;
using HyperTool.Services;
using Serilog;
using System.Collections.ObjectModel;

namespace HyperTool.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _windowTitle = "HyperTool";

    [ObservableProperty]
    private string _statusText = "Bereit";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _configurationNotice;

    [ObservableProperty]
    private VmDefinition? _selectedVm;

    [ObservableProperty]
    private HyperVSwitchInfo? _selectedSwitch;

    [ObservableProperty]
    private HyperVCheckpointInfo? _selectedCheckpoint;

    [ObservableProperty]
    private string _newCheckpointName = string.Empty;

    [ObservableProperty]
    private string _newCheckpointDescription = string.Empty;

    [ObservableProperty]
    private string _selectedVmState = "Unbekannt";

    [ObservableProperty]
    private string _selectedVmCurrentSwitch = "-";

    [ObservableProperty]
    private string _busyText = "Bitte warten...";

    public ObservableCollection<VmDefinition> AvailableVms { get; } = [];

    public ObservableCollection<HyperVSwitchInfo> AvailableSwitches { get; } = [];

    public ObservableCollection<HyperVCheckpointInfo> AvailableCheckpoints { get; } = [];

    public ObservableCollection<UiNotification> Notifications { get; } = [];

    public string DefaultSwitchName { get; }

    public string DefaultVmName { get; }

    public string VmConnectComputerName { get; }

    public bool HasConfigurationNotice => !string.IsNullOrWhiteSpace(ConfigurationNotice);

    public IAsyncRelayCommand StartDefaultVmCommand { get; }

    public IAsyncRelayCommand StopDefaultVmCommand { get; }

    public IAsyncRelayCommand ConnectDefaultVmCommand { get; }

    public IAsyncRelayCommand CreateCheckpointCommand { get; }

    public IAsyncRelayCommand StartSelectedVmCommand { get; }

    public IAsyncRelayCommand StopSelectedVmCommand { get; }

    public IAsyncRelayCommand TurnOffSelectedVmCommand { get; }

    public IAsyncRelayCommand RestartSelectedVmCommand { get; }

    public IAsyncRelayCommand OpenConsoleCommand { get; }

    public IAsyncRelayCommand LoadSwitchesCommand { get; }

    public IAsyncRelayCommand ConnectSelectedSwitchCommand { get; }

    public IAsyncRelayCommand DisconnectSwitchCommand { get; }

    public IAsyncRelayCommand RefreshVmStatusCommand { get; }

    public IAsyncRelayCommand LoadCheckpointsCommand { get; }

    public IAsyncRelayCommand ApplyCheckpointCommand { get; }

    public IAsyncRelayCommand DeleteCheckpointCommand { get; }

    private readonly IHyperVService _hyperVService;

    private readonly CancellationTokenSource _lifetimeCancellation = new();

    public MainViewModel(ConfigLoadResult configResult, IHyperVService hyperVService)
    {
        _hyperVService = hyperVService;

        WindowTitle = configResult.Config.Ui.WindowTitle;
        DefaultVmName = configResult.Config.DefaultVmName;
        DefaultSwitchName = configResult.Config.DefaultSwitchName;
        VmConnectComputerName = configResult.Config.VmConnectComputerName;
        ConfigurationNotice = configResult.Notice;

        foreach (var vm in configResult.Config.Vms)
        {
            if (vm is null || string.IsNullOrWhiteSpace(vm.Name))
            {
                continue;
            }

            AvailableVms.Add(new VmDefinition
            {
                Name = vm.Name,
                Label = string.IsNullOrWhiteSpace(vm.Label) ? vm.Name : vm.Label
            });
        }

        if (configResult.IsGenerated)
        {
            StatusText = "Beispiel-Konfiguration erzeugt";
        }
        else if (configResult.HasValidationFixes)
        {
            StatusText = "Konfiguration korrigiert geladen";
        }
        else
        {
            StatusText = "Konfiguration geladen";
        }

        StartSelectedVmCommand = new AsyncRelayCommand(StartSelectedVmAsync, CanExecuteVmAction);
        StopSelectedVmCommand = new AsyncRelayCommand(StopSelectedVmAsync, CanExecuteVmAction);
        TurnOffSelectedVmCommand = new AsyncRelayCommand(TurnOffSelectedVmAsync, CanExecuteVmAction);
        RestartSelectedVmCommand = new AsyncRelayCommand(RestartSelectedVmAsync, CanExecuteVmAction);
        OpenConsoleCommand = new AsyncRelayCommand(OpenConsoleAsync, CanExecuteVmAction);

        LoadSwitchesCommand = new AsyncRelayCommand(LoadSwitchesAsync, () => !IsBusy);
        ConnectSelectedSwitchCommand = new AsyncRelayCommand(ConnectSelectedSwitchAsync, () => !IsBusy && SelectedVm is not null && SelectedSwitch is not null);
        DisconnectSwitchCommand = new AsyncRelayCommand(DisconnectSwitchAsync, CanExecuteVmAction);
        RefreshVmStatusCommand = new AsyncRelayCommand(RefreshVmStatusAsync, () => !IsBusy);

        LoadCheckpointsCommand = new AsyncRelayCommand(LoadCheckpointsAsync, CanExecuteVmAction);
        ApplyCheckpointCommand = new AsyncRelayCommand(ApplyCheckpointAsync, () => !IsBusy && SelectedVm is not null && SelectedCheckpoint is not null);
        DeleteCheckpointCommand = new AsyncRelayCommand(DeleteCheckpointAsync, () => !IsBusy && SelectedVm is not null && SelectedCheckpoint is not null);

        StartDefaultVmCommand = new AsyncRelayCommand(StartDefaultVmAsync, () => !IsBusy);
        StopDefaultVmCommand = new AsyncRelayCommand(StopDefaultVmAsync, () => !IsBusy);
        ConnectDefaultVmCommand = new AsyncRelayCommand(ConnectDefaultVmAsync, () => !IsBusy);
        CreateCheckpointCommand = new AsyncRelayCommand(CreateCheckpointAsync, CanExecuteVmAction);

        SelectedVm = AvailableVms.FirstOrDefault(vm =>
            string.Equals(vm.Name, configResult.Config.DefaultVmName, StringComparison.OrdinalIgnoreCase))
            ?? AvailableVms.FirstOrDefault();

        _ = InitializeAsync();
    }

    partial void OnConfigurationNoticeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasConfigurationNotice));
    }

    partial void OnIsBusyChanged(bool value)
    {
        StartSelectedVmCommand.NotifyCanExecuteChanged();
        StopSelectedVmCommand.NotifyCanExecuteChanged();
        TurnOffSelectedVmCommand.NotifyCanExecuteChanged();
        RestartSelectedVmCommand.NotifyCanExecuteChanged();
        OpenConsoleCommand.NotifyCanExecuteChanged();
        LoadSwitchesCommand.NotifyCanExecuteChanged();
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
        DisconnectSwitchCommand.NotifyCanExecuteChanged();
        RefreshVmStatusCommand.NotifyCanExecuteChanged();
        LoadCheckpointsCommand.NotifyCanExecuteChanged();
        ApplyCheckpointCommand.NotifyCanExecuteChanged();
        DeleteCheckpointCommand.NotifyCanExecuteChanged();
        StartDefaultVmCommand.NotifyCanExecuteChanged();
        StopDefaultVmCommand.NotifyCanExecuteChanged();
        ConnectDefaultVmCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedVmChanged(VmDefinition? value)
    {
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();

        if (value is null)
        {
            SelectedVmState = "Unbekannt";
            SelectedVmCurrentSwitch = "-";
            AvailableCheckpoints.Clear();
            SelectedCheckpoint = null;
            return;
        }

        _ = RefreshVmStatusAsync();
        _ = LoadCheckpointsAsync();
    }

    partial void OnSelectedSwitchChanged(HyperVSwitchInfo? value)
    {
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCheckpointChanged(HyperVCheckpointInfo? value)
    {
        ApplyCheckpointCommand.NotifyCanExecuteChanged();
        DeleteCheckpointCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteVmAction() => !IsBusy && SelectedVm is not null;

    private async Task InitializeAsync()
    {
        await LoadSwitchesAsync();
        await RefreshVmStatusAsync();
        await LoadCheckpointsAsync();
    }

    private async Task StartSelectedVmAsync()
    {
        await ExecuteBusyActionAsync("VM wird gestartet...", async token =>
        {
            await _hyperVService.StartVmAsync(SelectedVm!.Name, token);
            AddNotification($"VM '{SelectedVm.Name}' gestartet.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task StopSelectedVmAsync()
    {
        await ExecuteBusyActionAsync("VM wird gestoppt...", async token =>
        {
            await _hyperVService.StopVmGracefulAsync(SelectedVm!.Name, token);
            AddNotification($"VM '{SelectedVm.Name}' graceful gestoppt.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task TurnOffSelectedVmAsync()
    {
        await ExecuteBusyActionAsync("VM wird hart ausgeschaltet...", async token =>
        {
            await _hyperVService.TurnOffVmAsync(SelectedVm!.Name, token);
            AddNotification($"VM '{SelectedVm.Name}' hart ausgeschaltet.", "Warning");
        });
        await RefreshVmStatusAsync();
    }

    private async Task RestartSelectedVmAsync()
    {
        await ExecuteBusyActionAsync("VM wird neu gestartet...", async token =>
        {
            await _hyperVService.RestartVmAsync(SelectedVm!.Name, token);
            AddNotification($"VM '{SelectedVm.Name}' neu gestartet.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task OpenConsoleAsync()
    {
        await ExecuteBusyActionAsync("vmconnect wird geöffnet...", async token =>
        {
            await _hyperVService.OpenVmConnectAsync(SelectedVm!.Name, VmConnectComputerName, token);
            AddNotification($"Konsole für '{SelectedVm.Name}' geöffnet.", "Info");
        });
    }

    private async Task LoadSwitchesAsync()
    {
        await ExecuteBusyActionAsync("Switches werden geladen...", async token =>
        {
            var switches = await _hyperVService.GetVmSwitchesAsync(token);

            AvailableSwitches.Clear();
            foreach (var vmSwitch in switches.OrderBy(item => item.Name))
            {
                AvailableSwitches.Add(vmSwitch);
            }

            SelectedSwitch = AvailableSwitches.FirstOrDefault(item => string.Equals(item.Name, DefaultSwitchName, StringComparison.OrdinalIgnoreCase))
                             ?? AvailableSwitches.FirstOrDefault();

            AddNotification($"{AvailableSwitches.Count} Switch(es) geladen.", "Info");
        });
    }

    private async Task ConnectSelectedSwitchAsync()
    {
        if (SelectedSwitch is null || SelectedVm is null)
        {
            return;
        }

        await ExecuteBusyActionAsync("VM-Netzwerk wird verbunden...", async token =>
        {
            await _hyperVService.ConnectVmNetworkAdapterAsync(SelectedVm.Name, SelectedSwitch.Name, token);
            AddNotification($"'{SelectedVm.Name}' mit '{SelectedSwitch.Name}' verbunden.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task DisconnectSwitchAsync()
    {
        await ExecuteBusyActionAsync("VM-Netzwerk wird getrennt...", async token =>
        {
            await _hyperVService.DisconnectVmNetworkAdapterAsync(SelectedVm!.Name, token);
            AddNotification($"Netzwerk von '{SelectedVm.Name}' getrennt.", "Warning");
        });
        await RefreshVmStatusAsync();
    }

    private async Task ConnectDefaultVmAsync()
    {
        var targetVm = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? SelectedVm?.Name
                       ?? AvailableVms.FirstOrDefault()?.Name;
        if (string.IsNullOrWhiteSpace(targetVm))
        {
            AddNotification("Keine VM für Default-Switch-Verbindung verfügbar.", "Error");
            return;
        }

        await ExecuteBusyActionAsync("Default VM wird mit Default Switch verbunden...", async token =>
        {
            await _hyperVService.ConnectVmNetworkAdapterAsync(targetVm, DefaultSwitchName, token);
            AddNotification($"'{targetVm}' mit '{DefaultSwitchName}' verbunden.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task StartDefaultVmAsync()
    {
        var targetVm = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? SelectedVm?.Name
                       ?? AvailableVms.FirstOrDefault()?.Name;
        if (string.IsNullOrWhiteSpace(targetVm))
        {
            AddNotification("Keine VM zum Starten gefunden.", "Error");
            return;
        }

        await ExecuteBusyActionAsync("Default VM wird gestartet...", async token =>
        {
            await _hyperVService.StartVmAsync(targetVm, token);
            AddNotification($"'{targetVm}' gestartet.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task StopDefaultVmAsync()
    {
        var targetVm = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? SelectedVm?.Name
                       ?? AvailableVms.FirstOrDefault()?.Name;
        if (string.IsNullOrWhiteSpace(targetVm))
        {
            AddNotification("Keine VM zum Stoppen gefunden.", "Error");
            return;
        }

        await ExecuteBusyActionAsync("Default VM wird gestoppt...", async token =>
        {
            await _hyperVService.StopVmGracefulAsync(targetVm, token);
            AddNotification($"'{targetVm}' gestoppt.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task RefreshVmStatusAsync()
    {
        if (SelectedVm is null)
        {
            return;
        }

        await ExecuteBusyActionAsync("VM-Status wird aktualisiert...", async token =>
        {
            var vms = await _hyperVService.GetVmsAsync(token);
            var vmInfo = vms.FirstOrDefault(item => string.Equals(item.Name, SelectedVm.Name, StringComparison.OrdinalIgnoreCase));
            SelectedVmState = vmInfo?.State ?? "Unbekannt";
            SelectedVmCurrentSwitch = string.IsNullOrWhiteSpace(vmInfo?.CurrentSwitchName) ? "-" : vmInfo.CurrentSwitchName;
            StatusText = vmInfo is null ? "VM nicht gefunden" : $"{vmInfo.Name}: {vmInfo.State}";
        }, showNotificationOnErrorOnly: true);
    }

    private async Task LoadCheckpointsAsync()
    {
        if (SelectedVm is null)
        {
            return;
        }

        await ExecuteBusyActionAsync("Checkpoints werden geladen...", async token =>
        {
            var checkpoints = await _hyperVService.GetCheckpointsAsync(SelectedVm.Name, token);

            AvailableCheckpoints.Clear();
            foreach (var checkpoint in checkpoints.OrderByDescending(item => item.Created))
            {
                AvailableCheckpoints.Add(checkpoint);
            }

            SelectedCheckpoint = AvailableCheckpoints.FirstOrDefault();
            AddNotification($"{AvailableCheckpoints.Count} Checkpoint(s) für '{SelectedVm.Name}' geladen.", "Info");
        }, showNotificationOnErrorOnly: true);
    }

    private async Task CreateCheckpointAsync()
    {
        if (SelectedVm is null)
        {
            return;
        }

        var checkpointName = string.IsNullOrWhiteSpace(NewCheckpointName)
            ? $"checkpoint-{DateTime.Now:yyyyMMdd-HHmmss}"
            : NewCheckpointName.Trim();

        await ExecuteBusyActionAsync("Checkpoint wird erstellt...", async token =>
        {
            await _hyperVService.CreateCheckpointAsync(
                SelectedVm.Name,
                checkpointName,
                string.IsNullOrWhiteSpace(NewCheckpointDescription) ? null : NewCheckpointDescription.Trim(),
                token);

            AddNotification($"Checkpoint '{checkpointName}' für '{SelectedVm.Name}' erstellt.", "Success");
        });

        NewCheckpointName = string.Empty;
        NewCheckpointDescription = string.Empty;
        await LoadCheckpointsAsync();
    }

    private async Task ApplyCheckpointAsync()
    {
        if (SelectedVm is null || SelectedCheckpoint is null)
        {
            return;
        }

        await ExecuteBusyActionAsync("Checkpoint wird angewendet...", async token =>
        {
            await _hyperVService.ApplyCheckpointAsync(SelectedVm.Name, SelectedCheckpoint.Name, token);
            AddNotification($"Checkpoint '{SelectedCheckpoint.Name}' auf '{SelectedVm.Name}' angewendet.", "Warning");
        });

        await RefreshVmStatusAsync();
        await LoadCheckpointsAsync();
    }

    private async Task DeleteCheckpointAsync()
    {
        if (SelectedVm is null || SelectedCheckpoint is null)
        {
            return;
        }

        var checkpointName = SelectedCheckpoint.Name;
        await ExecuteBusyActionAsync("Checkpoint wird gelöscht...", async token =>
        {
            await _hyperVService.RemoveCheckpointAsync(SelectedVm.Name, checkpointName, token);
            AddNotification($"Checkpoint '{checkpointName}' von '{SelectedVm.Name}' gelöscht.", "Warning");
        });

        await LoadCheckpointsAsync();
    }

    private async Task ExecuteBusyActionAsync(string busyText, Func<CancellationToken, Task> action, bool showNotificationOnErrorOnly = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = busyText;

        try
        {
            await action(_lifetimeCancellation.Token);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Aktion fehlgeschlagen: {BusyText}", busyText);
            AddNotification($"Fehler: {ex.Message}", "Error");
            StatusText = "Fehler";
        }
        finally
        {
            IsBusy = false;
            BusyText = "Bitte warten...";

            if (!showNotificationOnErrorOnly && !StatusText.Equals("Fehler", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "Bereit";
            }
        }
    }

    private void AddNotification(string message, string level)
    {
        var entry = new UiNotification
        {
            Message = message,
            Level = level,
            Timestamp = DateTime.Now
        };

        Notifications.Insert(0, entry);
        while (Notifications.Count > 8)
        {
            Notifications.RemoveAt(Notifications.Count - 1);
        }

        Log.Information("UI Notification ({Level}): {Message}", level, message);
    }
}