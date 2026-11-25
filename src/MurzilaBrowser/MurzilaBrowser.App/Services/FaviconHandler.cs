using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using CefSharp;
using CefSharp.Enums;
using CefSharp.Structs;

namespace MurzilaBrowser.App.Services;

/// <summary>Загружает настоящий favicon сайта и отдаёт его в WPF-иконку вкладки.</summary>
public sealed class FaviconHandler : IDisplayHandler
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly Action<BitmapImage> _onFavicon;

    public FaviconHandler(Action<BitmapImage> onFavicon) => _onFavicon = onFavicon;

    public static void FetchForUrl(string url, Action<BitmapImage> callback)
    {
        try
        {
            var host = new Uri(url).Host;
            _ = Task.Run(async () =>
            {
                try
                {
                    string service = "https://www.google.com/s2/favicons?domain=" + Uri.EscapeDataString(host) + "&sz=32";
                    byte[] bytes = await Http.GetByteArrayAsync(service);
                    using var stream = new MemoryStream(bytes);
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    callback(image);
                }
                catch { }
            });
        }
        catch { }
    }

    public void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls)
    {
        string? url = urls.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        if (url is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                byte[] bytes = await Http.GetByteArrayAsync(url);
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                _onFavicon(image);
            }
            catch { }
        });
    }

    public void OnAddressChanged(IWebBrowser chromiumWebBrowser, AddressChangedEventArgs addressChangedArgs) { }
    public bool OnAutoResize(IWebBrowser chromiumWebBrowser, IBrowser browser, CefSharp.Structs.Size newSize) => false;
    public bool OnCursorChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IntPtr cursor, CursorType type, CursorInfo customCursorInfo) => false;
    public void OnTitleChanged(IWebBrowser chromiumWebBrowser, TitleChangedEventArgs titleChangedArgs) { }
    public void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen) { }
    public void OnLoadingProgressChange(IWebBrowser chromiumWebBrowser, IBrowser browser, double progress) { }
    public bool OnTooltipChanged(IWebBrowser chromiumWebBrowser, ref string text) => false;
    public void OnStatusMessage(IWebBrowser chromiumWebBrowser, StatusMessageEventArgs statusMessageArgs) { }
    public bool OnConsoleMessage(IWebBrowser chromiumWebBrowser, ConsoleMessageEventArgs consoleMessageArgs) => false;
}
