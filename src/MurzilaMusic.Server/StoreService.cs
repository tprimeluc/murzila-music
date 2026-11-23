using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MurzilaMusic.Shared;

namespace MurzilaMusic.Server;

public sealed class StoreException(string message) : Exception(message);

public sealed class StoreOrderDto
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductKind { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int AmountMinor { get; set; }
    public string Provider { get; set; } = "internal";
    public string Method { get; set; } = "manual";
    public string Status { get; set; } = "pending";
    public string? ConfirmationUrl { get; set; }
    public string? TrackId { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? PaidAt { get; set; }
}

public sealed class EntitlementDto
{
    public string Id { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductKind { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "active";
    public string StartsAt { get; set; } = "";
    public string? ExpiresAt { get; set; }
}

public sealed class RefundDto
{
    public string Id { get; set; } = "";
    public string OrderId { get; set; } = "";
    public int AmountMinor { get; set; }
    public string ReasonCode { get; set; } = "";
    public string? Comment { get; set; }
    public string Status { get; set; } = "requested";
    public string RequestedAt { get; set; } = "";
    public string? DecisionNote { get; set; }
    public string? CompletedAt { get; set; }
}

public sealed class AccountStateDto
{
    public UserDto User { get; set; } = new();
    public string ActivePlanId { get; set; } = "free";
    public string AdsLevel { get; set; } = "full";
    public List<string> Features { get; set; } = new();
    public List<EntitlementDto> Entitlements { get; set; } = new();
    public List<StoreOrderDto> Orders { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
    public int BalanceMinor { get; set; }
    public List<string> UnlockedTrackIds { get; set; } = new();
}

public sealed class RestoreResultDto
{
    public int Restored { get; set; }
    public string ActivePlanId { get; set; } = "free";
    public int BalanceMinor { get; set; }
    public string Message { get; set; } = "";
}

public sealed class PromoResultDto
{
    public string Code { get; set; } = "";
    public string CodeHint { get; set; } = "";
    public string ProductId { get; set; } = "";
    public int MaxUses { get; set; }
    public string? ExpiresAt { get; set; }
}

/// <summary>
/// Свой магазин без карточных платежей: промокоды, внутренний баланс,
/// ручное подтверждение админом и локальный тестовый режим. ЮKassa остаётся опциональной.
/// </summary>
public sealed class StoreService
{
    private readonly Db _db;
    private readonly SmtpMailer _mail;
    public CatalogDto Catalog { get; }
    public bool TestPayments => EnvFlag("MURZILA_ALLOW_TEST_PAYMENTS");
    public bool SelfRefund => EnvFlag("MURZILA_SELF_REFUND");
    public bool PromoEnabled => true;
    public bool RestoreEnabled => true;
    public string DefaultMethod =>
        Environment.GetEnvironmentVariable("MURZILA_STORE_METHOD") ?? (TestPayments ? "test" : "manual");

    public StoreService(Db db, SmtpMailer mail)
    {
        _db = db;
        _mail = mail;
        Catalog = CatalogLoader.LoadEmbedded();
        var errors = CatalogLoader.Validate(Catalog);
        if (errors.Count > 0)
            throw new InvalidOperationException("Ошибки каталога планов: " + string.Join("; ", errors));
    }

    public static bool EnvFlag(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsAdmin(HttpContext context)
    {
        string expected = Environment.GetEnvironmentVariable("MURZILA_ADMIN_TOKEN") ?? "";
        if (string.IsNullOrWhiteSpace(expected)) return false;
        string actual = context.Request.Headers["X-Admin-Token"].ToString();
        if (actual.Length == 0 || actual.Length != expected.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    }

    public async Task<StoreOrderDto?> CreateOrderAsync(string userId, string productId, string method,
        string? promoCode, string? trackId, string? idempotencyKey)
    {
        var plan = Catalog.FindPlan(productId);
        var addon = Catalog.FindAddon(productId);
        if (plan is null && addon is null) throw new StoreException("Неизвестный товар.");
        if (plan is not null && plan.PriceRub <= 0) throw new StoreException("Бесплатный план не покупается.");
        if (addon?.Kind == "trackUnlock" && string.IsNullOrWhiteSpace(trackId))
            throw new StoreException("Укажи трек для разового доступа.");

        method = string.IsNullOrWhiteSpace(method) ? DefaultMethod : method.Trim().ToLowerInvariant();
        if (method is not ("promo" or "balance" or "test" or "manual"))
            throw new StoreException("Неизвестный способ покупки.");
        if (method == "test" && !TestPayments)
            throw new StoreException("Тестовые покупки выключены на сервере.");

        string key = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey;
        await using var connection = _db.Open();
        var existing = await ReadOrderByKeyAsync(connection, userId, key);
        if (existing is not null) return existing;

        string id = Guid.NewGuid().ToString("N");
        string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        int amount = plan?.PriceRub ?? addon!.PriceRub;
        string kind = plan is not null ? "plan" : "addon";
        string name = plan?.Name ?? addon!.Name;
        string provider = method == "test" ? "test" : "internal";
        string status = method == "manual" ? "awaiting_confirm" : "paid";
        string? confirmation = method == "manual" ? $"/pay/manual/{id}" : null;

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            if (method == "promo")
            {
                if (string.IsNullOrWhiteSpace(promoCode)) throw new StoreException("Введи промокод.");
                await ApplyPromoAsync(connection, tx, userId, id, promoCode, productId, now);
            }
            else if (method == "balance")
            {
                int balance = await ReadBalanceAsync(connection, tx, userId);
                if (balance < amount * 100) throw new StoreException("На балансе недостаточно средств.");
                await ChangeBalanceAsync(connection, tx, userId, -amount * 100, "bonus_spend", id, "Покупка " + name, now);
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO Orders
                      (Id, UserId, ProductId, ProductKind, AmountMinor, Currency, Provider, Method,
                       Status, ConfirmationUrl, IdempotencyKey, TrackId, CreatedAt, PaidAt)
                    VALUES
                      ($id, $user, $product, $kind, $amount, 'RUB', $provider, $method,
                       $status, $confirm, $key, $track, $created, $paid)
                    """;
                insert.Parameters.AddWithValue("$id", id);
                insert.Parameters.AddWithValue("$user", userId);
                insert.Parameters.AddWithValue("$product", productId);
                insert.Parameters.AddWithValue("$kind", kind);
                insert.Parameters.AddWithValue("$amount", amount * 100);
                insert.Parameters.AddWithValue("$provider", provider);
                insert.Parameters.AddWithValue("$method", method);
                insert.Parameters.AddWithValue("$status", status);
                insert.Parameters.AddWithValue("$confirm", (object?)confirmation ?? DBNull.Value);
                insert.Parameters.AddWithValue("$key", key);
                insert.Parameters.AddWithValue("$track", (object?)trackId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$created", now);
                insert.Parameters.AddWithValue("$paid", status == "paid" ? now : (object?)DBNull.Value);
                await insert.ExecuteNonQueryAsync();
            }

            var order = new StoreOrderDto
            {
                Id = id, UserId = userId, ProductId = productId, ProductKind = kind, ProductName = name,
                AmountMinor = amount * 100, Provider = provider, Method = method, Status = status,
                ConfirmationUrl = confirmation, TrackId = trackId, CreatedAt = now,
                PaidAt = status == "paid" ? now : null
            };

            if (status == "paid") await GrantAsync(connection, tx, order, method);
            await RecomputeTariffAsync(connection, tx, userId);
            await tx.CommitAsync();

            if (method == "manual")
            {
                try
                {
                    await _mail.SendAdminAsync("Murzila-Music — новый заказ " + id,
                        $"Заказ: {id}\nПользователь: {userId}\nТовар: {name} ({amount} ₽)\n" +
                        $"Подтвердить: POST /api/admin/orders/{id}/decide с X-Admin-Token.");
                }
                catch { /* заказ остаётся в базе, админ видит его в списке */ }
            }
            return order;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<StoreOrderDto?> GetOrderAsync(string userId, string orderId)
    {
        await using var connection = _db.Open();
        var order = await ReadOrderAsync(connection, orderId);
        return order is not null && order.UserId == userId ? order : null;
    }

    public async Task<List<StoreOrderDto>> GetOrdersAsync(string? status = null)
    {
        await using var connection = _db.Open();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, UserId, ProductId, ProductKind, AmountMinor, Provider, Method, Status,
                   ConfirmationUrl, TrackId, CreatedAt, PaidAt
            FROM Orders
            """;
        if (!string.IsNullOrWhiteSpace(status))
        {
            cmd.CommandText += " WHERE Status=$status";
            cmd.Parameters.AddWithValue("$status", status);
        }
        cmd.CommandText += " ORDER BY CreatedAt DESC LIMIT 200";
        var result = new List<StoreOrderDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(ReadOrder(reader));
        return result;
    }

    public async Task<StoreOrderDto?> ConfirmOrderAsync(string orderId, string actor, bool approve, string? note)
    {
        await using var connection = _db.Open();
        var order = await ReadOrderAsync(connection, orderId);
        if (order is null || order.Status != "awaiting_confirm") return null;
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = "UPDATE Orders SET Status=$status, PaidAt=$paid WHERE Id=$id";
                update.Parameters.AddWithValue("$status", approve ? "paid" : "canceled");
                update.Parameters.AddWithValue("$paid", approve ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) : (object?)DBNull.Value);
                update.Parameters.AddWithValue("$id", orderId);
                await update.ExecuteNonQueryAsync();
            }
            if (approve)
            {
                order.Status = "paid";
                await GrantAsync(connection, tx, order, "manual");
            }
            await RecomputeTariffAsync(connection, tx, order.UserId);
            await AuditAsync(connection, tx, actor, approve ? "order.approve" : "order.reject", orderId, note);
            await tx.CommitAsync();
            return order;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<AccountStateDto> GetAccountStateAsync(string userId)
    {
        await using var connection = _db.Open();
        var user = await ReadUserAsync(connection, userId) ?? new UserDto { Id = userId };

        // Владелец проекта получает максимум бесплатно: 48 функций, без рекламы.
        bool isOwner = string.Equals(user.Email, "tprimeluc@gmail.com", StringComparison.OrdinalIgnoreCase);
        var plan = isOwner
            ? Catalog.Plans.OrderByDescending(p => p.FeatureCount).First()
            : Catalog.PlanForLegacyTariff(user.Tariff);
        if (isOwner) user.Tariff = plan.LegacyTariff;

        var state = new AccountStateDto
        {
            User = user,
            ActivePlanId = plan.Id,
            AdsLevel = isOwner ? "none" : plan.AdsLevel,
            Features = plan.Features.ToList(),
            BalanceMinor = await ReadBalanceAsync(connection, null, userId)
        };

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id, ProductId, ProductKind, Source, Status, StartsAt, ExpiresAt
                FROM Entitlements WHERE UserId=$user ORDER BY CreatedAt DESC
                """;
            cmd.Parameters.AddWithValue("$user", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                state.Entitlements.Add(new EntitlementDto
                {
                    Id = reader.GetString(0), ProductId = reader.GetString(1),
                    ProductKind = reader.GetString(2), Source = reader.GetString(3),
                    Status = reader.GetString(4), StartsAt = reader.GetString(5),
                    ExpiresAt = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
        }

        foreach (var order in await ReadOrdersAsync(connection, userId)) state.Orders.Add(order);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id, OrderId, AmountMinor, ReasonCode, Comment, Status, RequestedAt, DecisionNote, CompletedAt
                FROM Refunds WHERE UserId=$user ORDER BY RequestedAt DESC
                """;
            cmd.Parameters.AddWithValue("$user", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                state.Refunds.Add(new RefundDto
                {
                    Id = reader.GetString(0), OrderId = reader.GetString(1), AmountMinor = reader.GetInt32(2),
                    ReasonCode = reader.GetString(3), Comment = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Status = reader.GetString(5), RequestedAt = reader.GetString(6),
                    DecisionNote = reader.IsDBNull(7) ? null : reader.GetString(7),
                    CompletedAt = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT TrackId FROM TrackUnlocks WHERE UserId=$user";
            cmd.Parameters.AddWithValue("$user", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) state.UnlockedTrackIds.Add(reader.GetString(0));
        }
        return state;
    }

    public async Task<RestoreResultDto> RestoreAsync(string userId)
    {
        int restored = 0;
        await using var connection = _db.Open();
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, UserId, ProductId, ProductKind, AmountMinor, Provider, Method, Status,
                           ConfirmationUrl, TrackId, CreatedAt, PaidAt
                    FROM Orders WHERE UserId=$user AND Status IN ('paid','credited')
                    """;
                cmd.Parameters.AddWithValue("$user", userId);
                var orders = new List<StoreOrderDto>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) orders.Add(ReadOrder(reader));
                await reader.CloseAsync();
                foreach (var order in orders)
                {
                    int before = await CountEntitlementsAsync(connection, tx, userId);
                    await GrantAsync(connection, tx, order, "restore");
                    if (await CountEntitlementsAsync(connection, tx, userId) > before) restored++;
                }
            }

            await using (var legacy = connection.CreateCommand())
            {
                legacy.CommandText = """
                    SELECT Id, Tariff, TrackId FROM Payments
                    WHERE UserId=$user AND Status='succeeded'
                    """;
                legacy.Parameters.AddWithValue("$user", userId);
                var payments = new List<(string Id, int Tariff, string? TrackId)>();
                await using var reader = await legacy.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    payments.Add((reader.GetString(0), reader.GetInt32(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2)));
                await reader.CloseAsync();

                foreach (var payment in payments)
                {
                    string productId = payment.Tariff == 5 ? "single5" : Catalog.PlanForLegacyTariff(payment.Tariff).Id;
                    var order = new StoreOrderDto
                    {
                        Id = "legacy-" + payment.Id, UserId = userId, ProductId = productId,
                        ProductKind = payment.Tariff == 5 ? "addon" : "plan",
                        Provider = "legacy", Method = "restore", Status = "paid", TrackId = payment.TrackId
                    };
                    int before = await CountEntitlementsAsync(connection, tx, userId);
                    await GrantAsync(connection, tx, order, "restore");
                    if (await CountEntitlementsAsync(connection, tx, userId) > before) restored++;
                }
            }

            await RecomputeTariffAsync(connection, tx, userId);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        var state = await GetAccountStateAsync(userId);
        return new RestoreResultDto
        {
            Restored = restored,
            ActivePlanId = state.ActivePlanId,
            BalanceMinor = state.BalanceMinor,
            Message = restored > 0
                ? $"Восстановлено доступов: {restored}. Активный план: {Catalog.FindPlan(state.ActivePlanId)?.Name ?? state.ActivePlanId}."
                : "Все покупки уже на месте. Ничего нового не найдено."
        };
    }

    public async Task<RefundDto> RequestRefundAsync(string userId, string orderId, string reasonCode, string? comment)
    {
        await using var connection = _db.Open();
        var order = await ReadOrderAsync(connection, orderId);
        if (order is null || order.UserId != userId) throw new StoreException("Заказ не найден.");
        if (order.Status != "paid") throw new StoreException("Этот заказ нельзя вернуть.");

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT Id FROM Refunds WHERE OrderId=$id";
            check.Parameters.AddWithValue("$id", orderId);
            if (await check.ExecuteScalarAsync() is not null) throw new StoreException("Возврат по этому заказу уже есть.");
        }

        string id = Guid.NewGuid().ToString("N");
        string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        bool auto = SelfRefund || order.Method is "promo" or "balance" or "test" or "manual";
        var refund = new RefundDto
        {
            Id = id, OrderId = orderId, AmountMinor = order.AmountMinor, ReasonCode = reasonCode,
            Comment = comment, Status = auto ? "completed" : "requested", RequestedAt = now,
            CompletedAt = auto ? now : null
        };

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO Refunds (Id, OrderId, UserId, AmountMinor, ReasonCode, Comment, Status, RequestedAt, CompletedAt)
                    VALUES ($id, $order, $user, $amount, $reason, $comment, $status, $created, $completed)
                    """;
                insert.Parameters.AddWithValue("$id", id);
                insert.Parameters.AddWithValue("$order", orderId);
                insert.Parameters.AddWithValue("$user", userId);
                insert.Parameters.AddWithValue("$amount", order.AmountMinor);
                insert.Parameters.AddWithValue("$reason", reasonCode);
                insert.Parameters.AddWithValue("$comment", (object?)comment ?? DBNull.Value);
                insert.Parameters.AddWithValue("$status", refund.Status);
                insert.Parameters.AddWithValue("$created", now);
                insert.Parameters.AddWithValue("$completed", auto ? now : (object?)DBNull.Value);
                await insert.ExecuteNonQueryAsync();
            }

            if (auto)
            {
                await CompleteRefundAsync(connection, tx, refund, order.UserId, "self");
            }
            else
            {
                try
                {
                    await _mail.SendAdminAsync("Murzila-Music — запрос на возврат " + id,
                        $"Заказ: {orderId}\nПользователь: {userId}\nПричина: {reasonCode}\nКомментарий: {comment}");
                }
                catch { }
            }
            await RecomputeTariffAsync(connection, tx, order.UserId);
            await tx.CommitAsync();
            return refund;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<RefundDto?> DecideRefundAsync(string refundId, string actor, bool approve, string? note)
    {
        await using var connection = _db.Open();
        var refund = await ReadRefundAsync(connection, refundId);
        if (refund is null || refund.Status != "requested") return null;
        var order = await ReadOrderAsync(connection, refund.OrderId);
        if (order is null) return null;

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            if (approve)
            {
                refund.Status = "completed";
                refund.CompletedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                refund.DecisionNote = note;
                await using (var update = connection.CreateCommand())
                {
                    update.Transaction = tx;
                    update.CommandText = "UPDATE Refunds SET Status='completed', DecisionNote=$note, CompletedAt=$at WHERE Id=$id";
                    update.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
                    update.Parameters.AddWithValue("$at", refund.CompletedAt);
                    update.Parameters.AddWithValue("$id", refundId);
                    await update.ExecuteNonQueryAsync();
                }
                await CompleteRefundAsync(connection, tx, refund, order.UserId, actor);
            }
            else
            {
                await using (var update = connection.CreateCommand())
                {
                    update.Transaction = tx;
                    update.CommandText = "UPDATE Refunds SET Status='rejected', DecisionNote=$note WHERE Id=$id";
                    update.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
                    update.Parameters.AddWithValue("$id", refundId);
                    await update.ExecuteNonQueryAsync();
                }
                refund.Status = "rejected";
                refund.DecisionNote = note;
            }
            await RecomputeTariffAsync(connection, tx, order.UserId);
            await AuditAsync(connection, tx, actor, approve ? "refund.approve" : "refund.reject", refundId, note);
            await tx.CommitAsync();
            return refund;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<PromoResultDto> CreatePromoAsync(string productId, int maxUses, string? expiresAt,
        string? note, string actor)
    {
        if (Catalog.FindPlan(productId) is null && Catalog.FindAddon(productId) is null)
            throw new StoreException("Неизвестный товар.");
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code = "MURZILA-" + new string(Enumerable.Range(0, 8)
            .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.ToUpperInvariant())));
        string hint = code[..8] + "****";
        string id = Guid.NewGuid().ToString("N");
        await using var connection = _db.Open();
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO PromoCodes (Id, CodeHash, CodeHint, ProductId, MaxUses, ExpiresAt, CreatedBy, CreatedAt, Note)
                    VALUES ($id, $hash, $hint, $product, $uses, $expires, $actor, $created, $note)
                    """;
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$hash", hash);
                cmd.Parameters.AddWithValue("$hint", hint);
                cmd.Parameters.AddWithValue("$product", productId);
                cmd.Parameters.AddWithValue("$uses", Math.Max(1, maxUses));
                cmd.Parameters.AddWithValue("$expires", (object?)expiresAt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$actor", actor);
                cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            await AuditAsync(connection, tx, actor, "promo.create", id, productId);
            await tx.CommitAsync();
            return new PromoResultDto { Code = code, CodeHint = hint, ProductId = productId, MaxUses = maxUses, ExpiresAt = expiresAt };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task GrantBalanceAsync(string userId, int amountMinor, string reason, string actor)
    {
        await using var connection = _db.Open();
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            await ChangeBalanceAsync(connection, tx, userId, amountMinor, "admin_grant", Guid.NewGuid().ToString("N"), reason,
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await AuditAsync(connection, tx, actor, "balance.grant", userId, $"{amountMinor/100.0:0.00} ₽: {reason}");
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ---------- внутренние операции ----------

    private async Task ApplyPromoAsync(SqliteConnection connection, SqliteTransaction tx,
        string userId, string orderId, string promoCode, string productId, string now)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(promoCode.Trim().ToUpperInvariant())));
        string? codeId = null, hint = null, product = null, expires = null;
        int maxUses = 0, used = 0, active = 0;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT Id, CodeHint, ProductId, MaxUses, UsedCount, ExpiresAt, IsActive
                FROM PromoCodes WHERE CodeHash=$hash
                """;
            cmd.Parameters.AddWithValue("$hash", hash);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                codeId = reader.GetString(0);
                hint = reader.GetString(1);
                product = reader.GetString(2);
                maxUses = reader.GetInt32(3);
                used = reader.GetInt32(4);
                expires = reader.IsDBNull(5) ? null : reader.GetString(5);
                active = reader.GetInt32(6);
            }
        }
        if (codeId is null || active == 0) throw new StoreException("Промокод не найден.");
        if (!string.Equals(product, productId, StringComparison.OrdinalIgnoreCase))
            throw new StoreException($"Промокод {hint} не подходит для этого тарифа.");
        if (used >= maxUses) throw new StoreException("Промокод уже использован.");
        if (!string.IsNullOrEmpty(expires) && DateTime.Parse(expires, CultureInfo.InvariantCulture) < DateTime.UtcNow)
            throw new StoreException("Срок действия промокода истёк.");

        await using (var usedCmd = connection.CreateCommand())
        {
            usedCmd.Transaction = tx;
            usedCmd.CommandText = "SELECT 1 FROM PromoRedemptions WHERE CodeId=$code AND UserId=$user";
            usedCmd.Parameters.AddWithValue("$code", codeId);
            usedCmd.Parameters.AddWithValue("$user", userId);
            if (await usedCmd.ExecuteScalarAsync() is not null)
                throw new StoreException("Ты уже использовал этот промокод.");
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO PromoRedemptions (Id, CodeId, UserId, OrderId, CreatedAt)
                VALUES ($id, $code, $user, $order, $created)
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$code", codeId);
            insert.Parameters.AddWithValue("$user", userId);
            insert.Parameters.AddWithValue("$order", orderId);
            insert.Parameters.AddWithValue("$created", now);
            await insert.ExecuteNonQueryAsync();
        }
        await using (var bump = connection.CreateCommand())
        {
            bump.Transaction = tx;
            bump.CommandText = "UPDATE PromoCodes SET UsedCount=UsedCount+1 WHERE Id=$id";
            bump.Parameters.AddWithValue("$id", codeId);
            await bump.ExecuteNonQueryAsync();
        }
    }

    private async Task GrantAsync(SqliteConnection connection, SqliteTransaction tx, StoreOrderDto order, string source)
    {
        string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var plan = Catalog.FindPlan(order.ProductId);
        string? expires = null;
        if (plan?.DurationDays is int days) expires = DateTime.UtcNow.AddDays(days).ToString("O", CultureInfo.InvariantCulture);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR IGNORE INTO Entitlements
                  (Id, UserId, ProductId, ProductKind, Source, OrderId, Provider, ProviderRef,
                   StartsAt, ExpiresAt, Status, CreatedAt)
                VALUES
                  ($id, $user, $product, $kind, $source, $order, $provider, $ref,
                   $starts, $expires, 'active', $created)
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$user", order.UserId);
            cmd.Parameters.AddWithValue("$product", order.ProductId);
            cmd.Parameters.AddWithValue("$kind", order.ProductKind);
            cmd.Parameters.AddWithValue("$source", source);
            cmd.Parameters.AddWithValue("$order", order.Id);
            cmd.Parameters.AddWithValue("$provider", order.Provider);
            cmd.Parameters.AddWithValue("$ref", order.Id);
            cmd.Parameters.AddWithValue("$starts", now);
            cmd.Parameters.AddWithValue("$expires", (object?)expires ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", now);
            await cmd.ExecuteNonQueryAsync();
        }

        if (order.ProductKind == "addon" && !string.IsNullOrWhiteSpace(order.TrackId))
        {
            await using var unlock = connection.CreateCommand();
            unlock.Transaction = tx;
            unlock.CommandText = """
                INSERT OR IGNORE INTO TrackUnlocks (UserId, TrackId, OrderId, CreatedAt)
                VALUES ($user, $track, $order, $created)
                """;
            unlock.Parameters.AddWithValue("$user", order.UserId);
            unlock.Parameters.AddWithValue("$track", order.TrackId);
            unlock.Parameters.AddWithValue("$order", order.Id);
            unlock.Parameters.AddWithValue("$created", now);
            await unlock.ExecuteNonQueryAsync();
        }
    }

    private async Task CompleteRefundAsync(SqliteConnection connection, SqliteTransaction tx,
        RefundDto refund, string userId, string actor)
    {
        var order = await ReadOrderAsync(connection, refund.OrderId) ?? throw new StoreException("Заказ не найден.");
        await using (var revoke = connection.CreateCommand())
        {
            revoke.Transaction = tx;
            revoke.CommandText = """
                UPDATE Entitlements SET Status='revoked', RevokedAt=$at
                WHERE OrderId=$order AND Status='active'
                """;
            revoke.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            revoke.Parameters.AddWithValue("$order", order.Id);
            await revoke.ExecuteNonQueryAsync();
        }
        if (order.ProductKind == "addon" && !string.IsNullOrWhiteSpace(order.TrackId))
        {
            await using var unlock = connection.CreateCommand();
            unlock.Transaction = tx;
            unlock.CommandText = "DELETE FROM TrackUnlocks WHERE UserId=$user AND TrackId=$track AND OrderId=$order";
            unlock.Parameters.AddWithValue("$user", userId);
            unlock.Parameters.AddWithValue("$track", order.TrackId);
            unlock.Parameters.AddWithValue("$order", order.Id);
            await unlock.ExecuteNonQueryAsync();
        }
        await using (var paid = connection.CreateCommand())
        {
            paid.Transaction = tx;
            paid.CommandText = "UPDATE Orders SET Status='credited' WHERE Id=$id";
            paid.Parameters.AddWithValue("$id", order.Id);
            await paid.ExecuteNonQueryAsync();
        }
        await ChangeBalanceAsync(connection, tx, userId, refund.AmountMinor, "refund", refund.Id,
            "Возврат: " + refund.ReasonCode, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await AuditAsync(connection, tx, actor, "refund.complete", refund.Id, refund.ReasonCode);
    }

    private async Task RecomputeTariffAsync(SqliteConnection connection, SqliteTransaction? tx, string userId)
    {
        int tariff = 0;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT ProductId, ExpiresAt FROM Entitlements
                WHERE UserId=$user AND Status='active' AND ProductKind='plan'
                """;
            cmd.Parameters.AddWithValue("$user", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string? expires = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (!string.IsNullOrEmpty(expires) && DateTime.Parse(expires, CultureInfo.InvariantCulture) < DateTime.UtcNow)
                    continue;
                tariff = Math.Max(tariff, Catalog.FindPlan(reader.GetString(0))?.LegacyTariff ?? 0);
            }
        }
        await using (var legacy = connection.CreateCommand())
        {
            legacy.Transaction = tx;
            legacy.CommandText = "SELECT Tariff FROM Payments WHERE UserId=$user AND Status='succeeded'";
            legacy.Parameters.AddWithValue("$user", userId);
            await using var reader = await legacy.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tariff = Math.Max(tariff, reader.GetInt32(0));
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = "UPDATE Users SET Tariff=$tariff WHERE Id=$user";
            update.Parameters.AddWithValue("$tariff", tariff);
            update.Parameters.AddWithValue("$user", userId);
            await update.ExecuteNonQueryAsync();
        }
    }

    private static async Task<int> CountEntitlementsAsync(SqliteConnection connection, SqliteTransaction tx, string userId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM Entitlements WHERE UserId=$user AND Status='active'";
        cmd.Parameters.AddWithValue("$user", userId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> ReadBalanceAsync(SqliteConnection connection, SqliteTransaction? tx, string userId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT AmountMinor FROM Balances WHERE UserId=$user";
        cmd.Parameters.AddWithValue("$user", userId);
        object? value = await cmd.ExecuteScalarAsync();
        return value is null ? 0 : Convert.ToInt32(value);
    }

    private static async Task ChangeBalanceAsync(SqliteConnection connection, SqliteTransaction tx,
        string userId, int delta, string reason, string refId, string description, string now)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT OR IGNORE INTO BalanceLedger (Id, UserId, DeltaMinor, Reason, RefId, Description, CreatedAt)
                VALUES ($id, $user, $delta, $reason, $ref, $desc, $created)
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$user", userId);
            insert.Parameters.AddWithValue("$delta", delta);
            insert.Parameters.AddWithValue("$reason", reason);
            insert.Parameters.AddWithValue("$ref", refId);
            insert.Parameters.AddWithValue("$desc", description);
            insert.Parameters.AddWithValue("$created", now);
            await insert.ExecuteNonQueryAsync();
        }
        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO Balances (UserId, AmountMinor, UpdatedAt) VALUES ($user, $delta, $now)
                ON CONFLICT(UserId) DO UPDATE SET AmountMinor=AmountMinor+excluded.AmountMinor, UpdatedAt=excluded.UpdatedAt
                """;
            upsert.Parameters.AddWithValue("$user", userId);
            upsert.Parameters.AddWithValue("$delta", delta);
            upsert.Parameters.AddWithValue("$now", now);
            await upsert.ExecuteNonQueryAsync();
        }
    }

    private static async Task AuditAsync(SqliteConnection connection, SqliteTransaction tx,
        string actor, string action, string? target, string? payload)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO AdminAudit (Id, Actor, Action, Target, PayloadJson, CreatedAt)
            VALUES ($id, $actor, $action, $target, $payload, $created)
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$actor", actor);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$target", (object?)target ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload", (object?)payload ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<StoreOrderDto?> ReadOrderByKeyAsync(SqliteConnection connection, string userId, string key)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, UserId, ProductId, ProductKind, AmountMinor, Provider, Method, Status,
                   ConfirmationUrl, TrackId, CreatedAt, PaidAt
            FROM Orders WHERE UserId=$user AND IdempotencyKey=$key
            """;
        cmd.Parameters.AddWithValue("$user", userId);
        cmd.Parameters.AddWithValue("$key", key);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadOrder(reader) : null;
    }

    private async Task<StoreOrderDto?> ReadOrderAsync(SqliteConnection connection, string id)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, UserId, ProductId, ProductKind, AmountMinor, Provider, Method, Status,
                   ConfirmationUrl, TrackId, CreatedAt, PaidAt
            FROM Orders WHERE Id=$id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadOrder(reader) : null;
    }

    private StoreOrderDto ReadOrder(SqliteDataReader reader)
    {
        string productId = reader.GetString(2);
        return new StoreOrderDto
        {
            Id = reader.GetString(0), UserId = reader.GetString(1), ProductId = productId,
            ProductKind = reader.GetString(3), AmountMinor = reader.GetInt32(4),
            Provider = reader.GetString(5), Method = reader.GetString(6), Status = reader.GetString(7),
            ConfirmationUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
            TrackId = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAt = reader.GetString(10), PaidAt = reader.IsDBNull(11) ? null : reader.GetString(11),
            ProductName = Catalog.FindPlan(productId)?.Name ?? Catalog.FindAddon(productId)?.Name ?? productId
        };
    }

    private async Task<List<StoreOrderDto>> ReadOrdersAsync(SqliteConnection connection, string userId)
    {
        var result = new List<StoreOrderDto>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, UserId, ProductId, ProductKind, AmountMinor, Provider, Method, Status,
                   ConfirmationUrl, TrackId, CreatedAt, PaidAt
            FROM Orders WHERE UserId=$user ORDER BY CreatedAt DESC LIMIT 100
            """;
        cmd.Parameters.AddWithValue("$user", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(ReadOrder(reader));
        return result;
    }

    private static async Task<RefundDto?> ReadRefundAsync(SqliteConnection connection, string id)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OrderId, AmountMinor, ReasonCode, Comment, Status, RequestedAt, DecisionNote, CompletedAt, UserId
            FROM Refunds WHERE Id=$id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new RefundDto
        {
            Id = reader.GetString(0), OrderId = reader.GetString(1), AmountMinor = reader.GetInt32(2),
            ReasonCode = reader.GetString(3), Comment = reader.IsDBNull(4) ? null : reader.GetString(4),
            Status = reader.GetString(5), RequestedAt = reader.GetString(6),
            DecisionNote = reader.IsDBNull(7) ? null : reader.GetString(7),
            CompletedAt = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }

    private static async Task<UserDto?> ReadUserAsync(SqliteConnection connection, string id)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Email, Tariff FROM Users WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new UserDto
        {
            Id = reader.GetString(0), Name = reader.GetString(1),
            Email = reader.GetString(2), Tariff = reader.GetInt32(3)
        };
    }
}
