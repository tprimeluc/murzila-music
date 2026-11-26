$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Task Scheduler не всегда подхватывает пользовательские переменные из реестра —
# читаем их явно, чтобы SMTP и админские токены были доступны серверу.
$registry = Get-ItemProperty -Path 'HKCU:\Environment' -ErrorAction SilentlyContinue
if ($registry) {
    foreach ($name in @(
        'SMTP_HOST', 'SMTP_PORT', 'SMTP_USER', 'SMTP_PASSWORD', 'SMTP_FROM', 'SMTP_SSL',
        'MURZILA_SERVER_URL', 'MURZILA_BIND_URL', 'MURZILA_BOT_KEY',
        'MURZILA_ADMIN_TOKEN', 'MURZILA_ALLOW_TEST_PAYMENTS', 'MURZILA_SELF_REFUND',
        'MURZILA_ADMIN_EMAIL', 'YOOKASSA_SHOP_ID', 'YOOKASSA_SECRET_KEY',
        'MURZILA_RECOGNIZE_PROVIDER', 'MURZILA_RECOGNIZE_KEY'
    )) {
        if ($null -ne $registry.PSObject.Properties[$name]) {
            [Environment]::SetEnvironmentVariable($name, [string]$registry.$name, 'Process')
        }
    }
}

Set-Location $root
& (Join-Path $PSScriptRoot 'start-server.ps1')