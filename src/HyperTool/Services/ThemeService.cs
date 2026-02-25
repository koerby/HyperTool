using ControlzEx.Theming;
using Serilog;
using System.Windows;

namespace HyperTool.Services;

public sealed class ThemeService : IThemeService
{
    private static readonly Uri DarkThemeUri = new("Themes/Theme.Dark.xaml", UriKind.Relative);
    private static readonly Uri LightThemeUri = new("Themes/Theme.Light.xaml", UriKind.Relative);

    public string CurrentTheme { get; private set; } = "Dark";

    public void ApplyTheme(string? theme)
    {
        var normalizedTheme = NormalizeTheme(theme);
        CurrentTheme = normalizedTheme;

        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        var appThemeName = string.Equals(normalizedTheme, "Light", StringComparison.Ordinal)
            ? "Light.Blue"
            : "Dark.Blue";

        ThemeManager.Current.ChangeTheme(app, appThemeName);

        var targetDictionaryUri = string.Equals(normalizedTheme, "Light", StringComparison.Ordinal)
            ? LightThemeUri
            : DarkThemeUri;

        var mergedDictionaries = app.Resources.MergedDictionaries;
        var activeThemeDictionary = mergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Source is not null
            && (dictionary.Source.OriginalString.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase)
                || dictionary.Source.OriginalString.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase)));

        if (activeThemeDictionary is null)
        {
            mergedDictionaries.Add(new ResourceDictionary { Source = targetDictionaryUri });
            return;
        }

        var sourceDictionary = new ResourceDictionary { Source = targetDictionaryUri };

        foreach (var entry in sourceDictionary.Keys)
        {
            if (entry is null)
            {
                continue;
            }

            if (sourceDictionary[entry] is not System.Windows.Media.SolidColorBrush sourceBrush)
            {
                activeThemeDictionary[entry] = sourceDictionary[entry];
                continue;
            }

            if (activeThemeDictionary[entry] is System.Windows.Media.SolidColorBrush targetBrush)
            {
                if (targetBrush.IsFrozen)
                {
                    targetBrush = targetBrush.CloneCurrentValue();
                    activeThemeDictionary[entry] = targetBrush;
                }

                targetBrush.Color = sourceBrush.Color;
            }
            else
            {
                activeThemeDictionary[entry] = sourceBrush.Clone();
            }
        }

        Log.Information("Theme applied: {Theme}", normalizedTheme);
    }

    private static string NormalizeTheme(string? theme)
    {
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            || string.Equals(theme, "Bright", StringComparison.OrdinalIgnoreCase))
        {
            return "Light";
        }

        return "Dark";
    }
}
