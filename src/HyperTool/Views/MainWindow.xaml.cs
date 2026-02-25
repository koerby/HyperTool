using ControlzEx.Theming;
using HyperTool.ViewModels;
using MahApps.Metro.Controls;
using System.ComponentModel;
using System.Windows;

namespace HyperTool.Views;

public partial class MainWindow : MetroWindow
{
    private MainViewModel? _currentViewModel;

    public MainWindow()
    {
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
            ApplyTheme(_currentViewModel.UiTheme);
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
            ApplyTheme(vm.UiTheme);
        }
    }

    private void ApplyTheme(string? theme)
    {
        var isBright = string.Equals(theme, "Bright", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);

        ThemeManager.Current.ChangeTheme(this, isBright ? "Light.Blue" : "Dark.Blue");

        if (isBright)
        {
            SetBrushColor("PageBackground", "#F3F6FB");
            SetBrushColor("PanelBackground", "#FFFFFF");
            SetBrushColor("PanelBorder", "#C4D1E2");
            SetBrushColor("TextPrimary", "#0F172A");
            SetBrushColor("TextMuted", "#334155");
            SetBrushColor("InputBackground", "#FFFFFF");
            SetBrushColor("InputBorder", "#A0B3CC");
            SetBrushColor("SidebarBackground", "#E9F0F8");
            SetBrushColor("OverlayBackground", "#ECF2F9");
            SetBrushColor("ButtonBackground", "#2A5B91");
            SetBrushColor("ButtonBorder", "#1E4B7A");
            SetBrushColor("ButtonForeground", "#FFFFFF");
            SetBrushColor("NavHoverBackground", "#D9E6F5");
            SetBrushColor("NavSelectedBackground", "#C2D8F2");
            SetBrushColor("WarningText", "#8A4A14");
        }
        else
        {
            SetBrushColor("PageBackground", "#0A1220");
            SetBrushColor("PanelBackground", "#141E32");
            SetBrushColor("PanelBorder", "#2D4265");
            SetBrushColor("TextPrimary", "#F8FAFF");
            SetBrushColor("TextMuted", "#B9C8E6");
            SetBrushColor("InputBackground", "#111A2C");
            SetBrushColor("InputBorder", "#4D74A5");
            SetBrushColor("SidebarBackground", "#10192B");
            SetBrushColor("OverlayBackground", "#0F1A2A");
            SetBrushColor("ButtonBackground", "#264A76");
            SetBrushColor("ButtonBorder", "#4370A6");
            SetBrushColor("ButtonForeground", "#FFFFFF");
            SetBrushColor("NavHoverBackground", "#1F3555");
            SetBrushColor("NavSelectedBackground", "#2E507C");
            SetBrushColor("WarningText", "#FFEEC9A6");
        }
    }

    private void SetBrushColor(string key, string hexColor)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);

        if (Resources[key] is System.Windows.Media.SolidColorBrush localBrush)
        {
            if (localBrush.IsFrozen)
            {
                localBrush = localBrush.CloneCurrentValue();
                Resources[key] = localBrush;
            }

            localBrush.Color = color;
            return;
        }

        if (System.Windows.Application.Current?.Resources[key] is System.Windows.Media.SolidColorBrush appBrush)
        {
            var mutableBrush = appBrush.IsFrozen ? appBrush.CloneCurrentValue() : appBrush.Clone();
            mutableBrush.Color = color;
            Resources[key] = mutableBrush;
        }
    }
}