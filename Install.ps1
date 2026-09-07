#requires -version 5.1
<#
.SYNOPSIS
Installs the persistent Soft Black Arrow scheme for the current Windows user.
.DESCRIPTION
Copies all cursor files and the small event-driven helper into a permanent folder.
The first registry backup is preserved across reinstalls. The -Restore switch is
used by Restore.ps1. Current cursor size, base size, and color are never changed.
#>
[CmdletBinding()]
param([switch]$Restore)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$schemeName = 'Soft Black Arrow Persistent'
$runName = 'SoftBlackArrowCursorKeeper'
$runPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$installRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CustomCursors\SoftBlackArrowPermanent'))
$helperPath = Join-Path $installRoot 'CursorKeeper.exe'
$backupPath = Join-Path $installRoot 'original-state.clixml'
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$roleNames = @('Arrow', 'Help', 'AppStarting', 'Wait', 'Crosshair', 'IBeam', 'NWPen', 'No', 'SizeNS', 'SizeWE', 'SizeNWSE', 'SizeNESW', 'SizeAll', 'UpArrow', 'Hand', 'Pin', 'Person')
$targets = @(
    foreach ($name in ($roleNames + @('', 'Scheme Source'))) {
        [pscustomobject]@{ Path = 'Control Panel\Cursors'; Name = $name }
    }
    [pscustomobject]@{ Path = 'Control Panel\Cursors\Schemes'; Name = $schemeName }
    [pscustomobject]@{ Path = 'Software\Microsoft\Windows\CurrentVersion\Themes'; Name = 'ThemeChangesMousePointers' }
    [pscustomobject]@{ Path = 'Software\Microsoft\Accessibility'; Name = 'CursorType' }
    [pscustomobject]@{ Path = $runPath; Name = $runName }
)

function Get-RegistrySnapshot {
    $records = foreach ($target in $targets) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($target.Path, $false)
        try {
            $exists = ($null -ne $key) -and (@($key.GetValueNames()) -contains $target.Name)
            $kind = $null
            $raw = $null
            if ($exists) {
                $kind = $key.GetValueKind($target.Name).ToString()
                $raw = $key.GetValue($target.Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                if ($kind -in @('Binary', 'None')) { $raw = [Convert]::ToBase64String([byte[]]$raw) }
                elseif ($kind -in @('DWord', 'QWord')) { $raw = $raw.ToString([Globalization.CultureInfo]::InvariantCulture) }
            }
            [pscustomobject]@{ Path = $target.Path; Name = $target.Name; Exists = $exists; Kind = $kind; Value = $raw }
        }
        finally { if ($null -ne $key) { $key.Dispose() } }
    }
    [pscustomobject]@{
        Format = 'SoftBlackArrowPersistent.RegistryBackup.v1'
        UserSid = $sid
        SavedUtc = [DateTime]::UtcNow.ToString('o')
        Records = @($records)
    }
}

