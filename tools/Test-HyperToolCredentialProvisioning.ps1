param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ForwardArgs
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$newScriptPath = Join-Path $scriptDir 'HyperToolCredentialProvisioning.ps1'

if (-not (Test-Path -LiteralPath $newScriptPath)) {
    Write-Error "HyperToolCredentialProvisioning.ps1 wurde nicht gefunden: $newScriptPath"
    exit 1
}

& $newScriptPath @ForwardArgs
exit $LASTEXITCODE
