$ErrorActionPreference = 'Stop'

# One command to publish Murzila-Music online with a permanent short URL.
# First time only: open the invitation link from your e-mail, then run
#   C:\Tools\zrok\zrok2.exe enable <TOKEN_FROM_EMAIL>
# After that this script keeps the share running.

$root = Join-Path (Split-Path -Parent $PSScriptRoot) '.'
$zrok = 'C:\Tools\zrok\zrok2.exe'
$publicUrl = 'https://murzila-music.shares.zrok.io'
$localPort = 5188

if (-not (Test-Path $zrok)) {
    throw "zrok2.exe not found at $zrok. See docs/SETUP-REMOTE.md."
}

$envRoot = Join-Path $env:USERPROFILE '.zrok2'
if (-not (Test-Path (Join-Path $envRoot 'environment.json'))) {
    throw 'zrok is not enabled yet. Open the invitation e-mail, then run: C:\Tools\zrok\zrok2.exe enable <TOKEN>'
}

# Reserve the permanent short name once. Existing names are fine.
# zrok writes some diagnostics to stderr, so do not let them stop the script.
$oldEap = $ErrorActionPreference
$ErrorActionPreference = 'SilentlyContinue'
& $zrok create name -n public murzila-music 2>&1 | Out-Null
$ErrorActionPreference = $oldEap

$listener = Get-NetTCPConnection -LocalPort $localPort -State Listen -ErrorAction SilentlyContinue
if (-not $listener) {
    Write-Host 'Starting local server...'
    Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $PSScriptRoot 'start-server.ps1')
    ) -WorkingDirectory $root -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 3
}

Write-Host "Online URL: $publicUrl"
$share = Start-Process -FilePath $zrok -ArgumentList @(
    'share', 'public', "localhost:$localPort",
    '-n', 'public:murzila-music', '--headless'
) -PassThru -WindowStyle Hidden

try {
    while (-not $share.HasExited) {
        curl.exe -sS -m 10 -o NUL "$publicUrl/api/info"
        Start-Sleep -Seconds 15
    }
}
finally {
    if (-not $share.HasExited) { Stop-Process -Id $share.Id -Force -ErrorAction SilentlyContinue }
}
