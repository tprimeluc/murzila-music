using System.IO;
using System.Windows;
using System.Windows.Threading;
using CefSharp;
using CefSharp.Wpf;
using MurzilaBrowser.App.Scheme;
using MurzilaBrowser.App.Services;

namespace MurzilaBrowser.App;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    public static string? StartupUrl { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        Dispatcher.UnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Murzila Browser", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _singleInstance = new Mutex(true, "MurzilaBrowser_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Murzila Browser уже запущен.", "Murzila Browser");
            Shutdown();
            return;
        }

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            StartupUrl = NormalizeUrl(args[1]);

        var prefs = BrowserPrefs.Load();
        var settings = new CefSettings
        {
            CachePath = BrowserPrefs.ProfilePath,
            RootCachePath = BrowserPrefs.ProfilePath,
            Locale = "ru",
            AcceptLanguageList = "ru,en;q=0.9",
            LogFile = Path.Combine(BrowserPrefs.ProfilePath, "cef.log")
        };
        if (!prefs.HardwareAcceleration || prefs.LowEndMode) settings.DisableGpuAcceleration();
        if (prefs.MaxPerformance || prefs.DisableVSync)
        {
            settings.CefCommandLineArgs["disable-frame-rate-limit"] = "1";
            settings.CefCommandLineArgs["disable-gpu-vsync"] = "1";
            settings.CefCommandLineArgs["disable-smooth-scrolling"] = "1";
        }
        settings.RegisterScheme(new CefCustomScheme
        {
            SchemeName = "murzila",
            SchemeHandlerFactory = new MurzilaSchemeHandlerFactory(),
            IsSecure = true,
            IsCorsEnabled = true
        });

        Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
        var window = new MainWindow(StartupUrl);
        MainWindow = window;
        window.Show();
        base.OnStartup(e);
    }

    private static string NormalizeUrl(string input)
    {
        string value = input.Trim();
        if (value.StartsWith("murzila://", StringComparison.OrdinalIgnoreCase)) return value;
        if (value.Contains("://", StringComparison.Ordinal)) return value;
        if (value.Contains(' ') || !value.Contains('.')) return "https://duckduckgo.com/?q=" + Uri.EscapeDataString(value);
        return "http://" + value;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Cef.IsInitialized == true) Cef.Shutdown();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
