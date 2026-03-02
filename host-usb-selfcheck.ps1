param(
    [string]$HostAddress = "127.0.0.1",
    [string]$BusId = "",
    [string]$ServiceGuid = "6c4eb1be-40e8-4c8b-a4d6-5b0f67d7e40f"
)

$ErrorActionPreference = 'Stop'

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

function Write-Result($ok, $name, $details) {
    $state = if ($ok) { "OK" } else { "FAIL" }
    $color = if ($ok) { "Green" } else { "Red" }
    Write-Host ("[{0}] {1} - {2}" -f $state, $name, $details) -ForegroundColor $color
    [PSCustomObject]@{
        Name = $name
        Ok = $ok
        Details = $details
    }
}

function Test-IsAdmin {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

function Get-UsbipdExecutablePath {
    $candidates = @(
        "usbipd.exe",
        "$env:ProgramFiles\usbipd-win\usbipd.exe",
        "$env:ProgramFiles(x86)\usbipd-win\usbipd.exe"
    )

    foreach ($candidate in $candidates) {
        try {
            if ($candidate -eq "usbipd.exe") {
                $cmd = Get-Command usbipd.exe -ErrorAction SilentlyContinue
                if ($null -ne $cmd -and -not [string]::IsNullOrWhiteSpace($cmd.Source)) {
                    return $cmd.Source
                }
            }
            else {
                if (Test-Path -LiteralPath $candidate) {
                    return $candidate
                }
            }
        }
        catch {
        }
    }

    return $null
}

$results = New-Object System.Collections.Generic.List[object]

Write-Host "HyperTool USB Host Selfcheck" -ForegroundColor Yellow
Write-Host "HostAddress: $HostAddress"
if (-not [string]::IsNullOrWhiteSpace($BusId)) {
    Write-Host "BusId: $BusId"
}
Write-Host "ServiceGuid: $ServiceGuid"

Write-Section "Basis"
$isAdmin = Test-IsAdmin
$adminDetails = if ($isAdmin) { "PowerShell läuft erhöht." } else { "Nicht erhöht. Einige Checks können unvollständig sein." }
$results.Add((Write-Result $isAdmin "Admin-Rechte" $adminDetails))

Write-Section "usbipd Dienst"
try {
    $svc = Get-Service usbipd -ErrorAction Stop
    $svcOk = $svc.Status -eq 'Running'
    $results.Add((Write-Result $svcOk "Dienst usbipd" ("Status: {0}" -f $svc.Status)))
}
catch {
    $results.Add((Write-Result $false "Dienst usbipd" "Dienst nicht gefunden."))
}

Write-Section "Hyper-V Socket Registry Service"
try {
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices\$ServiceGuid"
    $key = Get-ItemProperty -Path $regPath -ErrorAction Stop
    $elementName = [string]$key.ElementName
    $ok = -not [string]::IsNullOrWhiteSpace($elementName)
    $details = if ($ok) { "ElementName: $elementName" } else { "Key vorhanden, aber ElementName leer." }
    $results.Add((Write-Result $ok "Registry-Service-GUID" $details))
}
catch {
    $results.Add((Write-Result $false "Registry-Service-GUID" "Key/ElementName nicht gefunden."))
}

Write-Section "Firewall"
try {
    $rules = @(Get-NetFirewallRule -ErrorAction Stop | Where-Object { $_.DisplayName -match 'usbipd' -or $_.Name -match 'usbipd' })
    if ($null -eq $rules -or $rules.Count -eq 0) {
        $results.Add((Write-Result $false "Firewall usbipd-Regeln" "Keine usbipd-Regel gefunden."))
    }
    else {
        $enabledRules = @($rules | Where-Object { $_.Enabled -eq 'True' })
        $ok = $enabledRules.Count -gt 0
        $details = "Gefunden: $($rules.Count), Aktiv: $($enabledRules.Count)"
        $results.Add((Write-Result $ok "Firewall usbipd-Regeln" $details))
    }
}
catch {
    $results.Add((Write-Result $false "Firewall usbipd-Regeln" "Check fehlgeschlagen (NetSecurity Modul/Permissions)."))
}

Write-Section "Port 3240"
try {
    $listener = Get-NetTCPConnection -LocalPort 3240 -State Listen -ErrorAction SilentlyContinue
    $listenOk = $null -ne $listener
    $listenDetails = if ($listenOk) {
        $pids = ($listener | Select-Object -ExpandProperty OwningProcess -Unique) -join ','
        "LISTEN aktiv (PID: $pids)"
    }
    else {
        "Kein Listener auf Port 3240 gefunden."
    }
    $results.Add((Write-Result $listenOk "Lokaler Listener 3240" $listenDetails))
}
catch {
    $results.Add((Write-Result $false "Lokaler Listener 3240" "Listener-Check fehlgeschlagen."))
}

try {
    $tnc = Test-NetConnection -ComputerName $HostAddress -Port 3240 -WarningAction SilentlyContinue
    $remoteOk = [bool]$tnc.TcpTestSucceeded
    $remoteDetails = if ($remoteOk) {
        "TcpTestSucceeded=True"
    }
    else {
        "TcpTestSucceeded=False"
    }
    $results.Add((Write-Result $remoteOk "Erreichbarkeit ${HostAddress}:3240" $remoteDetails))
}
catch {
    $results.Add((Write-Result $false "Erreichbarkeit $HostAddress:3240" "Test-NetConnection fehlgeschlagen."))
}

Write-Section "usbipd Geräte"
try {
    $usbipdExe = Get-UsbipdExecutablePath
    if ([string]::IsNullOrWhiteSpace($usbipdExe)) {
        throw "usbipd.exe wurde nicht gefunden (PATH/Standardpfade)."
    }

    $nativePref = $null
    if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
        $nativePref = $Global:PSNativeCommandUseErrorActionPreference
        $Global:PSNativeCommandUseErrorActionPreference = $false
    }

    $usbipdRaw = $null
    $usbipdText = ""
    $tsUsbWarningDetected = $false

    try {
        $usbipdRaw = & $usbipdExe list 2>&1
        $usbipdText = ($usbipdRaw | Out-String)
        $tsUsbWarningDetected = $usbipdText -match 'TsUsbFlt' -or $usbipdText -match 'bind --force'

        $listOk = -not [string]::IsNullOrWhiteSpace($usbipdText)
        $listDetails = if ($listOk) {
            if ($tsUsbWarningDetected) {
                "Ausgabe empfangen (Hinweis: TsUsbFlt erkannt, ggf. bind --force erforderlich)."
            }
            else {
                "Ausgabe empfangen."
            }
        }
        else {
            "Keine Ausgabe."
        }
        $results.Add((Write-Result $listOk "usbipd list" $listDetails))
    }
    finally {
        if ($null -ne $nativePref) {
            $Global:PSNativeCommandUseErrorActionPreference = $nativePref
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($BusId)) {
        $line = ($usbipdText -split "`r?`n") | Where-Object { $_ -match "^\s*$([regex]::Escape($BusId))\s" } | Select-Object -First 1
        if ($null -eq $line) {
            $results.Add((Write-Result $false "BUSID $BusId gefunden" "BUSID nicht in 'usbipd list' gefunden."))
        }
        else {
            $isShared = $line -match '(?i)Shared|Bind|Attached'
            $details = "Zeile: $($line.Trim())"
            $results.Add((Write-Result $isShared "BUSID $BusId freigegeben" $details))

            if ($tsUsbWarningDetected) {
                $results.Add((Write-Result $false "TsUsbFlt Kompatibilität" ("Inkompatibler USB-Filter erkannt. Für BUSID ${BusId}: usbipd bind --busid $BusId --force")))
            }
        }
    }
    elseif ($tsUsbWarningDetected) {
        $results.Add((Write-Result $false "TsUsbFlt Kompatibilität" "Inkompatibler USB-Filter erkannt. Für jedes Zielgerät ist usbipd bind --force erforderlich."))
    }
}
catch {
    $results.Add((Write-Result $false "usbipd list" ("Aufruf fehlgeschlagen: {0}" -f $_.Exception.Message)))
}

Write-Section "Zusammenfassung"
$failed = @($results | Where-Object { -not $_.Ok })
$passed = @($results | Where-Object { $_.Ok })
Write-Host "Checks OK: $($passed.Count)" -ForegroundColor Green
Write-Host "Checks FAIL: $($failed.Count)" -ForegroundColor Red

if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "Fehlgeschlagene Checks:" -ForegroundColor Yellow
    $failed | ForEach-Object {
        Write-Host ("- {0}: {1}" -f $_.Name, $_.Details)
    }
}

Write-Host ""
if ($failed.Count -eq 0) {
    Write-Host "Ergebnis: Host-Basis sieht gut aus." -ForegroundColor Green
}
else {
    Write-Host "Ergebnis: Mindestens ein kritischer Punkt blockiert den USB-Flow." -ForegroundColor Red
}
