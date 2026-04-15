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
Compress-Archive -Path "$guiOut\*" -DestinationPath $zipDest -CompressionLevel Optimal

Write-Host "  Payload.zip created ($([math]::Round((Get-Item $zipDest).Length / 1MB, 1)) MB)" -ForegroundColor Green

Write-Host "=== Step 3: Publish LichessBotSetup ===" -ForegroundColor Cyan
$setupOut = "$root\publish_setup"
if (Test-Path $setupOut) { Remove-Item $setupOut -Recurse -Force }

dotnet publish "$root\LichessBotSetup\LichessBotSetup.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $setupOut

if ($LASTEXITCODE -ne 0) { throw "LichessBotSetup publish failed" }

$distDir = "$root\dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$setupExe = "$setupOut\LichessBotSetup.exe"
Copy-Item $setupExe "$distDir\LichessBotSetup.exe" -Force

$sizeMB = [math]::Round((Get-Item "$distDir\LichessBotSetup.exe").Length / 1MB, 1)
Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "  Installer: $distDir\LichessBotSetup.exe" -ForegroundColor Yellow
Write-Host "  Size: $sizeMB MB" -ForegroundColor Yellow
