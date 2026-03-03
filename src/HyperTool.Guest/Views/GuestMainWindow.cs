using HyperTool.Models;
using HyperTool.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Net.Http;
using System.Reflection;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI;
using IOPath = System.IO.Path;

namespace HyperTool.Guest.Views;

internal sealed class GuestMainWindow : Window
{
    private enum UsbHostSearchStatusKind
    {
        Neutral,
        Running,
        Success,
        Error
    }

    public const int DefaultWindowWidth = 1400;
    public const int DefaultWindowHeight = 940;
    private const int GuestSplashMinVisibleMs = 900;
    private const int GuestSplashStatusCycleMs = 420;
    private const string UpdateOwner = "koerby";
    private const string UpdateRepo = "HyperTool";
    private const string GuestInstallerAssetHint = "HyperTool-Guest-Setup";
    private const string GuestUsbRuntimeOwner = "vadimgrn";
    private const string GuestUsbRuntimeRepo = "usbip-win2";
    private const string GuestUsbRuntimeAssetHint = "x64-release";

    private readonly Func<Task<IReadOnlyList<UsbIpDeviceInfo>>> _refreshUsbDevicesAsync;
    private readonly Func<string, Task<int>> _connectUsbAsync;
    private readonly Func<string, Task<int>> _disconnectUsbAsync;
    private readonly Func<GuestConfig, Task> _saveConfigAsync;
    private readonly Func<string, Task> _restartForThemeChangeAsync;
    private readonly Func<Task<(bool hyperVSocketActive, bool registryServiceOk)>> _runTransportDiagnosticsTestAsync;
    private readonly Func<Task<string?>> _discoverUsbHostAddressAsync;
    private readonly Func<Task<IReadOnlyList<HostSharedFolderDefinition>>> _fetchHostSharedFoldersAsync;
    private readonly bool _isUsbClientAvailable;
    private readonly IUpdateService _updateService = new GitHubUpdateService();
    private static readonly HttpClient UpdateDownloadClient = new();

    private readonly List<Button> _navButtons = [];
    private readonly ContentPresenter _pageContent = new();
    private readonly Border _overlay = new();
    private readonly Border _overlayCard = new();
    private readonly TextBlock _overlayTitle = new();
    private readonly TextBlock _overlayText = new();
    private readonly ProgressBar _overlayProgressBar = new();
    private TextBlock? _reloadOverlayStatusText;
    private readonly RotateTransform _logoRotateTransform = new();
    private readonly Storyboard _overlayAmbientStoryboard = new();

    private readonly ObservableCollection<string> _notifications = [];
    private readonly Border _notificationSummaryBorder = new();
    private readonly Grid _notificationExpandedGrid = new() { Visibility = Visibility.Collapsed };
    private readonly ListView _notificationsListView = new();
    private readonly TextBlock _statusText = new() { Text = "Bereit.", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _updateStatusValueText = new() { Text = "Noch nicht geprüft", TextWrapping = TextWrapping.Wrap, Opacity = 0.9 };
    private Button? _installUpdateButton;
    private readonly Ellipse _usbRuntimeStatusDot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _usbRuntimeStatusText = new() { Opacity = 0.9, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _diagHyperVSocketText = new() { Text = "Unbekannt", Opacity = 0.9 };
    private readonly TextBlock _diagRegistryServiceText = new() { Text = "Unbekannt", Opacity = 0.9 };
    private readonly TextBlock _diagFallbackText = new() { Text = "Nein", Opacity = 0.9 };
    private readonly Button _toggleLogButton = new();
    private bool _isLogExpanded;
    private string _releaseUrl = "https://github.com/koerby/HyperTool/releases";
    private string _installerDownloadUrl = string.Empty;
    private string _installerFileName = string.Empty;
    private bool _updateCheckSucceeded;
    private bool _updateAvailable;

    private readonly ListView _usbListView = new();
    private readonly ObservableCollection<GuestSharedFolderMapping> _sharedFolderMappings = [];
    private readonly ListView _sharedFolderMappingsListView = new();
    private readonly Ellipse _sharedFolderCredentialSocketStatusDot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sharedFolderCredentialSocketStatusText = new() { Opacity = 0.9, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sharedFolderReconnectStatusText = new() { Text = "Reconnect: inaktiv · Letzter Lauf: noch keiner", Opacity = 0.84, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sharedFolderHostDiscoveryText = new() { Text = "Ermittelter Hostname: -", Opacity = 0.84, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sharedFolderStatusText = new() { Text = "Bereit.", Opacity = 0.88, TextWrapping = TextWrapping.Wrap };
    private readonly GuestDriveMappingService _driveMappingService = new();
    private readonly SemaphoreSlim _sharedFolderUiOperationGate = new(1, 1);
    private CancellationTokenSource? _sharedFolderAutoApplyCts;
    private bool _suppressSharedFolderAutoApply;
    private string _sharedFolderLastError = "-";
    private Button? _usbRefreshButton;
    private Button? _usbConnectButton;
    private Button? _usbDisconnectButton;
    private Button? _usbRuntimeInstallButton;
    private readonly ComboBox _themeCombo = new();
    private readonly ToggleSwitch _themeToggle = new();
    private readonly TextBlock _themeText = new();
    private readonly TextBox _usbHostAddressTextBox = new();
    private Button? _usbHostSearchButton;
    private UsbHostSearchStatusKind _usbHostSearchStatusKind = UsbHostSearchStatusKind.Neutral;
    private readonly TextBlock _usbHostSearchStatusText = new()
    {
        Opacity = 0.88,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        Text = "Bereit"
    };
    private readonly TextBlock _usbResolvedHostNameText = new()
    {
        Opacity = 0.88,
        TextWrapping = TextWrapping.Wrap,
        Text = "Ermittelter Hostname: -"
    };
    private readonly CheckBox _startWithWindowsCheckBox = new() { Content = "Mit Windows starten" };
    private readonly CheckBox _startMinimizedCheckBox = new() { Content = "Beim Start minimiert" };
    private readonly CheckBox _minimizeToTrayCheckBox = new() { Content = "Tasktray-Menü aktiv" };
    private readonly CheckBox _checkForUpdatesOnStartupCheckBox = new() { Content = "Beim Start auf Updates prüfen" };
    private readonly CheckBox _useHyperVSocketCheckBox = new() { Content = "Hyper-V Socket verwenden (bevorzugt)" };
    private readonly CheckBox _usbAutoConnectCheckBox = new() { Content = "Auto-Connect für ausgewähltes Gerät" };
    private readonly Border _usbHostAddressEditorCard = new()
    {
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(10, 8, 10, 8)
    };
    private readonly TextBlock _usbModeHintText = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.88 };
    private readonly Border _usbTransportModeBadge = new()
    {
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(10, 5, 10, 5),
        MinHeight = 30,
        BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _usbTransportModeBadgeText = new()
    {
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 12,
        Text = "Modus: -"
    };
    private readonly Border _usbHyperVModeBadge = new()
    {
        Width = 152,
        MinHeight = 30,
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(10, 5, 10, 5),
        BorderThickness = new Thickness(1)
    };
    private readonly Border _usbHyperVModeIconBadge = new()
    {
        Width = 18,
        Height = 18,
        CornerRadius = new CornerRadius(5),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly SymbolIcon _usbHyperVModeIcon = new()
    {
        Symbol = Symbol.Switch,
        Width = 12,
        Height = 12
    };
    private readonly TextBlock _usbHyperVModeBadgeText = new()
    {
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 12,
        Text = "Hyper-V Socket"
    };
    private readonly Border _usbIpModeBadge = new()
    {
        Width = 152,
        MinHeight = 30,
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(10, 5, 10, 5),
        BorderThickness = new Thickness(1)
    };
    private readonly Border _usbIpModeIconBadge = new()
    {
        Width = 18,
        Height = 18,
        CornerRadius = new CornerRadius(5),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly SymbolIcon _usbIpModeIcon = new()
    {
        Symbol = Symbol.World,
        Width = 12,
        Height = 12
    };
    private readonly TextBlock _usbIpModeBadgeText = new()
    {
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 12,
        Text = "IP-Mode"
    };

    private HelpWindow? _helpWindow;
    private int _selectedMenuIndex;
    private GuestConfig _config;
    private IReadOnlyList<UsbIpDeviceInfo> _usbDevices = [];
    private bool _suppressThemeEvents;
    private bool _isThemeRestartInProgress;
    private bool _isThemeToggleHandlerAttached;
    private bool _isUsbTransportToggleHandlerAttached;
    private bool _isUsbModeBadgeHandlersAttached;
    private bool _suppressUsbTransportToggleEvents;
    private bool _suppressUsbAutoConnectToggleEvents;
    private CancellationTokenSource? _usbTransportAutoRefreshCts;
    private MediaPlayer? _logoSpinPlayer;
    private List<UIElement>? _startupMainElements;
    private UIElement? _usbPage;
    private UIElement? _sharedFoldersPage;
    private UIElement? _settingsPage;
    private UIElement? _infoPage;

    public GuestMainWindow(
        GuestConfig config,
        Func<Task<IReadOnlyList<UsbIpDeviceInfo>>> refreshUsbDevicesAsync,
        Func<string, Task<int>> connectUsbAsync,
        Func<string, Task<int>> disconnectUsbAsync,
        Func<GuestConfig, Task> saveConfigAsync,
        Func<string, Task> restartForThemeChangeAsync,
        Func<Task<(bool hyperVSocketActive, bool registryServiceOk)>> runTransportDiagnosticsTestAsync,
        Func<Task<string?>> discoverUsbHostAddressAsync,
        Func<Task<IReadOnlyList<HostSharedFolderDefinition>>> fetchHostSharedFoldersAsync,
        bool isUsbClientAvailable)
    {
        _config = config;
        _refreshUsbDevicesAsync = refreshUsbDevicesAsync;
        _connectUsbAsync = connectUsbAsync;
        _disconnectUsbAsync = disconnectUsbAsync;
        _saveConfigAsync = saveConfigAsync;
        _restartForThemeChangeAsync = restartForThemeChangeAsync;
        _runTransportDiagnosticsTestAsync = runTransportDiagnosticsTestAsync;
        _discoverUsbHostAddressAsync = discoverUsbHostAddressAsync;
        _fetchHostSharedFoldersAsync = fetchHostSharedFoldersAsync;
        _isUsbClientAvailable = isUsbClientAvailable;

        Title = "HyperTool Guest";
        ExtendsContentIntoTitleBar = false;
        TryApplyInitialWindowSize();

        var initialTheme = GuestConfigService.NormalizeTheme(config.Ui.Theme);
        ApplyThemePalette(initialTheme == "dark");

        _themeCombo.Items.Add("dark");
        _themeCombo.Items.Add("light");

        Content = BuildLayout();
        ApplyConfigToControls();
        ApplyTheme(config.Ui.Theme);
        TryApplyWindowIcon();

        GuestLogger.EntryWritten += OnLoggerEntryWritten;
        Closed += (_, _) => GuestLogger.EntryWritten -= OnLoggerEntryWritten;
    }

    public string CurrentTheme => GuestConfigService.NormalizeTheme((_themeCombo.SelectedItem as string) ?? _config.Ui.Theme);

    public int SelectedMenuIndex => _selectedMenuIndex;

    public void SelectMenuIndex(int index)
    {
        var normalized = Math.Clamp(index, 0, 3);
        if (_selectedMenuIndex == normalized)
        {
            return;
        }

        _selectedMenuIndex = normalized;
        UpdateNavSelection();
        UpdatePageContent();
    }

    public void ApplyTheme(string theme)
    {
        var normalized = GuestConfigService.NormalizeTheme(theme);
        var isDark = normalized == "dark";

        ApplyThemePalette(isDark);

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        }

        _suppressThemeEvents = true;
        _themeCombo.SelectedItem = normalized;
        _themeToggle.IsOn = isDark;
        _suppressThemeEvents = false;
        _themeText.Text = isDark ? "Dunkles Theme" : "Helles Theme";

        _overlay.Background = HyperTool.WinUI.Views.LifecycleVisuals.CreateRootBackgroundBrush();

        _overlayCard.Background = HyperTool.WinUI.Views.LifecycleVisuals.CreateCardSurfaceBrush();
        _overlayCard.BorderBrush = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.CardBorder);
        _overlayTitle.Foreground = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.TextPrimary);
        _overlayText.Foreground = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.TextSecondary);

        ApplyUsbHostSearchStatusColor();

        UpdateTitleBarAppearance(isDark);
    }

    public async Task PlayStartupAnimationAsync()
    {
        var mainElements = _startupMainElements;
        if (mainElements is null && Content is Grid rootGrid)
        {
            mainElements = rootGrid.Children
                .OfType<UIElement>()
                .Where(element => !ReferenceEquals(element, _overlay))
                .ToList();

            foreach (var element in mainElements)
            {
                element.Opacity = 0;
            }
        }

        _overlay.Visibility = Visibility.Visible;
        _overlay.Opacity = 1;
        _overlayProgressBar.Value = 8;

        var startupStart = Stopwatch.StartNew();
        var statusIndex = 0;
        while (startupStart.ElapsedMilliseconds < GuestSplashMinVisibleMs)
        {
            var status = HyperTool.WinUI.Views.LifecycleVisuals.StartupStatusMessages[
                statusIndex % HyperTool.WinUI.Views.LifecycleVisuals.StartupStatusMessages.Length];
            _overlayText.Text = status;

            _overlayProgressBar.Value = Math.Min(92, 8 + (statusIndex * 17));
            statusIndex++;

            var remaining = GuestSplashMinVisibleMs - startupStart.ElapsedMilliseconds;
            var delay = (int)Math.Min(GuestSplashStatusCycleMs, remaining);
            if (delay > 0)
            {
                await Task.Delay(delay);
            }
        }

        _overlayText.Text = "Starte HyperTool Guest Oberfläche …";
        _overlayProgressBar.Value = 100;

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = HyperTool.WinUI.Views.LifecycleVisuals.CreateEaseInOut(),
            EnableDependentAnimation = true
        };

        var story = new Storyboard();
        Storyboard.SetTarget(fadeOut, _overlay);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        story.Children.Add(fadeOut);

        if (mainElements is not null)
        {
            foreach (var element in mainElements)
            {
                var fadeInMain = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(420),
                    EasingFunction = HyperTool.WinUI.Views.LifecycleVisuals.CreateEaseOut(),
                    EnableDependentAnimation = true
                };

                Storyboard.SetTarget(fadeInMain, element);
                Storyboard.SetTargetProperty(fadeInMain, "Opacity");
                story.Children.Add(fadeInMain);
            }
        }

        story.Begin();

        await Task.Delay(460);
        _overlay.Visibility = Visibility.Collapsed;
        _startupMainElements = null;
    }

    public void PrepareStartupSplash()
    {
        if (Content is not Grid rootGrid)
        {
            return;
        }

        _startupMainElements = rootGrid.Children
            .OfType<UIElement>()
            .Where(element => !ReferenceEquals(element, _overlay))
            .ToList();

        foreach (var element in _startupMainElements)
        {
            element.Opacity = 0;
        }

        _overlay.Visibility = Visibility.Visible;
        _overlay.Opacity = 1;
        _overlayText.Text = HyperTool.WinUI.Views.LifecycleVisuals.StartupStatusMessages[0];
        _overlayProgressBar.Value = 8;
    }

    public void PrepareLifecycleGuard(string statusText)
    {
        if (Content is not Grid rootGrid)
        {
            return;
        }

        _startupMainElements = rootGrid.Children
            .OfType<UIElement>()
            .Where(element => !ReferenceEquals(element, _overlay))
            .ToList();

        foreach (var element in _startupMainElements)
        {
            element.Opacity = 0;
        }

        _overlay.Visibility = Visibility.Visible;
        _overlay.Opacity = 1;
        _overlayText.Text = string.IsNullOrWhiteSpace(statusText)
            ? "Design wird neu geladen …"
            : statusText.Trim();
        _overlayProgressBar.Value = 64;
    }

    public async Task DismissLifecycleGuardAsync()
    {
        var mainElements = _startupMainElements;
        var story = new Storyboard();

        var fadeOut = new DoubleAnimation
        {
            From = _overlay.Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = HyperTool.WinUI.Views.LifecycleVisuals.CreateEaseInOut(),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(fadeOut, _overlay);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        story.Children.Add(fadeOut);

        if (mainElements is not null)
        {
            foreach (var element in mainElements)
            {
                var fadeInMain = new DoubleAnimation
                {
                    From = element.Opacity,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = HyperTool.WinUI.Views.LifecycleVisuals.CreateEaseOut(),
                    EnableDependentAnimation = true
                };

                Storyboard.SetTarget(fadeInMain, element);
                Storyboard.SetTargetProperty(fadeInMain, "Opacity");
                story.Children.Add(fadeInMain);
            }
        }

        story.Begin();
        await Task.Delay(250);

        _overlay.Visibility = Visibility.Collapsed;
        _startupMainElements = null;
    }

    public async Task PlayExitAnimationAsync()
    {
        _overlay.Visibility = Visibility.Visible;
        _overlay.Opacity = 0;
        _overlayText.Text = "Guest-Dienste werden sicher beendet …";

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EnableDependentAnimation = true
        };

        var story = new Storyboard();
        Storyboard.SetTarget(fadeIn, _overlay);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        story.Children.Add(fadeIn);
        story.Begin();

        await Task.Delay(330);
    }

    public async Task PlayExitFadeAsync()
    {
        if (Content is not UIElement contentElement)
        {
            return;
        }

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EnableDependentAnimation = true
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(fadeOut, contentElement);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();

        await Task.Delay(240);
    }

    public async Task PlayThemeReloadSplashAsync(string targetTheme)
    {
        try
        {
            _overlayAmbientStoryboard.Stop();
        }
        catch
        {
        }

        var reloadOverlay = BuildReloadOverlayContent();
        _overlay.Child = reloadOverlay.Root;
        _reloadOverlayStatusText = reloadOverlay.StatusText;
        _overlay.Visibility = Visibility.Visible;
        _overlay.Opacity = 0;
        _reloadOverlayStatusText.Text = "Layout wird aktualisiert …";

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(130),
            EasingFunction = HyperTool.WinUI.Views.LifecycleVisuals.CreateEaseInOut(),
            EnableDependentAnimation = true
        };

        var showStoryboard = new Storyboard();
        Storyboard.SetTarget(fadeIn, _overlay);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        showStoryboard.Children.Add(fadeIn);
        showStoryboard.Begin();

        await Task.Delay(154);

        _reloadOverlayStatusText.Text = "Design wird neu geladen …";
        await Task.Delay(1000);
    }

