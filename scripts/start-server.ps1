$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/MurzilaMusic.Server/MurzilaMusic.Server.csproj'

if (-not $env:MURZILA_SERVER_URL) {
    $env:MURZILA_SERVER_URL = 'http://murzila-music.cc.cd'
}
if (-not $env:MURZILA_BIND_URL) {
    $env:MURZILA_BIND_URL = 'http://0.0.0.0:6001'
}

if (-not $env:MURZILA_ADMIN_TOKEN) {
    Write-Host 'MURZILA_ADMIN_TOKEN не задан — админские операции магазина будут закрыты (404).' -ForegroundColor Yellow
}
if ($env:MURZILA_ALLOW_TEST_PAYMENTS -eq 'true') {
    Write-Host 'Тестовые покупки включены: подтверждать заказы вручную не нужно.' -ForegroundColor Green
}
if ($env:MURZILA_SELF_REFUND -eq 'true') {
    Write-Host 'Возврат на баланс доступен пользователю без модерации.' -ForegroundColor Green
}
if (-not $env:YOOKASSA_SHOP_ID -or -not $env:YOOKASSA_SECRET_KEY) {
    Write-Host 'Свой магазин активен по умолчанию. ЮKassa не подключена, реальные деньги не списываются.' -ForegroundColor Green
} else {
    Write-Host 'ЮKassa подключена дополнительно: платежи создаются через API провайдера.' -ForegroundColor Green
}

Write-Host "Онлайн-сервис: $env:MURZILA_SERVER_URL" -ForegroundColor Cyan
if ($env:MURZILA_BIND_URL) {
    Write-Host "Привязка Kestrel: $env:MURZILA_BIND_URL + http://0.0.0.0:80 (свой хостинг)" -ForegroundColor DarkCyan
}
Write-Host "Сайты: /player (музыка) · /files (FileSharing) · /api/token · /download · корень домена" -ForegroundColor DarkCyan
dotnet run --project $project
