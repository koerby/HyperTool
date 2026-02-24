using Microsoft.Win32;

namespace HyperTool.Services;

public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool SetStartWithWindows(bool enabled, string appName, string executablePath, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                errorMessage = "Autostart-Registry konnte nicht geöffnet werden.";
                return false;
            }

            if (enabled)
            {
                key.SetValue(appName, $"\"{executablePath}\"");
            }
            else
            {
                key.DeleteValue(appName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
