$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts'
$stage = Join-Path $out ('Murzila-Music-beta-win-x64-' + [guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Force $out | Out-Null

dotnet publish (Join-Path $root 'src/MurzilaMusic.App/MurzilaMusic.App.csproj') `
    -c Release -r win-x64 --self-contained false `
    -o $stage

$serverStage = Join-Path $stage 'Server'
dotnet publish (Join-Path $root 'src/MurzilaMusic.Server/MurzilaMusic.Server.csproj') `
    -c Release -r win-x64 --self-contained false `
    -o $serverStage
Copy-Item (Join-Path $root 'scripts/start-server.cmd') $stage -Force
Copy-Item (Join-Path $root 'scripts/start-online.ps1') $stage -Force
Copy-Item (Join-Path $root 'scripts/admin.ps1') $stage -Force
Copy-Item (Join-Path $root 'scripts/install-autostart.ps1') $stage -Force
Copy-Item (Join-Path $root 'scripts/run-server-autostart.ps1') $stage -Force

$zip = Join-Path $out 'Murzila-Music-beta-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

# Временные папки сборки больше не нужны — в архиве остаётся только актуальная версия.
Get-ChildItem $out -Directory -Filter 'Murzila-Music-beta-win-x64-*' |
    Where-Object { $_.FullName -ne $stage } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Done: $zip"
