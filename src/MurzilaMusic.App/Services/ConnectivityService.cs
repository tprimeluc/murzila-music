using System.Net;
using System.Net.Http;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Проверка сети перед запуском. Раньше проверка опиралась только на lrclib: если сервис текстов
/// медленно отвечал или был заблокирован, приложение ошибочно показывало
/// «нет сети» при работающем интернете. Теперь сначала проверяется DNS,
/// затем — несколько коротких HTTP-проб. Любой успех считается наличием сети.
/// Код ошибки показывается пользователю, только если всё недоступно.
/// </summary>
public static class ConnectivityService
{
    private static readonly string[] DnsHosts = { "lrclib.net", "microsoft.com", "dns.google" };

    private static readonly string[] HttpUrls =
    {
        "https://lrclib.net/api/search?track_name=a&artist_name=b&duration=1",
        "https://www.microsoft.com/",
        "https://www.google.com/generate_204"
    };

    public static async Task<(bool Ok, string Code)> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));

        // DNS — самый быстрый и надёжный признак того, что сеть физически есть.
        // Ограничиваем каждый запрос отдельно: системный резолвер иногда висит десятки секунд,
        // и заставка не должна из-за этого застревать.
        foreach (string host in DnsHosts)
        {
            try
            {
                var dnsTask = Dns.GetHostAddressesAsync(host, timeout.Token);
                var delayTask = Task.Delay(1200, timeout.Token);
                if (await Task.WhenAny(dnsTask, delayTask) != dnsTask)
                {
                    _ = dnsTask.ContinueWith(t => _ = t.Exception,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                    continue;
                }
                var addresses = await dnsTask;
                if (addresses.Length > 0) return (true, "");
            }
            catch
            {
                // пробуем следующий хост
            }
        }

        // Если DNS недоступен (например, только прокси), пробуем прямой HTTPS.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        string firstCode = "ERR_TIMEOUT_408";

        foreach (string url in HttpUrls)
        {
            try
            {
                using var response = await client.GetAsync(url, timeout.Token);
                if (response.IsSuccessStatusCode) return (true, "");
                if (firstCode == "ERR_TIMEOUT_408")
                    firstCode = $"ERR_HTTP_{(int)response.StatusCode}";
            }
            catch (OperationCanceledException)
            {
                // следующая проба
            }
            catch (HttpRequestException ex)
            {
                if (firstCode == "ERR_TIMEOUT_408")
                {
                    int code = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : (ex.HResult & 0xFFFF);
                    firstCode = $"ERR_NET_{code:X4}";
                }
            }
            catch (Exception ex)
            {
                if (firstCode == "ERR_TIMEOUT_408")
                    firstCode = $"ERR_{ex.HResult & 0xFFFF:X4}";
            }
        }

        return (false, firstCode);
    }
}
