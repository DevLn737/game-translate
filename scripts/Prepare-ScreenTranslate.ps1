[CmdletBinding()]
param([switch] $NoLaunch, [switch] $BuildOnly, [string] $DotnetPath)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repositoryRoot '.tools\dotnet\dotnet.exe'
$dotnet = if ($DotnetPath) { (Get-Command $DotnetPath -ErrorAction Stop).Source } elseif (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
Import-Module (Join-Path $PSScriptRoot 'ScreenTranslate.Build.psm1') -Force
Invoke-ScreenTranslatePreparation -RepositoryRoot $repositoryRoot -Dotnet $dotnet -BuildOnly:$BuildOnly -NoLaunch:$NoLaunch
