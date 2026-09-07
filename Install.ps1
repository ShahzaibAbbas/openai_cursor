#requires -version 5.1
<#
.SYNOPSIS
Installs all 17 Soft Black Arrow v3 cursor roles for the current Windows user.
.DESCRIPTION
Run normally for the subtle animated pointer, or with -Static for a still pointer.
Wait and Working in Background remain animated in both modes. The first backup is
retained across reinstalls. Restore.ps1 returns the exact values from that backup.
No administrator rights are needed. Mouse speed, trails and other settings are untouched.
#>
[CmdletBinding()]
param([switch]$Static, [switch]$Restore)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Static -and $Restore) { throw 'Choose either installation with -Static, or -Restore.' }

$schemeName = 'Soft Black Arrow v3'
$registryPath = 'Control Panel\Cursors'
$installRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CustomCursors\SoftBlackArrowV3'
$backupPath = Join-Path $installRoot 'previous-scheme.json'
$roleNames = @('Arrow', 'Help', 'AppStarting', 'Wait', 'Crosshair', 'IBeam', 'NWPen', 'No', 'SizeNS', 'SizeWE', 'SizeNWSE', 'SizeNESW', 'SizeAll', 'UpArrow', 'Hand', 'Pin', 'Person')
$valueNames = @($roleNames) + @('', 'Scheme Source')
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value

if (-not ('SoftBlackArrowV3.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace SoftBlackArrowV3 {
    public static class Native {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadImage(IntPtr instance, string fileName, uint imageType, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyCursor(IntPtr cursor);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);
    }
}
'@
}

function Read-RegistryValue {
    param([Microsoft.Win32.RegistryKey]$Key, [string]$Name)
    $exists = ($null -ne $Key) -and (@($Key.GetValueNames()) -contains $Name)
    if (-not $exists) {
        return [pscustomobject]@{ Name = $Name; Exists = $false; Kind = $null; Value = $null }
    }
    $kind = $Key.GetValueKind($Name).ToString()
    $raw = $Key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    # Base64 and decimal strings preserve binary data and 64-bit integer precision in JSON.
    if ($kind -eq 'Binary' -or $kind -eq 'None') { $raw = [Convert]::ToBase64String([byte[]]$raw) }
    elseif ($kind -eq 'DWord' -or $kind -eq 'QWord') { $raw = $raw.ToString([Globalization.CultureInfo]::InvariantCulture) }
    return [pscustomobject]@{ Name = $Name; Exists = $true; Kind = $kind; Value = $raw }
}

function Get-CursorSnapshot {
    $cursorKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath, $false)
    $schemesKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(($registryPath + '\Schemes'), $false)
    try {
        $values = foreach ($name in $valueNames) { Read-RegistryValue -Key $cursorKey -Name $name }
        return [pscustomobject]@{
            Format = 'SoftBlackArrowV3.RegistryBackup.v1'
            UserSid = $sid
            SavedUtc = [DateTime]::UtcNow.ToString('o')
            Values = @($values)
            NamedScheme = Read-RegistryValue -Key $schemesKey -Name $schemeName
        }
    }
    finally {
        if ($null -ne $cursorKey) { $cursorKey.Dispose() }
        if ($null -ne $schemesKey) { $schemesKey.Dispose() }
    }
}

function Assert-Snapshot {
    param($Snapshot)
    if ($Snapshot.Format -ne 'SoftBlackArrowV3.RegistryBackup.v1' -or $Snapshot.UserSid -ne $sid) {
        throw 'The cursor backup is not valid for this Windows user.'
    }
    if (@($Snapshot.Values).Count -ne $valueNames.Count -or $Snapshot.NamedScheme.Name -ne $schemeName) {
        throw 'The cursor backup has an unexpected set of registry values.'
    }
    foreach ($name in $valueNames) {
        if (@($Snapshot.Values | Where-Object { $_.Name -ceq $name }).Count -ne 1) {
            throw ('Missing or duplicated cursor backup value: ' + $name)
        }
    }
    foreach ($record in (@($Snapshot.Values) + @($Snapshot.NamedScheme))) {
        if ($record.Exists -isnot [bool]) { throw 'The cursor backup contains an invalid existence flag.' }
        if ($record.Exists -and $record.Kind -notin @('String', 'ExpandString', 'Binary', 'DWord', 'MultiString', 'QWord', 'None')) {
            throw 'The cursor backup contains an unsupported registry type.'
        }
    }
}

