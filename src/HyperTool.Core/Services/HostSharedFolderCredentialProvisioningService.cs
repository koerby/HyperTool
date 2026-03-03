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
    private const string SharedFolderTestingPassword = "HyperTool#26!";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string CreateDeterministicGuestPassword(string userName, string groupName)
    {
        return SharedFolderTestingPassword;
    }

    public static string GetTestingPassword()
    {
        return SharedFolderTestingPassword;
    }

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
        if (string.IsNullOrWhiteSpace(password) || !IsPasswordPolicyFriendly(password))
        {
            password = SharedFolderTestingPassword;
        }

        var lastProvisioningError = string.Empty;
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await EnsureLocalSecurityPrincipalAsync(groupName, userName, password, cancellationToken);
                lastProvisioningError = string.Empty;
                break;
            }
            catch (Exception ex)
            {
                lastProvisioningError = ex.Message;

                if (attempt >= maxAttempts || !LooksLikePasswordPolicyIssue(ex.Message))
                {
                    throw;
                }

                password = GenerateStrongPassword(24);
            }
        }

        if (!string.IsNullOrWhiteSpace(lastProvisioningError))
        {
            throw new InvalidOperationException(lastProvisioningError);
        }

        var principalState = await QueryPrincipalStateAsync(groupName, userName, cancellationToken);
        if (!principalState.GroupExists || !principalState.UserExists || !principalState.UserInGroup)
        {
            throw new InvalidOperationException(
                $"Lokale SharedFolder-Sicherheitsidentität ist unvollständig. GroupExists={principalState.GroupExists}; UserExists={principalState.UserExists}; UserInGroup={principalState.UserInGroup}; Probe={principalState.ProbeSource}; Error={principalState.Error}");
        }

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
        var username = string.IsNullOrWhiteSpace(state?.Username)
            ? DefaultUserName
            : state!.Username.Trim();
        var groupName = string.IsNullOrWhiteSpace(state?.GroupName)
            ? DefaultGroupName
            : state!.GroupName.Trim();
        var password = UnprotectPassword(state?.ProtectedPassword);

        var fallbackUsed = false;
        if (string.IsNullOrWhiteSpace(password))
        {
            password = SharedFolderTestingPassword;
            fallbackUsed = true;
        }

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(groupName)
            || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        try
        {
            using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _ = Task.Run(
                () => QueryPrincipalStateAsync(groupName, username, probeCts.Token),
                probeCts.Token).GetAwaiter().GetResult();
        }
        catch
        {
        }

        credential = new HostSharedFolderGuestCredential
        {
            Available = true,
            Username = username,
            Password = password,
            GroupName = groupName,
            GroupPrincipal = BuildLocalPrincipal(groupName),
            HostName = Environment.MachineName,
            Source = fallbackUsed ? "host-derived" : "host-state"
        };

        return true;
    }

    public bool TryPersistCredentialState(string userName, string groupName, string plainPassword, out string error)
    {
        error = string.Empty;

        var normalizedUserName = (userName ?? string.Empty).Trim();
        var normalizedGroupName = (groupName ?? string.Empty).Trim();
        var normalizedPassword = (plainPassword ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedUserName)
            || string.IsNullOrWhiteSpace(normalizedGroupName)
            || string.IsNullOrWhiteSpace(normalizedPassword))
        {
            error = "Ungültige Credential-State Parameter.";
            return false;
        }

        try
        {
            var state = new PersistedCredentialState
            {
                Username = normalizedUserName,
                GroupName = normalizedGroupName,
                ProtectedPassword = ProtectPassword(normalizedPassword),
                UpdatedAtUtc = DateTime.UtcNow.ToString("O")
            };

            WriteState(state);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed class PrincipalState
    {
        public bool GroupExists { get; set; }
        public bool UserExists { get; set; }
        public bool UserInGroup { get; set; }
        public string ProbeSource { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    private static async Task EnsureLocalSecurityPrincipalAsync(string groupName, string userName, string password, CancellationToken cancellationToken)
    {
        string netDetails = string.Empty;
        try
        {
            await EnsureLocalSecurityPrincipalViaNetCommandsAsync(groupName, userName, password, cancellationToken);
        {
            return;
        }
        }
        catch (Exception ex)
        {
            netDetails = ex.Message;
        }

        var localAccountsScript = $@"
$ErrorActionPreference = 'Stop'
$groupName = {ToPsSingleQuoted(groupName)}
$userName = {ToPsSingleQuoted(userName)}
$passwordPlain = {ToPsSingleQuoted(password)}
$securePassword = ConvertTo-SecureString -String $passwordPlain -AsPlainText -Force

$existingGroup = Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue
if ($null -eq $existingGroup) {{
    New-LocalGroup -Name $groupName -Description 'HyperTool shared group' | Out-Null
}}

$existingUser = Get-LocalUser -Name $userName -ErrorAction SilentlyContinue
if ($null -eq $existingUser) {{
    New-LocalUser -Name $userName -Password $securePassword -AccountNeverExpires -PasswordNeverExpires -UserMayNotChangePassword -Description 'HyperTool guest account' | Out-Null
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

        if (string.IsNullOrWhiteSpace(netDetails))
        {
            netDetails = "Unbekannter Fehler";
        }
        var localAccountsDetails = BuildErrorMessage(localAccountsResult.StandardError, localAccountsResult.StandardOutput);
        var adsiDetails = BuildErrorMessage(adsiResult.StandardError, adsiResult.StandardOutput);
        throw new InvalidOperationException(
            $"Lokale SharedFolder-Sicherheitsidentität konnte nicht bereitgestellt werden. NET: {netDetails} | LocalAccounts: {localAccountsDetails} | ADSI-Fallback: {adsiDetails}");
    }

    private static async Task EnsureLocalSecurityPrincipalViaNetCommandsAsync(string groupName, string userName, string password, CancellationToken cancellationToken)
    {
        static bool containsAny(string text, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (!string.IsNullOrWhiteSpace(needle)
                    && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        var addGroup = await RunProcessAsync(
            "net.exe",
            $"localgroup {QuoteArg(groupName)} /add",
            cancellationToken,
            timeoutMs: 15000);

        var addGroupOutput = BuildErrorMessage(addGroup.StandardError, addGroup.StandardOutput);
        if (addGroup.ExitCode != 0
            && !containsAny(addGroupOutput, "bereits", "already", "exists", "2223"))
        {
            throw new InvalidOperationException($"NET localgroup /add fehlgeschlagen: {addGroupOutput}");
        }

        var addUser = await RunProcessAsync(
            "net.exe",
            $"user {QuoteArg(userName)} {QuoteArg(password)} /add /passwordchg:no /expires:never",
            cancellationToken,
            timeoutMs: 15000);

        var addUserOutput = BuildErrorMessage(addUser.StandardError, addUser.StandardOutput);
        if (addUser.ExitCode != 0)
        {
            if (containsAny(addUserOutput, "bereits", "already", "exists", "2224"))
            {
                var setUser = await RunProcessAsync(
                    "net.exe",
                    $"user {QuoteArg(userName)} {QuoteArg(password)} /passwordchg:no /expires:never",
                    cancellationToken,
                    timeoutMs: 15000);

                var setUserOutput = BuildErrorMessage(setUser.StandardError, setUser.StandardOutput);
                if (setUser.ExitCode != 0)
                {
                    throw new InvalidOperationException($"NET user Passwort setzen fehlgeschlagen: {setUserOutput}");
                }
            }
            else
            {
                throw new InvalidOperationException($"NET user /add fehlgeschlagen: {addUserOutput}");
            }
        }

        var addMember = await RunProcessAsync(
            "net.exe",
            $"localgroup {QuoteArg(groupName)} {QuoteArg(userName)} /add",
            cancellationToken,
            timeoutMs: 15000);

        var addMemberOutput = BuildErrorMessage(addMember.StandardError, addMember.StandardOutput);
        if (addMember.ExitCode != 0
            && !containsAny(addMemberOutput, "bereits", "already", "member", "1378"))
        {
            throw new InvalidOperationException($"NET localgroup member /add fehlgeschlagen: {addMemberOutput}");
        }
    }

    private static string QuoteArg(string value)
    {
        var normalized = (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{normalized}\"";
    }

    private static async Task<PrincipalState> QueryPrincipalStateAsync(string groupName, string userName, CancellationToken cancellationToken)
    {
        var script = $@"
$ErrorActionPreference = 'Stop'
$groupName = {ToPsSingleQuoted(groupName)}
$userName = {ToPsSingleQuoted(userName)}
$machine = $env:COMPUTERNAME

$groupExists = $false
$userExists = $false
$memberExists = $false
$probeSource = 'LocalAccounts'

try {{
    $group = Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue
    $user = Get-LocalUser -Name $userName -ErrorAction SilentlyContinue
    $groupExists = $null -ne $group
    $userExists = $null -ne $user

    if ($groupExists -and $userExists) {{
        $memberPrincipal = ""$machine\$userName""
        $candidate = Get-LocalGroupMember -Group $groupName -ErrorAction SilentlyContinue |
            Where-Object {{
                $_.Name -ieq $memberPrincipal -or
                $_.Name -ieq $userName -or
                $_.Name -ieq "".\$userName""
            }} |
            Select-Object -First 1
        $memberExists = $null -ne $candidate
    }}
}}
catch {{
    $probeSource = 'ADSI'
    try {{
        $groupAdsi = [ADSI](""WinNT://$machine/$groupName,group"")
        $null = $groupAdsi.Name
        $groupExists = $true
    }} catch {{ $groupExists = $false }}

    try {{
        $userAdsi = [ADSI](""WinNT://$machine/$userName,user"")
        $null = $userAdsi.Name
        $userExists = $true
    }} catch {{ $userExists = $false }}

    if ($groupExists -and $userExists) {{
        try {{
            $groupAdsi = [ADSI](""WinNT://$machine/$groupName,group"")
            $members = @($groupAdsi.psbase.Invoke('Members'))
            foreach ($m in $members) {{
                $name = $m.GetType().InvokeMember('Name', 'GetProperty', $null, $m, $null)
                if ([string]::Equals($name, $userName, [System.StringComparison]::OrdinalIgnoreCase)) {{
                    $memberExists = $true
                    break
                }}
            }}
        }} catch {{ $memberExists = $false }}
    }}
}}

if (-not $groupExists -or -not $userExists) {{
    $probeSource = 'NetCommands'

    & net.exe localgroup $groupName | Out-Null
    if ($LASTEXITCODE -eq 0) {{
        $groupExists = $true
    }}

    & net.exe user $userName | Out-Null
    if ($LASTEXITCODE -eq 0) {{
        $userExists = $true
    }}

    if ($groupExists -and $userExists) {{
        $groupOutput = & net.exe localgroup $groupName 2>$null
        if ($LASTEXITCODE -eq 0 -and $groupOutput) {{
            $escapedUser = [Regex]::Escape($userName)
            $memberExists = [Regex]::IsMatch(($groupOutput -join ""`n""), ""(?im)^\s*$escapedUser\s*$"")
        }}
    }}
}}

if ($groupExists) {{ 'GROUP=1' }} else {{ 'GROUP=0' }}
if ($userExists) {{ 'USER=1' }} else {{ 'USER=0' }}
if ($memberExists) {{ 'MEMBER=1' }} else {{ 'MEMBER=0' }}
Write-Output ('PROBE=' + $probeSource)
";

        var result = await RunPowerShellAsync(script, cancellationToken);
        var output = result.StandardOutput ?? string.Empty;
        var error = result.StandardError ?? string.Empty;

        return new PrincipalState
        {
            GroupExists = output.Contains("GROUP=1", StringComparison.OrdinalIgnoreCase),
            UserExists = output.Contains("USER=1", StringComparison.OrdinalIgnoreCase),
            UserInGroup = output.Contains("MEMBER=1", StringComparison.OrdinalIgnoreCase),
            ProbeSource = output.Contains("PROBE=ADSI", StringComparison.OrdinalIgnoreCase) ? "ADSI" : "LocalAccounts",
            Error = result.ExitCode == 0 ? string.Empty : BuildErrorMessage(error, output)
        };
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

try {{
    $user.psbase.Invoke('SetPassword', $passwordPlain) | Out-Null
}}
catch {{
    try {{
        $resolvedUser = [ADSI](""WinNT://$machine/$userName,user"")
        $resolvedUser.psbase.Invoke('SetPassword', $passwordPlain) | Out-Null
    }}
    catch {{
        & net.exe user $userName $passwordPlain | Out-Null
        if ($LASTEXITCODE -ne 0) {{
            throw
        }}
    }}
}}

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
        foreach (var path in GetStateFileReadCandidates())
        {
            var state = TryReadStateFile(path);
            if (IsUsableState(state))
            {
                return state;
            }
        }

        return null;
    }

    private static PersistedCredentialState? TryReadStateFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var raw = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PersistedCredentialState>(raw, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUsableState(PersistedCredentialState? state)
    {
        if (state is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(state.Username)
               && !string.IsNullOrWhiteSpace(state.GroupName)
               && !string.IsNullOrWhiteSpace(state.ProtectedPassword);
    }

    private static void WriteState(PersistedCredentialState state)
    {
        var payload = JsonSerializer.Serialize(state, SerializerOptions);
        var primaryPath = GetStateFilePath();

        try
        {
            WriteStateFile(primaryPath, payload);
        }
        catch
        {
        }

        foreach (var fallbackPath in GetStateFileWriteFallbacks())
        {
            try
            {
                WriteStateFile(fallbackPath, payload);
                break;
            }
            catch
            {
            }
        }

        foreach (var mirrorPath in GetStateFileWriteFallbacks())
        {
            if (string.Equals(mirrorPath, primaryPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                WriteStateFile(mirrorPath, payload);
            }
            catch
            {
            }
        }
    }

    private static void WriteStateFile(string filePath, string payload)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, payload);
    }

    private static string GetStateFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperTool",
            "credentials",
            "host-sharedfolder-credential.json");
    }

    private static string GetLegacyUserStateFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperTool",
            "host-sharedfolder-credential.json");
    }

    private static string GetLegacyMachineStateFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HyperTool",
            "host-sharedfolder-credential.json");
    }

    private static IReadOnlyList<string> GetStateFileReadCandidates()
    {
        return
        [
            GetStateFilePath(),
            GetLegacyUserStateFilePath(),
            GetLegacyMachineStateFilePath()
        ];
    }

    private static IReadOnlyList<string> GetStateFileWriteFallbacks()
    {
        return
        [
            GetLegacyUserStateFilePath(),
            GetLegacyMachineStateFilePath()
        ];
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
        var protectedBytes = ProtectedData.Protect(bytes, GetPasswordEntropy(), DataProtectionScope.LocalMachine);
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
            var plain = ProtectedData.Unprotect(encrypted, GetPasswordEntropy(), DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain).Trim();
        }
        catch
        {
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
    }

    private static byte[] GetPasswordEntropy()
    {
        return Encoding.UTF8.GetBytes("HyperTool.Host.SharedFolder.Credential.v1");
    }

    private static string GenerateStrongPassword(int length)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!";
        const string all = upper + lower + digits + symbols;

        if (length < 16)
        {
            length = 16;
        }

        var chars = new List<char>(capacity: length)
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };

        for (var i = chars.Count; i < length; i++)
        {
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        }

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars.ToArray());
    }

    private static bool IsPasswordPolicyFriendly(string? value)
    {
        var password = (value ?? string.Empty).Trim();
        if (password.Length < 16)
        {
            return false;
        }

        var hasUpper = password.Any(char.IsUpper);
        var hasLower = password.Any(char.IsLower);
        var hasDigit = password.Any(char.IsDigit);
        var hasSymbol = password.Any(ch => !char.IsLetterOrDigit(ch));

        return hasUpper && hasLower && hasDigit && hasSymbol;
    }

    private static bool LooksLikePasswordPolicyIssue(string? message)
    {
        var text = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("kennwort", StringComparison.OrdinalIgnoreCase)
               || text.Contains("password", StringComparison.OrdinalIgnoreCase)
               || text.Contains("policy", StringComparison.OrdinalIgnoreCase)
               || text.Contains("anforder", StringComparison.OrdinalIgnoreCase)
               || text.Contains("complex", StringComparison.OrdinalIgnoreCase)
               || text.Contains("1385", StringComparison.OrdinalIgnoreCase)
               || text.Contains("2245", StringComparison.OrdinalIgnoreCase);
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
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

        var tempScriptPath = Path.Combine(
            Path.GetTempPath(),
            $"hypertool-sharedfolder-{Guid.NewGuid():N}.ps1");

        File.WriteAllText(tempScriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();

        try
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var standardErrorTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

            return (
                process.ExitCode,
                await standardOutputTask.ConfigureAwait(false),
                await standardErrorTask.ConfigureAwait(false));
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

            throw new TimeoutException("PowerShell-Prozess-Timeout beim SharedFolder-Credential-Probe.");
        }
        finally
        {
            try
            {
                File.Delete(tempScriptPath);
            }
            catch
            {
            }
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken, int timeoutMs)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs)));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();

        try
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var standardErrorTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);
            return (process.ExitCode, await standardOutputTask, await standardErrorTask);
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

            throw new TimeoutException($"Prozess-Timeout: {fileName} {arguments}");
        }
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
