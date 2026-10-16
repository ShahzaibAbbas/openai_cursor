#requires -version 5.1
<#
.SYNOPSIS
Rebuilds every derived Soft Black Arrow file from source.
.DESCRIPTION
Uses only the C# compiler that ships with Windows (.NET Framework 4.x). Compiles the
CursorForge tool, regenerates the Lively animations from the static artwork, the
application icon, CursorKeeper.exe, the README preview GIF and the docs/ web preview,
then loads every cursor through Windows and writes validation.json.

Nothing here changes the active pointer scheme: cursors are loaded into private
handles and drawn into off-screen bitmaps only.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\Build.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipAnimations,
    [switch]$SkipKeeper,
    [switch]$SkipPreviews,
    [switch]$SkipVerify
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root '.build'
$csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (-not [IO.File]::Exists($csc)) { throw ('The .NET Framework C# compiler was not found at ' + $csc) }
[void][IO.Directory]::CreateDirectory($out)

function Invoke-Tool {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw ([IO.Path]::GetFileName($Exe) + ' ' + $Arguments[0] + ' failed with exit code ' + $LASTEXITCODE) }
}

Write-Host '== CursorForge'
$forge = Join-Path $out 'CursorForge.exe'
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
Invoke-Tool $csc (@('/nologo', '/codepage:65001', '/optimize', '/warn:4', ('/out:' + $forge), '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll') + $sources)

if (-not $SkipAnimations) {
    Write-Host '== Animations'
    Invoke-Tool $forge @('build', $root)
}

$icon = Join-Path $PSScriptRoot 'CursorKeeper.ico'
Invoke-Tool $forge @('icon', $root, $icon)

if (-not $SkipKeeper) {
    Write-Host '== CursorKeeper.exe'
    $keeper = Join-Path $root 'CursorKeeper.exe'
    $built = Join-Path $out 'CursorKeeper.exe'
    Invoke-Tool $csc @('/nologo', '/codepage:65001', '/optimize', '/warn:4', '/target:winexe', '/platform:anycpu',
        ('/win32icon:' + $icon), ('/resource:' + $icon + ',CursorKeeper.ico'),
        '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll',
        ('/out:' + $built), (Join-Path $root 'CursorKeeper.cs'))
    $running = @([Diagnostics.Process]::GetProcessesByName('CursorKeeper') | Where-Object {
        try { [IO.Path]::GetFullPath($_.MainModule.FileName) -ieq $keeper } catch { $false } })
    if ($running.Count -gt 0) { throw 'CursorKeeper.exe from this folder is running; stop it before rebuilding.' }
    [IO.File]::Copy($built, $keeper, $true)
    Write-Host ('{0} ({1:N0} bytes)' -f $keeper, (Get-Item -LiteralPath $keeper).Length)
}

if (-not $SkipPreviews) {
    Write-Host '== Previews'
    Invoke-Tool $forge @('scheme', $root, (Join-Path $root 'scheme-preview.png'))
    Invoke-Tool $forge @('gif', $root, (Join-Path $root 'lively-preview.gif'))
    Invoke-Tool $forge @('web', $root, (Join-Path $root 'docs'))
}

if (-not $SkipVerify) {
    Write-Host '== Verify'
    Invoke-Tool $forge @('verify', $root, (Join-Path $root 'validation.json'))
}
