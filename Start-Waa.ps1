[CmdletBinding()]
param([switch]$NoBrowser, [string]$DataRoot)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $DataRoot) { $DataRoot = Join-Path $env:LOCALAPPDATA 'Waa' }
& (Join-Path $root 'src/Server.ps1') -Root $root -DataRoot $DataRoot -NoBrowser:$NoBrowser
