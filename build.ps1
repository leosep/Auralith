# Requires: .NET 8 SDK (WiX v7 is restored automatically via NuGet)
$ErrorActionPreference = "Stop"

Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue
dotnet publish src/Auralith.csproj -c Release -r win-x64 --self-contained true -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

dotnet build installer/Installer.wixproj -c Release
if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE" }

$msi = "installer\bin\x64\Release\Auralith-Setup.msi"
if (-not (Test-Path $msi)) { throw "MSI was not produced at $msi" }
Write-Host "MSI: $msi"
