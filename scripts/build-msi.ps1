$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $root 'artifacts\msi-stage'
$source = Join-Path $root 'src\MurzilaMusic.App\bin\Release\net9.0-windows\win-x64'
$out = Join-Path $root 'artifacts\MurzilaMusic-3.6.0-win-x64.msi'

dotnet build (Join-Path $root 'src\MurzilaMusic.App\MurzilaMusic.App.csproj') -c Release -v q --nologo

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $source $stage -Recurse -Force

wix build (Join-Path $root 'installer\Product.wxs') `
  -d "SourceDir=$stage" `
  -o $out -nologo

Copy-Item $out (Join-Path $root 'src\MurzilaMusic.Server\wwwroot\downloads\MurzilaMusic-3.6.0-win-x64.msi') -Force
Write-Host "MSI: $out"
