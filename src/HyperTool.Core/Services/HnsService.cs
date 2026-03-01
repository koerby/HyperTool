using Serilog;
using System.ComponentModel;
using System.Diagnostics;

namespace HyperTool.Services;

public sealed class HnsService : IHnsService
{
    public async Task<(bool Success, string Message)> RestartHnsElevatedAsync(CancellationToken cancellationToken)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return (false, "Aktueller EXE-Pfad konnte nicht ermittelt werden.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--restart-hns",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (false, "Elevated HNS-Helper konnte nicht gestartet werden.");
            }

            await Task.Run(() => process.WaitForExit(), cancellationToken);

            if (process.ExitCode == 0)
            {
                return (true, "HNS erfolgreich neu gestartet.");
            }

            return (false, $"Elevated HNS-Helper meldet ExitCode {process.ExitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "UAC-Abfrage wurde abgebrochen.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restart HNS elevated.");
            return (false, ex.Message);
        }
    }
}