param(
    [string]$GroupName = "HyperTool",
    [string]$UserName = "HyperToolGuest",
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-Admin {
    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

function Test-LocalGroupExists {
    param([string]$Name)

    try {
        $group = Get-LocalGroup -Name $Name -ErrorAction Stop
        return [PSCustomObject]@{ Exists = ($null -ne $group); Source = 'LocalAccounts'; Error = '' }
    }
    catch {
        try {
            $machine = $env:COMPUTERNAME
            $groupAdsi = [ADSI]("WinNT://$machine/$Name,group")
            $null = $groupAdsi.Name
            return [PSCustomObject]@{ Exists = $true; Source = 'ADSI'; Error = '' }
        }
        catch {
            return [PSCustomObject]@{ Exists = $false; Source = 'ADSI'; Error = $_.Exception.Message }
        }
    }
}

function Test-LocalUserExists {
    param([string]$Name)

    try {
        $user = Get-LocalUser -Name $Name -ErrorAction Stop
        return [PSCustomObject]@{ Exists = ($null -ne $user); Source = 'LocalAccounts'; Error = '' }
    }
    catch {
        try {
            $machine = $env:COMPUTERNAME
            $userAdsi = [ADSI]("WinNT://$machine/$Name,user")
            $null = $userAdsi.Name
            return [PSCustomObject]@{ Exists = $true; Source = 'ADSI'; Error = '' }
        }
        catch {
            return [PSCustomObject]@{ Exists = $false; Source = 'ADSI'; Error = $_.Exception.Message }
        }
    }
}

function Test-GroupMembership {
    param(
        [string]$Group,
        [string]$User
    )

    $machine = $env:COMPUTERNAME
    $memberPrincipal = "$machine\$User"

    try {
        $member = Get-LocalGroupMember -Group $Group -ErrorAction Stop |
            Where-Object {
                $_.Name -ieq $memberPrincipal -or
                $_.Name -ieq $User -or
                $_.Name -ieq ".\$User"
            } |
            Select-Object -First 1

        return [PSCustomObject]@{ IsMember = ($null -ne $member); Source = 'LocalAccounts'; Error = '' }
    }
    catch {
        try {
            $groupAdsi = [ADSI]("WinNT://$machine/$Group,group")
            $members = @($groupAdsi.psbase.Invoke('Members'))
            foreach ($m in $members) {
                $name = $m.GetType().InvokeMember('Name', 'GetProperty', $null, $m, $null)
                if ([string]::Equals($name, $User, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return [PSCustomObject]@{ IsMember = $true; Source = 'ADSI'; Error = '' }
                }
            }

            return [PSCustomObject]@{ IsMember = $false; Source = 'ADSI'; Error = '' }
        }
        catch {
            return [PSCustomObject]@{ IsMember = $false; Source = 'ADSI'; Error = $_.Exception.Message }
        }
    }
}

function Get-SocketRegistryStatus {
    $rootPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices'

    $required = @(
        [PSCustomObject]@{ Name = 'USB Tunnel'; Id = '6c4eb1be-40e8-4c8b-a4d6-5b0f67d7e40f'; Expected = 'HyperTool Hyper-V Socket USB Tunnel' },
        [PSCustomObject]@{ Name = 'Diagnostics'; Id = '67c53bca-3f3d-4628-98e4-e45be5d6d1ad'; Expected = 'HyperTool Hyper-V Socket Diagnostics' },
        [PSCustomObject]@{ Name = 'SharedFolder Catalog'; Id = 'e7db04df-0e32-4f30-a4dc-c6cbc31a8792'; Expected = 'HyperTool Hyper-V Socket Shared Folder Catalog' },
        [PSCustomObject]@{ Name = 'SharedFolder Credential'; Id = '0f9db05a-531f-4fd8-9b4d-675f5f06f0d8'; Expected = 'HyperTool Hyper-V Socket Shared Folder Credential' },
        [PSCustomObject]@{ Name = 'Host Identity'; Id = '54b2c423-6f79-47d8-a77d-8cab14e3f041'; Expected = 'HyperTool Hyper-V Socket Host Identity' }
    )

    $items = @()
    foreach ($entry in $required) {
        $keyPath = Join-Path $rootPath $entry.Id
        $exists = Test-Path -LiteralPath $keyPath
        $elementName = ''
        $matchesExpected = $false

        if ($exists) {
            try {
                $prop = Get-ItemProperty -LiteralPath $keyPath -Name ElementName -ErrorAction Stop
                $elementName = [string]$prop.ElementName
                $matchesExpected = [string]::Equals($elementName, $entry.Expected, [System.StringComparison]::Ordinal)
            }
            catch {
                $elementName = ''
                $matchesExpected = $false
            }
        }

        $items += [PSCustomObject]@{
            Service = $entry.Name
            ServiceId = $entry.Id
            Exists = $exists
            ElementName = $elementName
            ExpectedElementName = $entry.Expected
            ElementNameMatches = $matchesExpected
        }
    }

    return $items
}

function Test-DpapiPayload {
    param([string]$ProtectedPassword)

    $prefix = 'dpapi:'
    if ([string]::IsNullOrWhiteSpace($ProtectedPassword) -or -not $ProtectedPassword.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return [PSCustomObject]@{ IsDpapi = $false; LocalMachine = $false; CurrentUser = $false; Error = '' }
    }

    $payload = $ProtectedPassword.Substring($prefix.Length).Trim()
    if ([string]::IsNullOrWhiteSpace($payload)) {
        return [PSCustomObject]@{ IsDpapi = $true; LocalMachine = $false; CurrentUser = $false; Error = 'Payload leer' }
    }

    try {
        $encrypted = [Convert]::FromBase64String($payload)
    }
    catch {
        return [PSCustomObject]@{ IsDpapi = $true; LocalMachine = $false; CurrentUser = $false; Error = 'Base64 ungültig' }
    }

    $entropy = [System.Text.Encoding]::UTF8.GetBytes('HyperTool.Host.SharedFolder.Credential.v1')

    $localMachineOk = $false
    $currentUserOk = $false
    $lastError = ''

    try {
        [System.Security.Cryptography.ProtectedData]::Unprotect($encrypted, $entropy, [System.Security.Cryptography.DataProtectionScope]::LocalMachine) | Out-Null
        $localMachineOk = $true
    }
    catch {
        $lastError = $_.Exception.Message
    }

    try {
        [System.Security.Cryptography.ProtectedData]::Unprotect($encrypted, $entropy, [System.Security.Cryptography.DataProtectionScope]::CurrentUser) | Out-Null
        $currentUserOk = $true
    }
    catch {
        if ([string]::IsNullOrWhiteSpace($lastError)) {
            $lastError = $_.Exception.Message
        }
    }

    return [PSCustomObject]@{
        IsDpapi = $true
        LocalMachine = $localMachineOk
        CurrentUser = $currentUserOk
        Error = $lastError
    }
}

function Get-PrincipalSidValue {
    param([string]$AccountName)

    try {
        $account = New-Object System.Security.Principal.NTAccount($AccountName)
        return $account.Translate([System.Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        return ''
    }
}

function Get-PrivilegeEntries {
    param(
        [string[]]$Lines,
        [string]$Privilege
    )

    foreach ($line in $Lines) {
        if ($line -notmatch ('^{0}\s*=' -f [regex]::Escape($Privilege))) {
            continue
        }

        $parts = $line -split '=', 2
        if ($parts.Count -lt 2 -or [string]::IsNullOrWhiteSpace($parts[1])) {
            return @()
        }

        return $parts[1].Split(',') | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    return @()
}

function Test-NetworkLogonPolicy {
    param(
        [string]$Group,
        [string]$User
    )

    $machine = $env:COMPUTERNAME
    $userPrincipal = "$machine\$User"
    $groupPrincipal = "$machine\$Group"
    $userSid = Get-PrincipalSidValue -AccountName $userPrincipal
    $groupSid = Get-PrincipalSidValue -AccountName $groupPrincipal

    $tempRoot = Join-Path $env:TEMP ("hypertool-status-secpol-{0}" -f [Guid]::NewGuid().ToString('N'))
    $cfgPath = Join-Path $tempRoot 'security.inf'

    $allowEntries = @()
    $denyEntries = @()
    $errorText = ''
    $exportOk = $false

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        & secedit.exe /export /cfg $cfgPath /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $cfgPath)) {
            throw "secedit /export fehlgeschlagen (ExitCode=$LASTEXITCODE)."
        }

        $exportOk = $true
        $lines = Get-Content -LiteralPath $cfgPath -ErrorAction Stop
        $allowEntries = Get-PrivilegeEntries -Lines $lines -Privilege 'SeNetworkLogonRight'
        $denyEntries = Get-PrivilegeEntries -Lines $lines -Privilege 'SeDenyNetworkLogonRight'
    }
    catch {
        $errorText = $_.Exception.Message
    }
    finally {
        try {
            if (Test-Path -LiteralPath $tempRoot) {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
        }
    }

    $allowSet = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $allowEntries) {
        [void]$allowSet.Add($entry.Trim().TrimStart('*'))
    }

    $denySet = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $denyEntries) {
        [void]$denySet.Add($entry.Trim().TrimStart('*'))
    }

    $userAllow = (-not [string]::IsNullOrWhiteSpace($userSid) -and $allowSet.Contains($userSid)) -or $allowSet.Contains($userPrincipal) -or $allowSet.Contains($User) -or $allowSet.Contains(".\$User")
    $groupAllow = (-not [string]::IsNullOrWhiteSpace($groupSid) -and $allowSet.Contains($groupSid)) -or $allowSet.Contains($groupPrincipal) -or $allowSet.Contains($Group) -or $allowSet.Contains(".\$Group")
    $userDeny = (-not [string]::IsNullOrWhiteSpace($userSid) -and $denySet.Contains($userSid)) -or $denySet.Contains($userPrincipal) -or $denySet.Contains($User) -or $denySet.Contains(".\$User")
    $groupDeny = (-not [string]::IsNullOrWhiteSpace($groupSid) -and $denySet.Contains($groupSid)) -or $denySet.Contains($groupPrincipal) -or $denySet.Contains($Group) -or $denySet.Contains(".\$Group")

    return [PSCustomObject]@{
        ExportSucceeded = $exportOk
        Error = $errorText
        UserSid = $userSid
        GroupSid = $groupSid
        SeNetworkLogonRightUser = $userAllow
        SeNetworkLogonRightGroup = $groupAllow
        SeDenyNetworkLogonRightUser = $userDeny
        SeDenyNetworkLogonRightGroup = $groupDeny
        AllowEntries = $allowEntries
        DenyEntries = $denyEntries
    }
}

function Get-CredentialState {
    $localCredentialsPath = Join-Path $env:LOCALAPPDATA 'HyperTool\credentials\host-sharedfolder-credential.json'
    $localAppDataLegacyPath = Join-Path $env:LOCALAPPDATA 'HyperTool\host-sharedfolder-credential.json'
    $programDataPath = Join-Path $env:ProgramData 'HyperTool\host-sharedfolder-credential.json'

    $stateCandidates = @(
        [PSCustomObject]@{ Scope = 'LocalAppData(CurrentUser)'; Path = $localCredentialsPath },
        [PSCustomObject]@{ Scope = 'LocalAppData(Legacy)'; Path = $localAppDataLegacyPath },
        [PSCustomObject]@{ Scope = 'ProgramData(Legacy)'; Path = $programDataPath }
    )

    $states = @()

    foreach ($candidate in $stateCandidates) {
        $exists = Test-Path -LiteralPath $candidate.Path
        $jsonValid = $false
        $username = ''
        $groupName = ''
        $updatedAtUtc = ''
        $protectedPassword = ''
        $dpapi = $null
        $stateError = ''

        if ($exists) {
            try {
                $raw = Get-Content -LiteralPath $candidate.Path -Raw -ErrorAction Stop
                $parsed = $raw | ConvertFrom-Json -ErrorAction Stop
                $jsonValid = $true
                $username = [string]$parsed.Username
                $groupName = [string]$parsed.GroupName
                $updatedAtUtc = [string]$parsed.UpdatedAtUtc
                $protectedPassword = [string]$parsed.ProtectedPassword
                $dpapi = Test-DpapiPayload -ProtectedPassword $protectedPassword
            }
            catch {
                $stateError = $_.Exception.Message
            }
        }

        $states += [PSCustomObject]@{
            Scope = $candidate.Scope
            Path = $candidate.Path
            Exists = $exists
            JsonValid = $jsonValid
            Username = $username
            GroupName = $groupName
            UpdatedAtUtc = $updatedAtUtc
            ProtectedPasswordPresent = -not [string]::IsNullOrWhiteSpace($protectedPassword)
            Dpapi = $dpapi
            Error = $stateError
        }
    }

    return $states
}

$groupStatus = Test-LocalGroupExists -Name $GroupName
$userStatus = Test-LocalUserExists -Name $UserName
$membershipStatus = Test-GroupMembership -Group $GroupName -User $UserName
$socketRegistry = Get-SocketRegistryStatus
$credentialState = Get-CredentialState
$networkLogonPolicy = Test-NetworkLogonPolicy -Group $GroupName -User $UserName

$summary = [PSCustomObject]@{
    Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ComputerName = $env:COMPUTERNAME
    RunningAsAdministrator = Test-Admin
    GroupName = $GroupName
    UserName = $UserName
    GroupExists = $groupStatus.Exists
    UserExists = $userStatus.Exists
    UserInGroup = $membershipStatus.IsMember
    GroupProbeSource = $groupStatus.Source
    UserProbeSource = $userStatus.Source
    MembershipProbeSource = $membershipStatus.Source
    NetworkLogonRightUser = $networkLogonPolicy.SeNetworkLogonRightUser
    NetworkLogonRightGroup = $networkLogonPolicy.SeNetworkLogonRightGroup
    NetworkDenyRightUser = $networkLogonPolicy.SeDenyNetworkLogonRightUser
    NetworkDenyRightGroup = $networkLogonPolicy.SeDenyNetworkLogonRightGroup
    SocketRegistryAllPresent = ($socketRegistry | Where-Object { -not $_.Exists }).Count -eq 0
    SocketRegistryAllElementNamesMatch = ($socketRegistry | Where-Object { -not $_.ElementNameMatches }).Count -eq 0
    CredentialStateAnyPresent = ($credentialState | Where-Object { $_.Exists }).Count -gt 0
}

$result = [PSCustomObject]@{
    Summary = $summary
    GroupStatus = $groupStatus
    UserStatus = $userStatus
    MembershipStatus = $membershipStatus
    NetworkLogonPolicy = $networkLogonPolicy
    SocketRegistry = $socketRegistry
    CredentialState = $credentialState
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 7
    exit 0
}

Write-Host "=== HyperTool Read-Only Diagnose ==="
Write-Host ("Zeit: {0}" -f $summary.Timestamp)
Write-Host ("Computer: {0}" -f $summary.ComputerName)
Write-Host ("Admin: {0}" -f $summary.RunningAsAdministrator)
Write-Host ""

Write-Host "--- Principals ---"
Write-Host ("Gruppe '{0}': {1} (Quelle: {2})" -f $GroupName, $summary.GroupExists, $summary.GroupProbeSource)
Write-Host ("Benutzer '{0}': {1} (Quelle: {2})" -f $UserName, $summary.UserExists, $summary.UserProbeSource)
Write-Host ("Mitgliedschaft '{0} in {1}': {2} (Quelle: {3})" -f $UserName, $GroupName, $summary.UserInGroup, $summary.MembershipProbeSource)

if (-not [string]::IsNullOrWhiteSpace($groupStatus.Error)) { Write-Host ("Group-Fehler: {0}" -f $groupStatus.Error) }
if (-not [string]::IsNullOrWhiteSpace($userStatus.Error)) { Write-Host ("User-Fehler: {0}" -f $userStatus.Error) }
if (-not [string]::IsNullOrWhiteSpace($membershipStatus.Error)) { Write-Host ("Membership-Fehler: {0}" -f $membershipStatus.Error) }

Write-Host ""
Write-Host "--- SMB Logon Rights ---"
Write-Host ("SeNetworkLogonRight(User)={0}; SeNetworkLogonRight(Group)={1}; SeDenyNetworkLogonRight(User)={2}; SeDenyNetworkLogonRight(Group)={3}" -f `
    $summary.NetworkLogonRightUser, $summary.NetworkLogonRightGroup, $summary.NetworkDenyRightUser, $summary.NetworkDenyRightGroup)

if (-not [string]::IsNullOrWhiteSpace($networkLogonPolicy.Error)) {
    Write-Host ("Policy-Fehler: {0}" -f $networkLogonPolicy.Error)
}

Write-Host ""
Write-Host "--- Socket Registry ---"
$socketRegistry |
    Select-Object Service, Exists, ElementNameMatches, ServiceId, ElementName |
    Format-Table -AutoSize |
    Out-String |
    Write-Host

Write-Host ""
Write-Host "--- Credential State Files ---"
foreach ($state in $credentialState) {
    Write-Host ("[{0}] Exists={1}; JsonValid={2}; Username='{3}'; Group='{4}'; UpdatedAtUtc='{5}'; ProtectedPasswordPresent={6}" -f `
        $state.Scope, $state.Exists, $state.JsonValid, $state.Username, $state.GroupName, $state.UpdatedAtUtc, $state.ProtectedPasswordPresent)

    if ($null -ne $state.Dpapi) {
        Write-Host ("    DPAPI: IsDpapi={0}; LocalMachine={1}; CurrentUser={2}; Error='{3}'" -f `
            $state.Dpapi.IsDpapi, $state.Dpapi.LocalMachine, $state.Dpapi.CurrentUser, $state.Dpapi.Error)
    }

    if (-not [string]::IsNullOrWhiteSpace($state.Error)) {
        Write-Host ("    Fehler: {0}" -f $state.Error)
    }
}

Write-Host ""
Write-Host "--- Summary ---"
Write-Host ("SocketRegistryAllPresent={0}; SocketRegistryAllElementNamesMatch={1}; CredentialStateAnyPresent={2}" -f `
    $summary.SocketRegistryAllPresent, $summary.SocketRegistryAllElementNamesMatch, $summary.CredentialStateAnyPresent)

exit 0
