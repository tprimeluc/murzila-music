$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $root 'cef'
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Write-Host 'Fetching CEF build index...'
$index = Invoke-RestMethod -Uri 'https://cef-builds.spotifycdn.com/index.json' -TimeoutSec 120
$ver = $index.windows64.versions | Where-Object { $_.cef_version -like '156.*' } | Select-Object -First 1
if (-not $ver) { throw 'CEF 156 build not found' }
$file = $ver.files | Where-Object { $_.type -eq 'minimal' } | Select-Object -First 1
if (-not $file) { throw 'Minimal CEF archive not found' }

$url = 'https://cef-builds.spotifycdn.com/' + $file.name
$archive = Join-Path $dest $file.name
if (-not (Test-Path $archive)) {
    Write-Host ('Downloading ' + $file.name + ' (' + [math]::Round($file.size/1MB) + ' MB)...')
    Invoke-WebRequest -Uri $url -OutFile $archive -TimeoutSec 1800
}
Write-Host 'Extracting...'
tar -xf $archive -C $dest
Write-Host ('CEF SDK ready: ' + $dest)
