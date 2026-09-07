#requires -version 5.1
<#
.SYNOPSIS
Disables Soft Black Arrow cursor protection while keeping the current scheme.
.DESCRIPTION
Removes only this helper's startup entry and signals its installed process to
exit. It does not change any cursor, cursor size, theme, or other startup item.
Run Install.ps1 again to resume protection, or Restore.ps1 to restore the backup.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installedRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CustomCursors\SoftBlackArrowPermanent'))
$helperPath = Join-Path $installedRoot 'CursorKeeper.exe'
$runPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'SoftBlackArrowCursorKeeper'
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$operationMutex = [Threading.Mutex]::new($false, ('Local\SoftBlackArrowPersistent.Install.' + $sid))
$mutexTaken = $false
try {
    try { $mutexTaken = $operationMutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $mutexTaken = $true }
    if (-not $mutexTaken) { throw 'Another Soft Black Arrow install or restore is already running.' }
    if ([IO.File]::Exists($helperPath)) {
        $signal = Start-Process -FilePath $helperPath -ArgumentList '--stop' -WindowStyle Hidden -PassThru
        try {
            if (-not $signal.WaitForExit(10000)) { throw 'The cursor helper did not finish its stop request.' }
            if ($signal.ExitCode -ne 0) { throw ('The cursor helper stop request failed: ' + $signal.ExitCode) }
        }
        finally { $signal.Dispose() }
    }
    foreach ($process in [Diagnostics.Process]::GetProcessesByName('CursorKeeper')) {
        try {
            $isInstalledHelper = $false
            try { $isInstalledHelper = [IO.Path]::GetFullPath($process.MainModule.FileName) -ieq $helperPath }
            catch { continue }
            if ($isInstalledHelper -and -not $process.WaitForExit(10000)) { throw 'The installed cursor helper is still stopping. Please retry.' }
        }
        finally { $process.Dispose() }
    }
    $startupWasReassigned = $false
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath, $true)
    try {
        if ($null -ne $key) {
            $value = [string]$key.GetValue($runName, '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($value -eq ('"' + $helperPath + '"') -or $value -eq $helperPath) {
                $key.DeleteValue($runName, $false)
                $key.Flush()
            }
            elseif (@($key.GetValueNames()) -contains $runName) {
                $startupWasReassigned = $true
                Write-Warning 'The startup value has been changed to another command and was left untouched.'
            }
        }
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
    if ($startupWasReassigned) {
        Write-Host 'Cursor protection is stopped. The reassigned startup value and applied cursor scheme were kept.'
    }
    else {
        Write-Host 'Cursor protection is stopped and its startup entry is disabled. The applied cursor scheme was kept.'
    }
}
finally {
    if ($mutexTaken) { $operationMutex.ReleaseMutex() }
    $operationMutex.Dispose()
}
