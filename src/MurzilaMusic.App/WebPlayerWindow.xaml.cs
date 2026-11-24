using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;
using MurzilaMusic.App.Windows;

namespace MurzilaMusic.App;

/// <summary>
/// Веб-плеер внутри приложения + desktop-функции, которых нет в вебе:
/// синхронизация, папка треков, backup, эквалайзер, метаданные и тексты.
/// </summary>
public partial class WebPlayerWindow : Window
{
    private readonly LibraryService _library = new();
    private readonly CloudSyncService _sync;
    private string _webUrl = "https://murzila-music.onrender.com/player";

    public WebPlayerWindow()
    {
        InitializeComponent();
        _sync = new CloudSyncService(_library);
        LbLocal.ItemsSource = _library.Tracks;
        Loaded += async (_, _) => await InitWebAsync();
        Closed += (_, _) => Web?.Dispose();
    }

    private async Task InitWebAsync()
    {
        // Сначала локальный сервер (быстрее), если он запущен.
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
            var r = await http.GetAsync("http://localhost:6001/api/health");
            if (r.IsSuccessStatusCode) _webUrl = "http://localhost:6001/player";
        }
        catch { /* облачный адрес по умолчанию */ }

        try
        {
            await Web.EnsureCoreWebView2Async();
            Web.CoreWebView2.Navigate(_webUrl);
            Web.CoreWebView2.NavigationCompleted += (_, _) =>
                TbWebStatus.Text = "Плеер загружен: " + _webUrl
                    + "  ·  Desktop-функции слева и сверху доступны без сайта";
        }
        catch (Exception ex)
        {
            TbWebStatus.Text = "WebView2 недоступен (" + ex.Message + ") — открываю браузер.";
            OpenInBrowser();
        }
    }

    private void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_webUrl) { UseShellExecute = true });
        }
        catch { }
    }

    private TrackItem? Selected => LbLocal.SelectedItem as TrackItem;

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack();
    }

    private void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoForward == true) Web.CoreWebView2.GoForward();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();

    private void BtnBrowser_Click(object sender, RoutedEventArgs e) => OpenInBrowser();

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        BtnSync.IsEnabled = false;
        TbWebStatus.Text = "Синхронизирую свои треки…";
        try
        {
            var result = await _sync.RunAsync();
            LbLocal.ItemsSource = null;
            LbLocal.ItemsSource = _library.Tracks;
            TbWebStatus.Text = result.Message;
        }
        catch (Exception ex) { TbWebStatus.Text = "Синхронизация не удалась: " + ex.Message; }
        finally { BtnSync.IsEnabled = true; }
    }

    private void BtnFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start("explorer.exe", AppPaths.Tracks); }
        catch (Exception ex) { TbWebStatus.Text = ex.Message; }
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        new BackupWindow(_library) { Owner = this }.ShowDialog();
        TbWebStatus.Text = "Резервная копия создана.";
    }

    private void BtnEqualizer_Click(object sender, RoutedEventArgs e)
    {
        new EqualizerWindow { Owner = this }.ShowDialog();
    }

    private void BtnInfo_Click(object sender, RoutedEventArgs e)
    {
        var track = Selected;
        if (track is null) { TbWebStatus.Text = "Выбери трек в локальной библиотеке справа."; return; }
        var draft = new TrackInfoDraft
        {
            Title = track.Title, Artist = track.Artist, Album = track.Album,
            Comment = track.Comment, AllowExternal = track.AllowExternal
        };
        var result = TrackInfoWindow.Ask(this, "Информация о треке", Path.GetFileName(track.FileName), draft);
        if (result is null) return;
        _library.UpdateInfo(track, result);
        LbLocal.Items.Refresh();
        TbWebStatus.Text = "Метаданные сохранены: " + track.Title;
    }

    private void BtnLyrics_Click(object sender, RoutedEventArgs e)
    {
        var track = Selected;
        if (track is null) { TbWebStatus.Text = "Выбери трек в локальной библиотеке справа."; return; }
        new LyricsEditorWindow(track, text =>
        {
            string path = string.IsNullOrWhiteSpace(track.LyricsFile)
                ? Path.Combine(AppPaths.Lyrics, track.Id + ".lrc")
                : track.LyricsFile;
            File.WriteAllText(path, text);
            track.LyricsFile = path;
            _library.Save();
            TbWebStatus.Text = "Текст сохранён: " + track.Title;
        }) { Owner = this }.ShowDialog();
    }
}
