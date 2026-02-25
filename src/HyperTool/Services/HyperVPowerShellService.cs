using HyperTool.Models;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVPowerShellService : IHyperVService
{
    public async Task<IReadOnlyList<HyperVVmInfo>> GetVmsAsync(CancellationToken cancellationToken)
    {
        const string script = """
            @(
                Get-VM | ForEach-Object {
                    $adapter = Get-VMNetworkAdapter -VMName $_.Name -ErrorAction SilentlyContinue | Select-Object -First 1

                    [pscustomobject]@{
                        Name = $_.Name
                        State = $_.State.ToString()
                        Status = $_.Status
                        CurrentSwitchName = if ($null -ne $adapter -and $null -ne $adapter.SwitchName) { $adapter.SwitchName } else { '' }
                    }
                }
            ) | ConvertTo-Json -Depth 4 -Compress
            """;

        var rows = await InvokeJsonArrayAsync(script, cancellationToken);
        return rows.Select(row => new HyperVVmInfo
        {
            Name = GetString(row, "Name"),
            State = GetString(row, "State"),
            Status = GetString(row, "Status"),
            CurrentSwitchName = GetString(row, "CurrentSwitchName")
        }).ToList();
    }

    public Task StartVmAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync($"Start-VM -VMName {ToPsSingleQuoted(vmName)} -Confirm:$false", cancellationToken);

    public Task StopVmGracefulAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync($"Stop-VM -VMName {ToPsSingleQuoted(vmName)} -Shutdown -Confirm:$false", cancellationToken);

    public Task TurnOffVmAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync($"Stop-VM -VMName {ToPsSingleQuoted(vmName)} -TurnOff -Confirm:$false", cancellationToken);

    public Task RestartVmAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync($"Restart-VM -VMName {ToPsSingleQuoted(vmName)} -Force -Confirm:$false", cancellationToken);

    public async Task<IReadOnlyList<HyperVSwitchInfo>> GetVmSwitchesAsync(CancellationToken cancellationToken)
    {
        const string script = """
            @(Get-VMSwitch | Select-Object Name, SwitchType) | ConvertTo-Json -Depth 3 -Compress
            """;

        var rows = await InvokeJsonArrayAsync(script, cancellationToken);
        return rows.Select(row => new HyperVSwitchInfo
        {
            Name = GetString(row, "Name"),
            SwitchType = GetString(row, "SwitchType")
        }).ToList();
    }

    public Task ConnectVmNetworkAdapterAsync(string vmName, string switchName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            $"Connect-VMNetworkAdapter -VMName {ToPsSingleQuoted(vmName)} -SwitchName {ToPsSingleQuoted(switchName)}",
            cancellationToken);

    public Task DisconnectVmNetworkAdapterAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            $"Disconnect-VMNetworkAdapter -VMName {ToPsSingleQuoted(vmName)}",
            cancellationToken);

    public async Task<IReadOnlyList<HyperVCheckpointInfo>> GetCheckpointsAsync(string vmName, CancellationToken cancellationToken)
    {
        var script = $"""
            @(Get-VMCheckpoint -VMName {ToPsSingleQuoted(vmName)} | Select-Object Name, CreationTime, CheckpointType) | ConvertTo-Json -Depth 4 -Compress
            """;

        var rows = await InvokeJsonArrayAsync(script, cancellationToken);
        return rows.Select(row => new HyperVCheckpointInfo
        {
            Name = GetString(row, "Name"),
            Created = GetDateTime(row, "CreationTime"),
            Type = GetString(row, "CheckpointType")
        }).ToList();
    }

    public async Task CreateCheckpointAsync(string vmName, string checkpointName, string? description, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            Log.Information("Checkpoint description currently informational only for VM {VmName}: {Description}", vmName, description);
        }

        await InvokeNonQueryAsync(
            $"Checkpoint-VM -VMName {ToPsSingleQuoted(vmName)} -SnapshotName {ToPsSingleQuoted(checkpointName)} -Confirm:$false",
            cancellationToken);
    }

    public Task ApplyCheckpointAsync(string vmName, string checkpointName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            $"Restore-VMCheckpoint -VMCheckpoint (Get-VMCheckpoint -VMName {ToPsSingleQuoted(vmName)} -Name {ToPsSingleQuoted(checkpointName)}) -Confirm:$false",
            cancellationToken);

    public Task RemoveCheckpointAsync(string vmName, string checkpointName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            $"Remove-VMCheckpoint -VMName {ToPsSingleQuoted(vmName)} -Name {ToPsSingleQuoted(checkpointName)} -Confirm:$false",
            cancellationToken);

    public Task OpenVmConnectAsync(string vmName, string computerName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var processStartInfo = new ProcessStartInfo("vmconnect.exe")
        {
            UseShellExecute = true
        };

        processStartInfo.ArgumentList.Add(computerName);
        processStartInfo.ArgumentList.Add(vmName);

        try
        {
            Process.Start(processStartInfo);
            Log.Information("vmconnect started for VM {VmName} on host {ComputerName}", vmName, computerName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start vmconnect for VM {VmName}", vmName);
            throw;
        }

        return Task.CompletedTask;
    }

    private async Task InvokeNonQueryAsync(string script, CancellationToken cancellationToken)
    {
        _ = await InvokePowerShellAsync(script, cancellationToken);
    }

    private static async Task<IReadOnlyList<JsonElement>> InvokeJsonArrayAsync(string script, CancellationToken cancellationToken)
    {
        var json = await InvokePowerShellAsync(script, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList();
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            return [document.RootElement.Clone()];
        }

        return [];
    }

    private static async Task<string> InvokePowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var wrappedScript = $"$ErrorActionPreference = 'Stop'; Import-Module Hyper-V -ErrorAction Stop; {script}";
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        processStartInfo.ArgumentList.Add("-NoProfile");
        processStartInfo.ArgumentList.Add("-NonInteractive");
        processStartInfo.ArgumentList.Add("-ExecutionPolicy");
        processStartInfo.ArgumentList.Add("Bypass");
        processStartInfo.ArgumentList.Add("-Command");
        processStartInfo.ArgumentList.Add(wrappedScript);

        using var process = Process.Start(processStartInfo);
        if (process is null)
        {
            throw new InvalidOperationException("PowerShell konnte nicht gestartet werden.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            throw;
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;

            if (IsHyperVPermissionError(message))
            {
                throw new UnauthorizedAccessException(
                    "Keine Berechtigung für Hyper-V. Bitte HyperTool als Administrator starten oder den Benutzer zur Gruppe 'Hyper-V-Administratoren' hinzufügen.");
            }

            throw new InvalidOperationException($"Hyper-V PowerShell command failed:{Environment.NewLine}{message.Trim()}");
        }

        return standardOutput.Trim();
    }

    private static string ToPsSingleQuoted(string value)
    {
        var escaped = (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
        return $"'{escaped}'";
    }

    private static string GetString(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            _ => value.ToString()
        };
    }

    private static DateTime GetDateTime(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var value))
        {
            return DateTime.MinValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) => parsed,
            _ => DateTime.MinValue
        };
    }

    private static bool IsHyperVPermissionError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("erforderliche Berechtigung", StringComparison.OrdinalIgnoreCase)
               || message.Contains("authorization policy", StringComparison.OrdinalIgnoreCase)
               || message.Contains("required permission", StringComparison.OrdinalIgnoreCase)
               || message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
               || message.Contains("virtualizationexception", StringComparison.OrdinalIgnoreCase);
    }
}