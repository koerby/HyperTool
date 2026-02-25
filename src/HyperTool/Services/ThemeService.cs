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
        var activeThemeDictionaryIndex = mergedDictionaries
            .Select((dictionary, index) => new { dictionary, index })
            .FirstOrDefault(item =>
                item.dictionary.Source is not null
                && (item.dictionary.Source.OriginalString.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase)
                    || item.dictionary.Source.OriginalString.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase)))
            ?.index;

        var targetThemeDictionary = new ResourceDictionary { Source = targetDictionaryUri };

        if (activeThemeDictionaryIndex is null)
        {
            mergedDictionaries.Add(targetThemeDictionary);
        }
        else
        {
            mergedDictionaries[activeThemeDictionaryIndex.Value] = targetThemeDictionary;
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
