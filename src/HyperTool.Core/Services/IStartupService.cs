namespace HyperTool.Services;

public interface IStartupService
{
    bool SetStartWithWindows(bool enabled, string appName, string executablePath, out string? errorMessage);
}
