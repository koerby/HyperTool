using HyperTool.WinUI.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace HyperTool.WinUI.Views;

internal sealed class TrayControlCenterWindow : Window
{
    private const int PanelCornerRadius = 18;
    private readonly Grid _windowRoot = new();
    private readonly Border _panelRoot = new();
    private readonly Border _headerBorder = new();
    private readonly Grid _headerGrid = new();
    private readonly Border _vmCard = new();
    private readonly Border _networkCard = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _vmNameText = new();
    private readonly TextBlock _vmMetaText = new();
    private readonly TextBlock _networkStatusText = new();
    private readonly Button _closeButton = new();
    private readonly Button _prevVmButton = new();
    private readonly Button _nextVmButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _restartButton = new();
    private readonly Button _consoleButton = new();
    private readonly Button _snapshotButton = new();
    private readonly Button _fullVisibilityButton = new();
    private readonly Button _exitButton = new();
    private readonly Button _compactVisibilityButton = new();
    private readonly Button _compactExitButton = new();
    private readonly TextBlock _vmIndexText = new();
    private readonly StackPanel _switchChipPanel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _fullContentPanel = new() { Spacing = 12 };
    private readonly StackPanel _compactActionsPanel = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private string? _lastSwitchRenderKey;
    private SolidColorBrush _textSecondaryBrush = new(new Color { A = 0xFF, R = 0xB5, G = 0xC8, B = 0xE6 });
    private SolidColorBrush _vmRunningBrush = new(new Color { A = 0xFF, R = 0xA8, G = 0xD8, B = 0xBE });
    private SolidColorBrush _vmOffBrush = new(new Color { A = 0xFF, R = 0xDE, G = 0xA2, B = 0xA8 });
    private bool _isDarkTheme = true;
    private Color _chipBackground = Color.FromArgb(0xFF, 0x2A, 0x39, 0x58);
    private Color _chipBorder = Color.FromArgb(0xFF, 0x5A, 0x7B, 0xAF);
    private Color _chipForeground = Color.FromArgb(0xFF, 0xE8, 0xF1, 0xFF);
    private Color _chipSelectedBackground = Color.FromArgb(0xFF, 0x55, 0xA9, 0xF2);
    private Color _chipSelectedBorder = Color.FromArgb(0xFF, 0x8F, 0xD0, 0xFF);
    private Color _chipSelectedForeground = Color.FromArgb(0xFF, 0x11, 0x24, 0x38);

    public event Action? CloseRequested;
    public event Action? PreviousVmRequested;
    public event Action? NextVmRequested;
    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? RestartRequested;
    public event Action? OpenConsoleRequested;
    public event Action? SnapshotRequested;
    public event Action<string>? SwitchSelected;
    public event Action? ExitRequested;
    public event Action? ToggleVisibilityRequested;

