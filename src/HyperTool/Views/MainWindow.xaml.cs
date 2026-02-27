using ControlzEx.Theming;
using HyperTool.Models;
using HyperTool.Services;
using HyperTool.ViewModels;
using MahApps.Metro.Controls;
using System;
using System.ComponentModel;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HyperTool.Views;

public partial class MainWindow : MetroWindow
{
    private const string LogoEasterEggSoundFileName = "logo-spin.wav";
    private const double LogoEasterEggSoundVolume = 0.30;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int WindowCornerRadius = 16;

    private readonly IThemeService _themeService;
    private MainViewModel? _currentViewModel;
    private HelpWindow? _helpWindow;
    private MediaPlayer? _logoSoundPlayer;
    private string? _loadedLogoSoundPath;

    public MainWindow(IThemeService themeService)
    {
        _themeService = themeService;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnWindowShapeChanged;
        StateChanged += OnWindowShapeChanged;
        DataContextChanged += OnDataContextChanged;
        Closed += OnWindowClosed;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref DwmWindowCornerPreference pvAttribute,
        int cbAttribute);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int nLeftRect,
        int nTopRect,
        int nRightRect,
        int nBottomRect,
        int nWidthEllipse,
        int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        TryApplyRoundedCorners();
        UpdateWindowRegion();
    }

    private void OnWindowShapeChanged(object? sender, EventArgs e)
    {
        UpdateWindowRegion();
    }

    private void UpdateWindowRegion()
    {
        try
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                _ = SetWindowRgn(windowHandle, IntPtr.Zero, true);
                return;
            }

            var width = Math.Max(1, (int)ActualWidth);
            var height = Math.Max(1, (int)ActualHeight);
            var diameter = WindowCornerRadius * 2;

            var regionHandle = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (regionHandle == IntPtr.Zero)
            {
                return;
            }

            var result = SetWindowRgn(windowHandle, regionHandle, true);
            if (result == 0)
            {
                _ = DeleteObject(regionHandle);
            }
        }
        catch
        {
        }
    }

    private void TryApplyRoundedCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        try
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            var preference = DwmWindowCornerPreference.Round;
            _ = DwmSetWindowAttribute(
                windowHandle,
                DwmWindowCornerPreferenceAttribute,
                ref preference,
                Marshal.SizeOf<DwmWindowCornerPreference>());
        }
        catch
        {
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_logoSoundPlayer is not null)
        {
            _logoSoundPlayer.Close();
            _logoSoundPlayer = null;
            _loadedLogoSoundPath = null;
        }
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

        rotateTransform.Angle = 0;

        var spinAnimation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(1700)
        };

        spinAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
        spinAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(360, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
        spinAnimation.KeyFrames.Add(new DiscreteDoubleKeyFrame(360, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        spinAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(720, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
        spinAnimation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1700))));

        rotateTransform.BeginAnimation(RotateTransform.AngleProperty, spinAnimation);
        PlayLogoEasterEggSound();
    }

    private void PlayLogoEasterEggSound()
    {
        try
        {
            var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
            var soundPath = Path.Combine(assetsDir, LogoEasterEggSoundFileName);

            if (File.Exists(soundPath))
            {
                _logoSoundPlayer ??= new MediaPlayer();

                if (!string.Equals(_loadedLogoSoundPath, soundPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logoSoundPlayer.Open(new Uri(soundPath, UriKind.Absolute));
                    _loadedLogoSoundPath = soundPath;
                }

                _logoSoundPlayer.Volume = LogoEasterEggSoundVolume;
                _logoSoundPlayer.Position = TimeSpan.Zero;
                _logoSoundPlayer.Play();
                return;
            }
        }
        catch
        {
        }

        SystemSounds.Asterisk.Play();
    }
}