    private void TryApplyInitialWindowSize()
    {
        try
        {
            if (AppWindow is not null)
            {
                AppWindow.Resize(new SizeInt32(DefaultWindowWidth, DefaultWindowHeight));
            }
        }
        catch
        {
        }
    }

    public void UpdateUsbDevices(IReadOnlyList<UsbIpDeviceInfo> devices)
    {
        _usbDevices = devices;

        if (!_isUsbClientAvailable)
        {
            _usbListView.ItemsSource = new[]
            {
                "USB/IP-Client nicht installiert. USB-Funktionen sind deaktiviert."
            };
            return;
        }

        _usbListView.ItemsSource = devices.Select(item => item.DisplayName).ToList();
        UpdateAutoConnectToggleFromSelection();
    }

    public UsbIpDeviceInfo? GetSelectedUsbDevice()
    {
        if (_usbListView.SelectedIndex < 0 || _usbListView.SelectedIndex >= _usbDevices.Count)
        {
            return null;
        }

        return _usbDevices[_usbListView.SelectedIndex];
    }

    public void UpdateTransportDiagnostics(bool hyperVSocketActive, bool registryServicePresent, bool fallbackActive)
    {
        _diagHyperVSocketText.Text = hyperVSocketActive ? "Ja" : "Nein";
        _diagRegistryServiceText.Text = registryServicePresent ? "Ja" : "Nein";
        _diagFallbackText.Text = fallbackActive ? "Ja" : "Nein";
        UpdateUsbTransportHeaderStatus();
    }

    public void UpdateSharedFolderReconnectStatus(bool reconnectActive, DateTimeOffset? lastRunUtc, string summary)
    {
        var lastRunText = lastRunUtc.HasValue
            ? lastRunUtc.Value.ToLocalTime().ToString("HH:mm:ss")
            : "noch keiner";
        var activeText = reconnectActive ? "aktiv" : "inaktiv";
        var normalizedSummary = string.IsNullOrWhiteSpace(summary) ? "-" : summary.Trim();

        _sharedFolderReconnectStatusText.Text = $"Reconnect: {activeText} · Letzter Lauf: {lastRunText} · {normalizedSummary}";
    }

    public void UpdateSharedFolderCredentialSocketStatus(bool socketActive, DateTimeOffset? lastSyncUtc)
    {
        var lastSyncText = lastSyncUtc.HasValue
            ? lastSyncUtc.Value.ToLocalTime().ToString("HH:mm:ss")
            : "-";

        _sharedFolderCredentialSocketStatusDot.Fill = new SolidColorBrush(socketActive
            ? Windows.UI.Color.FromArgb(0xFF, 0x32, 0xD7, 0x4B)
            : Windows.UI.Color.FromArgb(0xFF, 0xE8, 0x4A, 0x5F));
        _sharedFolderCredentialSocketStatusText.Text = socketActive
            ? $"Credential Socket: aktiv · Letzte Sync: {lastSyncText}"
            : $"Credential Socket: inaktiv · Letzte Sync: {lastSyncText}";
    }

    private async Task RunTransportDiagnosticsTestAsync()
    {
        try
        {
            var (hyperVSocketActive, registryServiceOk) = await _runTransportDiagnosticsTestAsync();
            var ok = hyperVSocketActive && registryServiceOk;

            AppendNotification(ok
                ? "[Info] Hyper-V Socket Test erfolgreich: Verbindung steht und Registry-Service ist erreichbar."
                : $"[Warn] Hyper-V Socket Test: Verbindung={(hyperVSocketActive ? "OK" : "FAIL")}, Registry-Service={(registryServiceOk ? "OK" : "FAIL")}. ");
        }
        catch (Exception ex)
        {
            AppendNotification($"[Error] Hyper-V Socket Test fehlgeschlagen: {ex.Message}");
        }
    }

    private UIElement BuildLayout()
    {
        var root = new Grid
        {
            Background = Application.Current.Resources["PageBackgroundBrush"] as Brush
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerCard = CreateCard(new Thickness(16, 16, 16, 0), 14, 14);
        headerCard.Child = BuildHeader();
        Grid.SetRow(headerCard, 0);
        root.Children.Add(headerCard);

        var contentCard = CreateCard(new Thickness(16, 0, 16, 0), 12, 14);
        contentCard.Child = BuildMainContentGrid();
        Grid.SetRow(contentCard, 2);
        root.Children.Add(contentCard);

        var bottomCard = CreateCard(new Thickness(16, 0, 16, 16), 12, 12);
        bottomCard.Child = BuildBottomArea();
        Grid.SetRow(bottomCard, 4);
        root.Children.Add(bottomCard);

        _overlay.Visibility = Visibility.Collapsed;
        _overlay.Child = BuildOverlayContent();
        Grid.SetRow(_overlay, 0);
        Grid.SetRowSpan(_overlay, 5);
        root.Children.Add(_overlay);

        UpdateNavSelection();
        UpdatePageContent();
        UpdateBusyAndNotificationPanel();

        return root;
    }

    private Grid BuildHeader()
    {
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/HyperTool.Guest.Icon.Transparent.png")),
            Width = 28,
            Height = 28
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = "HyperTool Guest",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        titleStack.Children.Add(titleRow);
        titleStack.Children.Add(new TextBlock
        {
            Text = "dein nützlicher Hyper V Helfer",
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 6)
        });

        headerGrid.Children.Add(titleStack);

        var titleActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };

