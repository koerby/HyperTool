using HyperTool.Models;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace HyperTool.Services;

public sealed class HyperVPowerShellService : IHyperVService
{
    public async Task<IReadOnlyList<HyperVVmInfo>> GetVmsAsync(CancellationToken cancellationToken)
    {
        const string script = """
            Get-VM | ForEach-Object {
                $adapter = Get-VMNetworkAdapter -VMName $_.Name -ErrorAction SilentlyContinue | Select-Object -First 1

                [pscustomobject]@{
                    Name = $_.Name
                    State = $_.State.ToString()
                    Status = $_.Status
                    CurrentSwitchName = if ($null -ne $adapter -and $null -ne $adapter.SwitchName) { $adapter.SwitchName } else { '' }
                }
            }
            """;

        var rows = await InvokeAsync(script, null, cancellationToken);
        return rows.Select(row => new HyperVVmInfo
        {
            Name = GetString(row, "Name"),
            State = GetString(row, "State"),
            Status = GetString(row, "Status"),
            CurrentSwitchName = GetString(row, "CurrentSwitchName")
        }).ToList();
    }

    public Task StartVmAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync("Start-VM -VMName $VmName -Confirm:$false", new Dictionary<string, object?> { ["VmName"] = vmName }, cancellationToken);

    public Task StopVmGracefulAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync("Stop-VM -VMName $VmName -Shutdown -Confirm:$false", new Dictionary<string, object?> { ["VmName"] = vmName }, cancellationToken);

    public Task TurnOffVmAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync("Stop-VM -VMName $VmName -TurnOff -Confirm:$false", new Dictionary<string, object?> { ["VmName"] = vmName }, cancellationToken);

    public Task RestartVmAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync("Restart-VM -VMName $VmName -Force -Confirm:$false", new Dictionary<string, object?> { ["VmName"] = vmName }, cancellationToken);

    public async Task<IReadOnlyList<HyperVSwitchInfo>> GetVmSwitchesAsync(CancellationToken cancellationToken)
    {
        const string script = """
            Get-VMSwitch | Select-Object Name, SwitchType
            """;

        var rows = await InvokeAsync(script, null, cancellationToken);
        return rows.Select(row => new HyperVSwitchInfo
        {
            Name = GetString(row, "Name"),
            SwitchType = GetString(row, "SwitchType")
        }).ToList();
    }

    public Task ConnectVmNetworkAdapterAsync(string vmName, string switchName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            "Connect-VMNetworkAdapter -VMName $VmName -SwitchName $SwitchName",
            new Dictionary<string, object?>
            {
                ["VmName"] = vmName,
                ["SwitchName"] = switchName
            },
            cancellationToken);

    public Task DisconnectVmNetworkAdapterAsync(string vmName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            "Disconnect-VMNetworkAdapter -VMName $VmName",
            new Dictionary<string, object?> { ["VmName"] = vmName },
            cancellationToken);

    public async Task<IReadOnlyList<HyperVCheckpointInfo>> GetCheckpointsAsync(string vmName, CancellationToken cancellationToken)
    {
        const string script = """
            Get-VMCheckpoint -VMName $VmName | Select-Object Name, CreationTime, CheckpointType
            """;

        var rows = await InvokeAsync(script, new Dictionary<string, object?> { ["VmName"] = vmName }, cancellationToken);
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
            "Checkpoint-VM -VMName $VmName -SnapshotName $CheckpointName -Confirm:$false",
            new Dictionary<string, object?>
            {
                ["VmName"] = vmName,
                ["CheckpointName"] = checkpointName
            },
            cancellationToken);
    }

    public Task ApplyCheckpointAsync(string vmName, string checkpointName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            "Restore-VMCheckpoint -VMCheckpoint (Get-VMCheckpoint -VMName $VmName -Name $CheckpointName) -Confirm:$false",
            new Dictionary<string, object?>
            {
                ["VmName"] = vmName,
                ["CheckpointName"] = checkpointName
            },
            cancellationToken);

    public Task RemoveCheckpointAsync(string vmName, string checkpointName, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            "Remove-VMCheckpoint -VMName $VmName -Name $CheckpointName -Confirm:$false",
            new Dictionary<string, object?>
            {
                ["VmName"] = vmName,
                ["CheckpointName"] = checkpointName
            },
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

    private async Task InvokeNonQueryAsync(string script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await InvokeAsync(script, parameters, cancellationToken);
    }

    private static async Task<IReadOnlyList<PSObject>> InvokeAsync(
        string script,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var powerShell = PowerShell.Create();

            var initialState = InitialSessionState.CreateDefault();
            using var runspace = RunspaceFactory.CreateRunspace(initialState);
            runspace.Open();
            powerShell.Runspace = runspace;

            powerShell.AddScript("Import-Module Hyper-V -ErrorAction Stop");

            if (parameters is not null)
            {
                foreach (var parameter in parameters)
                {
                    runspace.SessionStateProxy.SetVariable(parameter.Key, parameter.Value);
                }
            }

            powerShell.AddScript(script);

            var output = powerShell.Invoke();

            if (powerShell.HadErrors)
            {
                var errors = powerShell.Streams.Error.Select(error => error.ToString()).ToArray();
                var message = string.Join(Environment.NewLine, errors);
                throw new InvalidOperationException($"Hyper-V PowerShell command failed:{Environment.NewLine}{message}");
            }

            return (IReadOnlyList<PSObject>)output;
        }, cancellationToken);
    }

    private static string GetString(PSObject source, string propertyName)
    {
        var value = source.Properties[propertyName]?.Value;
        return value?.ToString() ?? string.Empty;
    }

    private static DateTime GetDateTime(PSObject source, string propertyName)
    {
        var value = source.Properties[propertyName]?.Value;
        return value switch
        {
            DateTime dateTime => dateTime,
            string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) => parsed,
            _ => DateTime.MinValue
        };
    }
}