function Write-RegistryRecord {
    param([Microsoft.Win32.RegistryKey]$Key, $Record)
    if (-not $Record.Exists) { $Key.DeleteValue([string]$Record.Name, $false); return }
    $kind = [Microsoft.Win32.RegistryValueKind][Enum]::Parse([Microsoft.Win32.RegistryValueKind], [string]$Record.Kind)
    $raw = $Record.Value
    switch ([string]$Record.Kind) {
        'String' { $raw = [string]$raw }
        'ExpandString' { $raw = [string]$raw }
        'MultiString' { $raw = [string[]]@($raw) }
        'Binary' { $raw = [Convert]::FromBase64String([string]$raw) }
        'None' { $raw = [Convert]::FromBase64String([string]$raw) }
        'DWord' { $raw = [int]::Parse([string]$raw, [Globalization.CultureInfo]::InvariantCulture) }
        'QWord' { $raw = [long]::Parse([string]$raw, [Globalization.CultureInfo]::InvariantCulture) }
    }
    $Key.SetValue([string]$Record.Name, $raw, $kind)
}

function Set-CursorSnapshot {
    param($Snapshot)
    Assert-Snapshot $Snapshot
    $cursorKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
    $schemesKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(($registryPath + '\Schemes'))
    try {
        foreach ($record in $Snapshot.Values) { Write-RegistryRecord -Key $cursorKey -Record $record }
        Write-RegistryRecord -Key $schemesKey -Record $Snapshot.NamedScheme
        $cursorKey.Flush()
        $schemesKey.Flush()
    }
    finally { $cursorKey.Dispose(); $schemesKey.Dispose() }
}

function Reload-Cursors {
    if (-not [SoftBlackArrowV3.Native]::SystemParametersInfo(0x57, 0, [IntPtr]::Zero, 2)) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw ('Windows could not reload the cursor scheme. Win32 error: ' + $errorCode)
    }
}

function Assert-SnapshotApplied {
    param($Expected)
    $actual = Get-CursorSnapshot
    foreach ($name in $valueNames) {
        $wanted = $Expected.Values | Where-Object { $_.Name -ceq $name }
        $found = $actual.Values | Where-Object { $_.Name -ceq $name }
        if (($wanted | ConvertTo-Json -Depth 6 -Compress) -cne ($found | ConvertTo-Json -Depth 6 -Compress)) {
            throw ('Registry verification failed for cursor value: ' + $name)
        }
    }
    if (($Expected.NamedScheme | ConvertTo-Json -Depth 6 -Compress) -cne ($actual.NamedScheme | ConvertTo-Json -Depth 6 -Compress)) {
        throw 'Registry verification failed for the saved scheme entry.'
    }
}

function Test-CursorFile {
    param([string]$Path)
    if (-not [IO.File]::Exists($Path)) { throw ('Required cursor file is missing: ' + $Path) }
    $cursorHandle = [SoftBlackArrowV3.Native]::LoadImage([IntPtr]::Zero, $Path, 2, 0, 0, 0x10)
    if ($cursorHandle -eq [IntPtr]::Zero) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw ('Windows rejected cursor file: ' + $Path + ' (Win32 error ' + $errorCode + ')')
    }
    [void][SoftBlackArrowV3.Native]::DestroyCursor($cursorHandle)
}

