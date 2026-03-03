using HyperTool.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HostSharedFolderCredentialProvisioningService
{
    private const string ProtectedPasswordPrefix = "dpapi:";
    private const string DefaultGroupName = "HyperTool";
    private const string DefaultUserName = "HyperToolGuest";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed class PersistedCredentialState
    {
        public string Username { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string ProtectedPassword { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }

    public async Task<HostSharedFolderGuestCredential> EnsureProvisionedAsync(CancellationToken cancellationToken)
    {
        EnsureRunningAsAdministrator();

        var state = TryReadState();
        var userName = string.IsNullOrWhiteSpace(state?.Username) ? DefaultUserName : state.Username.Trim();
        var groupName = string.IsNullOrWhiteSpace(state?.GroupName) ? DefaultGroupName : state.GroupName.Trim();
        var password = UnprotectPassword(state?.ProtectedPassword);
        if (string.IsNullOrWhiteSpace(password))
        {
            password = GenerateStrongPassword(32);
        }

        await EnsureLocalSecurityPrincipalAsync(groupName, userName, password, cancellationToken);

        var normalized = new PersistedCredentialState
        {
            Username = userName,
            GroupName = groupName,
            ProtectedPassword = ProtectPassword(password),
            UpdatedAtUtc = DateTime.UtcNow.ToString("O")
        };

        WriteState(normalized);

        return new HostSharedFolderGuestCredential
        {
            Available = true,
            Username = normalized.Username,
            Password = password,
            GroupName = normalized.GroupName,
            GroupPrincipal = BuildLocalPrincipal(normalized.GroupName),
            HostName = Environment.MachineName,
            Source = "host-provisioning"
        };
    }

    public bool TryGetCredential(out HostSharedFolderGuestCredential credential)
    {
        credential = new HostSharedFolderGuestCredential();

        var state = TryReadState();
        if (state is null)
        {
            return false;
        }

        var username = (state.Username ?? string.Empty).Trim();
        var groupName = (state.GroupName ?? string.Empty).Trim();
        var password = UnprotectPassword(state.ProtectedPassword);

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(groupName)
            || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        credential = new HostSharedFolderGuestCredential
        {
            Available = true,
            Username = username,
            Password = password,
            GroupName = groupName,
            GroupPrincipal = BuildLocalPrincipal(groupName),
            HostName = Environment.MachineName,
            Source = "host-state"
        };

        return true;
    }

    private static async Task EnsureLocalSecurityPrincipalAsync(string groupName, string userName, string password, CancellationToken cancellationToken)
    {
        var localAccountsScript = $@"
$ErrorActionPreference = 'Stop'
$groupName = {ToPsSingleQuoted(groupName)}
$userName = {ToPsSingleQuoted(userName)}
$passwordPlain = {ToPsSingleQuoted(password)}
$securePassword = ConvertTo-SecureString -String $passwordPlain -AsPlainText -Force

$existingGroup = Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue
if ($null -eq $existingGroup) {{
    New-LocalGroup -Name $groupName -Description 'HyperTool shared folder access group' | Out-Null
}}

$existingUser = Get-LocalUser -Name $userName -ErrorAction SilentlyContinue
if ($null -eq $existingUser) {{
    New-LocalUser -Name $userName -Password $securePassword -AccountNeverExpires -PasswordNeverExpires -UserMayNotChangePassword -Description 'HyperTool guest shared-folder account' | Out-Null
}}
else {{
    Set-LocalUser -Name $userName -Password $securePassword | Out-Null
}}

$memberPrincipal = ""$env:COMPUTERNAME\$userName""
$alreadyMember = Get-LocalGroupMember -Group $groupName -ErrorAction SilentlyContinue |
    Where-Object {{
        $_.Name -ieq $memberPrincipal -or
        $_.Name -ieq $userName -or
        $_.Name -ieq "".\\$userName""
    }} |
    Select-Object -First 1

if ($null -eq $alreadyMember) {{
    Add-LocalGroupMember -Group $groupName -Member $memberPrincipal -ErrorAction Stop
}}
";

        var localAccountsResult = await RunPowerShellAsync(localAccountsScript, cancellationToken);
        if (localAccountsResult.ExitCode == 0)
        {
            return;
        }

        var adsiScript = BuildAdsiProvisioningScript(groupName, userName, password);
        var adsiResult = await RunPowerShellAsync(adsiScript, cancellationToken);
        if (adsiResult.ExitCode == 0)
        {
            return;
        }

        var localAccountsDetails = BuildErrorMessage(localAccountsResult.StandardError, localAccountsResult.StandardOutput);
        var adsiDetails = BuildErrorMessage(adsiResult.StandardError, adsiResult.StandardOutput);
        throw new InvalidOperationException(
            $"Lokale SharedFolder-Sicherheitsidentität konnte nicht bereitgestellt werden. LocalAccounts: {localAccountsDetails} | ADSI-Fallback: {adsiDetails}");
    }

    private static string BuildAdsiProvisioningScript(string groupName, string userName, string password)
    {
        return $@"
$ErrorActionPreference = 'Stop'
$groupName = {ToPsSingleQuoted(groupName)}
$userName = {ToPsSingleQuoted(userName)}
$passwordPlain = {ToPsSingleQuoted(password)}
$machine = $env:COMPUTERNAME

$computer = [ADSI](""WinNT://$machine,computer"")

try {{
    $group = [ADSI](""WinNT://$machine/$groupName,group"")
    $null = $group.Name
}}
catch {{
    $group = $computer.Create('group', $groupName)
    $group.SetInfo()
}}

$userCreated = $false
try {{
    $user = [ADSI](""WinNT://$machine/$userName,user"")
    $null = $user.Name
}}
catch {{
    $user = $computer.Create('user', $userName)
    $user.SetInfo()
    $userCreated = $true
}}

$user.SetPassword($passwordPlain)

# ADS_USER_FLAG_ENUM bits
$ADS_UF_PASSWD_CANT_CHANGE = 0x40
$ADS_UF_DONT_EXPIRE_PASSWD = 0x10000

$flags = 0
try {{ $flags = [int]$user.UserFlags.Value }} catch {{ $flags = 0 }}
$user.UserFlags = ($flags -bor $ADS_UF_PASSWD_CANT_CHANGE -bor $ADS_UF_DONT_EXPIRE_PASSWD)
$user.SetInfo()

$memberPath = ""WinNT://$machine/$userName,user""
try {{
    $group.Add($memberPath)
}}
catch {{
    $message = $_.Exception.Message
    if ($message -notmatch 'already' -and $message -notmatch 'bereits') {{
        throw
    }}
}}
";
    }

    private static PersistedCredentialState? TryReadState()
    {
        try
        {
            if (!File.Exists(GetStateFilePath()))
            {
                return null;
            }

            var raw = File.ReadAllText(GetStateFilePath());
            return JsonSerializer.Deserialize<PersistedCredentialState>(raw, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(PersistedCredentialState state)
    {
        var filePath = GetStateFilePath();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(state, SerializerOptions);
        File.WriteAllText(filePath, payload);
    }

    private static string GetStateFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperTool",
            "host-sharedfolder-credential.json");
    }

    private static string BuildLocalPrincipal(string groupName)
    {
        var machine = Environment.MachineName;
        return string.IsNullOrWhiteSpace(machine)
            ? $".\\{groupName}"
            : $"{machine}\\{groupName}";
    }

    private static string ProtectPassword(string plainPassword)
    {
        var normalized = (plainPassword ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(normalized);
        var protectedBytes = ProtectedData.Protect(bytes, GetPasswordEntropy(), DataProtectionScope.CurrentUser);
        return ProtectedPasswordPrefix + Convert.ToBase64String(protectedBytes);
    }

    private static string UnprotectPassword(string? stored)
    {
        var normalized = (stored ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (!normalized.StartsWith(ProtectedPasswordPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var payload = normalized[ProtectedPasswordPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = Convert.FromBase64String(payload);
            var plain = ProtectedData.Unprotect(encrypted, GetPasswordEntropy(), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] GetPasswordEntropy()
    {
        return Encoding.UTF8.GetBytes("HyperTool.Host.SharedFolder.Credential.v1");
    }

    private static string GenerateStrongPassword(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%*+-_=#?";
        if (length < 16)
        {
            length = 16;
        }

        var buffer = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[buffer[i] % alphabet.Length];
        }

        return new string(chars);
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

        throw new InvalidOperationException("Administratorrechte sind für die Provisionierung der SharedFolder-Sicherheitsidentität erforderlich.");
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

    private static string ToPsSingleQuoted(string value)
    {
        return $"'{(value ?? string.Empty).Replace("'", "''")}'";
    }

    private static string EscapeForPowerShellCommandArgument(string script)
    {
        return (script ?? string.Empty).Replace("\"", "`\"");
    }
}
