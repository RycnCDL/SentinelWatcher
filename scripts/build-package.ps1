#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the deployable SentinelWatcher package: the API with the SPA in wwwroot, as one zip.

.DESCRIPTION
    1. Builds the client (npm ci, npm run build).
    2. Publishes the API (dotnet publish, Release, framework-dependent).
    3. Copies the client build into the published wwwroot folder.
    4. Zips the result to artifacts/sentinelwatcher.zip with forward-slash entry names, as
       App Service on Linux expects for WEBSITE_RUN_FROM_PACKAGE and az webapp deploy.

    Requires PowerShell 7, Node.js 22 and the .NET 10 SDK. Runs on Windows, Linux and macOS.

.PARAMETER Dotnet
    Path to the dotnet executable. Defaults to dotnet on PATH, then ~/.dotnet/dotnet.

.PARAMETER OutputDir
    Folder for the package. Defaults to artifacts in the repository root.

.EXAMPLE
    pwsh scripts/build-package.ps1
#>
[CmdletBinding()]
param(
    [string] $Dotnet,
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
$publishDir = Join-Path $OutputDir 'publish'
$zipPath = Join-Path $OutputDir 'sentinelwatcher.zip'

if (-not $Dotnet) {
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    $userInstall = Join-Path $HOME ($IsWindows ? '.dotnet/dotnet.exe' : '.dotnet/dotnet')
    $Dotnet = if ($onPath) { $onPath.Source } elseif (Test-Path $userInstall) { $userInstall } else { 'dotnet' }
}

function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

Push-Location (Join-Path $root 'src/client')
try {
    Invoke-Step 'npm ci' { npm ci --no-audit --no-fund }
    Invoke-Step 'npm run build' { npm run build }
}
finally {
    Pop-Location
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Step 'dotnet publish' {
    & $Dotnet publish (Join-Path $root 'src/api/SentinelWatcher.Api.csproj') -c Release -o $publishDir --nologo
}

# Development settings must never ship. appsettings.Development.json is gitignored, but a local
# copy would otherwise be published.
Get-ChildItem $publishDir -Filter 'appsettings.Development*.json' | Remove-Item -Force

$wwwroot = Join-Path $publishDir 'wwwroot'
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $root 'src/client/dist/*') -Destination $wwwroot -Recurse -Force

Write-Host "==> zip $zipPath"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $base = (Resolve-Path $publishDir).Path.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem $publishDir -Recurse -File) {
        $entryName = $file.FullName.Substring($base.Length).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

$size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Package: $zipPath ($size MB)"
