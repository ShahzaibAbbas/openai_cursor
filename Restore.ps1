$ErrorActionPreference = 'Stop'

$schemeName = 'Soft Black Arrow v2'
$installFolder = Join-Path $env:LOCALAPPDATA 'CustomCursors\SoftBlackArrowV2'
$backupPath = Join-Path $installFolder 'previous-scheme.json'
$cursorKeyPath = 'Control Panel\Cursors'

if (-not (Test-Path -LiteralPath $backupPath)) {
    throw 'No previous cursor-scheme backup was found.'
}

$backup = Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json
$cursorKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($cursorKeyPath, $true)

$roles = @(
    'Arrow', 'Help', 'AppStarting', 'Wait', 'Crosshair', 'IBeam', 'NWPen',
    'No', 'SizeNS', 'SizeWE', 'SizeNWSE', 'SizeNESW', 'SizeAll', 'UpArrow',
    'Hand', 'Pin', 'Person'
)
foreach ($role in $roles) {
    $cursorKey.SetValue($role, [string]$backup.$role, [Microsoft.Win32.RegistryValueKind]::String)
}
$cursorKey.SetValue('', [string]$backup.SchemeName, [Microsoft.Win32.RegistryValueKind]::String)
$cursorKey.Close()

$schemesKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
    "$cursorKeyPath\Schemes",
    $true
)
if ($schemesKey) {
    $schemesKey.DeleteValue($schemeName, $false)
    $schemesKey.Close()
}

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

Write-Host 'Your previous complete cursor scheme was restored.'
