using HyperTool.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HyperTool.WinUI.Services;

public sealed class ThemeService : IThemeService
{
    private static readonly Uri DarkThemeUri = new("ms-appx:///Themes/Theme.Dark.xaml");
    private static readonly Uri LightThemeUri = new("ms-appx:///Themes/Theme.Light.xaml");

    public string CurrentTheme { get; private set; } = "Dark";

    public void ApplyTheme(string? theme)
    {
        var normalizedTheme = NormalizeTheme(theme);
        CurrentTheme = normalizedTheme;

        if (Microsoft.UI.Xaml.Application.Current is not Microsoft.UI.Xaml.Application app)
        {
            return;
        }

        if (app.Resources is not ResourceDictionary resources)
        {
            return;
        }

        var targetDictionaryUri = string.Equals(normalizedTheme, "Light", StringComparison.Ordinal)
            ? LightThemeUri
            : DarkThemeUri;

        ResourceDictionary targetThemeDictionary;
        try
        {
            targetThemeDictionary = new ResourceDictionary { Source = targetDictionaryUri };
        }
        catch
        {
            return;
        }

        ApplyThemeResources(resources, targetThemeDictionary);

        var mergedDictionaries = resources.MergedDictionaries;
        var activeThemeDictionaryIndex = mergedDictionaries
            .Select((dictionary, index) => new { dictionary, index })
            .FirstOrDefault(item =>
                item.dictionary.Source is not null
                && (item.dictionary.Source.OriginalString.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase)
                    || item.dictionary.Source.OriginalString.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase)))
            ?.index;

        if (activeThemeDictionaryIndex is null)
        {
            mergedDictionaries.Add(targetThemeDictionary);
        }
        else
        {
            mergedDictionaries[activeThemeDictionaryIndex.Value] = targetThemeDictionary;
        }
    }

    private static void ApplyThemeResources(ResourceDictionary appResources, ResourceDictionary sourceResources)
    {
        IReadOnlyList<object> keys;
        try
        {
            keys = sourceResources.Keys.Cast<object>().ToList();
        }
        catch
        {
            return;
        }

        foreach (var key in keys)
        {
            try
            {
                var sourceValue = sourceResources[key];

                if (sourceValue is not SolidColorBrush sourceBrush)
                {
                    continue;
                }

                if (appResources.TryGetValue(key, out var existingValue) && existingValue is SolidColorBrush existingBrush)
                {
                    existingBrush.Color = sourceBrush.Color;
                }
                else
                {
                    appResources[key] = new SolidColorBrush(sourceBrush.Color);
                }
            }
            catch
            {
            }
        }
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
