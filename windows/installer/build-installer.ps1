# Builds the getEQd MSI from the packaged v0.2.0 executable.
#
#   pwsh -File windows\installer\build-installer.ps1
#
# WiX is pinned in windows\.config\dotnet-tools.json, so `dotnet tool restore` in the
# windows directory is the only setup. The MSI lands in windows\installer\dist\.

[CmdletBinding()]
param(
    [string] $Version = "0.2.0",
    [string] $SourceExe,
    [string] $OutputDirectory
)

$ErrorActionPreference = "Stop"

$installerDirectory = $PSScriptRoot
$windowsDirectory = Split-Path -Parent $installerDirectory
$repositoryRoot = Split-Path -Parent $windowsDirectory

if (-not $SourceExe) {
    $SourceExe = Join-Path $windowsDirectory "dist-v$Version\getEQd.exe"
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $installerDirectory "dist"
}

if (-not (Test-Path -LiteralPath $SourceExe)) {
    throw "The packaged executable was not found at '$SourceExe'. Publish it first (see windows\README.md), or pass -SourceExe."
}

$dotnet = Join-Path $repositoryRoot ".tools\dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Push-Location $windowsDirectory
try {
    & $dotnet tool restore | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE." }

    $msi = Join-Path $OutputDirectory "getEQd-$Version.msi"
    # The source path is passed in rather than hard-coded, so the same .wxs builds from any
    # checkout. It is checked above so a missing publish fails readably rather than in WiX.
    & $dotnet tool run wix build (Join-Path $installerDirectory "getEQd.wxs") `
        -arch x64 `
        -d "SourceExe=$SourceExe" `
        -o $msi | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "wix build failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

$built = Get-Item -LiteralPath $msi
Write-Host ("Built {0} ({1:N1} MB)" -f $built.FullName, ($built.Length / 1MB))
