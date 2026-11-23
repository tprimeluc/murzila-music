using System.Security.Cryptography;
using System.Text.Json;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App.Services;

public sealed class AccountRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Salt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class AccountStore
{
    public List<AccountRecord> Accounts { get; set; } = new();
}

/// <summary>
/// Резервные локальные аккаунты для случая, когда сервер не запущен.
/// Пароли не хранятся в открытом виде: PBKDF2-SHA256, 120 000 итераций, случайная соль.
/// Основной путь — реальная регистрация через ApiClient на локальном сервере.
/// </summary>
public sealed class AccountService
{
    public static AccountService Default { get; } = new(Path.Combine(AppPaths.Root, "accounts.json"));

    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private readonly string _filePath;

    public AccountService(string filePath)
    {
        _filePath = filePath;
        AppPaths.Ensure();
    }

    public bool Register(string name, string email, string password, out string error)
    {
        bool ok = Register(name, email, password, out error, out var account);
        if (ok && account is not null) SignIn(account);
        return ok;
    }

    public bool Register(string name, string email, string password, out string error, out AccountRecord? account)
    {
        account = null;
        error = "";

        name = name.Trim();
        email = NormalizeEmail(email);

        if (name.Length < 2)
        {
            error = "Имя должно быть не короче 2 символов.";
            return false;
        }
        if (!email.Contains('@') || !email.Contains('.'))
        {
            error = Txt.InvalidEmail;
            return false;
        }
        if (password.Length < 6)
        {
            error = "Пароль должен быть не короче 6 символов.";
            return false;
        }

        var store = Load();
        if (store.Accounts.Any(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            error = Txt.AccountExists;
            return false;
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        var record = new AccountRecord
        {
            Name = name,
            Email = email,
            Salt = Convert.ToBase64String(salt),
            PasswordHash = HashPassword(password, salt)
        };
        store.Accounts.Add(record);
        Save(store);
        account = record;
        return true;
    }

    public bool Login(string email, string password, out string error)
    {
        bool ok = Login(email, password, out error, out var account);
        if (ok && account is not null) SignIn(account);
        return ok;
    }

    public bool Login(string email, string password, out string error, out AccountRecord? account)
    {
        account = null;
        error = "";
        email = NormalizeEmail(email);
        var store = Load();
        var record = store.Accounts.FirstOrDefault(a =>
            string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));

        if (record is null)
        {
            error = Txt.AccountWrong;
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(record.Salt);
            string hash = HashPassword(password, salt);
            byte[] expected = Convert.FromBase64String(record.PasswordHash);
            byte[] actual = Convert.FromBase64String(hash);
            if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                error = Txt.AccountWrong;
                return false;
            }
        }
        catch
        {
            error = Txt.AccountWrong;
            return false;
        }

        account = record;
        return true;
    }

    public void SignIn(AccountRecord account)
    {
        SettingsService.Current.LoggedIn = true;
        SettingsService.Current.DisplayName = account.Name;
        SettingsService.Current.AccountEmail = account.Email;
        SettingsService.Save();
    }

    public static void Logout()
    {
        SettingsService.Current.LoggedIn = false;
        SettingsService.Current.DisplayName = Txt.Guest;
        SettingsService.Current.AccountEmail = "";
        SettingsService.Current.AccessToken = "";
        SettingsService.Current.AccountId = "";
        SettingsService.Current.ServerMode = false;
        SettingsService.Current.Tariff = Tariff.Free;
        SettingsService.Save();
    }

    public static string HashPassword(string password, byte[] salt)
    {
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return Convert.ToBase64String(hash);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private AccountStore Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var store = JsonSerializer.Deserialize<AccountStore>(File.ReadAllText(_filePath));
                if (store is not null) return store;
            }
        }
        catch
        {
            // Повреждённый файл аккаунтов не должен ронять приложение.
        }
        return new AccountStore();
    }

    private void Save(AccountStore store)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Ошибка записи покажется при следующем входе; приложение продолжает работать.
        }
    }
}
