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

# Include Python bot files at root level
$pyFiles = @("cli.py", "bot.py", "config.py", "engine.py", "engine_setup.py",
             "eval_server.py", "license.py", "requirements.txt", "version.txt")
foreach ($f in $pyFiles) {
    $src = "$root\$f"
    if (Test-Path $src) { Copy-Item $src "$tempPayload\$f" }
}

# Stockfish is NOT bundled - installer downloads it during install to keep the
# setup ZIP small enough for Telegram auto-upload (~49 MB limit).
Write-Host "  Stockfish skipped - installer will download at install time" -ForegroundColor Yellow

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
    -p:DebugType=None `
    -o $setupOut

if ($LASTEXITCODE -ne 0) { throw "LichessBotSetup publish failed" }

Write-Host "=== Step 4: Create dist/LichessBotSetup.zip ===" -ForegroundColor Cyan
$distDir = "$root\dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$oldExe = "$distDir\LichessBotSetup.exe"
if (Test-Path $oldExe) { Remove-Item $oldExe -Force }

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
Write-Host "  Installer ZIP: $zipOut" -ForegroundColor Yellow
Write-Host "  Size: $sizeMB MB" -ForegroundColor Yellow
