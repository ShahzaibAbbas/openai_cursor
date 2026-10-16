#requires -version 5.1
# Installs the scheme with no animated pointers (same as Install.ps1 -Style Static).
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Install.ps1') -Style Static
