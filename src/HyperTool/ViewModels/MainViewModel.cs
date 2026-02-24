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

    [ObservableProperty]
    private VmDefinition? _selectedVmForConfig;

    [ObservableProperty]
    private VmDefinition? _selectedDefaultVmForConfig;

    [ObservableProperty]
    private string _newVmName = string.Empty;

    [ObservableProperty]
    private string _newVmLabel = string.Empty;

    [ObservableProperty]
    private bool _hnsEnabled;

    [ObservableProperty]
    private bool _hnsAutoRestartAfterDefaultSwitch;

    [ObservableProperty]
    private bool _hnsAutoRestartAfterAnyConnect;

    [ObservableProperty]
    private string _defaultVmName = string.Empty;

    public ObservableCollection<VmDefinition> AvailableVms { get; } = [];

    public ObservableCollection<HyperVSwitchInfo> AvailableSwitches { get; } = [];

    public ObservableCollection<HyperVCheckpointInfo> AvailableCheckpoints { get; } = [];

    public ObservableCollection<UiNotification> Notifications { get; } = [];

    public string DefaultSwitchName { get; }

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

    public IRelayCommand AddVmCommand { get; }

    public IRelayCommand RemoveVmCommand { get; }

    public IRelayCommand SetDefaultVmCommand { get; }

    public IAsyncRelayCommand SaveConfigCommand { get; }

    public IAsyncRelayCommand ReloadConfigCommand { get; }

    public IAsyncRelayCommand RestartHnsCommand { get; }

    private readonly IHyperVService _hyperVService;
    private readonly IHnsService _hnsService;
    private readonly IConfigService _configService;
    private readonly string _configPath;

    private readonly CancellationTokenSource _lifetimeCancellation = new();

    public MainViewModel(ConfigLoadResult configResult, IHyperVService hyperVService, IHnsService hnsService, IConfigService configService)
    {
        _hyperVService = hyperVService;
        _hnsService = hnsService;
        _configService = configService;
        _configPath = configResult.ConfigPath;

        WindowTitle = configResult.Config.Ui.WindowTitle;
        DefaultVmName = configResult.Config.DefaultVmName;
        DefaultSwitchName = configResult.Config.DefaultSwitchName;
        VmConnectComputerName = configResult.Config.VmConnectComputerName;
        ConfigurationNotice = configResult.Notice;
        HnsEnabled = configResult.Config.Hns.Enabled;
        HnsAutoRestartAfterDefaultSwitch = configResult.Config.Hns.AutoRestartAfterDefaultSwitch;
        HnsAutoRestartAfterAnyConnect = configResult.Config.Hns.AutoRestartAfterAnyConnect;

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

        AddVmCommand = new RelayCommand(AddVm);
        RemoveVmCommand = new RelayCommand(RemoveSelectedVm);
        SetDefaultVmCommand = new RelayCommand(SetDefaultVmFromSelection);
        SaveConfigCommand = new AsyncRelayCommand(SaveConfigAsync, () => !IsBusy);
        ReloadConfigCommand = new AsyncRelayCommand(ReloadConfigAsync, () => !IsBusy);
        RestartHnsCommand = new AsyncRelayCommand(RestartHnsAsync, () => !IsBusy);

        StartDefaultVmCommand = new AsyncRelayCommand(StartDefaultVmAsync, () => !IsBusy);
        StopDefaultVmCommand = new AsyncRelayCommand(StopDefaultVmAsync, () => !IsBusy);
        ConnectDefaultVmCommand = new AsyncRelayCommand(ConnectDefaultVmAsync, () => !IsBusy);
        CreateCheckpointCommand = new AsyncRelayCommand(CreateCheckpointAsync, CanExecuteVmAction);

        SelectedVm = AvailableVms.FirstOrDefault(vm =>
            string.Equals(vm.Name, configResult.Config.DefaultVmName, StringComparison.OrdinalIgnoreCase))
            ?? AvailableVms.FirstOrDefault();

        SelectedVmForConfig = SelectedVm;
        SelectedDefaultVmForConfig = SelectedVm;

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
        SaveConfigCommand.NotifyCanExecuteChanged();
        ReloadConfigCommand.NotifyCanExecuteChanged();
        RestartHnsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedVmChanged(VmDefinition? value)
    {
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
        SelectedVmForConfig = value;

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

    partial void OnSelectedVmForConfigChanged(VmDefinition? value)
    {
        RemoveVmCommand.NotifyCanExecuteChanged();
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

            if (ShouldAutoRestartHnsAfterConnect(SelectedSwitch.Name))
            {
                var hnsResult = await _hnsService.RestartHnsElevatedAsync(token);
                AddNotification(
                    hnsResult.Success ? hnsResult.Message : $"HNS Neustart fehlgeschlagen: {hnsResult.Message}",
                    hnsResult.Success ? "Success" : "Error");
            }
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

            if (ShouldAutoRestartHnsAfterConnect(DefaultSwitchName))
            {
                var hnsResult = await _hnsService.RestartHnsElevatedAsync(token);
                AddNotification(
                    hnsResult.Success ? hnsResult.Message : $"HNS Neustart fehlgeschlagen: {hnsResult.Message}",
                    hnsResult.Success ? "Success" : "Error");
            }
        });
        await RefreshVmStatusAsync();
    }

    private bool ShouldAutoRestartHnsAfterConnect(string connectedSwitch)
    {
        if (!HnsEnabled)
        {
            return false;
        }

        if (HnsAutoRestartAfterAnyConnect)
        {
            return true;
        }

        return HnsAutoRestartAfterDefaultSwitch
               && string.Equals(connectedSwitch, DefaultSwitchName, StringComparison.OrdinalIgnoreCase);
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

    private void AddVm()
    {
        var vmName = NewVmName.Trim();
        if (string.IsNullOrWhiteSpace(vmName))
        {
            AddNotification("VM-Name darf nicht leer sein.", "Error");
            return;
        }

        if (AvailableVms.Any(vm => string.Equals(vm.Name, vmName, StringComparison.OrdinalIgnoreCase)))
        {
            AddNotification("VM existiert bereits in der Konfiguration.", "Warning");
            return;
        }

        var vmLabel = string.IsNullOrWhiteSpace(NewVmLabel) ? vmName : NewVmLabel.Trim();
        var vm = new VmDefinition
        {
            Name = vmName,
            Label = vmLabel
        };

        AvailableVms.Add(vm);
        SelectedVmForConfig = vm;
        SelectedDefaultVmForConfig ??= vm;

        NewVmName = string.Empty;
        NewVmLabel = string.Empty;
        AddNotification($"VM '{vmName}' zur Konfiguration hinzugefügt.", "Success");
    }

    private void RemoveSelectedVm()
    {
        if (SelectedVmForConfig is null)
        {
            AddNotification("Keine VM zum Entfernen ausgewählt.", "Warning");
            return;
        }

        var vmToRemove = SelectedVmForConfig;
        AvailableVms.Remove(vmToRemove);

        if (SelectedVm == vmToRemove)
        {
            SelectedVm = AvailableVms.FirstOrDefault();
        }

        if (SelectedDefaultVmForConfig == vmToRemove)
        {
            SelectedDefaultVmForConfig = AvailableVms.FirstOrDefault();
            DefaultVmName = SelectedDefaultVmForConfig?.Name ?? string.Empty;
        }

        SelectedVmForConfig = AvailableVms.FirstOrDefault();
        AddNotification($"VM '{vmToRemove.Name}' aus Konfiguration entfernt.", "Warning");
    }

    private void SetDefaultVmFromSelection()
    {
        if (SelectedDefaultVmForConfig is null)
        {
            AddNotification("Keine Default-VM ausgewählt.", "Warning");
            return;
        }

        DefaultVmName = SelectedDefaultVmForConfig.Name;
        AddNotification($"Default VM gesetzt: '{DefaultVmName}'.", "Info");
    }

    private async Task SaveConfigAsync()
    {
        await ExecuteBusyActionAsync("Konfiguration wird gespeichert...", _ =>
        {
            if (AvailableVms.Count == 0)
            {
                AddNotification("Mindestens eine VM ist erforderlich.", "Error");
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(DefaultVmName))
            {
                DefaultVmName = AvailableVms[0].Name;
            }

            var config = new HyperToolConfig
            {
                DefaultVmName = DefaultVmName,
                DefaultSwitchName = DefaultSwitchName,
                VmConnectComputerName = VmConnectComputerName,
                Vms = AvailableVms.Select(vm => new VmDefinition
                {
                    Name = vm.Name,
                    Label = vm.Label
                }).ToList(),
                Hns = new HnsSettings
                {
                    Enabled = HnsEnabled,
                    AutoRestartAfterDefaultSwitch = HnsAutoRestartAfterDefaultSwitch,
                    AutoRestartAfterAnyConnect = HnsAutoRestartAfterAnyConnect
                },
                Ui = new UiSettings
                {
                    WindowTitle = WindowTitle,
                    StartMinimized = false,
                    MinimizeToTray = true
                }
            };

            if (_configService.TrySave(_configPath, config, out var errorMessage))
            {
                AddNotification("Konfiguration gespeichert.", "Success");
            }
            else
            {
                AddNotification($"Konfiguration nicht gespeichert: {errorMessage}", "Error");
            }

            return Task.CompletedTask;
        });
    }

    private async Task RestartHnsAsync()
    {
        await ExecuteBusyActionAsync("HNS wird mit UAC neu gestartet...", async token =>
        {
            var result = await _hnsService.RestartHnsElevatedAsync(token);
            if (result.Success)
            {
                AddNotification(result.Message, "Success");
                return;
            }

            AddNotification($"HNS Neustart fehlgeschlagen: {result.Message}", "Error");
        });
    }

    public IReadOnlyList<VmDefinition> GetTrayVms()
    {
        return AvailableVms
            .Select(vm => new VmDefinition
            {
                Name = vm.Name,
                Label = vm.Label
            })
            .ToList();
    }

    public IReadOnlyList<HyperVSwitchInfo> GetTraySwitches()
    {
        return AvailableSwitches
            .Select(vmSwitch => new HyperVSwitchInfo
            {
                Name = vmSwitch.Name,
                SwitchType = vmSwitch.SwitchType
            })
            .ToList();
    }

    public Task ReloadTrayDataAsync()
    {
        return LoadSwitchesAsync();
    }

    public async Task StartVmFromTrayAsync(string vmName)
    {
        await ExecuteBusyActionAsync($"VM '{vmName}' wird gestartet...", async token =>
        {
            await _hyperVService.StartVmAsync(vmName, token);
            AddNotification($"VM '{vmName}' gestartet.", "Success");
        });
        await RefreshVmStatusByNameAsync(vmName);
    }

    public async Task StopVmFromTrayAsync(string vmName)
    {
        await ExecuteBusyActionAsync($"VM '{vmName}' wird gestoppt...", async token =>
        {
            await _hyperVService.StopVmGracefulAsync(vmName, token);
            AddNotification($"VM '{vmName}' graceful gestoppt.", "Success");
        });
        await RefreshVmStatusByNameAsync(vmName);
    }

    public async Task CreateSnapshotFromTrayAsync(string vmName)
    {
        var checkpointName = $"checkpoint-{DateTime.Now:yyyyMMdd-HHmmss}";

        await ExecuteBusyActionAsync($"Checkpoint für '{vmName}' wird erstellt...", async token =>
        {
            await _hyperVService.CreateCheckpointAsync(vmName, checkpointName, null, token);
            AddNotification($"Checkpoint '{checkpointName}' für '{vmName}' erstellt.", "Success");
        });
    }

    public async Task ConnectVmSwitchFromTrayAsync(string vmName, string switchName)
    {
        await ExecuteBusyActionAsync($"'{vmName}' wird mit '{switchName}' verbunden...", async token =>
        {
            await _hyperVService.ConnectVmNetworkAdapterAsync(vmName, switchName, token);
            AddNotification($"'{vmName}' mit '{switchName}' verbunden.", "Success");

            if (ShouldAutoRestartHnsAfterConnect(switchName))
            {
                var hnsResult = await _hnsService.RestartHnsElevatedAsync(token);
                AddNotification(
                    hnsResult.Success ? hnsResult.Message : $"HNS Neustart fehlgeschlagen: {hnsResult.Message}",
                    hnsResult.Success ? "Success" : "Error");
            }
        });

        await RefreshVmStatusByNameAsync(vmName);
    }

    private async Task RefreshVmStatusByNameAsync(string vmName)
    {
        var currentSelectedVm = SelectedVm;

        SelectedVm = AvailableVms.FirstOrDefault(vm =>
                        string.Equals(vm.Name, vmName, StringComparison.OrdinalIgnoreCase))
                    ?? currentSelectedVm;

        await RefreshVmStatusAsync();
    }

    private async Task ReloadConfigAsync()
    {
        await ExecuteBusyActionAsync("Konfiguration wird neu geladen...", _ =>
        {
            var configResult = _configService.LoadOrCreate(_configPath);
            var config = configResult.Config;
            var previousSelectionName = SelectedVm?.Name;

            WindowTitle = config.Ui.WindowTitle;
            ConfigurationNotice = configResult.Notice;
            HnsEnabled = config.Hns.Enabled;
            HnsAutoRestartAfterDefaultSwitch = config.Hns.AutoRestartAfterDefaultSwitch;
            HnsAutoRestartAfterAnyConnect = config.Hns.AutoRestartAfterAnyConnect;
            DefaultVmName = config.DefaultVmName;

            AvailableVms.Clear();
            foreach (var vm in config.Vms)
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

            if (string.IsNullOrWhiteSpace(DefaultVmName) && AvailableVms.Count > 0)
            {
                DefaultVmName = AvailableVms[0].Name;
            }

            SelectedVm = AvailableVms.FirstOrDefault(vm =>
                             string.Equals(vm.Name, previousSelectionName, StringComparison.OrdinalIgnoreCase))
                         ?? AvailableVms.FirstOrDefault(vm =>
                             string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))
                         ?? AvailableVms.FirstOrDefault();

            SelectedVmForConfig = SelectedVm;
            SelectedDefaultVmForConfig = AvailableVms.FirstOrDefault(vm =>
                                           string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))
                                       ?? SelectedVm;

            AddNotification("Konfiguration neu geladen.", "Info");
            return Task.CompletedTask;
        });

        await LoadSwitchesAsync();
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