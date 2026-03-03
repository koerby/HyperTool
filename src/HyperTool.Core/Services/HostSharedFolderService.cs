using HyperTool.Models;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace HyperTool.Services;

public sealed class HostSharedFolderService : IHostSharedFolderService
{
    private readonly HostSharedFolderCredentialProvisioningService _credentialProvisioningService = new();

    public async Task EnsureShareAsync(HostSharedFolderDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        EnsureRunningAsAdministrator();

        var sharedFolderCredential = await _credentialProvisioningService.EnsureProvisionedAsync(cancellationToken);

        var shareName = (definition.ShareName ?? string.Empty).Trim();
        var localPath = (definition.LocalPath ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(shareName))
        {
            throw new ArgumentException("Freigabename darf nicht leer sein.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw new ArgumentException("Lokaler Ordnerpfad darf nicht leer sein.", nameof(definition));
        }

        if (!Directory.Exists(localPath))
        {
            throw new DirectoryNotFoundException($"Lokaler Ordner '{localPath}' wurde nicht gefunden.");
        }

        var shareNamePs = ToPsSingleQuoted(shareName);
        var localPathPs = ToPsSingleQuoted(localPath);
        var groupPrincipal = string.IsNullOrWhiteSpace(sharedFolderCredential.GroupPrincipal)
            ? ResolveWorldSidAccountName()
            : sharedFolderCredential.GroupPrincipal;
        var groupPrincipalPs = ToPsSingleQuoted(groupPrincipal);
        var grantRight = definition.ReadOnly ? "RX" : "M";
        var grantRightPs = ToPsSingleQuoted(grantRight);

        var createShareCommand = definition.ReadOnly
            ? "New-SmbShare -Name $name -Path $path -Description 'HyperTool Shared Folder' -ReadAccess $groupPrincipal -Confirm:$false | Out-Null"
            : "New-SmbShare -Name $name -Path $path -Description 'HyperTool Shared Folder' -FullAccess $groupPrincipal -Confirm:$false | Out-Null";

        var script = $@"
$ErrorActionPreference = 'Stop'
$name = {shareNamePs}
$path = {localPathPs}
$groupPrincipal = {groupPrincipalPs}
$grantRight = {grantRightPs}
$existing = Get-SmbShare -Name $name -ErrorAction SilentlyContinue

if ($null -ne $existing -and -not [string]::Equals($existing.Path, $path, [System.StringComparison]::OrdinalIgnoreCase)) {{
    Remove-SmbShare -Name $name -Force -Confirm:$false | Out-Null
    $existing = $null
}}

if ($null -ne $existing) {{
    Remove-SmbShare -Name $name -Force -Confirm:$false | Out-Null
}}

{createShareCommand}

$aclTarget = ""$groupPrincipal:(OI)(CI)$grantRight""
$aclResult = & icacls $path /grant $aclTarget /inheritance:e
if ($LASTEXITCODE -ne 0) {{
    throw ""NTFS-Berechtigungen konnten nicht gesetzt werden: $($aclResult -join ' ')""
}}
";

        await RunPowerShellNonQueryAsync(script, cancellationToken);
    }

    public async Task RemoveShareAsync(string shareName, CancellationToken cancellationToken)
    {
        EnsureRunningAsAdministrator();

        var trimmed = (shareName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Freigabename darf nicht leer sein.", nameof(shareName));
        }

        var script = $@"
$ErrorActionPreference = 'Stop'
$name = {ToPsSingleQuoted(trimmed)}
$existing = Get-SmbShare -Name $name -ErrorAction SilentlyContinue
if ($null -ne $existing) {{
    Remove-SmbShare -Name $name -Force -Confirm:$false | Out-Null
}}
";

        await RunPowerShellNonQueryAsync(script, cancellationToken);
    }

    public async Task<bool> ShareExistsAsync(string shareName, CancellationToken cancellationToken)
    {
        var trimmed = (shareName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var script = $@"
    $name = {ToPsSingleQuoted(trimmed)}
    $existing = Get-SmbShare -Name $name -ErrorAction SilentlyContinue
    if ($null -eq $existing) {{ 'false' }} else {{ 'true' }}
    ";

        var output = await RunPowerShellQueryAsync(script, cancellationToken);
        return string.Equals(output.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RunPowerShellNonQueryAsync(string script, CancellationToken cancellationToken)
    {
        var result = await RunPowerShellAsync(script, cancellationToken);
        if (result.ExitCode != 0)
        {
            var details = BuildErrorMessage(result.StandardError, result.StandardOutput);
            if (LooksLikeAccountMappingError(details))
            {
                throw new InvalidOperationException(
                    "SMB-Berechtigungskonto konnte nicht aufgelöst werden (SID/Name-Mapping). Bitte HyperTool aktualisieren oder Freigabe manuell mit lokalisierter Gruppe setzen.");
            }

            if (LooksLikeAccessDenied(details))
            {
                throw new InvalidOperationException(
                    "Zugriff verweigert beim Erstellen/Ändern der SMB-Freigabe. Bitte HyperTool Host als Administrator starten.");
            }

            throw new InvalidOperationException($"Freigabe-Befehl fehlgeschlagen: {details}");
        }
    }

    private static async Task<string> RunPowerShellQueryAsync(string script, CancellationToken cancellationToken)
    {
        var result = await RunPowerShellAsync(script, cancellationToken);
        if (result.ExitCode != 0)
        {
            var details = BuildErrorMessage(result.StandardError, result.StandardOutput);
            throw new InvalidOperationException($"Freigabe-Abfrage fehlgeschlagen: {details}");
        }

        return result.StandardOutput ?? string.Empty;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{EscapeForPowerShellCommandArgument(script)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    private static string BuildErrorMessage(string? standardError, string? standardOutput)
    {
        var stderr = (standardError ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            return stderr;
        }

        var stdout = (standardOutput ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            return stdout;
        }

        return "Unbekannter Fehler";
    }

    private static void EnsureRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                return;
            }
        }
        catch
        {
        }

        throw new InvalidOperationException(
            "Für SMB-Freigaben sind Administratorrechte erforderlich. Bitte HyperTool Host als Administrator starten.");
    }

    private static bool LooksLikeAccessDenied(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return false;
        }

        return details.Contains("Zugriff verweigert", StringComparison.OrdinalIgnoreCase)
               || details.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
               || details.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase)
               || details.Contains("System Error 5", StringComparison.OrdinalIgnoreCase)
               || details.Contains("Windows System Error 5", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAccountMappingError(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return false;
        }

        return details.Contains("System Error 1332", StringComparison.OrdinalIgnoreCase)
               || details.Contains("Zuordnungen von Kontennamen und Sicherheitskennungen", StringComparison.OrdinalIgnoreCase)
               || details.Contains("No mapping between account names and security IDs", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWorldSidAccountName()
    {
        try
        {
            var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var account = sid.Translate(typeof(NTAccount)) as NTAccount;
            if (!string.IsNullOrWhiteSpace(account?.Value))
            {
                return account.Value;
            }
        }
        catch
        {
        }

        return "S-1-1-0";
    }

    private static string ToPsSingleQuoted(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string EscapeForPowerShellCommandArgument(string script)
    {
        return (script ?? string.Empty).Replace("\"", "`\"");
    }
}
