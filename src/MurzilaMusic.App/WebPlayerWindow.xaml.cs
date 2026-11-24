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
        Loaded += async (_, _) => await InitWebAsync();
        Closed += (_, _) => Web?.Dispose();
    }

    private async Task InitWebAsync()
    {
        // Всегда облачная версия: на ней настроен GitHub OAuth и актуальные данные.
        _webUrl = "https://murzila-music.onrender.com/player";

        try
        {
            string userData = Path.Combine(AppPaths.Root, "WebView2");
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await Web.EnsureCoreWebView2Async(env);
            Web.CoreWebView2.Navigate(_webUrl);
            Web.CoreWebView2.NavigationCompleted += (_, _) =>
                TbWebStatus.Text = "Плеер загружен: " + _webUrl + "  ·  Desktop-функции — правый клик";
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

    private void BtnBrowser_Click(object sender, RoutedEventArgs e) => OpenInBrowser();

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        TbWebStatus.Text = "Синхронизирую свои треки…";
        try
        {
            var result = await _sync.RunAsync();
            TbWebStatus.Text = result.Message;
        }
        catch (Exception ex) { TbWebStatus.Text = "Синхронизация не удалась: " + ex.Message; }
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

}
