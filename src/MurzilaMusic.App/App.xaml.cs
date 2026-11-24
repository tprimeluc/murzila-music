using System.Windows;
using System.Windows.Threading;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class App : Application
{
    // Только одно приложение: вторая копия перезаписала бы settings.json при выходе.
    private static readonly Mutex SingleInstance = new(true, @"Global\MurzilaMusic_3_6_Single");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrash(args.ExceptionObject as Exception, "AppDomain");
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstance.WaitOne(TimeSpan.Zero, false))
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);

        try
        {
            AppPaths.Ensure();
            ThemeService.Apply(SettingsService.Current.Theme);

            var splash = new SplashWindow();
            splash.Show();

            splash.SetProgress(8, "Проверка соединения…");
            var checkTask = ConnectivityService.CheckAsync();
            var serverTask = ApiClient.DetectServerAsync();
            await Task.Delay(220);

            splash.SetProgress(38, "Загрузка настроек и библиотеки…");
            await Task.Delay(260);

            splash.SetProgress(68, "Каталог тарифов…");
            await Task.WhenAll(checkTask, serverTask, Task.Delay(420));

            splash.SetProgress(100, "Готово");
            await Task.Delay(220);
            splash.Close();

            var (ok, code) = checkTask.Result;
            if (!ok)
            {
                var offline = new OfflineWindow(code);
                bool? continueOffline = offline.ShowDialog();
                if (continueOffline != true)
                {
                    Shutdown();
                    return;
                }
            }

            var main = new MainWindow();
            MainWindow = main;
            main.Show();

            // Если сервер запущен, подтягиваем актуальный тариф после оплаты в браузере.
            if (SettingsService.Current.ServerMode
                && !string.IsNullOrWhiteSpace(SettingsService.Current.AccessToken))
                _ = RefreshAccountAsync();

            // Можно открыть файл двойным кликом по нему (Open with / drag on exe).
            if (e.Args.Length > 0 && File.Exists(e.Args[0]))
                main.PlayLocalFile(e.Args[0]);
        }
        catch (Exception ex)
        {
            ShowFatal(ex);
        }
    }

    private static async Task RefreshAccountAsync()
    {
        try
        {
            if (!await ApiClient.IsAvailableAsync()) return;
            var me = await ApiClient.MeAsync(SettingsService.Current.AccessToken);
            SettingsService.Current.Tariff = (Tariff)me.Tariff;
            SettingsService.Current.DisplayName = me.Name;
            SettingsService.Current.AccountEmail = me.Email;
            SettingsService.Save();
        }
        catch
        {
            // Офлайн или сервер выключен — сохраняем последний известный тариф.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        string log = WriteCrash(e.Exception, "Dispatcher");

        // Если главное окно уже работает — показываем ошибку и продолжаем,
        // чтобы одно неудачное действие не закрывало весь плеер.
        if (MainWindow is { IsLoaded: true })
        {
            MessageBox.Show(
                "Действие не удалось выполнить.\n\n" + e.Exception.Message +
                "\n\nЛог сохранён:\n" + log,
                "Murzila-Music — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ShowFatal(e.Exception);
    }

    private void ShowFatal(Exception exception)
    {
        string log = WriteCrash(exception, "Dispatcher");
        MessageBox.Show(
            "Murzila-Music не смог запуститься.\n\n" +
            exception.Message +
            "\n\nЛог сохранён:\n" + log,
            "Murzila-Music — ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    internal static string WriteCrash(Exception? exception, string source)
    {
        try
        {
            AppPaths.Ensure();
            string path = Path.Combine(AppPaths.Root, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{exception}\n\n");
            return path;
        }
        catch
        {
            return "(не удалось сохранить лог)";
        }
    }
}