        var helpButton = new Button
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            Padding = new Thickness(0),
            Content = new TextBlock
            {
                Text = "?",
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
        helpButton.Click += (_, _) => OpenHelpWindow();
        titleActions.Children.Add(helpButton);

        var logoBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            CornerRadius = new CornerRadius(12),
            Width = 54,
            Height = 54,
            Padding = new Thickness(6)
        };
        var logo = new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Logo.png")),
            Width = 40,
            Height = 40,
            RenderTransform = _logoRotateTransform,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5)
        };
        logoBorder.Child = logo;
        logoBorder.Tapped += (_, _) => RunLogoEasterEgg();
        titleActions.Children.Add(logoBorder);

        Grid.SetColumn(titleActions, 1);
        headerGrid.Children.Add(titleActions);

        return headerGrid;
    }

    private Grid BuildMainContentGrid()
    {
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            Padding = new Thickness(10)
        };

        var sidebarStack = new StackPanel { Spacing = 8 };
        sidebarStack.Children.Add(CreateNavButton("🔌", "USB", 0));
        sidebarStack.Children.Add(CreateNavButton("📁", "Shared Folder", 1));
        sidebarStack.Children.Add(CreateNavButton("⚙", "Einstellungen", 2));
        sidebarStack.Children.Add(CreateNavButton("ℹ", "Info", 3));
        sidebar.Child = sidebarStack;

        mainGrid.Children.Add(sidebar);

        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _usbTransportModeBadge.Child = _usbTransportModeBadgeText;

        var topRowContent = new Grid { ColumnSpacing = 10 };
        topRowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        topRowContent.Children.Add(new TextBlock
        {
            Text = "Guest USB Connect & Management",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        Grid.SetColumn(_usbTransportModeBadge, 1);
        topRowContent.Children.Add(_usbTransportModeBadge);

        var topRow = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            Padding = new Thickness(12),
            Child = topRowContent
        };

        contentGrid.Children.Add(topRow);
        Grid.SetRow(_pageContent, 2);
        contentGrid.Children.Add(_pageContent);

        Grid.SetColumn(contentGrid, 2);
        mainGrid.Children.Add(contentGrid);

        return mainGrid;
    }

    private UIElement BuildBottomArea()
    {
        var bottom = new Grid { RowSpacing = 8 };
        bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topRow = new Grid { ColumnSpacing = 8 };
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.Children.Add(new TextBlock { Text = "Notifications", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        bottom.Children.Add(topRow);

        var summaryGrid = new Grid { ColumnSpacing = 8 };
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _notificationSummaryBorder.Padding = new Thickness(10);
        _notificationSummaryBorder.CornerRadius = new CornerRadius(8);
        _notificationSummaryBorder.BorderThickness = new Thickness(1);
        _notificationSummaryBorder.BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush;
        _notificationSummaryBorder.Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush;
        _notificationSummaryBorder.Child = _statusText;
        summaryGrid.Children.Add(_notificationSummaryBorder);

        var summaryButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var openLogButton = CreateIconButton("📄", "Log-Ordner öffnen", onClick: (_, _) => OpenLogFile());
        openLogButton.CornerRadius = new CornerRadius(8);
        openLogButton.Padding = new Thickness(8, 2, 8, 2);
        openLogButton.BorderThickness = new Thickness(1);
        openLogButton.BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush;
        openLogButton.Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush;
        summaryButtons.Children.Add(openLogButton);

        _toggleLogButton.CornerRadius = new CornerRadius(8);
        _toggleLogButton.Padding = new Thickness(8, 2, 8, 2);
        _toggleLogButton.BorderThickness = new Thickness(1);
        _toggleLogButton.BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush;
        _toggleLogButton.Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush;
        _toggleLogButton.Click += (_, _) =>
        {
            _isLogExpanded = !_isLogExpanded;
            UpdateBusyAndNotificationPanel();
        };
        summaryButtons.Children.Add(_toggleLogButton);

        Grid.SetColumn(summaryButtons, 1);
        summaryGrid.Children.Add(summaryButtons);

        Grid.SetRow(summaryGrid, 1);
        bottom.Children.Add(summaryGrid);

        _notificationExpandedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _notificationExpandedGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        _notificationExpandedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var expandedButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        expandedButtons.Children.Add(CreateIconButton("⧉", "Copy", onClick: (_, _) => CopyNotificationsToClipboard()));
        expandedButtons.Children.Add(CreateIconButton("⌫", "Clear", onClick: (_, _) =>
        {
            _notifications.Clear();
            _statusText.Text = "Keine Notifications.";
        }));
        Grid.SetRow(expandedButtons, 0);
        _notificationExpandedGrid.Children.Add(expandedButtons);

        var logListBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush
        };
        _notificationsListView.ItemsSource = _notifications;
        _notificationsListView.MaxHeight = 220;
        logListBorder.Child = _notificationsListView;
        Grid.SetRow(logListBorder, 2);
        _notificationExpandedGrid.Children.Add(logListBorder);
        Grid.SetRow(_notificationExpandedGrid, 2);
        bottom.Children.Add(_notificationExpandedGrid);

        return bottom;
    }

    private UIElement BuildOverlayContent()
    {
        var overlayRoot = new Grid
        {
            Background = HyperTool.WinUI.Views.LifecycleVisuals.CreateRootBackgroundBrush()
        };

        var focusLayerPrimary = new Ellipse
        {
            Width = 820,
            Height = 820,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = HyperTool.WinUI.Views.LifecycleVisuals.CreateCenterFocusBrush(HyperTool.WinUI.Views.LifecycleVisuals.BackgroundFocusSecondary)
        };
        overlayRoot.Children.Add(focusLayerPrimary);

        var focusLayerSecondary = new Ellipse
        {
            Width = 620,
            Height = 620,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(88, -52, -88, 52),
            Fill = HyperTool.WinUI.Views.LifecycleVisuals.CreateCenterFocusBrush(HyperTool.WinUI.Views.LifecycleVisuals.BackgroundFocusTertiary),
            Opacity = 0.54
        };
        overlayRoot.Children.Add(focusLayerSecondary);

        overlayRoot.Children.Add(new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = HyperTool.WinUI.Views.LifecycleVisuals.CreateVignetteBrush(0x78)
        });

        BuildOverlayNetworkLayer(overlayRoot);
        BuildOverlayAmbientBands(overlayRoot);

        var splashVersionText = new TextBlock
        {
            Text = HyperTool.WinUI.Views.LifecycleVisuals.ResolveDisplayVersion(ResolveGuestVersionText()),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 20, 14),
            FontSize = 12,
            Opacity = 0.72,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0x9B, 0xB7, 0xD7))
        };
        overlayRoot.Children.Add(splashVersionText);

        var splashCopyrightText = new TextBlock
        {
            Text = "Copyright: koerby",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(20, 0, 0, 14),
            FontSize = 12,
            Opacity = 0.72,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0x9B, 0xB7, 0xD7))
        };
        overlayRoot.Children.Add(splashCopyrightText);

        _overlayCard.Width = 520;
        _overlayCard.Height = double.NaN;
        _overlayCard.Padding = new Thickness(30, 28, 30, 24);
        _overlayCard.CornerRadius = new CornerRadius(24);
        _overlayCard.HorizontalAlignment = HorizontalAlignment.Center;
        _overlayCard.VerticalAlignment = VerticalAlignment.Center;
        _overlayCard.Background = HyperTool.WinUI.Views.LifecycleVisuals.CreateCardSurfaceBrush();
        _overlayCard.BorderBrush = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.CardBorder);
        _overlayCard.BorderThickness = new Thickness(1);
        _overlayCard.Shadow = new ThemeShadow();

        var innerFrame = new Border
        {
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.CardInnerOutline),
            Background = HyperTool.WinUI.Views.LifecycleVisuals.CreateCardInnerBrush(),
            Padding = new Thickness(20, 18, 20, 16)
        };

        _overlayTitle.Text = "HyperTool Guest";
        _overlayTitle.FontSize = 30;
        _overlayTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _overlayTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _overlayTitle.Foreground = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.TextPrimary);

        _overlayText.FontSize = 13;
        _overlayText.HorizontalAlignment = HorizontalAlignment.Center;
        _overlayText.TextAlignment = TextAlignment.Center;
        _overlayText.Opacity = 0.94;
        _overlayText.Margin = new Thickness(0, 6, 0, 2);

        _overlayProgressBar.Height = 10;
        _overlayProgressBar.Minimum = 0;
        _overlayProgressBar.Maximum = 100;
        _overlayProgressBar.Value = 8;
        _overlayProgressBar.CornerRadius = new CornerRadius(5);
        _overlayProgressBar.Foreground = HyperTool.WinUI.Views.LifecycleVisuals.CreateProgressBrush();
        _overlayProgressBar.Background = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.ProgressTrack);
        _overlayProgressBar.Margin = new Thickness(8, 6, 8, 0);

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
            Children =
            {
                new Grid
                {
                    Width = 122,
                    Height = 122,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new Ellipse
                        {
                            Width = 122,
                            Height = 122,
                            Fill = new RadialGradientBrush
                            {
                                Center = new Windows.Foundation.Point(0.5, 0.5),
                                GradientOrigin = new Windows.Foundation.Point(0.5, 0.5),
                                RadiusX = 0.5,
                                RadiusY = 0.5,
                                GradientStops =
                                {
                                    new GradientStop { Color = Color.FromArgb(0x22, 0x72, 0xC4, 0xFF), Offset = 0.0 },
                                    new GradientStop { Color = Color.FromArgb(0x00, 0x72, 0xC4, 0xFF), Offset = 1.0 }
                                }
                            },
                            Opacity = 0.30
                        },
                        new Ellipse
                        {
                            Width = 108,
                            Height = 108,
                            StrokeThickness = 1.1,
                            Stroke = new SolidColorBrush(Color.FromArgb(0x78, 0x88, 0xD1, 0xFF)),
                            Opacity = 0.36
                        },
                        new Border
                        {
                            Width = 98,
                            Height = 98,
                            CornerRadius = new CornerRadius(49),
                            Background = new SolidColorBrush(Color.FromArgb(0x64, 0x66, 0xC3, 0xFF)),
                            Opacity = 0.28
                        },
                        new Image
                        {
                            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/HyperTool.Guest.Icon.Transparent.png")),
                            Width = 68,
                            Height = 68,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                _overlayTitle,
                _overlayText,
                _overlayProgressBar
            }
        };

        innerFrame.Child = stack;
        _overlayCard.Child = innerFrame;

        overlayRoot.Children.Add(_overlayCard);

        try
        {
            _overlayAmbientStoryboard.Begin();
        }
        catch
        {
        }

        return overlayRoot;
    }

    private (UIElement Root, TextBlock StatusText) BuildReloadOverlayContent()
    {
        var overlayRoot = new Grid
        {
            Background = HyperTool.WinUI.Views.LifecycleVisuals.CreateRootBackgroundBrush()
        };

        overlayRoot.Children.Add(new TextBlock
        {
            Text = "Copyright: koerby",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(20, 0, 0, 14),
            FontSize = 12,
            Opacity = 0.72,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xC8, 0x9B, 0xB7, 0xD7))
        });

        overlayRoot.Children.Add(new TextBlock
        {
            Text = ResolveGuestVersionText(),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 20, 14),
            FontSize = 12,
            Opacity = 0.72,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xC8, 0x9B, 0xB7, 0xD7))
        });

        overlayRoot.Children.Add(new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = HyperTool.WinUI.Views.LifecycleVisuals.CreateVignetteBrush(0x78)
        });

        var statusText = new TextBlock
        {
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.TextSecondary),
            Opacity = 0.95,
            Margin = new Thickness(0)
        };

        var card = new Border
        {
            Width = 420,
            Height = double.NaN,
            Padding = new Thickness(24, 22, 24, 18),
            CornerRadius = new CornerRadius(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = HyperTool.WinUI.Views.LifecycleVisuals.CreateCardSurfaceBrush(),
            BorderBrush = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.CardBorder),
            BorderThickness = new Thickness(1),
            Shadow = new ThemeShadow()
        };

        card.Child = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/HyperTool.Guest.Icon.Transparent.png")),
                    Width = 54,
                    Height = 54,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.95
                },
                new TextBlock
                {
                    Text = "HyperTool Guest",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 24,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.TextPrimary)
                },
                statusText,
                new ProgressRing
                {
                    Width = 30,
                    Height = 30,
                    IsActive = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x8D, 0xCF, 0xFF)),
                    Margin = new Thickness(0, 2, 0, 0)
                }
            }
        };

        overlayRoot.Children.Add(card);
        return (overlayRoot, statusText);
    }

    private void BuildOverlayAmbientBands(Grid root)
    {
        var canvas = new Canvas { IsHitTestVisible = false };

        var band1 = CreateOverlayMovingBand(660, 44, 0.06, -14, -800, 1180, 7000, 0);
        var band2 = CreateOverlayMovingBand(520, 32, 0.04, -10, -760, 1140, 7600, 1200);

        Canvas.SetTop(band1, 152);
        Canvas.SetTop(band2, 476);

        canvas.Children.Add(band1);
        canvas.Children.Add(band2);

        root.Children.Add(canvas);
    }

    private void BuildOverlayNetworkLayer(Grid root)
    {
        var canvas = new Canvas { IsHitTestVisible = false };

        var nodes = new[]
        {
            (X: 236.0, Y: 260.0, Size: 9.8, Highlight: true, Label: "Host"),
            (X: 364.0, Y: 298.0, Size: 8.4, Highlight: false, Label: "Mgmt"),
            (X: 504.0, Y: 316.0, Size: 8.6, Highlight: true, Label: "Hyper-V"),
            (X: 642.0, Y: 294.0, Size: 8.4, Highlight: false, Label: (string?)null),
            (X: 780.0, Y: 274.0, Size: 9.4, Highlight: true, Label: "VM"),
            (X: 914.0, Y: 312.0, Size: 8.2, Highlight: false, Label: "Client"),
            (X: 1006.0, Y: 346.0, Size: 8.0, Highlight: false, Label: "Target"),
            (X: 422.0, Y: 388.0, Size: 7.2, Highlight: false, Label: (string?)null),
            (X: 838.0, Y: 392.0, Size: 7.2, Highlight: false, Label: (string?)null)
        };

        foreach (var node in nodes)
        {
            var circle = new Ellipse
            {
                Width = node.Size,
                Height = node.Size,
                Fill = new SolidColorBrush(node.Highlight
                    ? Color.FromArgb(0xD6, 0x67, 0xBF, 0xF8)
                    : HyperTool.WinUI.Views.LifecycleVisuals.NodeColor),
                Stroke = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.NodeStroke),
                StrokeThickness = node.Highlight ? 1.0 : 0.9,
                Opacity = node.Highlight ? 0.66 : 0.52
            };
            Canvas.SetLeft(circle, node.X - (node.Size / 2));
            Canvas.SetTop(circle, node.Y - (node.Size / 2));
            canvas.Children.Add(circle);

            if (!string.IsNullOrWhiteSpace(node.Label))
            {
                var label = new TextBlock
                {
                    Text = node.Label,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Opacity = 0.58,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xC2, 0xA6, 0xC4, 0xE3))
                };
                Canvas.SetLeft(label, node.X - 24);
                Canvas.SetTop(label, node.Y + 13);
                canvas.Children.Add(label);
            }
        }

        var links = new (int From, int To)[]
        {
            (0,4), (4,1), (1,5), (5,2), (2,6), (6,3), (1,7), (7,8), (8,3)
        };

        for (var i = 0; i < links.Length; i++)
        {
            var (from, to) = links[i];
            var a = nodes[from];
            var b = nodes[to];

            var line = new Line
            {
                X1 = a.X,
                Y1 = a.Y,
                X2 = b.X,
                Y2 = b.Y,
                Stroke = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.LineColor),
                StrokeThickness = 0.8,
                Opacity = 0.0
            };
            canvas.Children.Add(line);

            var lineAppear = new DoubleAnimation
            {
                From = 0.0,
                To = 0.34,
                Duration = TimeSpan.FromMilliseconds(340),
                BeginTime = TimeSpan.FromMilliseconds(170 + (i * 140)),
                FillBehavior = FillBehavior.HoldEnd,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(lineAppear, line);
            Storyboard.SetTargetProperty(lineAppear, "Opacity");
            _overlayAmbientStoryboard.Children.Add(lineAppear);

            var pulse = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = new SolidColorBrush(HyperTool.WinUI.Views.LifecycleVisuals.PulseColor),
                Opacity = 0.0
            };
            Canvas.SetLeft(pulse, a.X - 1.5);
            Canvas.SetTop(pulse, a.Y - 1.5);
            canvas.Children.Add(pulse);

            var pulseFade = new DoubleAnimation
            {
                From = 0.0,
                To = 0.28,
                Duration = TimeSpan.FromMilliseconds(220),
                BeginTime = TimeSpan.FromMilliseconds(560 + (i * 120)),
                FillBehavior = FillBehavior.HoldEnd,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(pulseFade, pulse);
            Storyboard.SetTargetProperty(pulseFade, "Opacity");
            _overlayAmbientStoryboard.Children.Add(pulseFade);

            var pulseX = new DoubleAnimation
            {
                From = a.X - 1.5,
                To = b.X - 1.5,
                Duration = TimeSpan.FromMilliseconds(1360 + (i * 90)),
                BeginTime = TimeSpan.FromMilliseconds(760 + (i * 100)),
                RepeatBehavior = RepeatBehavior.Forever,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(pulseX, pulse);
            Storyboard.SetTargetProperty(pulseX, "(Canvas.Left)");
            _overlayAmbientStoryboard.Children.Add(pulseX);

            var pulseY = new DoubleAnimation
            {
                From = a.Y - 1.5,
                To = b.Y - 1.5,
                Duration = TimeSpan.FromMilliseconds(1360 + (i * 90)),
                BeginTime = TimeSpan.FromMilliseconds(760 + (i * 100)),
                RepeatBehavior = RepeatBehavior.Forever,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(pulseY, pulse);
            Storyboard.SetTargetProperty(pulseY, "(Canvas.Top)");
            _overlayAmbientStoryboard.Children.Add(pulseY);
        }

        root.Children.Add(canvas);
    }

    private Rectangle CreateOverlayMovingBand(double width, double height, double opacity, double rotation, double fromX, double toX, int durationMs, int beginMs)
    {
        var band = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = height / 2,
            RadiusY = height / 2,
            Opacity = opacity,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0.5),
                EndPoint = new Windows.Foundation.Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(0x00, 0x63, 0xC1, 0xFF), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0xFF, 0x63, 0xC1, 0xFF), Offset = 0.5 },
                    new GradientStop { Color = Color.FromArgb(0x00, 0x63, 0xC1, 0xFF), Offset = 1 }
                }
            },
            RenderTransform = new CompositeTransform { Rotation = rotation, TranslateX = fromX }
        };

        var move = new DoubleAnimation
        {
            From = fromX,
            To = toX,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(move, band);
        Storyboard.SetTargetProperty(move, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
        _overlayAmbientStoryboard.Children.Add(move);

        return band;
    }

    private UIElement BuildUsbPage()
    {
        var root = new Grid { RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var actionsCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["SurfaceSoftBrush"] as Brush,
            Padding = new Thickness(10)
        };

        var actionsStack = new StackPanel { Spacing = 5 };
        actionsStack.Children.Add(new TextBlock
        {
            Text = "USB Host-Connect (Host-Freigaben)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var runtimeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        runtimeRow.Children.Add(_usbRuntimeStatusDot);
        runtimeRow.Children.Add(_usbRuntimeStatusText);
        actionsStack.Children.Add(runtimeRow);

        _usbRuntimeInstallButton = CreateIconButton("⬇", "Installation usbip-win2", onClick: async (_, _) => await InstallGuestUsbRuntimeAsync());
        _usbRuntimeInstallButton.Visibility = Visibility.Collapsed;
        actionsStack.Children.Add(_usbRuntimeInstallButton);

        var actionRow = new Grid { ColumnSpacing = 8 };
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _usbRefreshButton = CreateIconButton("⟳", "Refresh", onClick: async (_, _) => await RefreshUsbAsync());
        _usbConnectButton = CreateIconButton("🔌", "Connect", onClick: async (_, _) => await ConnectUsbAsync());
        _usbDisconnectButton = CreateIconButton("⏏", "Disconnect", onClick: async (_, _) => await DisconnectUsbAsync());

        _usbRefreshButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _usbConnectButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _usbDisconnectButton.HorizontalAlignment = HorizontalAlignment.Stretch;

        _usbRefreshButton.IsEnabled = _isUsbClientAvailable;
        _usbConnectButton.IsEnabled = _isUsbClientAvailable;
        _usbDisconnectButton.IsEnabled = _isUsbClientAvailable;

        Grid.SetColumn(_usbRefreshButton, 0);
        Grid.SetColumn(_usbConnectButton, 1);
        Grid.SetColumn(_usbDisconnectButton, 2);

        actionRow.Children.Add(_usbRefreshButton);
        actionRow.Children.Add(_usbConnectButton);
        actionRow.Children.Add(_usbDisconnectButton);
        actionsStack.Children.Add(actionRow);

        _usbAutoConnectCheckBox.IsEnabled = _isUsbClientAvailable;
        _usbAutoConnectCheckBox.Margin = new Thickness(0);
        _usbAutoConnectCheckBox.Checked += async (_, _) => await SetSelectedUsbDeviceAutoConnectAsync(true);
        _usbAutoConnectCheckBox.Unchecked += async (_, _) => await SetSelectedUsbDeviceAutoConnectAsync(false);
        actionsStack.Children.Add(_usbAutoConnectCheckBox);

        if (!_isUsbClientAvailable)
        {
            actionsStack.Children.Add(new TextBlock
            {
                Text = "USB/IP-Client (usbip-win2) ist nicht installiert. USB-Funktionen sind deaktiviert. Quelle: github.com/vadimgrn/usbip-win2",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.88,
                Foreground = Application.Current.Resources["TextMutedBrush"] as Brush
            });
        }

        UpdateUsbRuntimeStatusUi();

        actionsCard.Child = actionsStack;
        root.Children.Add(actionsCard);

        var listBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            Padding = new Thickness(8),
            Child = _usbListView
        };

        _usbListView.SelectionChanged += (_, _) => UpdateAutoConnectToggleFromSelection();

        Grid.SetRow(listBorder, 1);
        root.Children.Add(listBorder);

        return root;
    }

    private UIElement BuildSharedFoldersPage()
    {
        var root = new Grid { RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var editorCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["SurfaceSoftBrush"] as Brush,
            Padding = new Thickness(10)
        };

        var editorStack = new StackPanel { Spacing = 6 };

        var headerRow = new Grid { ColumnSpacing = 10 };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Shared Folder",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        headerRow.Children.Add(titleText);

        var credentialChipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        credentialChipRow.Children.Add(_sharedFolderCredentialSocketStatusDot);
        credentialChipRow.Children.Add(_sharedFolderCredentialSocketStatusText);
        Grid.SetColumn(credentialChipRow, 1);
        headerRow.Children.Add(credentialChipRow);

        editorStack.Children.Add(headerRow);

        editorStack.Children.Add(new TextBlock
        {
            Text = "Ablauf: Host-Liste laden, pro Share Laufwerk setzen, gewünschte Shares per Checkbox aktivieren/deaktivieren (wird direkt angewendet).",
            Opacity = 0.82,
            TextWrapping = TextWrapping.Wrap
        });

        var hostSyncRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        hostSyncRow.Children.Add(CreateIconButton("⇅", "Host-Liste laden", onClick: async (_, _) => await SyncSharedFoldersFromHostAsync()));
        hostSyncRow.Children.Add(CreateIconButton("🧪", "Self-Test", onClick: async (_, _) => await RunSharedFolderSelfTestAsync()));
        hostSyncRow.Children.Add(CreateIconButton("🔐", "Credentials löschen", onClick: async (_, _) => await ClearStoredSharedFolderCredentialsAsync()));
        editorStack.Children.Add(hostSyncRow);

        editorStack.Children.Add(new TextBlock
        {
            Text = "Host-Liste laden nutzt ausschließlich Hyper-V Socket (kein IP-Fallback).",
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });

        editorStack.Children.Add(_sharedFolderReconnectStatusText);
        editorStack.Children.Add(_sharedFolderHostDiscoveryText);

        editorStack.Children.Add(_sharedFolderStatusText);
        editorCard.Child = editorStack;
        root.Children.Add(editorCard);

        UpdateSharedFolderCredentialSocketStatus(socketActive: false, lastSyncUtc: null);

        var infoText = new TextBlock
        {
            Margin = new Thickness(2, 0, 0, 0),
            Opacity = 0.85,
            Text = "Für UNC-Pfade mit \\HOST wird bevorzugt der per Hyper-V Socket ermittelte Hostname genutzt (z. B. \\REALHOSTNAME\\Share); IP ist Fallback."
        };
        Grid.SetRow(infoText, 1);
        root.Children.Add(infoText);

        var listCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            Padding = new Thickness(8)
        };

        _sharedFolderMappingsListView.ItemsSource = _sharedFolderMappings;
        _sharedFolderMappingsListView.ItemTemplate = CreateSharedFolderMappingTemplate();
        _sharedFolderMappingsListView.SelectionMode = ListViewSelectionMode.None;
        _sharedFolderMappingsListView.IsItemClickEnabled = false;

        listCard.Child = _sharedFolderMappingsListView;
        Grid.SetRow(listCard, 2);
        root.Children.Add(listCard);

        RefreshSharedFolderMappingsFromConfig();
        UpdateHostDiscoveryPresentation();
        _ = SyncSharedFoldersFromHostAsync();
        _ = RefreshSharedFolderMountStatesSafeAsync();

        return root;
    }

    private static DataTemplate CreateSharedFolderMappingTemplate()
    {
        const string templateXaml = """
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
    <Grid ColumnSpacing='8' Margin='4,2,4,2'>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width='36'/>
            <ColumnDefinition Width='32'/>
            <ColumnDefinition Width='80'/>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='90'/>
        </Grid.ColumnDefinitions>
        <CheckBox IsChecked='{Binding Enabled, Mode=TwoWay}' VerticalAlignment='Center' HorizontalAlignment='Left'/>
        <TextBlock Grid.Column='1' Text='{Binding MountStateDot}' VerticalAlignment='Center' HorizontalAlignment='Center' FontSize='12'/>
        <TextBox Grid.Column='2' Text='{Binding DriveLetter, Mode=TwoWay}' MaxLength='2' Width='54' VerticalAlignment='Center'/>
        <TextBlock Grid.Column='3' Text='{Binding SharePath}' FontWeight='SemiBold' Opacity='0.9' TextTrimming='CharacterEllipsis' TextWrapping='NoWrap'/>
        <TextBlock Grid.Column='4' Text='{Binding MountStateText}' Opacity='0.82' TextTrimming='CharacterEllipsis' TextWrapping='NoWrap'/>
    </Grid>
</DataTemplate>
""";

        return (DataTemplate)XamlReader.Load(templateXaml);
    }

    private void RefreshSharedFolderMappingsFromConfig()
    {
        _suppressSharedFolderAutoApply = true;
        try
        {
            foreach (var existing in _sharedFolderMappings)
            {
                existing.PropertyChanged -= OnSharedFolderMappingPropertyChanged;
            }

        _config.SharedFolders ??= new GuestSharedFolderSettings();
        _config.SharedFolders.Mappings ??= [];

        var normalizedMappings = new List<GuestSharedFolderMapping>();
        foreach (var mapping in _config.SharedFolders.Mappings)
        {
            if (mapping is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(mapping.Id))
            {
                mapping.Id = Guid.NewGuid().ToString("N");
            }

            var normalizedDriveLetter = GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter);
            var normalizedSharePath = (mapping.SharePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedSharePath))
            {
                continue;
            }

            normalizedMappings.Add(new GuestSharedFolderMapping
            {
                Id = mapping.Id,
                Label = string.IsNullOrWhiteSpace(mapping.Label) ? normalizedSharePath : mapping.Label,
                SharePath = normalizedSharePath,
                DriveLetter = normalizedDriveLetter,
                Persistent = true,
                Enabled = mapping.Enabled,
                MountStateDot = mapping.Enabled ? "🔴" : "⚪",
                MountStateText = mapping.Enabled ? "getrennt" : "deaktiviert"
            });
        }

        EnsureUniqueDriveLetters(normalizedMappings);

        _sharedFolderMappings.Clear();
        foreach (var mapping in normalizedMappings)
        {
            _sharedFolderMappings.Add(mapping);
            mapping.PropertyChanged += OnSharedFolderMappingPropertyChanged;
        }
        }
        finally
        {
            _suppressSharedFolderAutoApply = false;
        }
    }

    private void OnSharedFolderMappingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSharedFolderAutoApply)
        {
            return;
        }

        if (sender is not GuestSharedFolderMapping)
        {
            return;
        }

        if (!string.Equals(e.PropertyName, nameof(GuestSharedFolderMapping.Enabled), StringComparison.Ordinal)
            && !string.Equals(e.PropertyName, nameof(GuestSharedFolderMapping.DriveLetter), StringComparison.Ordinal))
        {
            return;
        }

        ScheduleSharedFolderAutoApply();
    }

    private void ScheduleSharedFolderAutoApply()
    {
        try
        {
            _sharedFolderAutoApplyCts?.Cancel();
        }
        catch
        {
        }

        _sharedFolderAutoApplyCts?.Dispose();
        _sharedFolderAutoApplyCts = new CancellationTokenSource();
        var token = _sharedFolderAutoApplyCts.Token;

        _ = RunSharedFolderAutoApplyAsync(token);
    }

    private async Task RunSharedFolderAutoApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(180, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ApplySharedFolderSelectionAsync(autoTriggered: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _sharedFolderLastError = ex.Message;
            _sharedFolderStatusText.Text = $"Automatische Shared-Folder-Anwendung fehlgeschlagen: {ex.Message}";
            GuestLogger.Warn("sharedfolders.autoapply.failed", ex.Message, new
            {
                exceptionType = ex.GetType().FullName
            });
        }
    }

    private async Task RefreshSharedFolderMountStatesSafeAsync()
    {
        try
        {
            await RefreshSharedFolderMountStatesAsync();
        }
        catch (Exception ex)
        {
            _sharedFolderLastError = ex.Message;
            GuestLogger.Warn("sharedfolders.status.refresh_failed", ex.Message, new
            {
                exceptionType = ex.GetType().FullName
            });
        }
    }

    private async Task RefreshSharedFolderMountStatesAsync()
    {
        var mappingsSnapshot = _sharedFolderMappings.ToList();
        foreach (var mapping in mappingsSnapshot)
        {
            if (!mapping.Enabled)
            {
                mapping.MountStateDot = "⚪";
                mapping.MountStateText = "deaktiviert";
                continue;
            }

            try
            {
                var normalizedDriveLetter = GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter);
                var status = await _driveMappingService.QueryMappingAsync(normalizedDriveLetter, CancellationToken.None);
                var isMounted = status.Exists
                               && _driveMappingService.IsExpectedMapping(status.RemotePath, mapping.SharePath, ResolveSharedFolderHostTarget());

                mapping.MountStateDot = isMounted ? "🟢" : "🔴";
                mapping.MountStateText = isMounted ? "verbunden" : "getrennt";

                if (!isMounted && status.Exists)
                {
                    GuestLogger.Warn("sharedfolders.status.mismatch", "Laufwerk existiert, Zielpfad passt nicht zur Konfiguration.", new
                    {
                        mapping.Id,
                        mapping.Label,
                        mapping.SharePath,
                        mapping.DriveLetter,
                        mappedRemotePath = status.RemotePath,
                        hostAddress = ResolveSharedFolderHostTarget()
                    });
                }
            }
            catch (Exception ex)
            {
                mapping.MountStateDot = "🔴";
                mapping.MountStateText = "getrennt";
                GuestLogger.Warn("sharedfolders.status.query_failed", ex.Message, new
                {
                    mapping.Id,
                    mapping.Label,
                    mapping.SharePath,
                    mapping.DriveLetter,
                    exceptionType = ex.GetType().FullName
                });
                _sharedFolderLastError = ex.Message;
            }
        }

        _sharedFolderMappingsListView.ItemsSource = null;
        _sharedFolderMappingsListView.ItemsSource = _sharedFolderMappings;
    }

    private async Task ApplySharedFolderSelectionAsync(bool autoTriggered = false)
    {
        var lockAcquired = autoTriggered
            ? await _sharedFolderUiOperationGate.WaitAsync(0)
            : await _sharedFolderUiOperationGate.WaitAsync(TimeSpan.FromSeconds(8));

        if (!lockAcquired)
        {
            _sharedFolderStatusText.Text = autoTriggered
                ? "Änderung übernommen, sobald laufende Shared-Folder Aktion fertig ist."
                : "Shared-Folder Aktion läuft noch. Bitte in ein paar Sekunden erneut versuchen.";
            return;
        }

        try
        {
        _suppressSharedFolderAutoApply = true;
        if (!ValidateSharedFolderMappings(out var validationError))
        {
            _sharedFolderStatusText.Text = validationError;
            return;
        }

        await SaveSharedFolderMappingsToConfigAsync();

        var activatedCount = 0;
        var deactivatedCount = 0;
        var failedCount = 0;
        var firstError = string.Empty;
        var credentialPromptAttempted = false;

        var mappingsSnapshot = _sharedFolderMappings
            .Select(item => new GuestSharedFolderMapping
            {
                Id = item.Id,
                Label = item.Label,
                SharePath = item.SharePath,
                DriveLetter = GuestConfigService.NormalizeDriveLetter(item.DriveLetter),
                Persistent = true,
                Enabled = item.Enabled
            })
            .ToList();

        foreach (var mapping in mappingsSnapshot)
        {
            try
            {
                if (mapping.Enabled)
                {
                    await _driveMappingService.MountAsync(mapping, ResolveSharedFolderHostTarget(), _config.Credential, CancellationToken.None);
                    activatedCount++;
                    GuestLogger.Info("sharedfolders.apply.mounted", "Shared-Folder gemappt.", new
                    {
                        mapping.Id,
                        mapping.Label,
                        mapping.SharePath,
                        mapping.DriveLetter,
                        hostAddress = ResolveSharedFolderHostTarget()
                    });
                }
                else
                {
                    await _driveMappingService.UnmountAsync(mapping.DriveLetter, CancellationToken.None);
                    deactivatedCount++;
                    GuestLogger.Info("sharedfolders.apply.unmounted", "Shared-Folder getrennt.", new
                    {
                        mapping.Id,
                        mapping.Label,
                        mapping.SharePath,
                        mapping.DriveLetter
                    });
                }
            }
            catch (Exception ex)
            {
                if (mapping.Enabled
                    && !credentialPromptAttempted
                    && IsLikelyCredentialIssue(ex.Message))
                {
                    credentialPromptAttempted = true;

                    var promptResult = await PromptForSharedFolderCredentialsAsync(mapping.SharePath);
                    if (promptResult.Submitted)
                    {
                        _config.Credential ??= new GuestCredential();
                        _config.Credential.Username = (promptResult.Username ?? string.Empty).Trim();
                        _config.Credential.Password = promptResult.Password ?? string.Empty;
                        await _saveConfigAsync(_config);

                        try
                        {
                            await _driveMappingService.MountAsync(mapping, ResolveSharedFolderHostTarget(), _config.Credential, CancellationToken.None);
                            activatedCount++;
                            GuestLogger.Info("sharedfolders.apply.mounted", "Shared-Folder nach Credential-Eingabe gemappt.", new
                            {
                                mapping.Id,
                                mapping.Label,
                                mapping.SharePath,
                                mapping.DriveLetter,
                                hostAddress = ResolveSharedFolderHostTarget(),
                                credentialUser = _config.Credential.Username
                            });
                            continue;
                        }
                        catch (Exception retryEx)
                        {
                            ex = retryEx;
                        }
                    }
                    else
                    {
                        ex = new InvalidOperationException("Anmeldung für SMB-Freigabe wurde abgebrochen.", ex);
                    }
                }

                failedCount++;
                if (string.IsNullOrWhiteSpace(firstError))
                {
                    firstError = ex.Message;
                }

                GuestLogger.Warn("sharedfolders.apply.failed", ex.Message, new
                {
                    mapping.Id,
                    mapping.Label,
                    mapping.SharePath,
                    mapping.DriveLetter,
                    mapping.Enabled,
                    hostAddress = ResolveSharedFolderHostTarget(),
                    exceptionType = ex.GetType().FullName
                });
            }
        }

        _sharedFolderMappingsListView.ItemsSource = null;
        _sharedFolderMappingsListView.ItemsSource = _sharedFolderMappings;
        await RefreshSharedFolderMountStatesSafeAsync();

        var prefix = autoTriggered ? "Automatisch angewendet" : "Auswahl angewendet";
        _sharedFolderStatusText.Text = failedCount == 0
            ? $"{prefix}: {activatedCount} aktiv, {deactivatedCount} deaktiviert."
            : $"{prefix}: {activatedCount} aktiv, {deactivatedCount} deaktiviert, {failedCount} Fehler. {firstError}";

        if (failedCount == 0)
        {
            _sharedFolderLastError = "-";
        }
        else if (!string.IsNullOrWhiteSpace(firstError))
        {
            _sharedFolderLastError = firstError;
        }
        }
        finally
        {
            _suppressSharedFolderAutoApply = false;
            _sharedFolderUiOperationGate.Release();
        }
    }

    private static bool IsLikelyCredentialIssue(string message)
    {
        var normalized = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("Systemfehler 5", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Systemfehler 86", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Systemfehler 1326", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Logon failure", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Benutzername oder Kennwort", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Anmeldung", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Zugriff verweigert", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(bool Submitted, string Username, string Password)> PromptForSharedFolderCredentialsAsync(string sharePath)
    {
        var usernameBox = new TextBox
        {
            PlaceholderText = "Benutzername (z. B. HOST\\user)",
            Text = (_config.Credential?.Username ?? string.Empty).Trim()
        };

        var passwordBox = new PasswordBox
        {
            PlaceholderText = "Passwort"
        };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = $"Für die Freigabe '{sharePath}' werden Zugangsdaten benötigt.",
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(usernameBox);
        stack.Children.Add(passwordBox);

        var dialog = new ContentDialog
        {
            Title = "SMB-Anmeldung",
            Content = stack,
            PrimaryButtonText = "Speichern & Verbinden",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary
        };

        if (Content is FrameworkElement root)
        {
            dialog.XamlRoot = root.XamlRoot;
        }

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return (false, string.Empty, string.Empty);
        }

        return (true, usernameBox.Text?.Trim() ?? string.Empty, passwordBox.Password ?? string.Empty);
    }

    private async Task ClearStoredSharedFolderCredentialsAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Gespeicherte SMB-Zugangsdaten löschen",
            Content = new TextBlock
            {
                Text = "Gespeicherte Benutzername/Passwort für Shared-Folder werden entfernt. Beim nächsten Verbinden wird erneut gefragt.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };

        if (Content is FrameworkElement root)
        {
            dialog.XamlRoot = root.XamlRoot;
        }

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _config.Credential ??= new GuestCredential();
        _config.Credential.Username = string.Empty;
        _config.Credential.Password = string.Empty;
        await _saveConfigAsync(_config);

        _sharedFolderLastError = "-";
        _sharedFolderStatusText.Text = "SMB-Credentials gelöscht. Beim nächsten Verbinden wird erneut gefragt.";
        GuestLogger.Info("sharedfolders.credentials.cleared", "Gespeicherte SMB-Credentials wurden gelöscht.");
    }

    private string? ResolveSharedFolderHostTarget()
    {
        var hostName = (_config.Usb?.HostName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            return hostName;
        }

        return (_config.Usb?.HostAddress ?? string.Empty).Trim();
    }

    private void UpdateHostDiscoveryPresentation()
    {
        var hostName = (_config.Usb?.HostName ?? string.Empty).Trim();
        var hostAddress = (_config.Usb?.HostAddress ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(hostName))
        {
            _sharedFolderHostDiscoveryText.Text = $"Ermittelter Hostname: {hostName}";
            _usbResolvedHostNameText.Text = $"Ermittelter Hostname: {hostName}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(hostAddress))
        {
            _sharedFolderHostDiscoveryText.Text = $"Kein Hostname gefunden · Fallback-Ziel: {hostAddress}";
            _usbResolvedHostNameText.Text = $"Kein Hostname gefunden · Fallback-Ziel: {hostAddress}";
            return;
        }

        _sharedFolderHostDiscoveryText.Text = "Ermittelter Hostname: -";
        _usbResolvedHostNameText.Text = "Ermittelter Hostname: -";
    }

    private async Task SaveSharedFolderMappingsToConfigAsync()
    {
        _config.SharedFolders ??= new GuestSharedFolderSettings();
        _config.SharedFolders.Mappings = _sharedFolderMappings
            .Select(item => new GuestSharedFolderMapping
            {
                Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                Label = string.IsNullOrWhiteSpace(item.Label) ? item.SharePath : item.Label,
                SharePath = item.SharePath,
                DriveLetter = GuestConfigService.NormalizeDriveLetter(item.DriveLetter),
                Persistent = true,
                Enabled = item.Enabled
            })
            .ToList();

        await _saveConfigAsync(_config);
    }

    private static void EnsureUniqueDriveLetters(IList<GuestSharedFolderMapping> mappings)
    {
        var usedLetters = new HashSet<char>();

        foreach (var mapping in mappings)
        {
            var normalizedLetter = GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter)[0];
            if (usedLetters.Contains(normalizedLetter))
            {
                mapping.DriveLetter = GetNextAvailableDriveLetter(usedLetters);
                normalizedLetter = mapping.DriveLetter[0];
            }

            usedLetters.Add(normalizedLetter);
            mapping.DriveLetter = normalizedLetter.ToString();
            mapping.Persistent = true;
        }
    }

    private static string GetNextAvailableDriveLetter(ISet<char> usedLetters)
    {
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            if (!usedLetters.Contains(letter))
            {
                return letter.ToString();
            }
        }

        return "Z";
    }

    private bool ValidateSharedFolderMappings(out string error)
    {
        error = string.Empty;

        var enabledMappings = _sharedFolderMappings.Where(mapping => mapping.Enabled).ToList();
        if (enabledMappings.Count == 0)
        {
            return true;
        }

        foreach (var mapping in _sharedFolderMappings)
        {
            mapping.DriveLetter = GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter);
            mapping.Persistent = true;
        }

        var duplicateLetters = enabledMappings
            .Select(mapping => GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter))
            .Where(letter => !string.IsNullOrWhiteSpace(letter))
            .GroupBy(letter => letter, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(static letter => letter, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (duplicateLetters.Count > 0)
        {
            error = $"Laufwerksbuchstaben dürfen nicht doppelt sein (aktiv): {string.Join(", ", duplicateLetters)}.";
            return false;
        }

        return true;
    }

    private async Task SyncSharedFoldersFromHostAsync()
    {
        if (!await _sharedFolderUiOperationGate.WaitAsync(TimeSpan.FromSeconds(8)))
        {
            _sharedFolderStatusText.Text = "Shared-Folder Aktion läuft noch. Bitte in ein paar Sekunden erneut versuchen.";
            return;
        }

        try
        {
            var hostFolders = await _fetchHostSharedFoldersAsync();
            var existingByShareName = _sharedFolderMappings
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SharePath))
                .Select(mapping => new
                {
                    Mapping = mapping,
                    ShareName = ExtractShareNameFromUncPath(mapping.SharePath)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ShareName))
                .GroupBy(item => item.ShareName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Mapping, StringComparer.OrdinalIgnoreCase);

            var resolvedTarget = ResolveSharedFolderHostTarget();
            var synchronizedMappings = new List<GuestSharedFolderMapping>();
            var importedCount = 0;

            foreach (var hostFolder in hostFolders.Where(item => item.Enabled && !string.IsNullOrWhiteSpace(item.ShareName)))
            {
                var normalizedShareName = hostFolder.ShareName.Trim();
                var sharePath = BuildCatalogSharePath(normalizedShareName, resolvedTarget);

                if (existingByShareName.TryGetValue(normalizedShareName, out var existing))
                {
                    synchronizedMappings.Add(new GuestSharedFolderMapping
                    {
                        Id = string.IsNullOrWhiteSpace(existing.Id) ? Guid.NewGuid().ToString("N") : existing.Id,
                        Label = string.IsNullOrWhiteSpace(existing.Label) ? normalizedShareName : existing.Label,
                        SharePath = sharePath,
                        DriveLetter = GuestConfigService.NormalizeDriveLetter(existing.DriveLetter),
                        Persistent = true,
                        Enabled = existing.Enabled,
                        MountStateDot = existing.MountStateDot,
                        MountStateText = existing.MountStateText
                    });
                    continue;
                }

                synchronizedMappings.Add(new GuestSharedFolderMapping
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = normalizedShareName,
                    SharePath = sharePath,
                    DriveLetter = string.Empty,
                    Persistent = true,
                    Enabled = false
                });

                importedCount++;
            }

            var removedCount = Math.Max(0, _sharedFolderMappings.Count - synchronizedMappings.Count);

            EnsureUniqueDriveLetters(synchronizedMappings);

            _suppressSharedFolderAutoApply = true;
            try
            {
                foreach (var existing in _sharedFolderMappings)
                {
                    existing.PropertyChanged -= OnSharedFolderMappingPropertyChanged;
                }

                _sharedFolderMappings.Clear();
                foreach (var mapping in synchronizedMappings)
                {
                    _sharedFolderMappings.Add(mapping);
                    mapping.PropertyChanged += OnSharedFolderMappingPropertyChanged;
                }
            }
            finally
            {
                _suppressSharedFolderAutoApply = false;
            }

            _config.SharedFolders ??= new GuestSharedFolderSettings();
            await SaveSharedFolderMappingsToConfigAsync();
            await RefreshSharedFolderMountStatesSafeAsync();
            UpdateHostDiscoveryPresentation();

            if (synchronizedMappings.Count == 0)
            {
                _sharedFolderStatusText.Text = "Keine Shared-Folder vom Host empfangen.";
                return;
            }

            var targetSuffix = string.IsNullOrWhiteSpace(resolvedTarget)
                ? string.Empty
                : $" · Ziel: {resolvedTarget}";

            _sharedFolderStatusText.Text = removedCount == 0
                ? (importedCount == 0
                    ? $"Host-Liste geladen, keine Änderungen.{targetSuffix}"
                    : $"Host-Liste geladen: {importedCount} neue Shared-Folder.{targetSuffix}")
                : $"Host-Liste geladen: {importedCount} neu, {removedCount} veraltete Einträge entfernt.{targetSuffix}";
        }
        catch (Exception ex)
        {
            _sharedFolderStatusText.Text = $"Host-Liste konnte nicht geladen werden: {ex.Message}";
            AppendNotification($"[Warn] Host Shared-Folder Sync fehlgeschlagen: {ex.Message}");
            _sharedFolderLastError = ex.Message;
        }
        finally
        {
            _sharedFolderUiOperationGate.Release();
        }
    }

    private static string BuildCatalogSharePath(string shareName, string? hostTarget)
    {
        var normalizedShareName = (shareName ?? string.Empty).Trim();
        var normalizedHostTarget = (hostTarget ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedHostTarget))
        {
            normalizedHostTarget = "HOST";
        }

        return $"\\\\{normalizedHostTarget}\\{normalizedShareName}";
    }

    private static string ExtractShareNameFromUncPath(string? uncPath)
    {
        var normalizedPath = (uncPath ?? string.Empty).Trim();
        if (!normalizedPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var withoutPrefix = normalizedPath[2..];
        var firstSlash = withoutPrefix.IndexOf('\\');
        if (firstSlash <= 0)
        {
            return string.Empty;
        }

        var rest = withoutPrefix[(firstSlash + 1)..];
        var secondSlash = rest.IndexOf('\\');
        var shareName = (secondSlash >= 0 ? rest[..secondSlash] : rest).Trim();
        return shareName;
    }

    private async Task RunSharedFolderSelfTestAsync()
    {
        if (!await _sharedFolderUiOperationGate.WaitAsync(TimeSpan.FromSeconds(8)))
        {
            _sharedFolderStatusText.Text = "Shared-Folder Aktion läuft noch. Bitte in ein paar Sekunden erneut versuchen.";
            return;
        }

        try
        {
            var hostFolders = await _fetchHostSharedFoldersAsync();
            var hostShareNames = new HashSet<string>(
                hostFolders
                    .Where(folder => folder.Enabled && !string.IsNullOrWhiteSpace(folder.ShareName))
                    .Select(folder => folder.ShareName.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var enabledMappings = _sharedFolderMappings.Where(mapping => mapping.Enabled).ToList();
            var sharePresentCount = 0;
            var mappingPresentCount = 0;

            foreach (var mapping in enabledMappings)
            {
                var shareName = ExtractShareNameFromUncPath(mapping.SharePath);

                if (!string.IsNullOrWhiteSpace(shareName) && hostShareNames.Contains(shareName))
                {
                    sharePresentCount++;
                }

                var driveLetter = GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter);
                var mappingStatus = await _driveMappingService.QueryMappingAsync(driveLetter, CancellationToken.None);
                if (mappingStatus.Exists && _driveMappingService.IsExpectedMapping(mappingStatus.RemotePath, mapping.SharePath, ResolveSharedFolderHostTarget()))
                {
                    mappingPresentCount++;
                }
            }

            var sharePresentText = enabledMappings.Count == 0
                ? "n/a"
                : (sharePresentCount == enabledMappings.Count ? "Ja" : $"Teilweise ({sharePresentCount}/{enabledMappings.Count})");
            var mappingPresentText = enabledMappings.Count == 0
                ? "n/a"
                : (mappingPresentCount == enabledMappings.Count ? "Ja" : $"Teilweise ({mappingPresentCount}/{enabledMappings.Count})");

            _sharedFolderStatusText.Text = $"Self-Test · Share vorhanden: {sharePresentText} · Mapping vorhanden: {mappingPresentText} · Letzter Fehler: {_sharedFolderLastError}";
        }
        catch (Exception ex)
        {
            _sharedFolderLastError = ex.Message;
            _sharedFolderStatusText.Text = $"Self-Test fehlgeschlagen: {ex.Message}";
            GuestLogger.Warn("sharedfolders.selftest.failed", ex.Message, new
            {
                exceptionType = ex.GetType().FullName
            });
        }
        finally
        {
            _sharedFolderUiOperationGate.Release();
        }
    }

    private UIElement BuildSettingsPage()
    {
        var root = new StackPanel { Spacing = 12 };

        var headingCard = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["SurfaceSoftBrush"] as Brush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14)
        };
        headingCard.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Konfiguration", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                new TextBlock { Text = "Wichtige Einstellungen übersichtlich und schnell erreichbar.", Opacity = 0.9 }
            }
        };
        root.Children.Add(headingCard);

        var topBarGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        var saveButton = CreateIconButton("💾", "Speichern", onClick: async (_, _) => await SaveSettingsAsync());
        Grid.SetColumn(saveButton, 0);
        topBarGrid.Children.Add(saveButton);

        var reloadButton = CreateIconButton("⟳", "Neu laden", onClick: (_, _) =>
        {
            ApplyConfigToControls();
            AppendNotification("[Info] Einstellungen aus Config neu geladen.");
        });
        Grid.SetColumn(reloadButton, 1);
        topBarGrid.Children.Add(reloadButton);

        var configPathWrap = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        configPathWrap.Children.Add(new TextBlock
        {
            Text = "Aktive Config:",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.9,
            Foreground = Application.Current.Resources["TextMutedBrush"] as Brush
        });
        configPathWrap.Children.Add(new TextBlock
        {
            Text = GuestConfigService.DefaultConfigPath,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.88,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 560
        });
        Grid.SetColumn(configPathWrap, 2);
        topBarGrid.Children.Add(configPathWrap);

        var topBar = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["SurfaceSoftBrush"] as Brush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = topBarGrid
        };
        root.Children.Add(topBar);

        var systemSection = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14)
        };

        var systemStack = new StackPanel { Spacing = 8 };
        systemStack.Children.Add(new TextBlock { Text = "System & Updates", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });

        var quickTogglesGrid = new Grid { ColumnSpacing = 10, RowSpacing = 4 };
        quickTogglesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickTogglesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickTogglesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        quickTogglesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetColumn(_minimizeToTrayCheckBox, 0);
        Grid.SetRow(_minimizeToTrayCheckBox, 0);
        quickTogglesGrid.Children.Add(_minimizeToTrayCheckBox);

        Grid.SetColumn(_startMinimizedCheckBox, 1);
        Grid.SetRow(_startMinimizedCheckBox, 0);
        quickTogglesGrid.Children.Add(_startMinimizedCheckBox);

        Grid.SetColumn(_startWithWindowsCheckBox, 0);
        Grid.SetRow(_startWithWindowsCheckBox, 1);
        quickTogglesGrid.Children.Add(_startWithWindowsCheckBox);

        _checkForUpdatesOnStartupCheckBox.Margin = new Thickness(0);
        _checkForUpdatesOnStartupCheckBox.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(_checkForUpdatesOnStartupCheckBox, 1);
        Grid.SetRow(_checkForUpdatesOnStartupCheckBox, 1);
        quickTogglesGrid.Children.Add(_checkForUpdatesOnStartupCheckBox);

        systemStack.Children.Add(quickTogglesGrid);

        _minimizeToTrayCheckBox.Margin = new Thickness(0);
        _startMinimizedCheckBox.Margin = new Thickness(0);
        _startWithWindowsCheckBox.Margin = new Thickness(0);

        var themeRow = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
        themeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        themeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        themeRow.Children.Add(new TextBlock
        {
            Text = "Dark Mode",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources["TextMutedBrush"] as Brush
        });

        _themeToggle.OnContent = "An";
        _themeToggle.OffContent = "Aus";
        _themeToggle.HorizontalAlignment = HorizontalAlignment.Left;
        _themeToggle.MinWidth = 86;
        if (!_isThemeToggleHandlerAttached)
        {
            _themeToggle.Toggled += async (_, _) =>
            {
                if (_suppressThemeEvents)
                {
                    return;
                }

                _themeCombo.SelectedItem = _themeToggle.IsOn ? "dark" : "light";
                await ApplyThemeAndRestartImmediatelyAsync();
            };
            _isThemeToggleHandlerAttached = true;
        }
        Grid.SetColumn(_themeToggle, 1);
        themeRow.Children.Add(_themeToggle);

        systemStack.Children.Add(themeRow);
        systemSection.Child = systemStack;
        root.Children.Add(systemSection);

        var usbSection = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14)
        };

        var usbStack = new StackPanel { Spacing = 8 };

        _usbHyperVModeIconBadge.Child = _usbHyperVModeIcon;
        _usbIpModeIconBadge.Child = _usbIpModeIcon;

        var hyperVModeBadgeContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        hyperVModeBadgeContent.Children.Add(_usbHyperVModeIconBadge);
        hyperVModeBadgeContent.Children.Add(_usbHyperVModeBadgeText);
        _usbHyperVModeBadge.Child = hyperVModeBadgeContent;

        var ipModeBadgeContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        ipModeBadgeContent.Children.Add(_usbIpModeIconBadge);
        ipModeBadgeContent.Children.Add(_usbIpModeBadgeText);
        _usbIpModeBadge.Child = ipModeBadgeContent;

        if (!_isUsbModeBadgeHandlersAttached)
        {
            _usbHyperVModeBadge.Tapped += (_, _) =>
            {
                if (_useHyperVSocketCheckBox.IsChecked != true)
                {
                    _useHyperVSocketCheckBox.IsChecked = true;
                }
            };

            _usbIpModeBadge.Tapped += (_, _) =>
            {
                if (_useHyperVSocketCheckBox.IsChecked != false)
                {
                    _useHyperVSocketCheckBox.IsChecked = false;
                }
            };

            _isUsbModeBadgeHandlersAttached = true;
        }

        var modeBadgeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        modeBadgeRow.Children.Add(_usbHyperVModeBadge);
        modeBadgeRow.Children.Add(_usbIpModeBadge);

        var usbHeaderGrid = new Grid { ColumnSpacing = 10 };
        usbHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        usbHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var usbHeaderTextStack = new StackPanel { Spacing = 4 };
        var usbHeaderTitleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        usbHeaderTitleRow.Children.Add(new TextBlock
        {
            Text = "USB Host-Verbindung",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.9
        });
        usbHeaderTitleRow.Children.Add(new TextBlock
        {
            Text = "(Hyper-V Socket kann bevorzugt genutzt werden, IP Modus als Fallback)",
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.86
        });
        usbHeaderTextStack.Children.Add(usbHeaderTitleRow);
        usbHeaderGrid.Children.Add(usbHeaderTextStack);

        Grid.SetColumn(modeBadgeRow, 1);
        usbHeaderGrid.Children.Add(modeBadgeRow);
        usbStack.Children.Add(usbHeaderGrid);

        _useHyperVSocketCheckBox.Margin = new Thickness(0, 2, 0, 0);
        if (!_isUsbTransportToggleHandlerAttached)
        {
            _useHyperVSocketCheckBox.Checked += async (_, _) => await OnUsbTransportModeToggledAsync();
            _useHyperVSocketCheckBox.Unchecked += async (_, _) => await OnUsbTransportModeToggledAsync();
            _isUsbTransportToggleHandlerAttached = true;
        }
        usbStack.Children.Add(_useHyperVSocketCheckBox);

        _usbModeHintText.Foreground = Application.Current.Resources["TextMutedBrush"] as Brush;
        usbStack.Children.Add(_usbModeHintText);

        _usbHostAddressEditorCard.BorderThickness = new Thickness(0);
        _usbHostAddressEditorCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        _usbHostAddressEditorCard.Background = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        _usbHostAddressEditorCard.Padding = new Thickness(0);

        _usbHostAddressTextBox.PlaceholderText = "Beispiel: HOSTNAME oder 172.25.80.1";
        _usbHostAddressTextBox.MinWidth = 420;
        _usbHostAddressTextBox.MaxWidth = 620;
        _usbHostAddressTextBox.HorizontalAlignment = HorizontalAlignment.Left;
        _usbHostAddressTextBox.CornerRadius = new CornerRadius(8);
        _usbHostAddressTextBox.TextChanged += (_, _) => UpdateUsbTransportModePresentation();

        var usbHostAddressRow = new Grid { ColumnSpacing = 8 };
        usbHostAddressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        usbHostAddressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        usbHostAddressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        usbHostAddressRow.Children.Add(_usbHostAddressTextBox);

        _usbHostSearchButton = CreateIconButton("🔎", "Host suchen", onClick: async (_, _) => await SearchUsbHostAddressAsync());
        _usbHostSearchButton.HorizontalAlignment = HorizontalAlignment.Left;
        _usbHostSearchButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_usbHostSearchButton, 1);
        usbHostAddressRow.Children.Add(_usbHostSearchButton);

        SetUsbHostSearchStatus("Bereit", UsbHostSearchStatusKind.Neutral);
        Grid.SetColumn(_usbHostSearchStatusText, 2);
        usbHostAddressRow.Children.Add(_usbHostSearchStatusText);

        _usbHostAddressEditorCard.Child = usbHostAddressRow;
        usbStack.Children.Add(_usbHostAddressEditorCard);
        usbStack.Children.Add(_usbResolvedHostNameText);

        UpdateUsbTransportModePresentation();
        UpdateHostDiscoveryPresentation();

        usbSection.Child = usbStack;
        root.Children.Add(usbSection);

        return new ScrollViewer { Content = root };
    }

    private UIElement BuildInfoPage()
    {
        var version = ResolveGuestVersionText();

        var panel = new StackPanel { Spacing = 10 };
        var titleWrap = new Grid();
        titleWrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleWrap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titleWrap.Children.Add(new TextBlock { Text = "Info", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var versionWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom };
        versionWrap.Children.Add(new TextBlock { Text = "Version:", Opacity = 0.9, VerticalAlignment = VerticalAlignment.Bottom });
        versionWrap.Children.Add(new TextBlock { Opacity = 0.9, Text = version });
        Grid.SetColumn(versionWrap, 1);
        titleWrap.Children.Add(versionWrap);

        panel.Children.Add(titleWrap);

        var infoStatusRow = new Grid { ColumnSpacing = 8 };
        infoStatusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoStatusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var updateWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        updateWrap.Children.Add(new TextBlock { Text = "Update-Status:", Opacity = 0.9 });
        updateWrap.Children.Add(_updateStatusValueText);

        var copyrightText = new TextBlock
        {
            Text = "Copyright: koerby",
            Opacity = 0.9,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(updateWrap, 0);
        infoStatusRow.Children.Add(updateWrap);
        Grid.SetColumn(copyrightText, 1);
        infoStatusRow.Children.Add(copyrightText);
        panel.Children.Add(infoStatusRow);

        var projectCard = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PageBackgroundBrush"] as Brush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10)
        };

        var projectStack = new StackPanel { Spacing = 4 };
        projectStack.Children.Add(new TextBlock { Text = "HyperTool Projekt", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        projectStack.Children.Add(new TextBlock { Text = "HyperTool Guest wird über GitHub Releases verteilt. Hier findest du Version, Update-Status und Release-Links.", TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        projectStack.Children.Add(new TextBlock { Text = "GitHub Owner: koerby", Opacity = 0.9 });
        projectStack.Children.Add(new TextBlock { Text = "GitHub Repo: HyperTool", Opacity = 0.9 });
        projectCard.Child = projectStack;
        panel.Children.Add(projectCard);

        var usbipCard = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PageBackgroundBrush"] as Brush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10)
        };

        var usbipStack = new StackPanel { Spacing = 4 };
        usbipStack.Children.Add(new TextBlock { Text = "Externe USB/IP Quelle", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        usbipStack.Children.Add(new TextBlock { Text = "Quelle: vadimgrn/usbip-win2", Opacity = 0.9 });
        usbipStack.Children.Add(new TextBlock { Text = "Nutzung in HyperTool: externer CLI-Client ohne eigene GUI-Integration.", TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        usbipStack.Children.Add(new TextBlock { Text = "Lizenz/Eigentümer: siehe Original-Repository von vadimgrn.", TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        usbipCard.Child = usbipStack;
        panel.Children.Add(usbipCard);

        var diagnosticsCard = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PageBackgroundBrush"] as Brush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10)
        };

        var diagnosticsStack = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 240, 0)
        };
        diagnosticsStack.Children.Add(new TextBlock
        {
            Text = "USB Transport Diagnose (live)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var hyperVSocketRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        hyperVSocketRow.Children.Add(new TextBlock { Text = "Hyper-V Socket aktiv:", Opacity = 0.9, VerticalAlignment = VerticalAlignment.Center });
        hyperVSocketRow.Children.Add(_diagHyperVSocketText);
        diagnosticsStack.Children.Add(hyperVSocketRow);

        var registryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        registryRow.Children.Add(new TextBlock { Text = "Registry-Service erreichbar:", Opacity = 0.9, VerticalAlignment = VerticalAlignment.Center });
        registryRow.Children.Add(_diagRegistryServiceText);
        diagnosticsStack.Children.Add(registryRow);

        var fallbackRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        fallbackRow.Children.Add(new TextBlock { Text = "Fallback auf IP aktiv:", Opacity = 0.9, VerticalAlignment = VerticalAlignment.Center });
        fallbackRow.Children.Add(_diagFallbackText);
        diagnosticsStack.Children.Add(fallbackRow);

        var diagnosticsLayout = new Grid();
        diagnosticsLayout.Children.Add(diagnosticsStack);

        var diagnosticsTestButton = CreateIconButton("🧪", "Hyper-V Socket testen", onClick: async (_, _) => await RunTransportDiagnosticsTestAsync());
        diagnosticsTestButton.HorizontalAlignment = HorizontalAlignment.Right;
        diagnosticsTestButton.VerticalAlignment = VerticalAlignment.Top;
        diagnosticsLayout.Children.Add(diagnosticsTestButton);

        diagnosticsCard.Child = diagnosticsLayout;
        panel.Children.Add(diagnosticsCard);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttonRow.Children.Add(CreateIconButton("🛰", "Update prüfen", onClick: async (_, _) => await CheckForUpdatesAsync()));
        _installUpdateButton = CreateIconButton("⬇", "Update installieren", onClick: async (_, _) => await InstallUpdateAsync());
        _installUpdateButton.IsEnabled = CanInstallUpdate();
        buttonRow.Children.Add(_installUpdateButton);
        buttonRow.Children.Add(CreateIconButton("🌐", "Changelog / Release", onClick: (_, _) => OpenReleasePage()));
        buttonRow.Children.Add(CreateIconButton("🔗", "usbip-win2 Quelle", onClick: (_, _) => OpenUsbipClientRepository()));
        panel.Children.Add(buttonRow);

        return new ScrollViewer { Content = panel };
    }

    private static string ResolveGuestVersionText()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var raw = string.IsNullOrWhiteSpace(informationalVersion)
            ? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            : informationalVersion;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return "2.1.0";
        }

        var sanitized = raw.Split('+', 2)[0].Trim();

        if (Version.TryParse(sanitized, out var parsed))
        {
            return parsed.Revision == 0
                ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}"
                : parsed.ToString();
        }

        return sanitized;
    }

    private void UpdatePageContent()
    {
        _pageContent.Content = _selectedMenuIndex switch
        {
            0 => _usbPage ??= BuildUsbPage(),
            1 => _sharedFoldersPage ??= BuildSharedFoldersPage(),
            2 => GetOrCreateSettingsPage(),
            _ => _infoPage ??= BuildInfoPage()
        };
    }

    private UIElement GetOrCreateSettingsPage()
    {
        _settingsPage ??= BuildSettingsPage();
        ApplyConfigToControls();
        return _settingsPage;
    }

    private void UpdateNavSelection()
    {
        for (var index = 0; index < _navButtons.Count; index++)
        {
            var isSelected = index == _selectedMenuIndex;
            _navButtons[index].Background = isSelected
                ? Application.Current.Resources["AccentSoftBrush"] as Brush
                : Application.Current.Resources["SurfaceSoftBrush"] as Brush;
            _navButtons[index].BorderBrush = isSelected
                ? Application.Current.Resources["AccentBrush"] as Brush
                : Application.Current.Resources["PanelBorderBrush"] as Brush;
        }
    }

    private void ApplyConfigToControls()
    {
        var isDark = string.Equals(GuestConfigService.NormalizeTheme(_config.Ui.Theme), "dark", StringComparison.OrdinalIgnoreCase);

        _suppressThemeEvents = true;
        _themeCombo.SelectedItem = isDark ? "dark" : "light";
        _themeToggle.IsOn = isDark;
        _suppressThemeEvents = false;

        _themeText.Text = isDark ? "Dunkles Theme" : "Helles Theme";

        _usbHostAddressTextBox.Text = (_config.Usb?.HostAddress ?? string.Empty).Trim();
        _startWithWindowsCheckBox.IsChecked = _config.Ui.StartWithWindows;
        _startMinimizedCheckBox.IsChecked = _config.Ui.StartMinimized;
        _minimizeToTrayCheckBox.IsChecked = _config.Ui.MinimizeToTray;
        _checkForUpdatesOnStartupCheckBox.IsChecked = _config.Ui.CheckForUpdatesOnStartup;

        _suppressUsbTransportToggleEvents = true;
        try
        {
            _useHyperVSocketCheckBox.IsChecked = _config.Usb?.UseHyperVSocket != false;
        }
        finally
        {
            _suppressUsbTransportToggleEvents = false;
        }

        UpdateUsbTransportModePresentation();
        UpdateHostDiscoveryPresentation();
        UpdateAutoConnectToggleFromSelection();

        RefreshSharedFolderMappingsFromConfig();
        if (_sharedFoldersPage is not null)
        {
            _sharedFolderMappingsListView.ItemsSource = null;
            _sharedFolderMappingsListView.ItemsSource = _sharedFolderMappings;
            _ = RefreshSharedFolderMountStatesSafeAsync();
        }
    }

    private async Task SaveSettingsAsync()
    {
        _config.Ui.Theme = (_themeCombo.SelectedItem as string) ?? "dark";
        _config.Ui.StartWithWindows = _startWithWindowsCheckBox.IsChecked == true;
        _config.Ui.StartMinimized = _startMinimizedCheckBox.IsChecked == true;
        _config.Ui.MinimizeToTray = _minimizeToTrayCheckBox.IsChecked == true;
        _config.Ui.CheckForUpdatesOnStartup = _checkForUpdatesOnStartupCheckBox.IsChecked != false;
        _config.Usb ??= new GuestUsbSettings();
        _config.Usb.UseHyperVSocket = _useHyperVSocketCheckBox.IsChecked != false;
        _config.Usb.HostAddress = (_usbHostAddressTextBox.Text ?? string.Empty).Trim();

        await _saveConfigAsync(_config);
        ApplyTheme(_config.Ui.Theme);
        UpdateUsbTransportModePresentation();
        UpdateHostDiscoveryPresentation();

        AppendNotification("[Info] Einstellungen gespeichert.");
    }

    private async Task OnUsbTransportModeToggledAsync()
    {
        if (_suppressUsbTransportToggleEvents)
        {
            return;
        }

        _config.Usb ??= new GuestUsbSettings();
        _config.Usb.UseHyperVSocket = _useHyperVSocketCheckBox.IsChecked != false;

        UpdateUsbTransportModePresentation();

        await _saveConfigAsync(_config);
        AppendNotification(_config.Usb.UseHyperVSocket
            ? "[Info] USB Transportmodus: Hyper-V Socket bevorzugt."
            : "[Info] USB Transportmodus: IP-Mode aktiv.");

        if (_config.Usb.UseHyperVSocket)
        {
            ScheduleUsbTransportAutoRefresh();
        }
        else
        {
            CancelPendingUsbTransportAutoRefresh();
        }
    }

    private void CancelPendingUsbTransportAutoRefresh()
    {
        if (_usbTransportAutoRefreshCts is null)
        {
            return;
        }

        try
        {
            _usbTransportAutoRefreshCts.Cancel();
        }
        catch
        {
        }

        _usbTransportAutoRefreshCts.Dispose();
        _usbTransportAutoRefreshCts = null;
    }

    private void ScheduleUsbTransportAutoRefresh()
    {
        CancelPendingUsbTransportAutoRefresh();

        var cts = new CancellationTokenSource();
        _usbTransportAutoRefreshCts = cts;
        _ = RunUsbTransportAutoRefreshAsync(cts.Token);
    }

    private async Task RunUsbTransportAutoRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (_useHyperVSocketCheckBox.IsChecked != true || _config.Usb?.UseHyperVSocket != true)
            {
                return;
            }

            AppendNotification("[Info] Auto-Refresh nach Hyper-V Socket Aktivierung …");
            await RefreshUsbAsync();
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendNotification($"[Warn] Auto-Refresh nach Hyper-V Socket Aktivierung fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            if (_usbTransportAutoRefreshCts is not null && _usbTransportAutoRefreshCts.Token == cancellationToken)
            {
                _usbTransportAutoRefreshCts.Dispose();
                _usbTransportAutoRefreshCts = null;
            }
        }
    }

    private void UpdateUsbTransportModePresentation()
    {
        var useHyperVSocket = _useHyperVSocketCheckBox.IsChecked != false;
        var hyperVSocketLive = string.Equals(_diagHyperVSocketText.Text, "Ja", StringComparison.OrdinalIgnoreCase)
                              && string.Equals(_diagRegistryServiceText.Text, "Ja", StringComparison.OrdinalIgnoreCase)
                              && !string.Equals(_diagFallbackText.Text, "Ja", StringComparison.OrdinalIgnoreCase);
        var ipModeActive = !useHyperVSocket || !hyperVSocketLive;

        _usbHostAddressEditorCard.Visibility = ipModeActive ? Visibility.Visible : Visibility.Collapsed;
        _usbHostAddressTextBox.IsEnabled = ipModeActive;
        if (_usbHostSearchButton is not null)
        {
            _usbHostSearchButton.IsEnabled = ipModeActive;
        }
        _usbHostSearchStatusText.Visibility = ipModeActive ? Visibility.Visible : Visibility.Collapsed;

        if (useHyperVSocket)
        {
            _usbModeHintText.Text = ipModeActive
                ? "Hyper-V Socket ist aktiviert, aktuell aber nicht aktiv. IP-Mode/Fallback nutzt die Host-Adresse unten."
                : "Hyper-V Socket ist aktiviert. Bei Verfügbarkeitsproblemen wird auf IP zurückgefallen.";
        }
        else
        {
            _usbModeHintText.Text = "IP-Mode ist aktiviert. Die Host-Adresse unten wird für USB Connect verwendet.";
        }

        UpdateUsbTransportHeaderStatus();
    }

    private void UpdateUsbTransportHeaderStatus()
    {
        var useHyperVSocket = _config.Usb?.UseHyperVSocket != false;
        var hyperVSocketReportedActive = string.Equals(_diagHyperVSocketText.Text, "Ja", StringComparison.OrdinalIgnoreCase);
        var registryServicePresent = string.Equals(_diagRegistryServiceText.Text, "Ja", StringComparison.OrdinalIgnoreCase);
        var fallbackActive = string.Equals(_diagFallbackText.Text, "Ja", StringComparison.OrdinalIgnoreCase);
        var hyperVSocketLive = hyperVSocketReportedActive && registryServicePresent && !fallbackActive;

        if (useHyperVSocket)
        {
            _usbTransportModeBadgeText.Text = hyperVSocketLive
                ? "Hyper-Socket aktiv"
                : (fallbackActive ? "IP-Fallback aktiv" : "Hyper-Socket bevorzugt");
            var palette = fallbackActive
                ? ResolveUsbModePalette(forHyperV: false, isActive: true)
                : ResolveUsbModePalette(forHyperV: true, isActive: hyperVSocketLive);
            _usbTransportModeBadge.Background = palette.chipBackground;
            _usbTransportModeBadge.BorderBrush = palette.chipBorder;
            _usbTransportModeBadgeText.Foreground = palette.textForeground;
            UpdateUsbTransportModeBadges(useHyperVSocket: true, hyperVSocketLive: hyperVSocketLive, fallbackActive: fallbackActive);
            return;
        }

        var configuredHost = (_usbHostAddressTextBox.Text ?? _config.Usb?.HostAddress ?? string.Empty).Trim();
        var ipDisplay = string.IsNullOrWhiteSpace(configuredHost) ? "auto" : configuredHost;

        _usbTransportModeBadgeText.Text = $"IP-Mode: {ipDisplay}";
        var ipPalette = ResolveUsbModePalette(forHyperV: false, isActive: true);
        _usbTransportModeBadge.Background = ipPalette.chipBackground;
        _usbTransportModeBadge.BorderBrush = ipPalette.chipBorder;
        _usbTransportModeBadgeText.Foreground = ipPalette.textForeground;
        UpdateUsbTransportModeBadges(useHyperVSocket: false, hyperVSocketLive: false, fallbackActive: true);
    }

    private void UpdateUsbTransportModeBadges(bool useHyperVSocket, bool hyperVSocketLive, bool fallbackActive)
    {
        var hyperVPalette = ResolveUsbModePalette(forHyperV: true, isActive: useHyperVSocket && hyperVSocketLive);
        _usbHyperVModeBadge.Background = hyperVPalette.chipBackground;
        _usbHyperVModeBadge.BorderBrush = hyperVPalette.chipBorder;
        _usbHyperVModeIconBadge.Background = hyperVPalette.iconBackground;
        _usbHyperVModeIconBadge.BorderBrush = hyperVPalette.iconBorder;
        _usbHyperVModeIcon.Foreground = hyperVPalette.iconForeground;
        _usbHyperVModeBadgeText.Foreground = hyperVPalette.textForeground;

        var ipPalette = ResolveUsbModePalette(forHyperV: false, isActive: !useHyperVSocket || fallbackActive);
        _usbIpModeBadge.Background = ipPalette.chipBackground;
        _usbIpModeBadge.BorderBrush = ipPalette.chipBorder;
        _usbIpModeIconBadge.Background = ipPalette.iconBackground;
        _usbIpModeIconBadge.BorderBrush = ipPalette.iconBorder;
        _usbIpModeIcon.Foreground = ipPalette.iconForeground;
        _usbIpModeBadgeText.Foreground = ipPalette.textForeground;
    }

    private (Brush chipBackground, Brush chipBorder, Brush iconBackground, Brush iconBorder, Brush iconForeground, Brush textForeground) ResolveUsbModePalette(bool forHyperV, bool isActive)
    {
        static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));

        var isDarkMode = string.Equals(CurrentTheme, "dark", StringComparison.OrdinalIgnoreCase);

        if (!isActive)
        {
            return (
                Application.Current.Resources["SurfaceSoftBrush"] as Brush ?? Brush(0xFF, 0x20, 0x2A, 0x48),
                Application.Current.Resources["PanelBorderBrush"] as Brush ?? Brush(0xFF, 0x44, 0x57, 0x7F),
                Application.Current.Resources["PanelBackgroundBrush"] as Brush ?? Brush(0xFF, 0x18, 0x23, 0x3E),
                Application.Current.Resources["PanelBorderBrush"] as Brush ?? Brush(0xFF, 0x44, 0x57, 0x7F),
                Application.Current.Resources["TextMutedBrush"] as Brush ?? Brush(0xFF, 0xA6, 0xB9, 0xD8),
                Application.Current.Resources["TextMutedBrush"] as Brush ?? Brush(0xFF, 0xA6, 0xB9, 0xD8));
        }

        if (forHyperV)
        {
            if (isDarkMode)
            {
                return (
                    Brush(0xFF, 0x14, 0x3C, 0x2C),
                    Brush(0xFF, 0x43, 0xB5, 0x81),
                    Brush(0xFF, 0x43, 0xB5, 0x81),
                    Brush(0xFF, 0x43, 0xB5, 0x81),
                    Brush(0xFF, 0x09, 0x2D, 0x1E),
                    Brush(0xFF, 0xD9, 0xF6, 0xE8));
            }

            return (
                Brush(0xFF, 0xE8, 0xF8, 0xEF),
                Brush(0xFF, 0x2F, 0x9E, 0x68),
                Brush(0xFF, 0x2F, 0x9E, 0x68),
                Brush(0xFF, 0x2F, 0x9E, 0x68),
                Brush(0xFF, 0xF7, 0xFF, 0xFB),
                Brush(0xFF, 0x0E, 0x4F, 0x31));
        }

        if (isDarkMode)
        {
            return (
                Brush(0xFF, 0x47, 0x31, 0x1B),
                Brush(0xFF, 0xF2, 0x9A, 0x3A),
                Brush(0xFF, 0xF2, 0x9A, 0x3A),
                Brush(0xFF, 0xF2, 0x9A, 0x3A),
                Brush(0xFF, 0x2A, 0x1A, 0x08),
                Brush(0xFF, 0xFF, 0xE9, 0xCC));
        }

        return (
            Brush(0xFF, 0xFF, 0xF1, 0xDF),
            Brush(0xFF, 0xD7, 0x82, 0x2C),
            Brush(0xFF, 0xD7, 0x82, 0x2C),
            Brush(0xFF, 0xD7, 0x82, 0x2C),
            Brush(0xFF, 0xFF, 0xFA, 0xF3),
            Brush(0xFF, 0x6B, 0x3A, 0x0A));
    }

    private static string BuildAutoConnectKey(UsbIpDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.HardwareId))
        {
            return "hardware:" + device.HardwareId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.Description))
        {
            return "description:" + device.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.BusId))
        {
            return "busid:" + device.BusId.Trim();
        }

        return string.Empty;
    }

    private void UpdateAutoConnectToggleFromSelection()
    {
        var selected = GetSelectedUsbDevice();
        var keys = _config.Usb?.AutoConnectDeviceKeys ?? [];
        var key = selected is null ? string.Empty : BuildAutoConnectKey(selected);

        _suppressUsbAutoConnectToggleEvents = true;
        try
        {
            _usbAutoConnectCheckBox.IsEnabled = _isUsbClientAvailable && selected is not null;
            _usbAutoConnectCheckBox.IsChecked = selected is not null
                && !string.IsNullOrWhiteSpace(key)
                && keys.Contains(key, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _suppressUsbAutoConnectToggleEvents = false;
        }
    }

    private async Task SetSelectedUsbDeviceAutoConnectAsync(bool enabled)
    {
        if (_suppressUsbAutoConnectToggleEvents)
        {
            return;
        }

        var selected = GetSelectedUsbDevice();
        if (selected is null)
        {
            return;
        }

        var key = BuildAutoConnectKey(selected);
        if (string.IsNullOrWhiteSpace(key))
        {
            AppendNotification("[Warn] Auto-Connect konnte für dieses Gerät nicht gespeichert werden.");
            UpdateAutoConnectToggleFromSelection();
            return;
        }

        _config.Usb ??= new GuestUsbSettings();
        var keys = _config.Usb.AutoConnectDeviceKeys ?? [];
        var changed = false;

        if (enabled)
        {
            if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                keys.Add(key);
                changed = true;
            }
        }
        else
        {
            changed = keys.RemoveAll(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (!changed)
        {
            return;
        }

        _config.Usb.AutoConnectDeviceKeys = keys
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Select(static entry => entry.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _saveConfigAsync(_config);
        AppendNotification(enabled
            ? $"[Info] Auto-Connect aktiviert für: {selected.Description}"
            : $"[Info] Auto-Connect deaktiviert für: {selected.Description}");
    }

    public async Task CheckForUpdatesOnStartupIfEnabledAsync()
    {
        if (_config.Ui.CheckForUpdatesOnStartup != true)
        {
            return;
        }

        await CheckForUpdatesAsync();
    }

    private async Task ApplyThemeAndRestartImmediatelyAsync()
    {
        if (_isThemeRestartInProgress)
        {
            return;
        }

        var selectedTheme = GuestConfigService.NormalizeTheme((_themeCombo.SelectedItem as string) ?? _config.Ui.Theme);
        var currentTheme = GuestConfigService.NormalizeTheme(_config.Ui.Theme);
        if (string.Equals(selectedTheme, currentTheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _isThemeRestartInProgress = true;
        try
        {
            _config.Ui.Theme = selectedTheme;
            _themeText.Text = selectedTheme == "dark" ? "Dunkles Theme" : "Helles Theme";

            AppendNotification("[Info] Theme geändert – speichere und starte HyperTool Guest neu …");
            await _saveConfigAsync(_config);
            await _restartForThemeChangeAsync(selectedTheme);
        }
        finally
        {
            _isThemeRestartInProgress = false;
        }
    }

    private async Task RefreshUsbAsync()
    {
        try
        {
            var list = await _refreshUsbDevicesAsync();
            UpdateUsbDevices(list);
            AppendNotification($"[Info] {list.Count} USB-Gerät(e) geladen.");
        }
        catch (Exception ex)
        {
            AppendNotification($"[Error] USB Refresh fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task SearchUsbHostAddressAsync()
    {
        if (_discoverUsbHostAddressAsync is null)
        {
            return;
        }

        SetUsbHostSearchStatus("Suche läuft …", UsbHostSearchStatusKind.Running);

        if (_usbHostSearchButton is not null)
        {
            _usbHostSearchButton.IsEnabled = false;
        }

        try
        {
            AppendNotification("[Info] Suche Hostname (Hyper-V Socket), sonst IP-Fallback …");

            var discoveredTarget = (await _discoverUsbHostAddressAsync() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(discoveredTarget))
            {
                SetUsbHostSearchStatus("Kein Host gefunden", UsbHostSearchStatusKind.Error);
                AppendNotification("[Warn] Kein Hostname/IP gefunden. Stelle sicher, dass die Host-App läuft.");
                return;
            }

            _usbHostAddressTextBox.Text = discoveredTarget;
            _config.Usb ??= new GuestUsbSettings();
            _config.Usb.HostAddress = discoveredTarget;
            await _saveConfigAsync(_config);

            UpdateUsbTransportModePresentation();
            UpdateHostDiscoveryPresentation();

            var discoveredHostName = (_config.Usb.HostName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(discoveredHostName))
            {
                SetUsbHostSearchStatus($"Hostname gefunden: {discoveredHostName}", UsbHostSearchStatusKind.Success);
                AppendNotification($"[Info] Hostname gefunden: {discoveredHostName}");
            }
            else
            {
                SetUsbHostSearchStatus($"IP-Fallback gefunden: {discoveredTarget}", UsbHostSearchStatusKind.Success);
                AppendNotification($"[Info] Kein Hostname gefunden, IP-Fallback: {discoveredTarget}");
            }
        }
        catch (Exception ex)
        {
            SetUsbHostSearchStatus("Suche fehlgeschlagen", UsbHostSearchStatusKind.Error);
            AppendNotification($"[Warn] Host-Suche fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            if (_usbHostSearchButton is not null)
            {
                _usbHostSearchButton.IsEnabled = _usbHostAddressEditorCard.Visibility == Visibility.Visible;
            }
        }
    }

    private void SetUsbHostSearchStatus(string text, UsbHostSearchStatusKind statusKind)
    {
        _usbHostSearchStatusKind = statusKind;
        _usbHostSearchStatusText.Text = text;
        ApplyUsbHostSearchStatusColor();
    }

    private void ApplyUsbHostSearchStatusColor()
    {
        Brush? statusBrush = _usbHostSearchStatusKind switch
        {
            UsbHostSearchStatusKind.Success => Application.Current.Resources["SystemFillColorSuccessBrush"] as Brush,
            UsbHostSearchStatusKind.Error => Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush,
            UsbHostSearchStatusKind.Running => Application.Current.Resources["AccentStrongBrush"] as Brush,
            _ => Application.Current.Resources["TextMutedBrush"] as Brush
        };

        _usbHostSearchStatusText.Foreground = statusBrush
            ?? Application.Current.Resources["TextMutedBrush"] as Brush;
    }

    private async Task ConnectUsbAsync()
    {
        var selected = GetSelectedUsbDevice();
        if (selected is null)
        {
            AppendNotification("[Warn] Bitte ein USB-Gerät auswählen.");
            return;
        }

        var code = await _connectUsbAsync(selected.BusId);
        AppendNotification(code == 0
            ? "[Info] USB Host-Attach erfolgreich."
            : $"[Error] USB Host-Attach fehlgeschlagen (Code {code}).");

        await RefreshUsbAsync();
    }

    private async Task DisconnectUsbAsync()
    {
        var selected = GetSelectedUsbDevice();
        if (selected is null)
        {
            AppendNotification("[Warn] Bitte ein USB-Gerät auswählen.");
            return;
        }

        var code = await _disconnectUsbAsync(selected.BusId);
        AppendNotification(code == 0
            ? "[Info] USB Host-Detach erfolgreich."
            : $"[Error] USB Host-Detach fehlgeschlagen (Code {code}).");

        if (code == 0)
        {
            AppendNotification("[Info] Aktualisiere USB-Liste in 3 Sekunden …");
            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        await RefreshUsbAsync();
    }

    private void RunLogoEasterEgg()
    {
        _logoRotateTransform.Angle = 0;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 720,
            Duration = TimeSpan.FromMilliseconds(1400),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, _logoRotateTransform);
        Storyboard.SetTargetProperty(animation, nameof(RotateTransform.Angle));
        storyboard.Children.Add(animation);
        storyboard.Begin();

        try
        {
            var soundPath = IOPath.Combine(AppContext.BaseDirectory, "Assets", "logo-spin.wav");
            if (File.Exists(soundPath))
            {
                _logoSpinPlayer?.Dispose();

                var player = new MediaPlayer
                {
                    AudioCategory = MediaPlayerAudioCategory.SoundEffects,
                    Volume = 0.30,
                    Source = MediaSource.CreateFromUri(new Uri(soundPath))
                };

                player.MediaEnded += (_, _) =>
                {
                    try
                    {
                        player.Dispose();
                    }
                    catch
                    {
                    }

                    if (ReferenceEquals(_logoSpinPlayer, player))
                    {
                        _logoSpinPlayer = null;
                    }
                };

                _logoSpinPlayer = player;
                player.Play();
            }
            else
            {
                SystemSounds.Asterisk.Play();
            }
        }
        catch
        {
            SystemSounds.Asterisk.Play();
        }
    }

    private void OpenHelpWindow()
    {
        if (_helpWindow is not null)
        {
            try
            {
                _helpWindow.Activate();
                return;
            }
            catch (Exception ex)
            {
                GuestLogger.Warn("help.reopen.failed", "Vorhandenes Hilfe-Fenster konnte nicht erneut aktiviert werden. Es wird neu erstellt.", new
                {
                    exceptionType = ex.GetType().FullName,
                    ex.Message
                });

                try
                {
                    _helpWindow.Close();
                }
                catch
                {
                }

                _helpWindow = null;
            }
        }

        var repoUrl = "https://github.com/koerby/HyperTool";
        _helpWindow = new HelpWindow(GuestConfigService.DefaultConfigPath, repoUrl, CurrentTheme);
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Activate();
    }

    private void OpenReleasePage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(_releaseUrl) ? "https://github.com/koerby/HyperTool/releases" : _releaseUrl,
            UseShellExecute = true
        });
    }

    private void OpenUsbipClientRepository()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/vadimgrn/usbip-win2",
            UseShellExecute = true
        });
    }

    private async Task InstallGuestUsbRuntimeAsync()
    {
        if (_usbRuntimeInstallButton is not null)
        {
            _usbRuntimeInstallButton.IsEnabled = false;
        }

        try
        {
            AppendNotification("[Info] usbip-win2 Installer wird vorbereitet...");

            var installerResult = await _updateService.CheckForUpdateAsync(
                GuestUsbRuntimeOwner,
                GuestUsbRuntimeRepo,
                "0.0.0",
                CancellationToken.None,
                GuestUsbRuntimeAssetHint);

            if (!installerResult.Success || string.IsNullOrWhiteSpace(installerResult.InstallerDownloadUrl))
            {
                AppendNotification("[Warn] Installer-Asset konnte nicht automatisch ermittelt werden. Release-Seite wird geöffnet.");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/vadimgrn/usbip-win2/releases/latest",
                    UseShellExecute = true
                });
                return;
            }

            var targetDirectory = IOPath.Combine(IOPath.GetTempPath(), "HyperTool", "runtime-installers");
            Directory.CreateDirectory(targetDirectory);

            var fileName = ResolveInstallerFileName(
                installerResult.InstallerDownloadUrl,
                installerResult.InstallerFileName,
                "usbip-win2-x64.exe");

            var installerPath = IOPath.Combine(targetDirectory, fileName);

            AppendNotification($"[Info] Lade usbip-win2 herunter: {fileName}");
            using (var response = await UpdateDownloadClient.GetAsync(installerResult.InstallerDownloadUrl, CancellationToken.None))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(stream);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas"
            });

            AppendNotification("[Success] usbip-win2 Installer gestartet. Nach Abschluss App neu starten.");
        }
        catch (Exception ex)
        {
            AppendNotification($"[Error] Automatische usbip-win2 Installation fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            if (_usbRuntimeInstallButton is not null)
            {
                _usbRuntimeInstallButton.IsEnabled = true;
            }
        }
    }

    private void OpenLogFile()
    {
        var logDirectory = string.IsNullOrWhiteSpace(_config.Logging.DirectoryPath)
            ? GuestConfigService.DefaultLogDirectory
            : _config.Logging.DirectoryPath;

        Directory.CreateDirectory(logDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = logDirectory,
            UseShellExecute = true
        });
    }

    private void CopyNotificationsToClipboard()
    {
        var text = _notifications.Count == 0
            ? "Keine Notifications vorhanden."
            : string.Join(Environment.NewLine, _notifications.Select(entry => entry ?? string.Empty));

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        AppendNotification("[Info] Notifications in Zwischenablage kopiert.");
    }

    private async Task CheckForUpdatesAsync()
    {
        AppendNotification("[Info] Update-Prüfung gestartet.");

        var currentVersion = ResolveGuestVersionText();
        var result = await _updateService.CheckForUpdateAsync(
            UpdateOwner,
            UpdateRepo,
            currentVersion,
            CancellationToken.None,
            GuestInstallerAssetHint);

        _updateStatusValueText.Text = result.Message;
        _releaseUrl = string.IsNullOrWhiteSpace(result.ReleaseUrl) ? _releaseUrl : result.ReleaseUrl;
        _updateCheckSucceeded = result.Success;
        _updateAvailable = result.Success && result.HasUpdate;

        if (_updateAvailable)
        {
            _installerDownloadUrl = result.InstallerDownloadUrl ?? string.Empty;
            _installerFileName = result.InstallerFileName ?? string.Empty;
        }
        else
        {
            _installerDownloadUrl = string.Empty;
            _installerFileName = string.Empty;
        }

        if (_installUpdateButton is not null)
        {
            _installUpdateButton.IsEnabled = CanInstallUpdate();
        }

        if (!result.Success)
        {
            AppendNotification($"[Warn] {result.Message}");
            return;
        }

        if (result.HasUpdate)
        {
            if (string.IsNullOrWhiteSpace(_installerDownloadUrl))
            {
                AppendNotification("[Warn] Update gefunden, aber kein Guest-Installer-Asset erkannt.");
            }
            else
            {
                AppendNotification($"[Info] {result.Message}");
            }
        }
        else
        {
            AppendNotification("[Info] Bereits aktuell.");
        }
    }

    private bool CanInstallUpdate()
    {
        return _updateCheckSucceeded
            && _updateAvailable
            && !string.IsNullOrWhiteSpace(_installerDownloadUrl);
    }

    private async Task InstallUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(_installerDownloadUrl))
        {
            AppendNotification("[Warn] Kein Installer-Download verfügbar. Bitte zuerst Update prüfen.");
            return;
        }

        try
        {
            AppendNotification("[Info] Lade Update-Installer herunter...");

            var targetDirectory = IOPath.Combine(IOPath.GetTempPath(), "HyperTool", "updates");
            Directory.CreateDirectory(targetDirectory);

            var fileName = ResolveInstallerFileName(_installerDownloadUrl, _installerFileName);
            var installerPath = IOPath.Combine(targetDirectory, fileName);

            using var response = await UpdateDownloadClient.GetAsync(_installerDownloadUrl, CancellationToken.None);
            response.EnsureSuccessStatusCode();

            await using (var stream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(stream);
            }

            AppendNotification($"[Info] Installer gespeichert: {installerPath}");

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });

            AppendNotification("[Info] Installer gestartet.");
        }
        catch (Exception ex)
        {
            AppendNotification($"[Error] Update-Installation fehlgeschlagen: {ex.Message}");
        }
    }

    private void UpdateUsbRuntimeStatusUi()
    {
        var isAvailable = _isUsbClientAvailable;
        _usbRuntimeStatusDot.Fill = new SolidColorBrush(isAvailable
            ? Windows.UI.Color.FromArgb(0xFF, 0x32, 0xD7, 0x4B)
            : Windows.UI.Color.FromArgb(0xFF, 0xE8, 0x4A, 0x5F));
        _usbRuntimeStatusText.Text = isAvailable
            ? "USB/IP-Client: Verfügbar"
            : "USB/IP-Client: Nicht installiert";

        if (_usbRuntimeInstallButton is not null)
        {
            _usbRuntimeInstallButton.Visibility = isAvailable ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static string ResolveInstallerFileName(string downloadUrl, string? fileName, string defaultFileName = "HyperTool-Guest-Setup.exe")
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName.Trim();
        }

        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            var inferred = IOPath.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(inferred))
            {
                return inferred;
            }
        }

        return defaultFileName;
    }

    private void OnLoggerEntryWritten(string message)
    {
        if (!DispatcherQueue.TryEnqueue(() => AppendNotification(message)))
        {
            AppendNotification(message);
        }
    }

    private void AppendNotification(string message)
    {
        _statusText.Text = message;
        _notifications.Insert(0, message);

        while (_notifications.Count > 200)
        {
            _notifications.RemoveAt(_notifications.Count - 1);
        }

        UpdateBusyAndNotificationPanel();
    }

    private void UpdateBusyAndNotificationPanel()
    {
        _toggleLogButton.Content = _isLogExpanded ? "▾ Log einklappen" : "▸ Log ausklappen";
        _notificationExpandedGrid.Visibility = _isLogExpanded ? Visibility.Visible : Visibility.Collapsed;
        _notificationSummaryBorder.Visibility = _isLogExpanded ? Visibility.Collapsed : Visibility.Visible;
    }

    private string _configPathLabel() => $"Aktive Config: {GuestConfigService.DefaultConfigPath}";

    private Button CreateNavButton(string icon, string title, int index)
    {
        var button = new Button
        {
            Padding = new Thickness(8, 8, 8, 8),
            MinHeight = 78,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["SurfaceSoftBrush"] as Brush,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var content = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var navIconHost = new Grid
        {
            Width = 36,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        navIconHost.Children.Add(new Viewbox
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(1),
            Child = new TextBlock
            {
                Text = icon,
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        content.Children.Add(navIconHost);
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 72,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        button.Content = content;
        button.Click += (_, _) =>
        {
            if (_selectedMenuIndex == index)
            {
                return;
            }

            _selectedMenuIndex = index;
            UpdateNavSelection();
            UpdatePageContent();
        };

        _navButtons.Add(button);
        return button;
    }

    private static Button CreateIconButton(string icon, string label, RoutedEventHandler? onClick = null)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        content.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 7, 10, 7),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["SurfaceSoftBrush"] as Brush
        };

        if (onClick is not null)
        {
            button.Click += onClick;
        }

        return button;
    }

    private static Border CreateCard(Thickness margin, double padding, double cornerRadius)
    {
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };

        var topBrush = Application.Current.Resources["SurfaceTopBrush"] as SolidColorBrush;
        var bottomBrush = Application.Current.Resources["SurfaceBottomBrush"] as SolidColorBrush;

        gradientBrush.GradientStops.Add(new GradientStop
        {
            Color = topBrush?.Color ?? Color.FromArgb(0xFA, 0xFF, 0xFF, 0xFF),
            Offset = 0.0
        });
        gradientBrush.GradientStops.Add(new GradientStop
        {
            Color = bottomBrush?.Color ?? Color.FromArgb(0xF0, 0xF2, 0xF8, 0xFF),
            Offset = 1.0
        });

        return new Border
        {
            Margin = margin,
            Padding = new Thickness(padding),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(cornerRadius),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = gradientBrush
        };
    }

    private static void ApplyThemePalette(bool isDark)
    {
        if (Application.Current?.Resources is not ResourceDictionary resources)
        {
            return;
        }

        SetBrushColor(resources, "PageBackgroundBrush", isDark ? Color.FromArgb(0xFF, 0x14, 0x1A, 0x31) : Color.FromArgb(0xFF, 0xF3, 0xF7, 0xFD));
        SetBrushColor(resources, "PanelBackgroundBrush", isDark ? Color.FromArgb(0xFF, 0x1A, 0x22, 0x40) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        SetBrushColor(resources, "PanelBorderBrush", isDark ? Color.FromArgb(0xFF, 0x2A, 0x37, 0x60) : Color.FromArgb(0xFF, 0xC5, 0xD6, 0xEA));
        SetBrushColor(resources, "TextPrimaryBrush", isDark ? Color.FromArgb(0xFF, 0xE8, 0xF0, 0xFF) : Color.FromArgb(0xFF, 0x0C, 0x21, 0x38));
        SetBrushColor(resources, "TextMutedBrush", isDark ? Color.FromArgb(0xFF, 0x9E, 0xB4, 0xDA) : Color.FromArgb(0xFF, 0x2E, 0x4B, 0x69));
        SetBrushColor(resources, "AccentBrush", isDark ? Color.FromArgb(0xFF, 0x6C, 0xC9, 0xFF) : Color.FromArgb(0xFF, 0x1F, 0x79, 0xCC));
        SetBrushColor(resources, "AccentSoftBrush", isDark ? Color.FromArgb(0x3A, 0x7B, 0xC9, 0x66) : Color.FromArgb(0x26, 0x1F, 0x79, 0xCC));
        SetBrushColor(resources, "AccentStrongBrush", isDark ? Color.FromArgb(0xFF, 0x8B, 0xD4, 0xFF) : Color.FromArgb(0xFF, 0x1F, 0x79, 0xCC));
        SetBrushColor(resources, "SurfaceTopBrush", isDark ? Color.FromArgb(0xFF, 0x20, 0x29, 0x49) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        SetBrushColor(resources, "SurfaceBottomBrush", isDark ? Color.FromArgb(0xFF, 0x17, 0x1F, 0x39) : Color.FromArgb(0xFF, 0xF6, 0xFA, 0xFF));
        SetBrushColor(resources, "SurfaceSoftBrush", isDark ? Color.FromArgb(0xFF, 0x1D, 0x26, 0x45) : Color.FromArgb(0xFF, 0xF1, 0xF7, 0xFF));
    }

    private static void SetBrushColor(ResourceDictionary resources, string key, Color color)
    {
        if (resources.TryGetValue(key, out var existingValue) && existingValue is SolidColorBrush existingBrush)
        {
            existingBrush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private void UpdateTitleBarAppearance(bool isDark)
    {
        try
        {
            if (AppWindow?.TitleBar is not Microsoft.UI.Windowing.AppWindowTitleBar titleBar)
            {
                return;
            }

            if (isDark)
            {
                titleBar.BackgroundColor = Color.FromArgb(0xFF, 0x17, 0x1F, 0x3A);
                titleBar.ForegroundColor = Color.FromArgb(0xFF, 0xE8, 0xF0, 0xFF);
                titleBar.InactiveBackgroundColor = Color.FromArgb(0xFF, 0x14, 0x1A, 0x31);
                titleBar.InactiveForegroundColor = Color.FromArgb(0xFF, 0x98, 0xAE, 0xD3);

                titleBar.ButtonBackgroundColor = Color.FromArgb(0xFF, 0x17, 0x1F, 0x3A);
                titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0xE8, 0xF0, 0xFF);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0x22, 0x2D, 0x51);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0xFF, 0x2A, 0x36, 0x61);
                titleBar.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0xFF, 0x14, 0x1A, 0x31);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x98, 0xAE, 0xD3);
            }
            else
            {
                titleBar.BackgroundColor = Color.FromArgb(0xFF, 0xD8, 0xE9, 0xFF);
                titleBar.ForegroundColor = Color.FromArgb(0xFF, 0x0F, 0x24, 0x3C);
                titleBar.InactiveBackgroundColor = Color.FromArgb(0xFF, 0xE6, 0xF2, 0xFF);
                titleBar.InactiveForegroundColor = Color.FromArgb(0xFF, 0x4E, 0x66, 0x83);

                titleBar.ButtonBackgroundColor = Color.FromArgb(0xFF, 0xD8, 0xE9, 0xFF);
                titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0x0F, 0x24, 0x3C);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0xC7, 0xDE, 0xFC);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0x0A, 0x1B, 0x30);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0xFF, 0xBA, 0xD3, 0xF7);
                titleBar.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0x08, 0x19, 0x2C);
                titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0xFF, 0xE6, 0xF2, 0xFF);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x4E, 0x66, 0x83);
            }
        }
        catch
        {
        }
    }

    private void TryApplyWindowIcon()
    {
        try
        {
            if (AppWindow is null)
            {
                return;
            }

            var iconPath = new[]
            {
                IOPath.Combine(AppContext.BaseDirectory, "Assets", "HyperTool.Guest.ico"),
                IOPath.Combine(AppContext.BaseDirectory, "Assets", "HyperTool.ico"),
                IOPath.Combine(AppContext.BaseDirectory, "HyperTool.Guest.ico")
            }.FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
        }
    }
}
