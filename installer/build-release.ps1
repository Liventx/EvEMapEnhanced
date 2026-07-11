#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$DotNet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $DotNet)) {
    $DotNet = "dotnet"
}

$PublishDir = Join-Path $RepoRoot "release\publish"
$ReleaseDir = Join-Path $RepoRoot "release"
$IssPath = Join-Path $PSScriptRoot "EvEMapEnhanced.iss"
$AppVersion = "1.0.3"
if (Test-Path $IssPath) {
    $versionMatch = Select-String -Path $IssPath -Pattern '#define MyAppVersionFile "([^"]+)"' | Select-Object -First 1
    if ($versionMatch -and $versionMatch.Matches[0].Groups[1].Success) {
        $AppVersion = $versionMatch.Matches[0].Groups[1].Value
    }
}
$AppIconSource = Join-Path $RepoRoot "src\EvEMapEnhanced.Desktop\Assets\app-icon.ico"
$AppIconDest = Join-Path $PSScriptRoot "app-icon.ico"
if (Test-Path $AppIconSource) {
    Copy-Item $AppIconSource $AppIconDest -Force
}

Write-Host "Publishing Release win-x64..."
& $DotNet publish (Join-Path $RepoRoot "src\EvEMapEnhanced.Desktop\EvEMapEnhanced.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $PublishDir

$EsiClientSource = Join-Path $PSScriptRoot "esi-client.json"
if (Test-Path $EsiClientSource) {
    Copy-Item $EsiClientSource (Join-Path $PublishDir "esi-client.json") -Force
    Write-Host "Bundled esi-client.json into publish folder."
} else {
    Write-Warning "installer/esi-client.json not found - ESI sign-in will require manual setup on new PCs."
}

$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($IsccCandidates) {
    Write-Host "Building installer with Inno Setup..."
    & $IsccCandidates (Join-Path $PSScriptRoot "EvEMapEnhanced.iss")
    Write-Host "Installer: $ReleaseDir\EvEMapEnhanced-Setup-$AppVersion.exe"
} else {
    $ZipPath = Join-Path $ReleaseDir "EvEMapEnhanced-$AppVersion-win-x64.zip"
    Write-Host "Inno Setup not found; creating portable zip: $ZipPath"
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath
    Write-Host "Portable build: $PublishDir"
    Write-Host "Zip archive: $ZipPath"
    Write-Host "Install Inno Setup 6 to produce EvEMapEnhanced-Setup-$AppVersion.exe"
}

Write-Host "Done."
