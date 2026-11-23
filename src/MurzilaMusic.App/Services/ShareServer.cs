using System.Net;
using System.Net.Sockets;
using System.Text;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;

/// <summary>
/// Локальная публикация собственных треков: ссылка открывается в браузере
/// на этом же ПК или в локальной сети. Никакой загрузки в облако нет.
/// </summary>
public sealed class ShareServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly LibraryService _library;

    public bool IsRunning => _listener?.IsListening ?? false;
    public int Port { get; private set; }
    private bool _localOnly;
    public bool IsLocalOnly => _localOnly;

    public ShareServer(LibraryService library) => _library = library;

    public async Task<bool> StartAsync(int preferredPort)
    {
        Stop();
        Port = preferredPort;
        string[] prefixes = { $"http://+:{Port}/", $"http://localhost:{Port}/" };

        foreach (var prefix in prefixes)
        {
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                listener.Start();
                _listener = listener;
                _localOnly = prefix.Contains("localhost", StringComparison.OrdinalIgnoreCase);
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => LoopAsync(_cts.Token));
                return true;
            }
            catch (HttpListenerException)
            {
                // Для доступа из сети нужно разрешение URL ACL (см. README).
            }
            catch (Exception)
            {
                // пробуем следующий префикс
            }
        }
        return false;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        try { _listener?.Stop(); } catch { }
        _listener?.Close();
        _listener = null;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is not null)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { break; }

            _ = Task.Run(() => Handle(context));
        }
    }

    private void Handle(HttpListenerContext context)
    {
        try
        {
            string path = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/");
            if (path == "/" || path == "/index.html")
            {
                WriteHtml(context, ListPage());
                return;
            }

            if (path.StartsWith("/t/"))
            {
                string id = path.Substring(3).Trim('/');
                var track = _library.Find(id);
                if (track is null || !track.IsPublished) { Write404(context); return; }
                WriteHtml(context, PlayerPage(track));
                return;
            }

            if (path.StartsWith("/file/"))
            {
                string id = path.Substring(6).Trim('/');
                var track = _library.Find(id);
                if (track is null || !track.IsPublished || !File.Exists(track.FileName)) { Write404(context); return; }
                var info = new FileInfo(track.FileName);
                context.Response.ContentType = Mime(track.FileName);
                context.Response.ContentLength64 = info.Length;
                using var stream = info.OpenRead();
                stream.CopyTo(context.Response.OutputStream);
                return;
            }

            Write404(context);
        }
        catch
        {
            try { context.Response.StatusCode = 500; context.Response.Close(); } catch { }
        }
    }

    public string UrlFor(TrackItem track)
    {
        if (_localOnly) return $"http://localhost:{Port}/t/{track.Id}";
        string host = "localhost";
        try
        {
            var address = Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            if (address is not null) host = address.ToString();
        }
        catch { }
        return $"http://{host}:{Port}/t/{track.Id}";
    }

    public string LocalUrl => $"http://localhost:{Port}/";

    private static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" or ".aac" => "audio/mp4",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".wma" => "audio/x-ms-wma",
        _ => "application/octet-stream"
    };

    private static void WriteHtml(HttpListenerContext context, string html)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    private static void Write404(HttpListenerContext context)
    {
        context.Response.StatusCode = 404;
        WriteHtml(context, "<h1 style='color:white'>404 — трек не опубликован</h1>");
    }

    private static string Shell(string title, string body) => $@"<!doctype html><html lang='ru'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'><title>{WebUtility.HtmlEncode(title)} — Murzila-Music</title>
<style>body{{margin:0;background:#0B0B12;color:#f5f5f7;font-family:Segoe UI,system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh}}
.card{{background:#1c1c24;padding:28px;border-radius:20px;width:min(440px,92vw);box-shadow:0 24px 60px #0008}}
h1{{font-size:22px}} p{{color:#aaa}} audio{{width:100%;margin-top:18px}} a{{color:#7ee07e}}</style></head><body><div class='card'>{body}<p><a href='/'>Все опубликованные треки</a></p></div></body></html>";

    private string ListPage()
    {
        var items = new StringBuilder();
        foreach (var track in _library.Tracks.Where(t => t.IsPublished))
            items.Append($"<p><a href='/t/{track.Id}'>{WebUtility.HtmlEncode(track.Title)} — {WebUtility.HtmlEncode(track.Artist)}</a></p>");
        if (items.Length == 0) items.Append("<p>Пока нет опубликованных треков.</p>");
        return Shell("Моя музыка", "<h1>Murzila-Music</h1><p>Опубликованные треки (локальная сеть)</p>" + items);
    }

    private static string PlayerPage(TrackItem track) => Shell(track.Title,
        $"<h1>{WebUtility.HtmlEncode(track.Title)}</h1><p>{WebUtility.HtmlEncode(track.Artist)}</p>" +
        $"<audio controls autoplay src='/file/{track.Id}'></audio>");

    public void Dispose() => Stop();
}