    public TrayControlCenterWindow()
    {
        Title = "HyperTool Control Center";
        ExtendsContentIntoTitleBar = false;
        SystemBackdrop = null;

        DwmWindowHelper.ApplyRoundedCorners(this);
        BuildLayout();
        ConfigureWindowChrome();
        ApplyTheme(isDark: true);

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                CloseRequested?.Invoke();
            }
        };
    }

    public void ApplyTheme(bool isDark)
    {
        _isDarkTheme = isDark;
        _panelRoot.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;

        var panelBackground = isDark
            ? Color.FromArgb(0xF6, 0x0F, 0x19, 0x2D)
            : Color.FromArgb(0xF8, 0xF7, 0xFB, 0xFF);
        var panelBorder = isDark
            ? Color.FromArgb(0xFF, 0x38, 0x55, 0x82)
            : Color.FromArgb(0xFF, 0xAD, 0xC3, 0xE6);
        var cardBackground = isDark
            ? Color.FromArgb(0xFF, 0x19, 0x25, 0x41)
            : Color.FromArgb(0xFF, 0xFC, 0xFD, 0xFF);
        var headerGradientStart = isDark
            ? Color.FromArgb(0xFF, 0x17, 0x24, 0x40)
            : Color.FromArgb(0xFF, 0xE8, 0xF1, 0xFF);
        var headerGradientEnd = isDark
            ? Color.FromArgb(0xFF, 0x10, 0x1B, 0x31)
            : Color.FromArgb(0xFF, 0xD8, 0xE7, 0xFC);
        var textPrimary = isDark
            ? Color.FromArgb(0xFF, 0xE8, 0xF1, 0xFF)
            : Color.FromArgb(0xFF, 0x1A, 0x2C, 0x48);
        var textSecondary = isDark
            ? Color.FromArgb(0xFF, 0xB5, 0xC8, 0xE6)
            : Color.FromArgb(0xFF, 0x4E, 0x67, 0x8C);
        var vmRunning = isDark
            ? Color.FromArgb(0xFF, 0xA8, 0xD8, 0xBE)
            : Color.FromArgb(0xFF, 0x2C, 0x7A, 0x55);
        var vmOff = isDark
            ? Color.FromArgb(0xFF, 0xDE, 0xA2, 0xA8)
            : Color.FromArgb(0xFF, 0xA0, 0x45, 0x51);
        var actionButtonBackground = isDark
            ? Color.FromArgb(0xFF, 0x2B, 0x3F, 0x66)
            : Color.FromArgb(0xFF, 0xE7, 0xF1, 0xFF);
        var actionButtonForeground = isDark
            ? Color.FromArgb(0xFF, 0xE9, 0xF2, 0xFF)
            : Color.FromArgb(0xFF, 0x1A, 0x2E, 0x4C);
        var actionButtonBorder = isDark
            ? Color.FromArgb(0xFF, 0x5D, 0x85, 0xBC)
            : Color.FromArgb(0xFF, 0x9A, 0xB9, 0xE3);
        var smallButtonBackground = isDark
            ? Color.FromArgb(0xFF, 0x2A, 0x3A, 0x5C)
            : Color.FromArgb(0xFF, 0xEB, 0xF4, 0xFF);
        var smallButtonForeground = isDark
            ? Color.FromArgb(0xFF, 0xE5, 0xEE, 0xFF)
            : Color.FromArgb(0xFF, 0x1D, 0x33, 0x54);
        var smallButtonBorder = isDark
            ? Color.FromArgb(0xFF, 0x5B, 0x80, 0xB4)
            : Color.FromArgb(0xFF, 0xA4, 0xC0, 0xE7);

        _chipBackground = isDark
            ? Color.FromArgb(0xFF, 0x2A, 0x39, 0x58)
            : Color.FromArgb(0xFF, 0xE4, 0xF0, 0xFF);
        _chipBorder = isDark
            ? Color.FromArgb(0xFF, 0x5A, 0x7B, 0xAF)
            : Color.FromArgb(0xFF, 0x95, 0xB5, 0xE3);
        _chipForeground = isDark
            ? Color.FromArgb(0xFF, 0xE8, 0xF1, 0xFF)
            : Color.FromArgb(0xFF, 0x1A, 0x2E, 0x4C);
        _chipSelectedBackground = isDark
            ? Color.FromArgb(0xFF, 0x55, 0xA9, 0xF2)
            : Color.FromArgb(0xFF, 0x4D, 0x9F, 0xEB);
        _chipSelectedBorder = isDark
            ? Color.FromArgb(0xFF, 0x8F, 0xD0, 0xFF)
            : Color.FromArgb(0xFF, 0x6C, 0xB7, 0xF5);
        _chipSelectedForeground = isDark
            ? Color.FromArgb(0xFF, 0x11, 0x24, 0x38)
            : Color.FromArgb(0xFF, 0xF3, 0xF8, 0xFF);

        _windowRoot.Background = new SolidColorBrush(Colors.Transparent);
        _panelRoot.Background = new SolidColorBrush(panelBackground);
        _panelRoot.BorderBrush = new SolidColorBrush(panelBorder);

        _headerBorder.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = headerGradientStart, Offset = 0 },
                new GradientStop { Color = headerGradientEnd, Offset = 1 }
            }
        };

        _vmCard.Background = new SolidColorBrush(cardBackground);
        _networkCard.Background = new SolidColorBrush(cardBackground);

        _titleText.Foreground = new SolidColorBrush(textPrimary);
        _vmNameText.Foreground = new SolidColorBrush(textPrimary);
        _textSecondaryBrush = new SolidColorBrush(textSecondary);
        _vmRunningBrush = new SolidColorBrush(vmRunning);
        _vmOffBrush = new SolidColorBrush(vmOff);
        _vmMetaText.Foreground = _textSecondaryBrush;
        _networkStatusText.Foreground = _textSecondaryBrush;
        _vmIndexText.Foreground = _textSecondaryBrush;

        ApplyButtonColors(_startButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);
        ApplyButtonColors(_stopButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);
        ApplyButtonColors(_restartButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);
        ApplyButtonColors(_consoleButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);
        ApplyButtonColors(_snapshotButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);
        ApplyButtonColors(_exitButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);

        ApplyButtonColors(_closeButton, smallButtonBackground, smallButtonForeground, smallButtonBorder);
        ApplyButtonColors(_prevVmButton, smallButtonBackground, smallButtonForeground, smallButtonBorder);
        ApplyButtonColors(_nextVmButton, smallButtonBackground, smallButtonForeground, smallButtonBorder);
        ApplyButtonColors(_fullVisibilityButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);
        ApplyButtonColors(_compactVisibilityButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);
        ApplyButtonColors(_compactExitButton, actionButtonBackground, actionButtonForeground, actionButtonBorder);
    }

    public void UpdateView(TrayControlCenterViewState state)
    {
        _vmNameText.Text = state.SelectedVmDisplay;
        _vmMetaText.Text = state.SelectedVmMeta;
        _vmMetaText.Foreground = ResolveVmMetaBrush(state.SelectedVmMeta);
        _networkStatusText.Text = state.ActiveSwitchDisplay;
        _vmIndexText.Text = state.VmIndexDisplay;

        _prevVmButton.IsEnabled = state.CanMoveVm;
        _nextVmButton.IsEnabled = state.CanMoveVm;

        _startButton.IsEnabled = state.CanStart;
        _stopButton.IsEnabled = state.CanStop;
        _restartButton.IsEnabled = state.CanRestart;
        _consoleButton.IsEnabled = state.HasVm;
        _snapshotButton.IsEnabled = state.HasVm;

        _fullContentPanel.Visibility = state.IsCompactMode ? Visibility.Collapsed : Visibility.Visible;
        _compactActionsPanel.Visibility = state.IsCompactMode ? Visibility.Visible : Visibility.Collapsed;
        _fullVisibilityButton.Content = state.VisibilityButtonText;
        _compactVisibilityButton.Content = state.VisibilityButtonText;

        var switchRenderKey = string.Join('|', state.Switches.Select(item => $"{item.Name}:{item.IsSelected}:{item.IsEnabled}"));
        if (!string.Equals(_lastSwitchRenderKey, switchRenderKey, StringComparison.Ordinal))
        {
            _lastSwitchRenderKey = switchRenderKey;
            _switchChipPanel.Children.Clear();
            foreach (var switchItem in state.Switches)
            {
                var chip = CreateSwitchChipButton(switchItem.Label, switchItem.IsSelected, switchItem.IsEnabled);
                var switchName = switchItem.Name;
                chip.Click += (_, _) => SwitchSelected?.Invoke(switchName);
                _switchChipPanel.Children.Add(chip);
            }

            if (_switchChipPanel.Children.Count == 0)
            {
                _switchChipPanel.Children.Add(new TextBlock
                {
                    Text = "Keine Switches verfügbar",
                    Opacity = 0.8,
                    FontSize = 12,
                    Foreground = _textSecondaryBrush
                });
            }
        }
    }

    private Brush ResolveVmMetaBrush(string? vmMeta)
    {
        if (string.IsNullOrWhiteSpace(vmMeta))
        {
            return _textSecondaryBrush;
        }

        var runtimeState = vmMeta.Split('·')[0].Trim();
        if (runtimeState.Contains("running", StringComparison.OrdinalIgnoreCase)
            || runtimeState.Contains("wird ausgeführt", StringComparison.OrdinalIgnoreCase)
            || runtimeState.Contains("ausgeführt", StringComparison.OrdinalIgnoreCase)
            || runtimeState.Contains("läuft", StringComparison.OrdinalIgnoreCase))
        {
            return _vmRunningBrush;
        }

        if (runtimeState.Contains("off", StringComparison.OrdinalIgnoreCase)
            || runtimeState.Contains("aus", StringComparison.OrdinalIgnoreCase))
        {
            return _vmOffBrush;
        }

        return _textSecondaryBrush;
    }

    public void SetPosition(int x, int y)
    {
        AppWindow.Move(new PointInt32(x, y));
    }

    public void SetPanelSize(int width, int height)
    {
        AppWindow.Resize(new SizeInt32(width, height));
        DwmWindowHelper.ApplyRoundedRegion(this, width, height, PanelCornerRadius);
    }

    private void BuildLayout()
    {
        _windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _panelRoot.CornerRadius = new CornerRadius(PanelCornerRadius);
        _panelRoot.BorderThickness = new Thickness(1);
        _panelRoot.Padding = new Thickness(12);
        _panelRoot.Margin = new Thickness(0);
        _panelRoot.Shadow = new ThemeShadow();

        var stack = new StackPanel { Spacing = 12 };
        _fullContentPanel.Spacing = 12;

        _headerBorder.CornerRadius = new CornerRadius(14);
        _headerBorder.Padding = new Thickness(12, 10, 10, 10);

        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = new Image
        {
            Width = 24,
            Height = 24,
            Source = new BitmapImage(new Uri("ms-appx:///Assets/HyperTool.Icon.Transparent.png")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _headerGrid.Children.Add(logo);

        _titleText.Text = "HyperTool";
        _titleText.FontSize = 18;
        _titleText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_titleText, 1);
        _headerGrid.Children.Add(_titleText);

        _closeButton.Content = "✕";
        _closeButton.Width = 32;
        _closeButton.Height = 32;
        _closeButton.Style = CreateRoundedButtonStyle();
        _closeButton.Click += (_, _) => CloseRequested?.Invoke();
        Grid.SetColumn(_closeButton, 2);
        _headerGrid.Children.Add(_closeButton);

        _headerBorder.Child = _headerGrid;
        stack.Children.Add(_headerBorder);

        _vmCard.CornerRadius = new CornerRadius(14);
        _vmCard.Padding = new Thickness(12);

        var vmStack = new StackPanel { Spacing = 6 };
        vmStack.Children.Add(new TextBlock
        {
            Text = "Virtuelle Maschine",
            FontSize = 12,
            Opacity = 0.8
        });

        _vmNameText.Text = "Keine VM ausgewählt";
        _vmNameText.FontSize = 17;
        _vmNameText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        vmStack.Children.Add(_vmNameText);

        _vmMetaText.Text = "-";
        _vmMetaText.FontSize = 13;
        vmStack.Children.Add(_vmMetaText);

        var vmNavRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        vmNavRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        vmNavRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        vmNavRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _prevVmButton.Content = "◀";
        _prevVmButton.Width = 34;
        _prevVmButton.Height = 30;
        _prevVmButton.Style = CreateRoundedButtonStyle();
        _prevVmButton.Click += (_, _) => PreviousVmRequested?.Invoke();
        vmNavRow.Children.Add(_prevVmButton);

        _vmIndexText.Text = "0 / 0";
        _vmIndexText.HorizontalAlignment = HorizontalAlignment.Center;
        _vmIndexText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_vmIndexText, 1);
        vmNavRow.Children.Add(_vmIndexText);

        _nextVmButton.Content = "▶";
        _nextVmButton.Width = 34;
        _nextVmButton.Height = 30;
        _nextVmButton.Style = CreateRoundedButtonStyle();
        _nextVmButton.Click += (_, _) => NextVmRequested?.Invoke();
        Grid.SetColumn(_nextVmButton, 2);
        vmNavRow.Children.Add(_nextVmButton);

        vmStack.Children.Add(vmNavRow);
        _vmCard.Child = vmStack;
        _fullContentPanel.Children.Add(_vmCard);

        var actionsPanel = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        actionsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actionsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actionsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _startButton.Content = "▶  Start VM";
        _startButton.Style = CreateActionButtonStyle();
        _startButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _startButton.Click += (_, _) => StartRequested?.Invoke();
        Grid.SetRow(_startButton, 0);
        Grid.SetColumn(_startButton, 0);
        actionsPanel.Children.Add(_startButton);

        _stopButton.Content = "■  Stop VM";
        _stopButton.Style = CreateActionButtonStyle();
        _stopButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _stopButton.Click += (_, _) => StopRequested?.Invoke();
        Grid.SetRow(_stopButton, 0);
        Grid.SetColumn(_stopButton, 1);
        actionsPanel.Children.Add(_stopButton);

        _restartButton.Content = "↻  Restart VM";
        _restartButton.Style = CreateActionButtonStyle();
        _restartButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _restartButton.Click += (_, _) => RestartRequested?.Invoke();
        Grid.SetRow(_restartButton, 1);
        Grid.SetColumn(_restartButton, 0);
        actionsPanel.Children.Add(_restartButton);

        _consoleButton.Content = "🖥  Open Console";
        _consoleButton.Style = CreateActionButtonStyle();
        _consoleButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _consoleButton.Click += (_, _) => OpenConsoleRequested?.Invoke();
        Grid.SetRow(_consoleButton, 1);
        Grid.SetColumn(_consoleButton, 1);
        actionsPanel.Children.Add(_consoleButton);

        _snapshotButton.Content = "📸  Snapshot";
        _snapshotButton.Style = CreateActionButtonStyle();
        _snapshotButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _snapshotButton.Click += (_, _) => SnapshotRequested?.Invoke();
        Grid.SetRow(_snapshotButton, 2);
        Grid.SetColumn(_snapshotButton, 0);
        Grid.SetColumnSpan(_snapshotButton, 2);
        actionsPanel.Children.Add(_snapshotButton);

        _fullContentPanel.Children.Add(actionsPanel);

        _networkCard.CornerRadius = new CornerRadius(14);
        _networkCard.Padding = new Thickness(12);

        var networkStack = new StackPanel { Spacing = 8 };
        networkStack.Children.Add(new TextBlock
        {
            Text = "Netzwerk",
            FontSize = 12,
            Opacity = 0.8
        });

        _networkStatusText.Text = "Aktiv: -";
        _networkStatusText.FontSize = 13;
        networkStack.Children.Add(_networkStatusText);

        var switchScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = _switchChipPanel
        };
        networkStack.Children.Add(switchScroller);

        _networkCard.Child = networkStack;
        _fullContentPanel.Children.Add(_networkCard);

        var bottomActionsPanel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0)
        };

        _fullVisibilityButton.Content = "⌂  Ausblenden";
        _fullVisibilityButton.Style = CreateActionButtonStyle();
        _fullVisibilityButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _fullVisibilityButton.Click += (_, _) => ToggleVisibilityRequested?.Invoke();
        bottomActionsPanel.Children.Add(_fullVisibilityButton);

        _exitButton.Content = "⏻  Exit";
        _exitButton.Style = CreateActionButtonStyle();
        _exitButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _exitButton.Margin = new Thickness(0, 4, 0, 0);
        _exitButton.Click += (_, _) => ExitRequested?.Invoke();
        bottomActionsPanel.Children.Add(_exitButton);

        _fullContentPanel.Children.Add(bottomActionsPanel);

        _compactVisibilityButton.Content = "⌂  Ausblenden";
        _compactVisibilityButton.Style = CreateActionButtonStyle();
        _compactVisibilityButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _compactVisibilityButton.Click += (_, _) => ToggleVisibilityRequested?.Invoke();
        _compactActionsPanel.Children.Add(_compactVisibilityButton);

        _compactExitButton.Content = "⏻  Exit";
        _compactExitButton.Style = CreateActionButtonStyle();
        _compactExitButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _compactExitButton.Click += (_, _) => ExitRequested?.Invoke();
        _compactActionsPanel.Children.Add(_compactExitButton);

        stack.Children.Add(_fullContentPanel);
        stack.Children.Add(_compactActionsPanel);

        _panelRoot.Child = stack;
        _windowRoot.Children.Add(_panelRoot);
        _windowRoot.KeyDown += OnRootKeyDown;

        Content = _windowRoot;
    }

    private void ConfigureWindowChrome()
    {
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;
        SetPanelSize(404, 620);
    }

    private static Style CreateActionButtonStyle()
    {
        var style = new Style { TargetType = typeof(Button) };

        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 38d));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 12, 0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, Microsoft.UI.Text.FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        return style;
    }

    private static Style CreateRoundedButtonStyle()
    {
        var style = new Style { TargetType = typeof(Button) };
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, Microsoft.UI.Text.FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        return style;
    }

    private Button CreateSwitchChipButton(string text, bool isSelected, bool isEnabled)
    {
        var chip = new Button
        {
            Content = text,
            Height = 30,
            MinWidth = 86,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(15),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            IsEnabled = isEnabled,
            Opacity = isEnabled ? 1.0 : 0.55,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(isSelected ? _chipSelectedBackground : _chipBackground),
            BorderBrush = new SolidColorBrush(isSelected ? _chipSelectedBorder : _chipBorder),
            Foreground = new SolidColorBrush(isSelected ? _chipSelectedForeground : _chipForeground)
        };

        return chip;
    }

    private static void ApplyButtonColors(Button button, Color background, Color foreground, Color border)
    {
        button.Background = new SolidColorBrush(background);
        button.Foreground = new SolidColorBrush(foreground);
        button.BorderBrush = new SolidColorBrush(border);
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        CloseRequested?.Invoke();
    }
}

internal sealed class TrayControlCenterViewState
{
    public bool IsCompactMode { get; set; }

    public bool HasVm { get; set; }

    public bool CanMoveVm { get; set; }

    public bool CanStart { get; set; }

    public bool CanStop { get; set; }

    public bool CanRestart { get; set; }

    public string SelectedVmDisplay { get; set; } = "Keine VM ausgewählt";

    public string SelectedVmMeta { get; set; } = "-";

    public string ActiveSwitchDisplay { get; set; } = "Aktiv: -";

    public string VmIndexDisplay { get; set; } = "0 / 0";

    public string VisibilityButtonText { get; set; } = "⌂  Ausblenden";

    public List<TraySwitchItem> Switches { get; } = [];
}

internal sealed record TraySwitchItem(string Name, string Label, bool IsSelected, bool IsEnabled);
