using ControlzEx.Theming;
using HyperTool.Models;
using HyperTool.Services;
using HyperTool.ViewModels;
using MahApps.Metro.Controls;
using System;
using System.ComponentModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HyperTool.Views;

public partial class MainWindow : MetroWindow
{
    private const string LogoEasterEggSoundFileName = "logo-spin.wav";

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

    private async void HostNetworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentViewModel is null)
        {
            return;
        }

        var adapters = await _currentViewModel.GetHostNetworkAdaptersWithUplinkAsync();
        if (adapters.Count == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Keine Host-Netzwerkkarten gefunden.",
                "Host Network",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var popup = new HostNetworkWindow(adapters)
        {
            Owner = this
        };

        var detectedTheme = ThemeManager.Current.DetectTheme(this);
        if (detectedTheme is not null)
        {
            ThemeManager.Current.ChangeTheme(popup, detectedTheme.Name);
        }

        popup.ShowDialog();
    }

    private void CheckpointTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_currentViewModel is null)
        {
            return;
        }

        _currentViewModel.SelectedCheckpointNode = e.NewValue as HyperVCheckpointTreeItem;
    }

    private void LogoBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (LogoImage.RenderTransform is not RotateTransform rotateTransform)
        {
            rotateTransform = new RotateTransform(0);
            LogoImage.RenderTransform = rotateTransform;
            LogoImage.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        }

        var spinAnimation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(700),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        rotateTransform.BeginAnimation(RotateTransform.AngleProperty, spinAnimation);
        PlayLogoEasterEggSound();
    }

    private static void PlayLogoEasterEggSound()
    {
        try
        {
            var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
            var soundPath = Path.Combine(assetsDir, LogoEasterEggSoundFileName);

            if (File.Exists(soundPath))
            {
                using var player = new SoundPlayer(soundPath);
                player.Play();
                return;
            }
        }
        catch
        {
        }

        SystemSounds.Asterisk.Play();
    }
}