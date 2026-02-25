namespace HyperTool.Services;

public interface IThemeService
{
    string CurrentTheme { get; }

    void ApplyTheme(string? theme);
}
