$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== Step 1: Publish LichessBotGUI ===" -ForegroundColor Cyan
$guiOut = "$root\publish_gui"
if (Test-Path $guiOut) { Remove-Item $guiOut -Recurse -Force }

dotnet publish "$root\LichessBotGUI\LichessBotGUI.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
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

# Include Stockfish engine (skip download during install)
$sfExe = Get-ChildItem "$root\stockfish18\stockfish*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($sfExe) {
    $sfDir = "$tempPayload\stockfish18"
    New-Item -ItemType Directory -Force -Path $sfDir | Out-Null
    Copy-Item $sfExe.FullName "$sfDir\$($sfExe.Name)"
    Write-Host "  Stockfish bundled: $($sfExe.Name)" -ForegroundColor Green
}

Compress-Archive -Path "$tempPayload\*" -DestinationPath $zipDest -CompressionLevel Optimal
Remove-Item $tempPayload -Recurse -Force

Write-Host "  Payload.zip created ($([math]::Round((Get-Item $zipDest).Length / 1MB, 1)) MB)" -ForegroundColor Green

Write-Host "=== Step 3: Publish LichessBotSetup (folder) ===" -ForegroundColor Cyan
$setupOut = "$root\publish_setup"
if (Test-Path $setupOut) { Remove-Item $setupOut -Recurse -Force }

dotnet publish "$root\LichessBotSetup\LichessBotSetup.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
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

$zipOut = "$distDir\LichessBotSetup.zip"
if (Test-Path $zipOut) { Remove-Item $zipOut -Force }
Compress-Archive -Path "$zipStaging\LichessBotSetup" -DestinationPath $zipOut -CompressionLevel Optimal -Force
Remove-Item $zipStaging -Recurse -Force

$sizeMB = [math]::Round((Get-Item $zipOut).Length / 1MB, 1)
Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "  Installer ZIP: $zipOut" -ForegroundColor Yellow
Write-Host "  Size: $sizeMB MB" -ForegroundColor Yellow