if ($Restore) {
    if (-not [IO.File]::Exists($backupPath)) { throw ('No previous cursor backup was found: ' + $backupPath) }
    $targetSnapshot = Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json
    Assert-Snapshot $targetSnapshot
}
else {
    $fileNames = [ordered]@{}
    foreach ($role in $roleNames) { $fileNames[$role] = $role + '.cur' }
    $fileNames['Wait'] = 'Wait.ani'
    $fileNames['AppStarting'] = 'AppStarting.ani'
    if (-not $Static) { $fileNames['Arrow'] = 'Arrow.ani' }
    $requiredFiles = @(@($fileNames.Values) + @('Arrow.cur', 'Arrow.ani') | Select-Object -Unique)
    foreach ($file in $requiredFiles) { Test-CursorFile (Join-Path $PSScriptRoot $file) }

    [void][IO.Directory]::CreateDirectory($installRoot)
    if ([IO.File]::Exists($backupPath)) {
        $savedBackup = Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json
        Assert-Snapshot $savedBackup
    }
    else {
        $firstSnapshot = Get-CursorSnapshot
        $jsonBytes = [Text.UTF8Encoding]::new($false).GetBytes(($firstSnapshot | ConvertTo-Json -Depth 8))
        # CreateNew prevents an existing backup from being overwritten.
        $stream = [IO.File]::Open($backupPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($jsonBytes, 0, $jsonBytes.Length); $stream.Flush() }
        finally { $stream.Dispose() }
    }

    # Each install gets fresh files, so a failed reinstall cannot damage active assets.
    $generation = 'assets-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $assetRoot = Join-Path $installRoot $generation
    [void][IO.Directory]::CreateDirectory($assetRoot)
    foreach ($file in $requiredFiles) {
        $sourcePath = Join-Path $PSScriptRoot $file
        $destinationPath = Join-Path $assetRoot $file
        [IO.File]::Copy($sourcePath, $destinationPath, $false)
        if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash) {
            throw ('Copied cursor verification failed: ' + $file)
        }
        Test-CursorFile $destinationPath
    }
    foreach ($scriptName in @('Install.ps1', 'Restore.ps1')) {
        $sourceScript = Join-Path $PSScriptRoot $scriptName
        $destinationScript = Join-Path $installRoot $scriptName
        if ([IO.Path]::GetFullPath($sourceScript) -ine [IO.Path]::GetFullPath($destinationScript)) {
            [IO.File]::Copy($sourceScript, $destinationScript, $true)
        }
    }

    $targetSnapshot = Get-CursorSnapshot
    $paths = foreach ($role in $roleNames) { Join-Path $assetRoot $fileNames[$role] }
    $newValues = for ($index = 0; $index -lt $roleNames.Count; $index++) {
        [pscustomobject]@{ Name = $roleNames[$index]; Exists = $true; Kind = 'ExpandString'; Value = $paths[$index] }
    }
    $newValues += [pscustomobject]@{ Name = ''; Exists = $true; Kind = 'String'; Value = $schemeName }
    $newValues += [pscustomobject]@{ Name = 'Scheme Source'; Exists = $true; Kind = 'DWord'; Value = '1' }
    $targetSnapshot.Values = @($newValues)
    $targetSnapshot.NamedScheme = [pscustomobject]@{ Name = $schemeName; Exists = $true; Kind = 'String'; Value = ($paths -join ',') }
}

$beforeChange = Get-CursorSnapshot
try {
    Set-CursorSnapshot $targetSnapshot
    Assert-SnapshotApplied $targetSnapshot
    Reload-Cursors
}
catch {
    $originalFailure = $_.Exception.Message
    try {
        Set-CursorSnapshot $beforeChange
        Assert-SnapshotApplied $beforeChange
        Reload-Cursors
    }
    catch {
        throw ('Cursor change failed: ' + $originalFailure + '. Rollback also reported: ' + $_.Exception.Message + '. Backup retained at ' + $backupPath)
    }
    throw ('Cursor change failed and the previous state was restored: ' + $originalFailure)
}

if ($Restore) {
    Write-Host 'Previous cursor scheme restored. Backup and cursor assets were retained.'
}
else {
    $pointerStyle = if ($Static) { 'static pointer' } else { 'animated pointer' }
    Write-Host ($schemeName + ' installed and active for all 17 roles (' + $pointerStyle + ').')
    Write-Host ('Assets: ' + $assetRoot)
    Write-Host ('Restore: ' + (Join-Path $installRoot 'Restore.ps1'))
}
