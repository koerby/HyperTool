param(
    [string]$GroupName = "HyperTool",
    [string]$UserName = "HyperToolGuest",
    [string]$Password,
    [switch]$UseRandomPassword,
    [switch]$ShowPassword,
    [switch]$Cleanup,
    [switch]$UseAdsiOnly,
    [switch]$SkipStateWrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TestingPassword = 'HyperTool#26!'

trap {
    $message = if ($null -ne $_.Exception) { $_.Exception.Message } else { ($_ | Out-String).Trim() }
    Write-Error "Unerwarteter Skriptfehler: $message"
    Write-Host "[INFO] Fenster schließt in 5 Sekunden..."
    Start-Sleep -Seconds 5
    exit 99
}

function Test-IsAdministrator {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function New-StrongPassword([int]$Length = 32) {
    if ($Length -lt 16) { $Length = 16 }
    $alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%*+-_=#?"
    $bytes = New-Object byte[] $Length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $chars = for ($i = 0; $i -lt $Length; $i++) { $alphabet[$bytes[$i] % $alphabet.Length] }
    -join $chars
}

function New-DeterministicPassword {
    param(
        [string]$User,
        [string]$Group
    )

    $normalizedUser = if ([string]::IsNullOrWhiteSpace($User)) { 'HyperToolGuest' } else { $User.Trim() }
    $normalizedGroup = if ([string]::IsNullOrWhiteSpace($Group)) { 'HyperTool' } else { $Group.Trim() }

    $machineGuid = ''
    try {
        $machineGuid = [string](Get-ItemPropertyValue -Path 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name 'MachineGuid' -ErrorAction Stop)
    }
    catch {
        $machineGuid = ''
    }

    if ([string]::IsNullOrWhiteSpace($machineGuid)) {
        $machineGuid = $env:COMPUTERNAME
    }

    $seed = "HyperTool|SharedFolder|$machineGuid|$normalizedUser|$normalizedGroup|v2"
    $hashBytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($seed))

    $upper = "ABCDEFGHJKLMNPQRSTUVWXYZ"
    $lower = "abcdefghijkmnopqrstuvwxyz"
    $digits = "23456789"
    $symbols = "!@$%*+-_="
    $all = $upper + $lower + $digits + $symbols

    $chars = New-Object System.Collections.Generic.List[char]
    [void]$chars.Add($upper[$hashBytes[0] % $upper.Length])
    [void]$chars.Add($lower[$hashBytes[1] % $lower.Length])
    [void]$chars.Add($digits[$hashBytes[2] % $digits.Length])
    [void]$chars.Add($symbols[$hashBytes[3] % $symbols.Length])

    for ($i = 4; $chars.Count -lt 32; $i++) {
        $b = $hashBytes[$i % $hashBytes.Length]
        [void]$chars.Add($all[$b % $all.Length])
    }

    return -join $chars.ToArray()
}

function Ensure-WithLocalAccounts {
    param(
        [string]$Group,
        [string]$User,
        [string]$PlainPassword
    )

    $securePassword = ConvertTo-SecureString -String $PlainPassword -AsPlainText -Force

    $existingGroup = Get-LocalGroup -Name $Group -ErrorAction SilentlyContinue
    if ($null -eq $existingGroup) {
        New-LocalGroup -Name $Group -Description 'HyperTool shared group' | Out-Null
        Write-Host "[OK] Gruppe erstellt: $Group"
    }
    else {
        Write-Host "[OK] Gruppe vorhanden: $Group"
    }

    $existingUser = Get-LocalUser -Name $User -ErrorAction SilentlyContinue
    if ($null -eq $existingUser) {
        New-LocalUser -Name $User -Password $securePassword -AccountNeverExpires -PasswordNeverExpires -UserMayNotChangePassword -Description 'HyperTool guest account' | Out-Null
        Write-Host "[OK] Benutzer erstellt: $User"
    }
    else {
        Set-LocalUser -Name $User -Password $securePassword | Out-Null
        Write-Host "[OK] Benutzer vorhanden, Passwort aktualisiert: $User"
    }

    $memberPrincipal = "$env:COMPUTERNAME\$User"
    $alreadyMember = Get-LocalGroupMember -Group $Group -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -ieq $memberPrincipal -or
            $_.Name -ieq $User -or
            $_.Name -ieq ".\$User"
        } |
        Select-Object -First 1

    if ($null -eq $alreadyMember) {
        Add-LocalGroupMember -Group $Group -Member $memberPrincipal -ErrorAction Stop
        Write-Host "[OK] Benutzer zur Gruppe hinzugefügt: $memberPrincipal -> $Group"
    }
    else {
        Write-Host "[OK] Mitgliedschaft vorhanden: $memberPrincipal -> $Group"
    }
}

