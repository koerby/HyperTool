using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.IO;
using Windows.Graphics;
using Windows.UI;

namespace HyperTool.Guest.Views;

public sealed class HelpWindow : Window
{
    private readonly string _configPath;
    private readonly string _repoUrl;
    private readonly bool _isDarkMode;

    public HelpWindow(string configPath, string repoUrl, string uiTheme)
    {
        _configPath = configPath;
        _repoUrl = repoUrl;
        _isDarkMode = string.Equals(uiTheme, "dark", StringComparison.OrdinalIgnoreCase);

        Title = "HyperTool Guest Hilfe";
        AppWindow.Resize(new SizeInt32(860, 720));

        Content = BuildLayout();
        ApplyRequestedTheme();
        UpdateTitleBarAppearance();
    }

    private UIElement BuildLayout()
    {
        var host = new Grid
        {
            Background = Application.Current.Resources["PageBackgroundBrush"] as Brush
        };

        var root = new Grid
        {
            Margin = new Thickness(18),
            Background = Application.Current.Resources["PageBackgroundBrush"] as Brush
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerCard = CreateCard(16);
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        headerRow.Children.Add(new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/HyperTool.Guest.Icon.Transparent.png")),
            Width = 24,
            Height = 24
        });
        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(new TextBlock { Text = "HyperTool Guest Hilfe", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        titleStack.Children.Add(new TextBlock { Text = "Kurzübersicht über Guest-Funktionen und Einstellungen", Opacity = 0.8 });
        headerRow.Children.Add(titleStack);
        headerCard.Child = headerRow;
        root.Children.Add(headerCard);

        var bodyCard = CreateCard(12);
        var bodyStack = new StackPanel { Spacing = 12 };
        bodyStack.Children.Add(CreateSection("USB Modus", "In den Einstellungen kannst du zwischen Hyper-V Socket (bevorzugt) und IP-Mode umschalten."));
        bodyStack.Children.Add(CreateSection("Transport-Status", "Im USB-Host-Bereich zeigen Status-Chips den aktiven Transportmodus (Hyper-V Socket / IP-Mode) direkt und gut sichtbar an."));
        bodyStack.Children.Add(CreateSection("USB Tray", "Im Tray-Control-Center stehen USB-Schnellaktionen bereit. Wenn der Client fehlt, erscheint ein Installationsbutton mit automatischem Download und Installer-Start aus dem offiziellen GitHub-Release."));
        bodyStack.Children.Add(CreateSection("Sofortige Modusanzeige", "Nach Aktivieren/Deaktivieren von Hyper-V Socket wird der Modus im USB-Bereich sofort aktualisiert (ohne manuelles Refresh)."));
        bodyStack.Children.Add(CreateSection("Diagnose (live)", "Im Info-Bereich siehst du Hyper-V Socket-, Registry- und Fallback-Status live; der Test-Button startet eine direkte Socket-Prüfung."));
        bodyStack.Children.Add(CreateSection("Auto-Connect", "Für das ausgewählte USB-Gerät kann Auto-Connect aktiviert werden. Das Gerät verbindet sich dann automatisch nach Refresh/Freigabe."));
        bodyStack.Children.Add(CreateSection("Einstellungen", "Tasktray-Verhalten, Start mit Windows, Start minimiert, Updatecheck beim Start und Theme konfigurieren."));
        bodyStack.Children.Add(CreateSection("Single Instance", "Ein zweiter Start blendet die bereits laufende Guest-App ein."));
        bodyStack.Children.Add(CreateSection("Tray Control Center", "Linksklick und Rechtsklick im Tray öffnen das USB-zentrierte Control Center mit Schnellaktionen."));
        bodyStack.Children.Add(CreateSection("Logs", "'Logs öffnen' öffnet immer den Log-Ordner (nicht einzelne Dateien)."));

        bodyCard.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = bodyStack
        };

        Grid.SetRow(bodyCard, 2);
        root.Children.Add(bodyCard);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        actions.Children.Add(CreateActionButton("📄", "Logs öffnen", (_, _) => OpenLogs()));
        actions.Children.Add(CreateActionButton("⚙", "Config öffnen", (_, _) => OpenConfig()));
        actions.Children.Add(CreateActionButton("🌐", "GitHub Repo", (_, _) => OpenRepo()));
        actions.Children.Add(CreateActionButton("✓", "OK", (_, _) => Close()));

        Grid.SetRow(actions, 4);
        root.Children.Add(actions);

        host.Children.Add(root);
        return host;
    }

    private static Border CreateCard(double padding)
    {
        return new Border
        {
            Padding = new Thickness(padding),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush
        };
    }

    private static Border CreateSection(string title, string text)
    {
        var section = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["SurfaceSoftBrush"] as Brush
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.86 });
        section.Child = stack;
        return section;
    }

    private static Button CreateActionButton(string icon, string text, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["SurfaceSoftBrush"] as Brush,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = icon, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                }
            }
        };

        button.Click += onClick;
        return button;
    }

    private static void OpenLogs()
    {
        var logsPath = GuestConfigService.DefaultLogDirectory;
        Directory.CreateDirectory(logsPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = logsPath,
            UseShellExecute = true
        });
    }

    private void OpenConfig()
    {
        var directoryPath = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        try
        {
            if (File.Exists(_configPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_configPath}\"",
                    UseShellExecute = true
                });

                GuestLogger.Info("help.config.open", "Config-Datei im Explorer markiert.", new { configPath = _configPath });
                return;
            }

            var fallbackPath = directoryPath ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            Process.Start(new ProcessStartInfo
            {
                FileName = fallbackPath,
                UseShellExecute = true
            });

            GuestLogger.Info("help.config.open", "Config-Verzeichnis geöffnet (Datei noch nicht vorhanden).", new
            {
                configPath = _configPath,
                fallbackPath
            });
        }
        catch (Exception ex)
        {
            GuestLogger.Warn("help.config.open_failed", "Config konnte nicht im Explorer geöffnet werden.", new
            {
                configPath = _configPath,
                directoryPath,
                exceptionType = ex.GetType().FullName,
                ex.Message
            });
        }
    }

    private void OpenRepo()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _repoUrl,
            UseShellExecute = true
        });
    }

    private void ApplyRequestedTheme()
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = _isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
        }
    }

    private void UpdateTitleBarAppearance()
    {
        try
        {
            if (AppWindow?.TitleBar is not AppWindowTitleBar titleBar)
            {
                return;
            }

            if (_isDarkMode)
            {
                titleBar.BackgroundColor = Color.FromArgb(0xFF, 0x17, 0x1F, 0x3A);
                titleBar.ForegroundColor = Color.FromArgb(0xFF, 0xE8, 0xF0, 0xFF);
                titleBar.ButtonBackgroundColor = Color.FromArgb(0xFF, 0x17, 0x1F, 0x3A);
                titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0xE8, 0xF0, 0xFF);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0x22, 0x2D, 0x51);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            }
            else
            {
                titleBar.BackgroundColor = Color.FromArgb(0xFF, 0xD9, 0xE8, 0xFB);
                titleBar.ForegroundColor = Color.FromArgb(0xFF, 0x1B, 0x31, 0x4F);
                titleBar.ButtonBackgroundColor = Color.FromArgb(0xFF, 0xD9, 0xE8, 0xFB);
                titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0x1B, 0x31, 0x4F);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0xC3, 0xDC, 0xF6);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0x11, 0x26, 0x3F);
            }
        }
        catch
        {
        }
    }
}
