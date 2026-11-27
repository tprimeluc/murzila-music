$ErrorActionPreference = 'SilentlyContinue'

$domains = @(
    'murzila-music.cc.cd',
    'murzila-filesharing.cc.cd',
    'mursik-filesharing.cc.cd',
    'mursik-corp.de5.net'
)

Write-Host '=== DNS (A) ===' -ForegroundColor Cyan
foreach ($d in $domains) {
    $ips = (Resolve-DnsName -Name $d -Type A | Where-Object { $_.Type -eq 'A' } | Select-Object -ExpandProperty IPAddress) -join ', '
    if (-not $ips) { $ips = 'NO RECORD' }
    Write-Host ("{0,-32} {1}" -f $d, $ips)
}

Write-Host ''
Write-Host '=== HTTP (local via Host header) ===' -ForegroundColor Cyan
foreach ($d in $domains) {
    try {
        $r = Invoke-WebRequest -Uri 'http://127.0.0.1/' -Headers @{ Host = $d } -UseBasicParsing -TimeoutSec 6
        $title = [regex]::Match($r.Content, '<title>(.*?)</title>').Groups[1].Value
        Write-Host ("{0,-32} {1}  {2}" -f $d, $r.StatusCode, $title)
    } catch {
        Write-Host ("{0,-32} ERR {1}" -f $d, $_.Exception.Response.StatusCode.value__)
    }
}

Write-Host ''
Write-Host '=== Port 80 ===' -ForegroundColor Cyan
$listen = Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -eq 80 }
if ($listen) {
    Write-Host ('listening on: ' + (($listen | ForEach-Object { $_.LocalAddress }) -join ', '))
} else {
    Write-Host 'NOT LISTENING - check MurzilaMusic Server' -ForegroundColor Red
}
