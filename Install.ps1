$ErrorActionPreference = 'Stop'

$schemeName = 'Soft Black Arrow v2'
$installFolder = Join-Path $env:LOCALAPPDATA 'CustomCursors\SoftBlackArrowV2'
$backupPath = Join-Path $installFolder 'previous-scheme.json'
$cursorKeyPath = 'Control Panel\Cursors'

$roles = [ordered]@{
    Arrow       = 'Arrow.cur'
    Help        = 'Help.cur'
    AppStarting = 'AppStarting.ani'
    Wait        = 'Wait.ani'
    Crosshair   = 'Crosshair.cur'
    IBeam       = 'IBeam.cur'
    NWPen       = 'NWPen.cur'
    No          = 'No.cur'
    SizeNS      = 'SizeNS.cur'
    SizeWE      = 'SizeWE.cur'
    SizeNWSE    = 'SizeNWSE.cur'
    SizeNESW    = 'SizeNESW.cur'
    SizeAll     = 'SizeAll.cur'
    UpArrow     = 'UpArrow.cur'
    Hand        = 'Hand.cur'
    Pin         = 'Pin.cur'
    Person      = 'Person.cur'
}

New-Item -ItemType Directory -Path $installFolder -Force | Out-Null

$cursorKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
    $cursorKeyPath,
    $true
)
if (-not $cursorKey) {
    throw 'Could not open the current-user cursor registry key.'
}

if (-not (Test-Path -LiteralPath $backupPath)) {
    $backup = [ordered]@{}
    foreach ($role in $roles.Keys) {
        $backup[$role] = [string]$cursorKey.GetValue(
            $role,
            '',
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames
        )
    }

    # The first one-cursor package may already be active. Its installer saved
    # the original Arrow value, so prefer that for a full genuine restore.
    $v1ArrowBackup = Join-Path $env:LOCALAPPDATA 'CustomCursors\SoftBlackArrow\previous-arrow.txt'
    if (Test-Path -LiteralPath $v1ArrowBackup) {
        $backup['Arrow'] = (Get-Content -LiteralPath $v1ArrowBackup -Raw).TrimEnd("`r", "`n")
    }

    $backup['SchemeName'] = [string]$cursorKey.GetValue('')
    $backup | ConvertTo-Json | Set-Content -LiteralPath $backupPath -Encoding UTF8
}

foreach ($role in $roles.Keys) {
    $file = $roles[$role]
    $source = Join-Path $PSScriptRoot $file
    $destination = Join-Path $installFolder $file
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $cursorKey.SetValue($role, $destination, [Microsoft.Win32.RegistryValueKind]::String)
}

$cursorKey.SetValue('', $schemeName, [Microsoft.Win32.RegistryValueKind]::String)
$cursorKey.Close()

$schemesKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
    "$cursorKeyPath\Schemes"
)
$schemePaths = @(
    foreach ($file in $roles.Values) {
        Join-Path $installFolder $file
    }
) -join ','
$schemesKey.SetValue($schemeName, $schemePaths, [Microsoft.Win32.RegistryValueKind]::String)
$schemesKey.Close()

if (-not ('FullCursorRefresh' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FullCursorRefresh {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint flags);
}
'@
}
[FullCursorRefresh]::SystemParametersInfo(0x0057, 0, [IntPtr]::Zero, 0x0003) | Out-Null

Write-Host 'Soft Black Arrow v2 installed for all 17 Windows cursor roles.'