function Ensure-WithAdsi {
    param(
        [string]$Group,
        [string]$User,
        [string]$PlainPassword
    )

    $machine = $env:COMPUTERNAME
    $computer = [ADSI]("WinNT://$machine,computer")

    try {
        $groupObj = [ADSI]("WinNT://$machine/$Group,group")
        $null = $groupObj.Name
        Write-Host "[OK] Gruppe vorhanden (ADSI): $Group"
    }
    catch {
        $groupObj = $computer.Create('group', $Group)
        $groupObj.SetInfo()
        Write-Host "[OK] Gruppe erstellt (ADSI): $Group"
    }

    try {
        $userObj = [ADSI]("WinNT://$machine/$User,user")
        $null = $userObj.Name
        Write-Host "[OK] Benutzer vorhanden (ADSI): $User"
    }
    catch {
        $userObj = $computer.Create('user', $User)
        $userObj.SetInfo()
        Write-Host "[OK] Benutzer erstellt (ADSI): $User"
    }

    try {
        $userObj.psbase.Invoke('SetPassword', $PlainPassword)
    }
    catch {
        try {
            $resolvedUser = [ADSI]("WinNT://$machine/$User,user")
            $resolvedUser.psbase.Invoke('SetPassword', $PlainPassword)
        }
        catch {
            & net.exe user $User $PlainPassword | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw
            }
        }
    }
    $ADS_UF_PASSWD_CANT_CHANGE = 0x40
    $ADS_UF_DONT_EXPIRE_PASSWD = 0x10000

    $flags = 0
    try { $flags = [int]$userObj.UserFlags.Value } catch { $flags = 0 }
    $userObj.UserFlags = ($flags -bor $ADS_UF_PASSWD_CANT_CHANGE -bor $ADS_UF_DONT_EXPIRE_PASSWD)
    $userObj.SetInfo()

    $memberPath = "WinNT://$machine/$User,user"
    try {
        $groupObj.Add($memberPath)
        Write-Host "[OK] Benutzer zur Gruppe hinzugefügt (ADSI): $memberPath -> $Group"
    }
    catch {
        $message = $_.Exception.Message
        if ($message -match 'already' -or $message -match 'bereits') {
            Write-Host "[OK] Mitgliedschaft vorhanden (ADSI): $memberPath -> $Group"
        }
        else {
            throw
        }
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

function Set-PrivilegePolicyValue {
    param(
        [string[]]$Lines,
        [string]$Privilege,
        [string[]]$AddEntries,
        [string[]]$RemoveEntries
    )

    $normalizedAdd = @{}
    foreach ($entry in ($AddEntries | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $trimmed = $entry.Trim()
        $normalized = $trimmed.TrimStart('*').ToUpperInvariant()
        if (-not $normalizedAdd.ContainsKey($normalized)) {
            $normalizedAdd[$normalized] = if ($trimmed.StartsWith('*', [System.StringComparison]::Ordinal)) { $trimmed } else { "*$trimmed" }
        }
    }

    $normalizedRemove = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in ($RemoveEntries | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        [void]$normalizedRemove.Add($entry.Trim().TrimStart('*'))
    }

    $buffer = New-Object System.Collections.Generic.List[string]
    $buffer.AddRange($Lines)

    $sectionIndex = -1
    for ($i = 0; $i -lt $buffer.Count; $i++) {
        if ($buffer[$i] -match '^\[Privilege Rights\]\s*$') {
            $sectionIndex = $i
            break
        }
    }

    if ($sectionIndex -lt 0) {
        if ($buffer.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($buffer[$buffer.Count - 1])) {
            [void]$buffer.Add('')
        }
        [void]$buffer.Add('[Privilege Rights]')
        $sectionIndex = $buffer.Count - 1
    }

    $sectionEnd = $buffer.Count
    for ($i = $sectionIndex + 1; $i -lt $buffer.Count; $i++) {
        if ($buffer[$i] -match '^\[.+\]\s*$') {
            $sectionEnd = $i
            break
        }
    }

    $lineIndex = -1
    for ($i = $sectionIndex + 1; $i -lt $sectionEnd; $i++) {
        if ($buffer[$i] -match ('^{0}\s*=' -f [regex]::Escape($Privilege))) {
            $lineIndex = $i
            break
        }
    }

    $existingEntries = @()
    if ($lineIndex -ge 0) {
        $parts = $buffer[$lineIndex] -split '=', 2
        if ($parts.Count -gt 1) {
            $existingEntries = $parts[1].Split(',') | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        }
    }

    $resultMap = @{}
    foreach ($entry in $existingEntries) {
        $normalized = $entry.TrimStart('*')
        if ($normalizedRemove.Contains($normalized)) {
            continue
        }

        $key = $normalized.ToUpperInvariant()
        if (-not $resultMap.ContainsKey($key)) {
            $resultMap[$key] = $entry
        }
    }

    foreach ($pair in $normalizedAdd.GetEnumerator()) {
        if (-not $resultMap.ContainsKey($pair.Key)) {
            $resultMap[$pair.Key] = $pair.Value
        }
    }

    $finalEntries = $resultMap.Values
    $newLine = if ($finalEntries.Count -gt 0) {
        "{0} = {1}" -f $Privilege, ($finalEntries -join ',')
    }
    else {
        "{0} =" -f $Privilege
    }

    if ($lineIndex -ge 0) {
        $buffer[$lineIndex] = $newLine
    }
    else {
        [void]$buffer.Insert($sectionEnd, $newLine)
    }

    return $buffer.ToArray()
}

function Ensure-NetworkLogonRights {
    param(
        [string]$Group,
        [string]$User
    )

    $machine = $env:COMPUTERNAME
    $userPrincipal = "$machine\$User"
    $groupPrincipal = "$machine\$Group"

    $userSid = Get-PrincipalSidValue -AccountName $userPrincipal
    $groupSid = Get-PrincipalSidValue -AccountName $groupPrincipal

    if ([string]::IsNullOrWhiteSpace($userSid) -or [string]::IsNullOrWhiteSpace($groupSid)) {
        throw "SID-Auflösung fehlgeschlagen für '$userPrincipal' oder '$groupPrincipal'."
    }

    $allowEntries = @("*$userSid", "*$groupSid")
    $denyRemovalEntries = @(
        "*$userSid", "*$groupSid",
        $userPrincipal, $groupPrincipal,
        ".\$User", ".\$Group",
        $User, $Group
    )

    $tempRoot = Join-Path $env:TEMP ("hypertool-secpol-{0}" -f [Guid]::NewGuid().ToString('N'))
    $cfgPath = Join-Path $tempRoot 'security.inf'
    $dbPath = Join-Path $tempRoot 'security.sdb'

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        & secedit.exe /export /cfg $cfgPath /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $cfgPath)) {
            throw "secedit /export fehlgeschlagen (ExitCode=$LASTEXITCODE)."
        }

        $lines = Get-Content -LiteralPath $cfgPath -ErrorAction Stop
        $lines = Set-PrivilegePolicyValue -Lines $lines -Privilege 'SeNetworkLogonRight' -AddEntries $allowEntries -RemoveEntries @()
        $lines = Set-PrivilegePolicyValue -Lines $lines -Privilege 'SeDenyNetworkLogonRight' -AddEntries @() -RemoveEntries $denyRemovalEntries

        [System.IO.File]::WriteAllLines($cfgPath, $lines, [System.Text.Encoding]::Unicode)

        & secedit.exe /configure /db $dbPath /cfg $cfgPath /areas USER_RIGHTS /quiet | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "secedit /configure fehlgeschlagen (ExitCode=$LASTEXITCODE)."
        }

        Write-Host "[OK] Benutzerrechte gesetzt: SeNetworkLogonRight für '$userPrincipal' und '$groupPrincipal'; SeDenyNetworkLogonRight bereinigt."
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
}

