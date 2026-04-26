$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== Step 1: Publish LichessBotGUI (framework-dependent) ===" -ForegroundColor Cyan
$guiOut = "$root\publish_gui"
if (Test-Path $guiOut) { Remove-Item $guiOut -Recurse -Force }

dotnet publish "$root\LichessBotGUI\LichessBotGUI.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:DebugType=None `
    -o $guiOut

if ($LASTEXITCODE -ne 0) { throw "LichessBotGUI publish failed" }
Write-Host "  GUI published to $guiOut" -ForegroundColor Green

Write-Host "=== Step 1b: Publish LichessBotUninstall (single-file) ===" -ForegroundColor Cyan
$uninstOut = "$root\publish_uninstall"
if (Test-Path $uninstOut) { Remove-Item $uninstOut -Recurse -Force }

dotnet publish "$root\LichessBotUninstall\LichessBotUninstall.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -o $uninstOut

if ($LASTEXITCODE -ne 0) { throw "LichessBotUninstall publish failed" }
if (-not (Test-Path "$uninstOut\LichessBotUninstall.exe")) {
    throw "LichessBotUninstall.exe not produced"
}
Write-Host "  Uninstaller published to $uninstOut" -ForegroundColor Green

Write-Host "=== Step 2: Create Payload.zip ===" -ForegroundColor Cyan
$zipDest = "$root\LichessBotSetup\Payload.zip"

if (-not (Test-Path "$guiOut\LichessBotGUI.exe")) {
    throw "LichessBotGUI.exe not found at: $guiOut\LichessBotGUI.exe"
}

if (Test-Path $zipDest) { Remove-Item $zipDest }

$tempPayload = "$root\payload_tmp"
if (Test-Path $tempPayload) { Remove-Item $tempPayload -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$tempPayload\LichessBotGUI" | Out-Null
Copy-Item "$guiOut\*" "$tempPayload\LichessBotGUI\" -Recurse

Copy-Item "$uninstOut\LichessBotUninstall.exe" "$tempPayload\LichessBotUninstall.exe"
Write-Host "  Bundled uninstaller into payload" -ForegroundColor Green

# Include Python bot files at root level
$pyFiles = @("cli.py", "bot.py", "config.py", "engine.py", "engine_setup.py",
             "eval_server.py", "license.py", "live_config.py",
             "requirements.txt", "version.txt")
foreach ($f in $pyFiles) {
    $src = "$root\$f"
    if (Test-Path $src) { Copy-Item $src "$tempPayload\$f" }
}

# Stockfish is NOT bundled - installer downloads it during install to keep the
# setup ZIP small enough for Telegram auto-upload (~49 MB limit).
Write-Host "  Stockfish skipped - installer will download at install time" -ForegroundColor Yellow

# Bundle Python wheels so pip install runs offline in seconds
Write-Host "  Downloading wheels for requirements.txt..." -ForegroundColor Cyan
$wheelDir = "$tempPayload\wheels"
New-Item -ItemType Directory -Force -Path $wheelDir | Out-Null
$pipArgs = @(
    "-m", "pip", "download",
    "-r", "$root\requirements.txt",
    "-d", $wheelDir,
    "--platform", "win_amd64",
    "--python-version", "3.12",
    "--only-binary", ":all:",
    "-q"
)
& python @pipArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Wheel bundling failed - installer will fall back to PyPI" -ForegroundColor Yellow
    Remove-Item $wheelDir -Recurse -Force -ErrorAction SilentlyContinue
} else {
    $wheelCount = (Get-ChildItem $wheelDir -File).Count
    Write-Host "  Bundled $wheelCount wheel(s)" -ForegroundColor Green
}

Compress-Archive -Path "$tempPayload\*" -DestinationPath $zipDest -CompressionLevel Optimal
Remove-Item $tempPayload -Recurse -Force

Write-Host "  Payload.zip created ($([math]::Round((Get-Item $zipDest).Length / 1MB, 1)) MB)" -ForegroundColor Green

Write-Host "=== Step 3: Publish LichessBotSetup (framework-dependent) ===" -ForegroundColor Cyan
$setupOut = "$root\publish_setup"
if (Test-Path $setupOut) { Remove-Item $setupOut -Recurse -Force }

dotnet publish "$root\LichessBotSetup\LichessBotSetup.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -o $setupOut

if ($LASTEXITCODE -ne 0) { throw "LichessBotSetup publish failed" }

Write-Host "=== Step 4: Create dist/LichessBotSetup.zip and dist/LichessBotSetup.exe ===" -ForegroundColor Cyan
$distDir = "$root\dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$distExe = "$distDir\LichessBotSetup.exe"
if (Test-Path $distExe) { Remove-Item $distExe -Force }
Copy-Item "$setupOut\LichessBotSetup.exe" $distExe
$exeSizeMB = [math]::Round((Get-Item $distExe).Length / 1MB, 2)
Write-Host "  Single-file EXE: $distExe ($exeSizeMB MB)" -ForegroundColor Green

$zipStaging = "$root\setup_zip_tmp"
if (Test-Path $zipStaging) { Remove-Item $zipStaging -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$zipStaging\LichessBotSetup" | Out-Null
Copy-Item "$setupOut\*" "$zipStaging\LichessBotSetup\" -Recurse

$readme = @"
Lichess Bot - Installation
==========================

1. Double-click LichessBotSetup.exe
2. Confirm the UAC prompt (admin access is needed to remove a previous install)
3. Enter your License Key and Lichess API Token, then press Install

Requirements
------------
Windows 10/11 x64 with .NET Desktop Runtime 9.0 or newer.

If the installer does not start (Windows asks for ".NET Desktop Runtime"):
  - download and install:
    https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe
  - then re-run LichessBotSetup.exe

Stockfish 18 and the Python dependencies are downloaded automatically
during installation.
"@
Set-Content -Path "$zipStaging\LichessBotSetup\README.txt" -Value $readme -Encoding UTF8

$zipOut = "$distDir\LichessBotSetup.zip"
if (Test-Path $zipOut) { Remove-Item $zipOut -Force }
Compress-Archive -Path "$zipStaging\LichessBotSetup" -DestinationPath $zipOut -CompressionLevel Optimal -Force
Remove-Item $zipStaging -Recurse -Force

$sizeMB = [math]::Round((Get-Item $zipOut).Length / 1MB, 1)
Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "  Installer ZIP (Telegram): $zipOut" -ForegroundColor Yellow
Write-Host "  Size: $sizeMB MB" -ForegroundColor Yellow
Write-Host "  Installer EXE (site):     $distExe" -ForegroundColor Yellow
Write-Host "  Size: $exeSizeMB MB" -ForegroundColor Yellow
