using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTool.Models;
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

    public ObservableCollection<VmDefinition> AvailableVms { get; } = [];

    public string DefaultSwitchName { get; }

    public string VmConnectComputerName { get; }

    public bool HasConfigurationNotice => !string.IsNullOrWhiteSpace(ConfigurationNotice);

    public IRelayCommand StartDefaultVmCommand { get; }

    public IRelayCommand StopDefaultVmCommand { get; }

    public IRelayCommand ConnectDefaultVmCommand { get; }

    public IRelayCommand CreateCheckpointCommand { get; }

    public MainViewModel(ConfigLoadResult configResult)
    {
        WindowTitle = configResult.Config.Ui.WindowTitle;
        DefaultSwitchName = configResult.Config.DefaultSwitchName;
        VmConnectComputerName = configResult.Config.VmConnectComputerName;
        ConfigurationNotice = configResult.Notice;

        foreach (var vm in configResult.Config.Vms)
        {
            AvailableVms.Add(vm);
        }

        SelectedVm = AvailableVms.FirstOrDefault(vm =>
            vm.Name.Equals(configResult.Config.DefaultVmName, StringComparison.OrdinalIgnoreCase))
            ?? AvailableVms.FirstOrDefault();

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

        StartDefaultVmCommand = new RelayCommand(() => HandleQuickAction("Start Default VM"));
        StopDefaultVmCommand = new RelayCommand(() => HandleQuickAction("Stop Default VM"));
        ConnectDefaultVmCommand = new RelayCommand(() => HandleQuickAction("Connect Default VM to Default Switch"));
        CreateCheckpointCommand = new RelayCommand(() => HandleQuickAction("Create Checkpoint"));
    }

    partial void OnConfigurationNoticeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasConfigurationNotice));
    }

    private void HandleQuickAction(string actionName)
    {
        StatusText = $"Quick Action: {actionName}";
        Log.Information("Tray quick action triggered: {ActionName}", actionName);
    }
}