function Test-State {
    param(
        [string]$Group,
        [string]$User
    )

    $machine = $env:COMPUTERNAME

    $groupExists = $false
    $userExists = $false
    $membership = $false

    try {
        $group = Get-LocalGroup -Name $Group -ErrorAction Stop
        $groupExists = $null -ne $group
    }
    catch {
        try {
            $groupAdsi = [ADSI]("WinNT://$machine/$Group,group")
            $null = $groupAdsi.Name
            $groupExists = $true
        }
        catch {
            $groupExists = $false
        }
    }

    try {
        $user = Get-LocalUser -Name $User -ErrorAction Stop
        $userExists = $null -ne $user
    }
    catch {
        try {
            $userAdsi = [ADSI]("WinNT://$machine/$User,user")
            $null = $userAdsi.Name
            $userExists = $true
        }
        catch {
            $userExists = $false
        }
    }

    if ($groupExists -and $userExists) {
        try {
            $memberPrincipal = "$machine\$User"
            $candidate = Get-LocalGroupMember -Group $Group -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.Name -ieq $memberPrincipal -or
                    $_.Name -ieq $User -or
                    $_.Name -ieq ".\$User"
                } |
                Select-Object -First 1
            $membership = $null -ne $candidate
        }
        catch {
            try {
                $groupAdsi = [ADSI]("WinNT://$machine/$Group,group")
                $members = @($groupAdsi.psbase.Invoke('Members'))
                foreach ($m in $members) {
                    $name = $m.GetType().InvokeMember('Name', 'GetProperty', $null, $m, $null)
                    if ([string]::Equals($name, $User, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $membership = $true
                        break
                    }
                }
            }
            catch {
                $membership = $false
            }
        }
    }

    return [PSCustomObject]@{
        GroupExists = $groupExists
        UserExists = $userExists
        UserInGroup = $membership
    }
}

