#requires -version 5.1
<#
.SYNOPSIS
Stops cursor protection and restores the state saved before the first install.
.DESCRIPTION
Restores all 17 cursor roles, saved scheme, theme and accessibility preferences,
and the prior startup entry with exact registry types and existence. Current
cursor size, base size, and color are preserved. Files and backup are retained.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Install.ps1'
if (-not [IO.File]::Exists($installer)) {
    $installedRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CustomCursors\SoftBlackArrowPermanent'
    $installer = Join-Path $installedRoot 'Install.ps1'
}
if (-not [IO.File]::Exists($installer)) { throw 'Install.ps1 could not be found beside this script or in the permanent cursor folder.' }
& $installer -Restore