function Assert-Snapshot {
    param($Snapshot)
    if ($Snapshot.Format -ne 'SoftBlackArrowPersistent.RegistryBackup.v1' -or $Snapshot.UserSid -ne $sid) {
        throw 'This cursor backup is not valid for the current Windows user.'
    }
    if (@($Snapshot.Records).Count -ne $targets.Count) { throw 'The cursor backup contains an unexpected number of values.' }
    foreach ($target in $targets) {
        $matches = @($Snapshot.Records | Where-Object { $_.Path -ceq $target.Path -and $_.Name -ceq $target.Name })
        if ($matches.Count -ne 1) { throw ('Missing or duplicated backup value: ' + $target.Path + '\' + $target.Name) }
        $record = $matches[0]
        if ($record.Exists -isnot [bool]) { throw 'Invalid backup existence flag.' }
        if ($record.Exists -and $record.Kind -notin @('String', 'ExpandString', 'MultiString', 'Binary', 'None', 'DWord', 'QWord')) {
            throw 'Unsupported registry type in the cursor backup.'
        }
        if ($record.Exists) {
            switch ($record.Kind) {
                'Binary' { [void][Convert]::FromBase64String([string]$record.Value) }
                'None' { [void][Convert]::FromBase64String([string]$record.Value) }
                'DWord' { [void][int]::Parse([string]$record.Value, [Globalization.CultureInfo]::InvariantCulture) }
                'QWord' { [void][long]::Parse([string]$record.Value, [Globalization.CultureInfo]::InvariantCulture) }
            }
        }
    }
}

function Set-RegistrySnapshot {
    param($Snapshot)
    Assert-Snapshot $Snapshot
    foreach ($record in $Snapshot.Records) {
        if (-not $record.Exists) {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey([string]$record.Path, $true)
            try { if ($null -ne $key) { $key.DeleteValue([string]$record.Name, $false) } }
            finally { if ($null -ne $key) { $key.Dispose() } }
            continue
        }
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey([string]$record.Path)
        try {
            $kind = [Microsoft.Win32.RegistryValueKind][Enum]::Parse([Microsoft.Win32.RegistryValueKind], [string]$record.Kind)
            $raw = $record.Value
            switch ([string]$record.Kind) {
                'String' { $raw = [string]$raw }
                'ExpandString' { $raw = [string]$raw }
                'MultiString' { $raw = [string[]]@($raw) }
                'Binary' { $raw = [Convert]::FromBase64String([string]$raw) }
                'None' { $raw = [Convert]::FromBase64String([string]$raw) }
                'DWord' { $raw = [int]::Parse([string]$raw, [Globalization.CultureInfo]::InvariantCulture) }
                'QWord' { $raw = [long]::Parse([string]$raw, [Globalization.CultureInfo]::InvariantCulture) }
            }
            $key.SetValue([string]$record.Name, $raw, $kind)
            $key.Flush()
        }
        finally { $key.Dispose() }
    }
}

function Assert-SnapshotApplied {
    param($Expected)
    $actual = Get-RegistrySnapshot
    for ($index = 0; $index -lt $targets.Count; $index++) {
        $target = $targets[$index]
        $wanted = $Expected.Records | Where-Object { $_.Path -ceq $target.Path -and $_.Name -ceq $target.Name }
        $found = $actual.Records[$index]
        if (($wanted | ConvertTo-Json -Depth 6 -Compress) -cne ($found | ConvertTo-Json -Depth 6 -Compress)) {
            throw ('Registry verification failed: ' + $target.Path + '\' + $target.Name)
        }
    }
}

function Reload-Cursors {
    if (-not ('SoftBlackArrowPersistent.ControlNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace SoftBlackArrowPersistent {
    public static class ControlNative {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);
    }
}
'@
    }
    if (-not [SoftBlackArrowPersistent.ControlNative]::SystemParametersInfo(0x57, 0, [IntPtr]::Zero, 2)) {
        throw ('Windows could not reload the cursor scheme; error ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }
}

function Get-InstalledHelperProcesses {
    foreach ($process in [Diagnostics.Process]::GetProcessesByName('CursorKeeper')) {
        try {
            if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ieq $helperPath) { $process }
            else { $process.Dispose() }
        }
        catch { $process.Dispose() }
    }
}

function Stop-InstalledHelper {
    if ([IO.File]::Exists($helperPath)) {
        $signal = Start-Process -FilePath $helperPath -ArgumentList '--stop' -WindowStyle Hidden -PassThru
        try {
            if (-not $signal.WaitForExit(10000)) { throw 'The cursor helper did not finish its stop request.' }
            if ($signal.ExitCode -ne 0) { throw ('The cursor helper stop request failed: ' + $signal.ExitCode) }
        }
        finally { $signal.Dispose() }
    }
    foreach ($process in @(Get-InstalledHelperProcesses)) {
        try { if (-not $process.WaitForExit(10000)) { throw 'The installed cursor helper is still running; files were not replaced.' } }
        finally { $process.Dispose() }
    }
}

function Start-InstalledHelper {
    $process = Start-Process -FilePath $helperPath -WindowStyle Hidden -PassThru
    try {
        if ($process.WaitForExit(1500)) { throw ('The cursor protection helper exited early: ' + $process.ExitCode) }
    }
    finally { $process.Dispose() }
}

$operationMutex = [Threading.Mutex]::new($false, ('Local\SoftBlackArrowPersistent.Install.' + $sid))
$mutexTaken = $false
try {
    try { $mutexTaken = $operationMutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $mutexTaken = $true }
    if (-not $mutexTaken) { throw 'Another Soft Black Arrow install or restore is already running.' }

    if ($Restore) {
        if (-not [IO.File]::Exists($backupPath)) { throw ('No original cursor backup exists at ' + $backupPath) }
        $savedSnapshot = Import-Clixml -LiteralPath $backupPath
        Assert-Snapshot $savedSnapshot
    }
    else {
        $cursorFiles = @(
            foreach ($role in $roleNames) {
                if ($role -in @('Arrow', 'Wait', 'AppStarting')) { $role + '.ani' }
                else { $role + '.cur' }
            }
            'Arrow.cur'
        )
        $requiredFiles = @($cursorFiles) + @('CursorKeeper.exe', 'Install.ps1', 'Restore.ps1', 'Stop-Protection.ps1')
        foreach ($file in $requiredFiles) {
            $source = Join-Path $PSScriptRoot $file
            if (-not [IO.File]::Exists($source) -or (Get-Item -LiteralPath $source).Length -eq 0) {
                throw ('Missing or empty package file: ' + $source)
            }
        }
        [void][IO.Directory]::CreateDirectory($installRoot)
        if ([IO.File]::Exists($backupPath)) {
            Assert-Snapshot (Import-Clixml -LiteralPath $backupPath)
        }
        else {
            $original = Get-RegistrySnapshot
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes([Management.Automation.PSSerializer]::Serialize($original, 8))
            $stream = [IO.File]::Open($backupPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush() }
            finally { $stream.Dispose() }
            Assert-Snapshot (Import-Clixml -LiteralPath $backupPath)
        }
    }

    $beforeChange = Get-RegistrySnapshot
    $runningBefore = @(Get-InstalledHelperProcesses)
    $wasRunning = $runningBefore.Count -gt 0
    foreach ($process in $runningBefore) { $process.Dispose() }
    try {
        Stop-InstalledHelper
        if ($Restore) {
            Set-RegistrySnapshot $savedSnapshot
            Assert-SnapshotApplied $savedSnapshot
            Reload-Cursors
            Write-Host 'Original cursor scheme and startup preferences restored. Your current cursor size was preserved.'
            Write-Host ('Cursor files and the original backup are retained in ' + $installRoot)
        }
        else {
            $copyFiles = @($requiredFiles)
            foreach ($optional in @('CursorKeeper.cs', 'README.txt', 'README.md', 'scheme-preview.png', 'effects-preview.gif', 'cursor-manifest.json')) {
                if ([IO.File]::Exists((Join-Path $PSScriptRoot $optional))) { $copyFiles += $optional }
            }
            foreach ($file in $copyFiles) {
                $source = Join-Path $PSScriptRoot $file
                $destination = Join-Path $installRoot $file
                if ([IO.Path]::GetFullPath($source) -ine [IO.Path]::GetFullPath($destination)) {
                    [IO.File]::Copy($source, $destination, $true)
                }
                if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
                    throw ('Copied file verification failed: ' + $file)
                }
            }
            $runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runPath)
            try { $runKey.SetValue($runName, ('"' + $helperPath + '"'), [Microsoft.Win32.RegistryValueKind]::String); $runKey.Flush() }
            finally { $runKey.Dispose() }
            $apply = Start-Process -FilePath $helperPath -ArgumentList '--apply' -WindowStyle Hidden -PassThru
            try {
                if (-not $apply.WaitForExit(30000)) {
                    # This is the one-shot process launched by this installation.
                    # End it before rollback so a late write cannot undo recovery.
                    $apply.Kill()
                    [void]$apply.WaitForExit(5000)
                    throw 'Applying the permanent cursor scheme timed out.'
                }
                if ($apply.ExitCode -ne 0) { throw ('Applying the permanent cursor scheme failed, exit code ' + $apply.ExitCode + '. See keeper.log in ' + $installRoot) }
            }
            finally { $apply.Dispose() }
            Start-InstalledHelper
            Write-Host 'Soft Black Arrow Persistent is installed, applied, and protected at sign-in and after size changes.'
            Write-Host ('Permanent cursor folder: ' + $installRoot)
            Write-Host ('Original backup: ' + $backupPath)
        }
    }
    catch {
        $failure = $_.Exception.Message
        try {
            Stop-InstalledHelper
            Set-RegistrySnapshot $beforeChange
            Assert-SnapshotApplied $beforeChange
            Reload-Cursors
            if ($wasRunning) { Start-InstalledHelper }
        }
        catch {
            throw ('Cursor operation failed: ' + $failure + '. Rollback also reported: ' + $_.Exception.Message + '. Backup retained at ' + $backupPath)
        }
        throw ('Cursor operation failed; the prior registry state was restored. ' + $failure)
    }
}
finally {
    if ($mutexTaken) { $operationMutex.ReleaseMutex() }
    $operationMutex.Dispose()
}