function Invoke-Cleanup {
    param(
        [string]$Group,
        [string]$User
    )

    Write-Host "[INFO] Cleanup startet für User '$User' und Gruppe '$Group'..."

    try {
        $memberPrincipal = "$env:COMPUTERNAME\$User"
        Remove-LocalGroupMember -Group $Group -Member $memberPrincipal -ErrorAction SilentlyContinue | Out-Null
    }
    catch { }

    try {
        Remove-LocalUser -Name $User -ErrorAction SilentlyContinue
        Write-Host "[OK] Benutzer entfernt (falls vorhanden): $User"
    }
    catch {
        Write-Warning "Benutzer konnte nicht entfernt werden: $($_.Exception.Message)"
    }

    try {
        Remove-LocalGroup -Name $Group -ErrorAction SilentlyContinue
        Write-Host "[OK] Gruppe entfernt (falls vorhanden): $Group"
    }
    catch {
        Write-Warning "Gruppe konnte nicht entfernt werden: $($_.Exception.Message)"
    }
}

function Write-HostCredentialState {
    param(
        [string]$User,
        [string]$Group,
        [string]$PlainPassword
    )

    $statePath = Join-Path $env:LOCALAPPDATA 'HyperTool\credentials\host-sharedfolder-credential.json'
    $stateDir = Split-Path -Path $statePath -Parent
    if (-not (Test-Path -LiteralPath $stateDir)) {
        New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    }

    $entropy = [System.Text.Encoding]::UTF8.GetBytes('HyperTool.Host.SharedFolder.Credential.v1')
    $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($PlainPassword)
    $protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect(
        $plainBytes,
        $entropy,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $protectedPassword = 'dpapi:' + [Convert]::ToBase64String($protectedBytes)

    $state = [PSCustomObject]@{
        username = $User
        groupName = $Group
        protectedPassword = $protectedPassword
        updatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }

    $json = $state | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText($statePath, $json, [System.Text.UTF8Encoding]::new($false))

    try {
        $legacyUserPath = Join-Path $env:LOCALAPPDATA 'HyperTool\host-sharedfolder-credential.json'
        $legacyUserDir = Split-Path -Path $legacyUserPath -Parent
        if (-not (Test-Path -LiteralPath $legacyUserDir)) {
            New-Item -ItemType Directory -Path $legacyUserDir -Force | Out-Null
        }

        [System.IO.File]::WriteAllText($legacyUserPath, $json, [System.Text.UTF8Encoding]::new($false))

        $legacyMachinePath = Join-Path $env:ProgramData 'HyperTool\host-sharedfolder-credential.json'
        $legacyMachineDir = Split-Path -Path $legacyMachinePath -Parent
        if (-not (Test-Path -LiteralPath $legacyMachineDir)) {
            New-Item -ItemType Directory -Path $legacyMachineDir -Force | Out-Null
        }

        [System.IO.File]::WriteAllText($legacyMachinePath, $json, [System.Text.UTF8Encoding]::new($false))
        Write-Host "[OK] Credential-State (Legacy/Fallback) geschrieben: $legacyUserPath"
        Write-Host "[OK] Credential-State (Legacy/Fallback) geschrieben: $legacyMachinePath"
    }
    catch {
        Write-Warning "Credential-State Legacy/Fallback konnte nicht geschrieben werden: $($_.Exception.Message)"
    }

    try {
        $usersSid = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::BuiltinUsersSid, $null)
        $usersAccount = $usersSid.Translate([System.Security.Principal.NTAccount]).Value

        $dirAcl = Get-Acl -LiteralPath $stateDir
        $dirRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $usersAccount,
            [System.Security.AccessControl.FileSystemRights]::ReadAndExecute,
            [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit,
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $null = $dirAcl.SetAccessRule($dirRule)
        Set-Acl -LiteralPath $stateDir -AclObject $dirAcl

        $fileAcl = Get-Acl -LiteralPath $statePath
        $fileRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $usersAccount,
            [System.Security.AccessControl.FileSystemRights]::Read,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $null = $fileAcl.SetAccessRule($fileRule)
        Set-Acl -LiteralPath $statePath -AclObject $fileAcl
    }
    catch {
        Write-Warning "Credential-State ACL konnte nicht angepasst werden: $($_.Exception.Message)"
    }

    Write-Host "[OK] Credential-State geschrieben: $statePath"

    $rawState = [System.IO.File]::ReadAllText($statePath)
    $parsedState = $rawState | ConvertFrom-Json
    if ($null -eq $parsedState -or [string]::IsNullOrWhiteSpace([string]$parsedState.protectedPassword)) {
        throw "Credential-State ungültig: protectedPassword fehlt."
    }

    $storedPassword = [string]$parsedState.protectedPassword
    if (-not $storedPassword.StartsWith('dpapi:', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Credential-State ungültig: protectedPassword hat kein dpapi:-Präfix."
    }

    $payload = $storedPassword.Substring(6)
    $encrypted = [Convert]::FromBase64String($payload)
    $unprotected = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted,
        $entropy,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $roundtripPassword = [System.Text.Encoding]::UTF8.GetString($unprotected)
    if ([string]::IsNullOrWhiteSpace($roundtripPassword)) {
        throw "Credential-State ungültig: DPAPI-Roundtrip liefert leeres Passwort."
    }

    Write-Host "[OK] Credential-State validiert (lesen + DPAPI-Roundtrip)."
}

function Exit-WithDelay {
    param(
        [int]$Code
    )

    Write-Host "[INFO] Fenster schließt in 5 Sekunden..."
    Start-Sleep -Seconds 5
    exit $Code
}

if (-not (Test-IsAdministrator)) {
    Write-Error "Dieses Skript muss als Administrator ausgeführt werden."
    Exit-WithDelay -Code 1
}

if ($Cleanup) {
    Invoke-Cleanup -Group $GroupName -User $UserName
    $afterCleanup = Test-State -Group $GroupName -User $UserName
    Write-Host "[INFO] Nach Cleanup: Gruppe=$($afterCleanup.GroupExists), Benutzer=$($afterCleanup.UserExists), Mitgliedschaft=$($afterCleanup.UserInGroup)"
    Exit-WithDelay -Code 0
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    if ($UseRandomPassword) {
        $Password = New-StrongPassword 32
    }
    else {
        $Password = $TestingPassword
    }
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    Write-Error "Bitte Passwort angeben (Parameter -Password) oder -UseRandomPassword verwenden."
    Exit-WithDelay -Code 1
}

Write-Host "[INFO] Script-Version: 2026-03-03.7"
Write-Host "[INFO] Provisioning-Test startet..."
Write-Host "[INFO] Gruppe: $GroupName"
Write-Host "[INFO] Benutzer: $UserName"

$provisioningSucceeded = $false
$localAccountsError = $null

if (-not $UseAdsiOnly) {
    try {
        Ensure-WithLocalAccounts -Group $GroupName -User $UserName -PlainPassword $Password
        $provisioningSucceeded = $true
        Write-Host "[INFO] Provisionierung via LocalAccounts abgeschlossen."
    }
    catch {
        $localAccountsError = $_.Exception
        Write-Warning "LocalAccounts fehlgeschlagen: $($localAccountsError.Message)"
    }
}

if (-not $provisioningSucceeded) {
    try {
        Ensure-WithAdsi -Group $GroupName -User $UserName -PlainPassword $Password
        $provisioningSucceeded = $true
        Write-Host "[INFO] Provisionierung via ADSI abgeschlossen."
    }
    catch {
        Write-Error "ADSI-Fallback ebenfalls fehlgeschlagen: $($_.Exception.Message)"
        if ($null -ne $localAccountsError) {
            Write-Host "[INFO] Vorheriger LocalAccounts-Fehler: $($localAccountsError.Message)"
        }
        Exit-WithDelay -Code 2
    }
}

Ensure-NetworkLogonRights -Group $GroupName -User $UserName

$state = Test-State -Group $GroupName -User $UserName
Write-Host "[INFO] Prüfung: Gruppe=$($state.GroupExists), Benutzer=$($state.UserExists), Mitgliedschaft=$($state.UserInGroup)"

if ($ShowPassword) {
    Write-Host "[INFO] Passwort: $Password"
}

if ($state.GroupExists -and $state.UserExists -and $state.UserInGroup) {
    if (-not $SkipStateWrite) {
        Write-HostCredentialState -User $UserName -Group $GroupName -PlainPassword $Password
    }

    Write-Host "[OK] Test erfolgreich: Gruppe + Benutzer + Mitgliedschaft sind vorhanden."
    Exit-WithDelay -Code 0
}

Write-Error "Test fehlgeschlagen: Endzustand unvollständig."
Exit-WithDelay -Code 3
