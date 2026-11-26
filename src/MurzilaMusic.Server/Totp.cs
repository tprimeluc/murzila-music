using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MurzilaMusic.Server;

/// <summary>TOTP (RFC 6238) для Google/Microsoft Authenticator: 6 цифр, 30 секунд.</summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret(int bytes = 20)
    {
        return EncodeBase32(RandomNumberGenerator.GetBytes(bytes));
    }

    public static string OtpAuthUri(string email, string secret)
    {
        string label = Uri.EscapeDataString("Murzila Music:" + email);
        string issuer = Uri.EscapeDataString("Murzila Music");
        return $"otpauth://totp/{label}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
    }

    public static bool Verify(string secret, string code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || code.Length != 6) return false;
        long step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        for (int i = -window; i <= window; i++)
        {
            if (FixedEquals(Generate(secret, step + i), code)) return true;
        }
        return false;
    }

    private static string Generate(string secret, long step)
    {
        byte[] key = DecodeBase32(secret);
        byte[] counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        using var hmac = new HMACSHA1(key);
        byte[] hash = hmac.ComputeHash(counter);
        int offset = hash[^1] & 0x0f;
        int binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static bool FixedEquals(string a, string b)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    private static string EncodeBase32(byte[] bytes)
    {
        int bits = 0, value = 0;
        var result = new StringBuilder();
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(Alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) result.Append(Alphabet[(value << (5 - bits)) & 31]);
        return result.ToString();
    }

    private static byte[] DecodeBase32(string text)
    {
        var result = new List<byte>();
        int bits = 0, value = 0;
        foreach (char c in text.Trim().ToUpperInvariant())
        {
            int index = Alphabet.IndexOf(c);
            if (index < 0) continue;
            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }
        return result.ToArray();
    }
}
