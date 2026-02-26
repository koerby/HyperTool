using HyperTool.Models;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Text;
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
        InvokeNonQueryAsync($"Stop-VM -VMName {ToPsSingleQuoted(vmName)} -Confirm:$false", cancellationToken);

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

    public async Task<IReadOnlyList<HyperVVmNetworkAdapterInfo>> GetVmNetworkAdaptersAsync(string vmName, CancellationToken cancellationToken)
    {
        var script =
            $"@(Get-VMNetworkAdapter -VMName {ToPsSingleQuoted(vmName)} -ErrorAction SilentlyContinue | " +
            "ForEach-Object { [pscustomobject]@{ Name = if ($null -ne $_.Name) { $_.Name } else { '' }; " +
            "SwitchName = if ($null -ne $_.SwitchName) { $_.SwitchName } else { '' }; " +
            "MacAddress = if ($null -ne $_.MacAddress) { $_.MacAddress } else { '' } } }) | ConvertTo-Json -Depth 4 -Compress";

        var rows = await InvokeJsonArrayAsync(script, cancellationToken);
        return rows.Select(row => new HyperVVmNetworkAdapterInfo
        {
            Name = GetString(row, "Name"),
            SwitchName = GetString(row, "SwitchName"),
            MacAddress = GetString(row, "MacAddress")
        }).ToList();
    }

    public async Task<string?> GetVmCurrentSwitchNameAsync(string vmName, CancellationToken cancellationToken)
    {
        var script = $"$adapter = Get-VMNetworkAdapter -VMName {ToPsSingleQuoted(vmName)} -ErrorAction SilentlyContinue | Select-Object -First 1; if ($null -eq $adapter -or $null -eq $adapter.SwitchName) {{ '' }} else {{ $adapter.SwitchName }}";

        var value = await InvokePowerShellAsync(script, cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public Task ConnectVmNetworkAdapterAsync(string vmName, string switchName, string? adapterName, CancellationToken cancellationToken)
    {
        var script = string.IsNullOrWhiteSpace(adapterName)
            ? $"Connect-VMNetworkAdapter -VMName {ToPsSingleQuoted(vmName)} -SwitchName {ToPsSingleQuoted(switchName)}"
            : $"Connect-VMNetworkAdapter -VMName {ToPsSingleQuoted(vmName)} -Name {ToPsSingleQuoted(adapterName)} -SwitchName {ToPsSingleQuoted(switchName)}";

        return InvokeNonQueryAsync(script, cancellationToken);
    }

    public Task DisconnectVmNetworkAdapterAsync(string vmName, string? adapterName, CancellationToken cancellationToken)
    {
        var script = string.IsNullOrWhiteSpace(adapterName)
            ? $"Disconnect-VMNetworkAdapter -VMName {ToPsSingleQuoted(vmName)}"
            : $"Disconnect-VMNetworkAdapter -VMName {ToPsSingleQuoted(vmName)} -Name {ToPsSingleQuoted(adapterName)}";

        return InvokeNonQueryAsync(script, cancellationToken);
    }

    public async Task<IReadOnlyList<HostNetworkAdapterInfo>> GetHostNetworkAdaptersWithUplinkAsync(CancellationToken cancellationToken)
    {
        const string script = """
            @(
                Get-NetAdapter -ErrorAction SilentlyContinue |
                    Where-Object {
                        $_.Status -eq 'Up' -and
                        ($null -eq $_.MediaConnectionState -or $_.MediaConnectionState -eq 'Connected')
                    } |
                    ForEach-Object {
                        $adapter = $_
                        $ipConfig = Get-NetIPConfiguration -InterfaceIndex $adapter.ifIndex -ErrorAction SilentlyContinue
                        $ipv4Addresses = @($ipConfig.IPv4Address | Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace($_.IPAddress) })
                        $dnsServers = @($ipConfig.DnsServer.ServerAddresses | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                        $gateway = if ($null -ne $ipConfig.IPv4DefaultGateway -and -not [string]::IsNullOrWhiteSpace($ipConfig.IPv4DefaultGateway.NextHop)) {
                            $ipConfig.IPv4DefaultGateway.NextHop
                        } else {
                            ''
                        }

                        [pscustomobject]@{
                            AdapterName = if ($null -ne $adapter.Name) { $adapter.Name } else { '' }
                            InterfaceDescription = if ($null -ne $adapter.InterfaceDescription) { $adapter.InterfaceDescription } else { '' }
                            IpAddresses = if ($ipv4Addresses.Count -gt 0) { ($ipv4Addresses | ForEach-Object { $_.IPAddress }) -join ', ' } else { '' }
                            Subnets = if ($ipv4Addresses.Count -gt 0) { ($ipv4Addresses | ForEach-Object { '/' + $_.PrefixLength }) -join ', ' } else { '' }
                            Gateway = $gateway
                            DnsServers = if ($dnsServers.Count -gt 0) { $dnsServers -join ', ' } else { '' }
                        }
                    }
            ) | Sort-Object AdapterName | ConvertTo-Json -Depth 4 -Compress
            """;

        var rows = await InvokeJsonArrayAsync(script, cancellationToken);
        return rows.Select(row => new HostNetworkAdapterInfo
        {
            AdapterName = GetString(row, "AdapterName"),
            InterfaceDescription = GetString(row, "InterfaceDescription"),
            IpAddresses = GetString(row, "IpAddresses"),
            Subnets = GetString(row, "Subnets"),
            Gateway = GetString(row, "Gateway"),
            DnsServers = GetString(row, "DnsServers")
        }).ToList();
    }

    public async Task<IReadOnlyList<HyperVCheckpointInfo>> GetCheckpointsAsync(string vmName, CancellationToken cancellationToken)
    {
        var script = $"$vm = Get-VM -Name {ToPsSingleQuoted(vmName)} -ErrorAction SilentlyContinue; $currentSnapshotId = ''; if ($null -ne $vm -and $null -ne $vm.ParentSnapshotId) {{ $currentSnapshotId = $vm.ParentSnapshotId.ToString() }}; @(Get-VMCheckpoint -VMName {ToPsSingleQuoted(vmName)} | ForEach-Object {{ $id = if ($null -ne $_.VMCheckpointId) {{ $_.VMCheckpointId.ToString() }} elseif ($null -ne $_.Id) {{ $_.Id.ToString() }} else {{ '' }}; $parentId = ''; if ($null -ne $_.ParentCheckpointId) {{ $parentId = $_.ParentCheckpointId.ToString() }} elseif ($null -ne $_.Parent -and $null -ne $_.Parent.VMCheckpointId) {{ $parentId = $_.Parent.VMCheckpointId.ToString() }}; $isCurrent = $false; if ($null -ne $_.IsCurrentSnapshot) {{ $isCurrent = [bool]$_.IsCurrentSnapshot }}; if (-not $isCurrent -and -not [string]::IsNullOrWhiteSpace($currentSnapshotId) -and -not [string]::IsNullOrWhiteSpace($id) -and $id -ceq $currentSnapshotId) {{ $isCurrent = $true }}; [pscustomobject]@{{ Id = $id; ParentId = $parentId; IsCurrent = $isCurrent; Name = if ($null -ne $_.Name) {{ $_.Name }} else {{ '' }}; CreationTime = if ($null -ne $_.CreationTime) {{ $_.CreationTime.ToString('o') }} else {{ '' }}; CheckpointType = if ($null -ne $_.CheckpointType) {{ $_.CheckpointType.ToString() }} else {{ '' }} }} }}) | ConvertTo-Json -Depth 4 -Compress";

        var rows = await InvokeJsonArrayAsync(script, cancellationToken);
        return rows.Select(row => new HyperVCheckpointInfo
        {
            Id = GetString(row, "Id"),
            ParentId = GetString(row, "ParentId"),
            IsCurrent = GetBoolean(row, "IsCurrent"),
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

        try
        {
            await InvokeNonQueryAsync(
                $"Checkpoint-VM -VMName {ToPsSingleQuoted(vmName)} -SnapshotName {ToPsSingleQuoted(checkpointName)} -Confirm:$false",
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsProductionCheckpointError(ex.Message))
        {
            Log.Warning(ex,
                "Production checkpoint creation failed for VM {VmName}. Retrying once with temporary Standard checkpoint type.",
                vmName);

            var vmNameQuoted = ToPsSingleQuoted(vmName);
            var checkpointNameQuoted = ToPsSingleQuoted(checkpointName);
            var fallbackScript = $"$vmName = {vmNameQuoted}; " +
                                 "$vm = Get-VM -Name $vmName; " +
                                 "$originalType = $vm.CheckpointType; " +
                                 "try { " +
                                 "Set-VM -Name $vmName -CheckpointType Standard; " +
                                 $"Checkpoint-VM -VMName $vmName -SnapshotName {checkpointNameQuoted} -Confirm:$false; " +
                                 "} finally { " +
                                 "if ($null -ne $originalType) { Set-VM -Name $vmName -CheckpointType $originalType } " +
                                 "}";

            await InvokeNonQueryAsync(fallbackScript, cancellationToken);
        }
    }

    public Task ApplyCheckpointAsync(string vmName, string checkpointName, string? checkpointId, CancellationToken cancellationToken)
    {
        var script = $"$vmName = {ToPsSingleQuoted(vmName)}; " +
                     $"$checkpointName = {ToPsSingleQuoted(checkpointName)}; " +
                     $"$checkpointId = {ToPsSingleQuoted(checkpointId ?? string.Empty)}; " +
                     "$checkpoint = $null; " +
                     "if (-not [string]::IsNullOrWhiteSpace($checkpointId)) { $checkpoint = Get-VMCheckpoint -VMName $vmName | ForEach-Object { $currentId = ''; if ($null -ne $_.VMCheckpointId) { $currentId = $_.VMCheckpointId.ToString() } elseif ($null -ne $_.Id) { $currentId = $_.Id.ToString() }; if ($currentId -ceq $checkpointId) { $_ } } | Select-Object -First 1 }; " +
                     "if ($null -eq $checkpoint) { $checkpoint = Get-VMCheckpoint -VMName $vmName | Where-Object { $_.Name -ceq $checkpointName } | Select-Object -First 1 }; " +
                     "if ($null -eq $checkpoint) { throw \"Checkpoint '$checkpointName' wurde auf VM '$vmName' nicht gefunden.\" }; " +
                     "Restore-VMCheckpoint -VMCheckpoint $checkpoint -Confirm:$false";

        return InvokeNonQueryAsync(script, cancellationToken);
    }

    public Task RemoveCheckpointAsync(string vmName, string checkpointName, string? checkpointId, CancellationToken cancellationToken)
    {
        var script = $"$vmName = {ToPsSingleQuoted(vmName)}; " +
                     $"$checkpointName = {ToPsSingleQuoted(checkpointName)}; " +
                     $"$checkpointId = {ToPsSingleQuoted(checkpointId ?? string.Empty)}; " +
                     "$checkpoint = $null; " +
                     "if (-not [string]::IsNullOrWhiteSpace($checkpointId)) { $checkpoint = Get-VMCheckpoint -VMName $vmName | ForEach-Object { $currentId = ''; if ($null -ne $_.VMCheckpointId) { $currentId = $_.VMCheckpointId.ToString() } elseif ($null -ne $_.Id) { $currentId = $_.Id.ToString() }; if ($currentId -ceq $checkpointId) { $_ } } | Select-Object -First 1 }; " +
                     "if ($null -eq $checkpoint) { $checkpoint = Get-VMCheckpoint -VMName $vmName | Where-Object { $_.Name -ceq $checkpointName } | Select-Object -First 1 }; " +
                     "if ($null -eq $checkpoint) { throw \"Checkpoint '$checkpointName' wurde auf VM '$vmName' nicht gefunden.\" }; " +
                     "Remove-VMCheckpoint -VMCheckpoint $checkpoint -Confirm:$false";

        return InvokeNonQueryAsync(script, cancellationToken);
    }

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

    public Task ExportVmAsync(string vmName, string destinationPath, CancellationToken cancellationToken) =>
        InvokeNonQueryAsync(
            $"Export-VM -Name {ToPsSingleQuoted(vmName)} -Path {ToPsSingleQuoted(destinationPath)} -Confirm:$false",
            cancellationToken);

    public async Task<string> ImportVmAsync(string importPath, CancellationToken cancellationToken)
    {
        var script = $"$importPath = {ToPsSingleQuoted(importPath)}; " +
                     "if (-not (Test-Path -LiteralPath $importPath)) { throw \"Import-Pfad nicht gefunden: $importPath\" }; " +
                     "$configPath = $importPath; " +
                     "if (Test-Path -LiteralPath $importPath -PathType Container) { " +
                     "$configFile = Get-ChildItem -LiteralPath $importPath -Recurse -File | " +
                     "Where-Object { $_.Extension -in '.vmcx', '.xml' } | " +
                     "Sort-Object LastWriteTime -Descending | " +
                     "Select-Object -First 1; " +
                     "if ($null -eq $configFile) { throw \"Keine VM-Konfigurationsdatei (.vmcx/.xml) im Ordner gefunden.\" }; " +
                     "$configPath = $configFile.FullName; " +
                     "}; " +
                     "$importedVm = Import-VM -Path $configPath -Confirm:$false; " +
                     "if ($null -eq $importedVm) { throw \"Import-VM hat keine VM zurückgegeben.\" }; " +
                     "$importedVm.Name";

        var importedName = await InvokePowerShellAsync(script, cancellationToken);
        return importedName.Trim();
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
        var wrappedScript = "$ErrorActionPreference = 'Stop'; " +
                            "[Console]::InputEncoding = [System.Text.Encoding]::UTF8; " +
                            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                            "$OutputEncoding = [System.Text.Encoding]::UTF8; " +
                            $"Import-Module Hyper-V -ErrorAction Stop; {script}";
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
        processStartInfo.StandardOutputEncoding = Encoding.UTF8;
        processStartInfo.StandardErrorEncoding = Encoding.UTF8;

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

    private static bool GetBoolean(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
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

    private static bool IsProductionCheckpointError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("production checkpoint", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Produktionsprüfpunkt", StringComparison.OrdinalIgnoreCase)
               || message.Contains("VSS", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Die Erstellung eines Prüfpunkts ist fehlgeschlagen", StringComparison.OrdinalIgnoreCase)
               || message.Contains("failed to create checkpoint", StringComparison.OrdinalIgnoreCase);
    }
}