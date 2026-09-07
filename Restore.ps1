#requires -version 5.1
<#
.SYNOPSIS
Restores the exact cursor scheme saved before the first Soft Black Arrow v3 install.
.DESCRIPTION
Restores all 17 cursor values, their registry types and existence, the unnamed
scheme value, Scheme Source, and any previous saved Soft Black Arrow v3 entry.
Backup and assets are retained. Mouse speed, trails and other settings are untouched.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Install.ps1'
if (-not [IO.File]::Exists($installer)) {
    $installRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CustomCursors\SoftBlackArrowV3'
    $installer = Join-Path $installRoot 'Install.ps1'
}
if (-not [IO.File]::Exists($installer)) {
    throw 'Install.ps1 could not be located. Keep it alongside Restore.ps1 in the cursor package.'
}
& $installer -Restore
