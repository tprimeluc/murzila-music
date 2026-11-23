param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('promo', 'orders', 'approve', 'reject', 'refund-approve', 'refund-reject', 'balance', 'state')]
    [string]$Action,

    [string]$ProductId = 'pro50',
    [int]$MaxUses = 1,
    [string]$ExpiresAt,
    [string]$Note = '',
    [string]$OrderId = '',
    [string]$RefundId = '',
    [string]$UserId = '',
    [int]$AmountMinor = 0,
    [string]$Token = $env:MURZILA_ADMIN_TOKEN
)

$ErrorActionPreference = 'Stop'
$base = if ($env:MURZILA_SERVER_URL) { $env:MURZILA_SERVER_URL } else { 'http://localhost:5188' }

if (-not $Token) {
    throw 'Задай MURZILA_ADMIN_TOKEN (переменную окружения или параметр -Token).'
}

function Invoke-Admin {
    param([string]$Method, [string]$Path, [object]$Body)

    $headers = @{ 'X-Admin-Token' = $Token }
    $args = @{
        Uri     = "$base$Path"
        Method  = $Method
        Headers = $headers
        TimeoutSec = 20
    }
    if ($null -ne $Body) {
        $headers['Content-Type'] = 'application/json'
        $args['Body'] = $Body | ConvertTo-Json -Compress -Depth 6
    }
    try {
        $response = Invoke-RestMethod @args
        $response | ConvertTo-Json -Depth 10
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        throw "Ошибка $status при $Method $Path : $($_.Exception.Message)"
    }
}

switch ($Action) {
    'promo' {
        Invoke-Admin -Method POST -Path '/api/admin/promos' -Body @{
            productId = $ProductId
            maxUses   = $MaxUses
            expiresAt = $ExpiresAt
            note      = $Note
        }
    }
    'orders' {
        Invoke-Admin -Method GET -Path '/api/admin/orders'
    }
    'approve' {
        Invoke-Admin -Method POST -Path "/api/admin/orders/$OrderId/decide" -Body @{
            approve = $true
            note    = $Note
        }
    }
    'reject' {
        Invoke-Admin -Method POST -Path "/api/admin/orders/$OrderId/decide" -Body @{
            approve = $false
            note    = $Note
        }
    }
    'refund-approve' {
        Invoke-Admin -Method POST -Path "/api/admin/refunds/$RefundId/decide" -Body @{
            approve = $true
            note    = $Note
        }
    }
    'refund-reject' {
        Invoke-Admin -Method POST -Path "/api/admin/refunds/$RefundId/decide" -Body @{
            approve = $false
            note    = $Note
        }
    }
    'balance' {
        Invoke-Admin -Method POST -Path '/api/admin/balance/grant' -Body @{
            userId      = $UserId
            amountMinor = $AmountMinor
            reason      = $Note
        }
    }
    'state' {
        if (-not $Token) { throw 'Нужен токен.' }
        Invoke-RestMethod -Uri "$base/api/info" -TimeoutSec 20 | ConvertTo-Json -Depth 6
    }
}
