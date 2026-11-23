using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MurzilaMusic.App.Services;

/// <summary>Трек из личного хранилища нашего сервера.</summary>
public sealed class CloudTrackDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string AddedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public long FileSize { get; set; }
    public string FileName { get; set; } = "";
    public bool HasCover { get; set; }
    public bool HasVideo { get; set; }
    public bool HasLyrics { get; set; }
    public bool AllowExternal { get; set; }
}

public sealed record CloudProgress(int Percent, long Bytes, long Total);

/// <summary>
/// Клиент личного облака. Отправляет и скачивает только файлы, которые пользователь
/// сам отметил как опубликованные, и только под своим токеном.
/// </summary>
public static class CloudApiClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Url(string path) => ApiClient.BaseUrl + path;

    private static void Authorize(HttpRequestMessage request)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Войди в аккаунт перед синхронизацией.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static async Task<List<CloudTrackDto>> ListAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/cloud/tracks"));
        Authorize(request);
        using var response = await Client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw CloudError(text, response);
        return JsonSerializer.Deserialize<List<CloudTrackDto>>(text, Json) ?? new();
    }

    public static async Task UploadAsync(CloudTrackDto meta, string audioPath,
        string? coverPath, string? videoPath, string? lyricsPath, IProgress<CloudProgress>? progress = null)
    {
        using var form = new MultipartFormDataContent();
        void Add(string name, string value) => form.Add(new StringContent(value), name);
        Add("id", meta.Id);
        Add("title", meta.Title);
        Add("artist", meta.Artist);
        Add("album", meta.Album);
        Add("durationSeconds", meta.DurationSeconds.ToString("R", CultureInfo.InvariantCulture));
        Add("addedAt", meta.AddedAt);
        Add("fileSize", new FileInfo(audioPath).Length.ToString(CultureInfo.InvariantCulture));
        Add("hasCover", (coverPath is not null && File.Exists(coverPath)).ToString());
        Add("hasVideo", (videoPath is not null && File.Exists(videoPath)).ToString());
        Add("hasLyrics", (lyricsPath is not null && File.Exists(lyricsPath)).ToString());
        Add("allowExternal", meta.AllowExternal.ToString());

        var audio = Content(audioPath, "audio", progress);
        form.Add(audio.Content, "file", Path.GetFileName(audioPath));

        var extra = new List<MultipartFile>();
        if (coverPath is not null && File.Exists(coverPath))
            extra.Add(new MultipartFile(Content(coverPath, "image", null), "cover", coverPath));
        if (videoPath is not null && File.Exists(videoPath))
            extra.Add(new MultipartFile(Content(videoPath, "video", null), "video", videoPath));
        if (lyricsPath is not null && File.Exists(lyricsPath))
            extra.Add(new MultipartFile(Content(lyricsPath, "text/plain", null), "lyrics", lyricsPath));

        try
        {
            foreach (var file in extra) form.Add(file.Content.Content, file.Name, Path.GetFileName(file.Path));
            using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/cloud/tracks"))
            {
                Content = form
            };
            Authorize(request);
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode) throw CloudError(await response.Content.ReadAsStringAsync(), response);
        }
        finally
        {
            foreach (var file in extra) file.Content.Stream.Dispose();
        }
    }

    public static async Task DownloadAsync(string id, string kind, string targetPath,
        IProgress<CloudProgress>? progress = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            Url($"/api/cloud/tracks/{Uri.EscapeDataString(id)}/{kind}"));
        Authorize(request);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw CloudError(await response.Content.ReadAsStringAsync(), response);

        string? dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string temp = targetPath + ".download";
        long total = response.Content.Headers.ContentLength ?? 0;
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[64 * 1024];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    readTotal += read;
                    progress?.Report(new CloudProgress(total > 0 ? (int)(readTotal * 100 / total) : 0,
                        readTotal, total));
                }
            }
            File.Move(temp, targetPath, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public static async Task DeleteAsync(string id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            Url("/api/cloud/tracks/" + Uri.EscapeDataString(id)));
        Authorize(request);
        using var response = await Client.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw CloudError(await response.Content.ReadAsStringAsync(), response);
    }

    private sealed record MultipartFile(ProgressContent Content, string Name, string Path);

    private sealed class ProgressContent
    {
        public ProgressContent(StreamContent content, ProgressReadStream stream)
        {
            Content = content;
            Stream = stream;
        }
        public StreamContent Content { get; }
        public ProgressReadStream Stream { get; }
    }

    private static ProgressContent Content(string path, string mime,
        IProgress<CloudProgress>? progress)
    {
        var stream = new ProgressReadStream(File.OpenRead(path), progress);
        var content = new StreamContent(stream);
        // Короткие псевдонимы ("audio", "image", "video") — не MIME-типы:
        // MediaTypeHeaderValue их отклоняет, и загрузка трека падала.
        string mediaType = mime.Contains('/') ? mime : Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".m4a" or ".aac" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".lrc" or ".txt" => "text/plain",
            ".mp4" or ".mkv" or ".mov" => "video/mp4",
            _ => mime == "image" ? "image/jpeg" : mime == "video" ? "video/mp4" : "audio/mpeg"
        };
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new ProgressContent(content, stream);
    }

    private static Exception CloudError(string body, HttpResponseMessage response)
    {
        string message = "";
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error)) message = error.GetString() ?? "";
        }
        catch { }
        if (string.IsNullOrWhiteSpace(message))
            message = $"Сервер вернул ошибку {(int)response.StatusCode}.";
        return new InvalidOperationException(message);
    }

    private sealed class ProgressReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly IProgress<CloudProgress>? _progress;
        private readonly long _total;
        private long _read;

        public ProgressReadStream(Stream inner, IProgress<CloudProgress>? progress)
        {
            _inner = inner;
            _progress = progress;
            _total = inner.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _total;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            Report(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            Report(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken);
            Report(read);
            return read;
        }

        private void Report(int count)
        {
            if (count <= 0) return;
            _read += count;
            _progress?.Report(new CloudProgress(_total > 0 ? (int)(_read * 100 / _total) : 0, _read, _total));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
