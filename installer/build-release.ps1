#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$DotNet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $DotNet)) {
    $DotNet = "dotnet"
}

$PublishDir = Join-Path $RepoRoot "release\publish"
$ReleaseDir = Join-Path $RepoRoot "release"

Write-Host "Publishing Release win-x64..."
& $DotNet publish (Join-Path $RepoRoot "src\EvEMapEnhanced.Desktop\EvEMapEnhanced.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $PublishDir

$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($IsccCandidates) {
    Write-Host "Building installer with Inno Setup..."
    & $IsccCandidates (Join-Path $PSScriptRoot "EvEMapEnhanced.iss")
    Write-Host "Installer: $ReleaseDir\EvEMapEnhanced-Setup-1.0.exe"
} else {
    $ZipPath = Join-Path $ReleaseDir "EvEMapEnhanced-1.0-win-x64.zip"
    Write-Host "Inno Setup not found; creating portable zip: $ZipPath"
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath
    Write-Host "Portable build: $PublishDir"
    Write-Host "Zip archive: $ZipPath"
    Write-Host "Install Inno Setup 6 to produce EvEMapEnhanced-Setup-1.0.exe"
}

Write-Host "Done."
