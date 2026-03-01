using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Reflection;
using Windows.UI;

namespace HyperTool.WinUI.Views;

internal static class LifecycleVisuals
{
    internal static readonly string[] StartupStatusMessages =
    [
        "Initialisiere Hyper-V Umgebung …",
        "Lade VM-Konfigurationen …",
        "Prüfe virtuelle Switches …",
        "Synchronisiere Host/VM-Datenpfade …",
        "Starte HyperTool Oberfläche …"
    ];

    internal static readonly string[] ShutdownStatusMessages =
    [
        "Beende HyperTool …",
        "Fahre Datenkanäle kontrolliert herunter …",
        "Trenne aktive Verbindungen …",
        "Speichere Sitzungsstatus …",
        "Shutdown abgeschlossen"
    ];

    internal static Color BackgroundTop => Color.FromArgb(0xFF, 0x06, 0x10, 0x21);
    internal static Color BackgroundMid => Color.FromArgb(0xFF, 0x0B, 0x1B, 0x32);
    internal static Color BackgroundBottom => Color.FromArgb(0xFF, 0x11, 0x26, 0x44);
    internal static Color BackgroundFocusPrimary => Color.FromArgb(0x22, 0x67, 0xB8, 0xF3);
    internal static Color BackgroundFocusSecondary => Color.FromArgb(0x1A, 0x4E, 0x97, 0xDD);
    internal static Color BackgroundFocusTertiary => Color.FromArgb(0x14, 0x7A, 0xC8, 0xFF);

    internal static Color CardBackground => Color.FromArgb(0xD8, 0x0E, 0x1C, 0x34);
    internal static Color CardBorder => Color.FromArgb(0x9C, 0x4B, 0x73, 0xA7);
    internal static Color CardInnerOutline => Color.FromArgb(0x50, 0x88, 0xB3, 0xE3);
    internal static Color CardSurfaceTop => Color.FromArgb(0xDA, 0x12, 0x23, 0x3E);
    internal static Color CardSurfaceBottom => Color.FromArgb(0xCD, 0x0D, 0x1A, 0x30);
    internal static Color CardInnerTop => Color.FromArgb(0x1D, 0x9E, 0xCB, 0xFA);
    internal static Color CardInnerBottom => Color.FromArgb(0x08, 0x67, 0x9C, 0xCC);

    internal static Color TextPrimary => Color.FromArgb(0xFF, 0xE8, 0xF2, 0xFF);
    internal static Color TextSecondary => Color.FromArgb(0xFF, 0xB8, 0xCC, 0xE7);

    internal static Color AccentSoft => Color.FromArgb(0xCC, 0x6A, 0xC4, 0xFF);
    internal static Color AccentStrong => Color.FromArgb(0xFF, 0x67, 0xC0, 0xFF);
    internal static Color AccentBright => Color.FromArgb(0xFF, 0x9F, 0xDF, 0xFF);

    internal static Color NodeColor => Color.FromArgb(0xD2, 0x66, 0xBC, 0xF6);
    internal static Color NodeStroke => Color.FromArgb(0x90, 0xBF, 0xE2, 0xFF);
    internal static Color LineColor => Color.FromArgb(0x7E, 0x67, 0xAF, 0xEF);
    internal static Color PulseColor => Color.FromArgb(0xD6, 0x8A, 0xD7, 0xFF);
    internal static Color ParticleColor => Color.FromArgb(0x88, 0x82, 0xC9, 0xFF);

    internal static Color ProgressTrack => Color.FromArgb(0x32, 0x53, 0x72, 0xA0);
    internal static Color ProgressTrackEdge => Color.FromArgb(0x58, 0x5B, 0x8D, 0xC2);

    internal const int SplashMinVisibleMs = 2000;
    internal const int SplashStatusCycleMs = 1050;
    internal const int SplashPhaseCardInMs = 520;
    internal const int SplashPhaseNetworkInMs = 430;
    internal const int SplashPhasePulseStartMs = 980;

    internal static LinearGradientBrush CreateRootBackgroundBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = BackgroundTop, Offset = 0.0 },
                new GradientStop { Color = BackgroundMid, Offset = 0.56 },
                new GradientStop { Color = BackgroundBottom, Offset = 1.0 }
            }
        };
    }

    internal static LinearGradientBrush CreateProgressBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5),
            GradientStops =
            {
                new GradientStop { Color = AccentStrong, Offset = 0 },
                new GradientStop { Color = AccentBright, Offset = 1 }
            }
        };
    }

    internal static LinearGradientBrush CreateCardSurfaceBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.25, 0),
            EndPoint = new Windows.Foundation.Point(0.75, 1),
            GradientStops =
            {
                new GradientStop { Color = CardSurfaceTop, Offset = 0 },
                new GradientStop { Color = CardSurfaceBottom, Offset = 1 }
            }
        };
    }

    internal static LinearGradientBrush CreateCardInnerBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0),
            EndPoint = new Windows.Foundation.Point(0.5, 1),
            GradientStops =
            {
                new GradientStop { Color = CardInnerTop, Offset = 0 },
                new GradientStop { Color = CardInnerBottom, Offset = 1 }
            }
        };
    }

    internal static RadialGradientBrush CreateCenterFocusBrush(Color coreColor)
    {
        return new RadialGradientBrush
        {
            GradientOrigin = new Windows.Foundation.Point(0.5, 0.5),
            Center = new Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.66,
            RadiusY = 0.66,
            GradientStops =
            {
                new GradientStop { Color = coreColor, Offset = 0.0 },
                new GradientStop { Color = Color.FromArgb(0x00, coreColor.R, coreColor.G, coreColor.B), Offset = 1.0 }
            }
        };
    }

    internal static RadialGradientBrush CreateVignetteBrush(byte alpha)
    {
        return new RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.86,
            RadiusY = 0.86,
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(0x00, 0x00, 0x00, 0x00), Offset = 0.5 },
                new GradientStop { Color = Color.FromArgb(alpha, 0x04, 0x0A, 0x14), Offset = 1.0 }
            }
        };
    }

    internal static EasingFunctionBase CreateEaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };

    internal static EasingFunctionBase CreateEaseInOut() => new SineEase { EasingMode = EasingMode.EaseInOut };

    internal static string ResolveDisplayVersion(string? preferredVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            return $"v{FormatVersionForDisplay(preferredVersion)}";
        }

        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return $"v{FormatVersionForDisplay(informationalVersion)}";
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        return $"v{FormatVersionForDisplay(assemblyVersion)}";
    }

    private static string FormatVersionForDisplay(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }

        var raw = version.Trim();
        var plusIndex = raw.IndexOf('+');
        if (plusIndex >= 0)
        {
            raw = raw[..plusIndex];
        }

        raw = raw.TrimStart('v', 'V');

        if (Version.TryParse(raw, out var parsed))
        {
            if (parsed.Build > 0)
            {
                if (parsed.Revision > 0)
                {
                    return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}.{parsed.Revision}";
                }

                return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
            }

            return $"{parsed.Major}.{parsed.Minor}";
        }

        return raw;
    }
}
