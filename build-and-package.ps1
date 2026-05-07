param(
    [string]$Version,
    [string]$Changelog = "Automated package build",
    [string]$TargetAbi = "10.0.0.0",
    [switch]$SkipManifestUpdate
)

# Build and Package Script for Jellyfin.Plugin.TheSportsDB
$ErrorActionPreference = "Stop"

$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "Jellyfin.Plugin.TheSportsDB.csproj"
$ManifestFile = Join-Path $ProjectDir "manifest.json"
$BuildDir = Join-Path $ProjectDir "bin\Release\net8.0"
$DllName = "Jellyfin.Plugin.TheSportsDB.dll"
$ZipName = "Jellyfin.Plugin.TheSportsDB.zip"
$ZipPath = Join-Path $ProjectDir $ZipName

[xml]$csproj = Get-Content -Path $ProjectFile
if (-not $Version) {
    $Version = $csproj.Project.PropertyGroup.Version
}
if (-not $Version) {
    throw "No version specified and no <Version> found in csproj."
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Building Jellyfin.Plugin.TheSportsDB" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor White

# Stamp csproj versions before build so assembly/package metadata stays in sync.
$csproj.Project.PropertyGroup.Version = $Version
$csproj.Project.PropertyGroup.AssemblyVersion = $Version
$csproj.Project.PropertyGroup.FileVersion = $Version
$csproj.Save($ProjectFile)
Write-Host "Updated csproj version fields." -ForegroundColor Green

# Clean previous build
if (Test-Path $BuildDir) {
    Remove-Item -Path $BuildDir -Recurse -Force
}

# Build the project in Release mode
Write-Host "`nBuilding project in Release mode..." -ForegroundColor Yellow
dotnet build $ProjectFile --configuration Release

# Check if DLL exists
$DllPath = Join-Path $BuildDir $DllName
if (-not (Test-Path $DllPath)) {
    Write-Error "DLL not found at: $DllPath"
    exit 1
}

Write-Host "`nBuild successful!" -ForegroundColor Green

# Create the zip package
Write-Host "`nCreating zip package..." -ForegroundColor Yellow

# Remove old zip if exists
if (Test-Path $ZipPath) {
    Remove-Item -Path $ZipPath -Force
}

# Create zip from the build directory
Compress-Archive -Path "$BuildDir\*" -DestinationPath $ZipPath -Force

if (-not (Test-Path $ZipPath)) {
    Write-Error "Failed to create zip package!"
    exit 1
}

$ZipItem = Get-Item $ZipPath
$ZipSize = $ZipItem.Length / 1KB
$MD5 = (Get-FileHash -Path $ZipPath -Algorithm MD5).Hash.ToLowerInvariant()
$SHA256 = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
$Timestamp = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$SourceUrl = "https://github.com/retrorat1/Jellyfin.Plugin.TheSportsDB/releases/download/v$Version/Jellyfin.Plugin.TheSportsDB.zip"

Write-Host "`nPackage created successfully!" -ForegroundColor Green
Write-Host "  Location: $ZipPath" -ForegroundColor White
Write-Host "  Size: $([math]::Round($ZipSize, 2)) KB" -ForegroundColor White
Write-Host "`nChecksums:" -ForegroundColor Yellow
Write-Host "  MD5:    $MD5" -ForegroundColor White
Write-Host "  SHA256: $SHA256" -ForegroundColor Gray

if (-not $SkipManifestUpdate) {
    if (-not (Test-Path $ManifestFile)) {
        throw "manifest.json not found at: $ManifestFile"
    }

    $manifestRaw = Get-Content -Path $ManifestFile -Raw
    $manifestParsed = $manifestRaw | ConvertFrom-Json
    $manifest = @($manifestParsed)
    if (-not $manifest -or $manifest.Count -lt 1) {
        throw "manifest.json format invalid: expected root array with at least one plugin object."
    }

    $plugin = $manifest[0]
    if (-not $plugin.versions) {
        $plugin | Add-Member -NotePropertyName versions -NotePropertyValue @()
    }

    # Remove duplicate entry for this version if present, then prepend fresh one.
    $existing = @($plugin.versions | Where-Object { $_.version -ne $Version })
    $newEntry = [PSCustomObject]@{
        version   = $Version
        changelog = $Changelog
        targetAbi = $TargetAbi
        sourceUrl = $SourceUrl
        checksum  = $MD5
        timestamp = $Timestamp
    }
    $plugin.versions = @($newEntry) + $existing
    $manifest[0] = $plugin

    $manifestJson = $manifest | ConvertTo-Json -Depth 100
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($ManifestFile, $manifestJson, $utf8NoBom)
    Write-Host "`nUpdated manifest.json with new top version entry." -ForegroundColor Green
    Write-Host "  sourceUrl : $SourceUrl" -ForegroundColor White
    Write-Host "  checksum  : $MD5" -ForegroundColor White
    Write-Host "  timestamp : $Timestamp" -ForegroundColor White
}
else {
    Write-Host "`nSkipped manifest update as requested." -ForegroundColor Yellow
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Build and Package Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
