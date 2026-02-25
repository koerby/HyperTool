using ControlzEx.Theming;
using HyperTool.Services;
using HyperTool.ViewModels;
using MahApps.Metro.Controls;
using System.ComponentModel;
using System.Windows;

namespace HyperTool.Views;

public partial class MainWindow : MetroWindow
{
    private readonly IThemeService _themeService;
    private MainViewModel? _currentViewModel;
    private HelpWindow? _helpWindow;

    public MainWindow(IThemeService themeService)
    {
        _themeService = themeService;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentViewModel = e.NewValue as MainViewModel;

        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _themeService.ApplyTheme(_currentViewModel.UiTheme);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(MainViewModel.UiTheme), StringComparison.Ordinal))
        {
            _themeService.ApplyTheme(vm.UiTheme);
        }
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null || !_helpWindow.IsLoaded)
        {
            var configPath = _currentViewModel?.ConfigPath ?? string.Empty;
            var owner = _currentViewModel?.GithubOwner ?? "koerby";
            var repo = _currentViewModel?.GithubRepo ?? "HyperTool";
            var repoUrl = $"https://github.com/{owner}/{repo}";

            _helpWindow = new HelpWindow(configPath, repoUrl)
            {
                Owner = this
            };

            _helpWindow.Closed += (_, _) => _helpWindow = null;

            var detectedTheme = ThemeManager.Current.DetectTheme(this);
            if (detectedTheme is not null)
            {
                ThemeManager.Current.ChangeTheme(_helpWindow, detectedTheme.Name);
            }

            _helpWindow.Show();
            return;
        }

        _helpWindow.Activate();
    }
}