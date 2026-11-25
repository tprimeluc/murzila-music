using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CefSharp;
using CefSharp.Wpf;
using MurzilaBrowser.App.Services;
using MurzilaBrowser.App.Windows;

namespace MurzilaBrowser.App;

public sealed class BrowserTab
{
    public required TabItem Header { get; init; }
    public required TabItem Page { get; init; }
    public required ChromiumWebBrowser Browser { get; init; }
    public required TextBlock TitleText { get; init; }
    public required ProgressBar Loading { get; init; }
    public string Title { get; set; } = "Новая вкладка";
    public bool IsBuiltIn { get; set; }
}

public partial class MainWindow : Window
{
    private readonly BrowserPrefs _prefs;
    private readonly LocalDataService _data;
    private readonly TranslationService _translator;
    private readonly UpdateService _updates;
    private readonly VoiceService _voice;
    private readonly AccountService _account;
    private readonly ImageSource _tabIcon;
    private readonly List<BrowserTab> _tabs = new();
    private bool _syncing;

    public MainWindow(string? startupUrl)
    {
        InitializeComponent();
        _prefs = BrowserPrefs.Load();
        _data = new LocalDataService();
        _translator = new TranslationService();
        _updates = new UpdateService();
        _voice = new VoiceService();
        _account = new AccountService();
        _tabIcon = new BitmapImage(new Uri("pack://application:,,,/Assets/browser-icon.png"));
        Icon = _tabIcon;

        // Обычный браузер: первая вкладка — новая, потом главная.
        AddTab("murzila://newtab", "Новая вкладка", builtIn: true);
        AddTab(_prefs.HomeUrl, "Главная", builtIn: true);
        if (!string.IsNullOrWhiteSpace(startupUrl))
            AddTab(startupUrl, TitleFromUrl(startupUrl));

        Tabs.SelectedIndex = 0;
        PageTabs.SelectedIndex = 0;
        Loaded += MainWindow_Loaded;
        KeyDown += MainWindow_KeyDown;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "готово";
        if (_prefs.FullscreenOnStart) WindowState = WindowState.Maximized;
        ApplyZoomToAll(_prefs.DefaultZoomPercent);
        SidecarText.Text = "обновление: проверка…";
        var info = await _updates.CheckAsync();
        SidecarText.Text = info != null && info.IsNewer
            ? "доступно обновление " + info.LatestTag
            : "версия " + _updates.CurrentVersion;
    }

    private BrowserTab? Current()
    {
        int i = PageTabs.SelectedIndex;
        return i >= 0 && i < _tabs.Count ? _tabs[i] : null;
    }

    private void AddTab(string url, string? title = null, bool builtIn = false)
    {
        var browser = new ChromiumWebBrowser(url) { Focusable = true };
        string initialTitle = title ?? TitleFromUrl(url);
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

        var icon = new Image
        {
            Source = _tabIcon, Width = 14, Height = 14,
            Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        var titleText = new TextBlock
        {
            Text = initialTitle, FontSize = 12,
            Foreground = (Brush)FindResource("Text"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 2, 0)
        };
        var loading = new ProgressBar
        {
            Width = 12, Height = 3, IsIndeterminate = true,
            Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("Accent"), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        var close = new Button
        {
            Style = (Style)FindResource("TabCloseButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(loading, 2);
        Grid.SetColumn(close, 3);
        headerGrid.Children.Add(icon);
        headerGrid.Children.Add(titleText);
        headerGrid.Children.Add(loading);
        headerGrid.Children.Add(close);

        var header = new TabItem { Header = headerGrid };
        close.Tag = header;
        close.Click += CloseTab_Click;
        var page = new TabItem { Content = browser };
        var tab = new BrowserTab
        {
            Header = header, Page = page, Browser = browser,
            TitleText = titleText, Loading = loading,
            Title = initialTitle, IsBuiltIn = builtIn
        };

        header.ContextMenu = new ContextMenu();
        header.ContextMenu.Items.Add(MenuItem("Перевести страницу", TranslatePage_Click));
        header.ContextMenu.Items.Add(MenuItem("Озвучить обычным голосом", VoicePage_Click));
        header.ContextMenu.Items.Add(MenuItem("Живые голоса (даббинг)", DubPage_Click));
        header.ContextMenu.Items.Add(new Separator());
        header.ContextMenu.Items.Add(MenuItem("Войти через Murzila", AccountMurzila_Click));
        header.ContextMenu.Items.Add(MenuItem("Войти через Google", AccountGoogle_Click));

        browser.TitleChanged += (_, args) => Dispatcher.Invoke(() =>
        {
            string value = args.NewValue?.ToString() ?? "";
            tab.Title = string.IsNullOrWhiteSpace(value) ? "Без названия" : value;
            tab.TitleText.Text = tab.Title;
        });
        browser.AddressChanged += (_, args) => Dispatcher.Invoke(() =>
        {
            string address = args.NewValue?.ToString() ?? "";
            if (Current() == tab) AddressBox.Text = address;
            _data.AddHistory(address, tab.Title);
        });
        browser.LoadingStateChanged += (_, args) => Dispatcher.Invoke(() =>
        {
            if (Current() != tab) return;
            LoadProgress.Value = args.IsLoading ? 50 : 100;
            StatusText.Text = args.IsLoading ? "загрузка…" : "готово";
            tab.Loading.Visibility = args.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            if (!args.IsLoading && tab.Title == "Загрузка…")
            {
                tab.Title = TitleFromUrl(tab.Browser.Address);
                tab.TitleText.Text = tab.Title;
            }
        });
        browser.LoadError += (_, args) => Dispatcher.Invoke(() =>
        {
            if (Current() == tab) StatusText.Text = "ошибка: " + args.ErrorText;
            browser.Load("murzila://error");
        });
        browser.StatusMessage += (_, args) => Dispatcher.Invoke(() =>
        {
            string status = args.Value ?? "";
            if (Current() == tab && !string.IsNullOrWhiteSpace(status)) StatusText.Text = status;
        });

        _tabs.Add(tab);
        Tabs.Items.Add(header);
        PageTabs.Items.Add(page);
    }

    private static MenuItem MenuItem(string text, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = text };
        item.Click += handler;
        return item;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TabItem header) return;
        RemoveTabAt(Tabs.Items.IndexOf(header));
    }

    private void RemoveTabAt(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        var tab = _tabs[index];
        if (!tab.IsBuiltIn && _tabs.Count == 1) return;
        _tabs.RemoveAt(index);
        Tabs.Items.RemoveAt(index);
        PageTabs.Items.RemoveAt(index);
        tab.Browser.Dispose();
        if (_tabs.Count == 0) AddTab("murzila://newtab", "Новая вкладка", builtIn: true);
        if (Tabs.SelectedIndex >= _tabs.Count) Tabs.SelectedIndex = _tabs.Count - 1;
        SyncSelection(Tabs.SelectedIndex);
    }

    private void SyncSelection(int index)
    {
        if (_syncing || index < 0 || index >= _tabs.Count) return;
        _syncing = true;
        Tabs.SelectedIndex = index;
        PageTabs.SelectedIndex = index;
        var tab = _tabs[index];
        AddressBox.Text = tab.Browser.Address;
        StatusText.Text = "готово";
        LoadProgress.Value = 100;
        _syncing = false;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => SyncSelection(Tabs.SelectedIndex);
    private void PageTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => SyncSelection(PageTabs.SelectedIndex);

    // Правило пользователя: поиск и ввод адреса всегда открывают НОВУЮ вкладку.
    private void Navigate(string input)
    {
        string target = NormalizeUrl(input);
        AddTab(target, TitleFromUrl(target));
        Tabs.SelectedIndex = _tabs.Count - 1;
        SyncSelection(Tabs.SelectedIndex);
        AddressBox.Text = target;
    }

    private string TitleFromUrl(string url)
    {
        if (url.Contains("murzila://home", StringComparison.OrdinalIgnoreCase)) return "Главная";
        if (url.Contains("murzila://newtab", StringComparison.OrdinalIgnoreCase)) return "Новая вкладка";
        try { return new Uri(NormalizeUrl(url)).Host; } catch { return "Загрузка…"; }
    }

    private string NormalizeUrl(string input)
    {
        string value = input.Trim();
        if (value.Length == 0) return "murzila://newtab";
        if (value.StartsWith("murzila://", StringComparison.OrdinalIgnoreCase)) return value;
        if (value.Contains("://", StringComparison.Ordinal)) return value;
        if (value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || value.StartsWith("127.0.0.1", StringComparison.Ordinal))
            return "http://" + value;
        if (value.Contains(' ') || !value.Contains('.'))
            return string.Format(_prefs.SearchUrl, Uri.EscapeDataString(value));
        return "http://" + value;
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Navigate(AddressBox.Text); e.Handled = true; }
    }

    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(AddressBox.Text);
    private void Back_Click(object sender, RoutedEventArgs e) { if (Current()?.Browser.CanGoBack == true) Current()!.Browser.Back(); }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (Current()?.Browser.CanGoForward == true) Current()!.Browser.Forward(); }
    private void Reload_Click(object sender, RoutedEventArgs e) => Current()?.Browser.Reload();
    private void Home_Click(object sender, RoutedEventArgs e) => Navigate(_prefs.HomeUrl);
    private void NewTab_Click(object sender, RoutedEventArgs e) => AddTab("murzila://newtab", "Новая вкладка");

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        var tab = Current();
        if (tab == null) return;
        _data.AddBookmark(tab.Browser.Address, tab.Title);
        StarIcon.Fill = new SolidColorBrush(Color.FromRgb(0x4f, 0x8c, 0xff));
        StatusText.Text = "закладка добавлена: " + tab.Title;
    }

    private void Features_Click(object sender, RoutedEventArgs e)
    {
        var window = new FeaturesWindow(_data, _updates, _voice) { Owner = this };
        window.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_prefs) { Owner = this };
        if (window.ShowDialog() == true)
        {
            ApplyZoomToAll(_prefs.DefaultZoomPercent);
            StatusText.Text = "настройки сохранены";
        }
    }

    private static double PercentToZoom(int percent) => Math.Log(Math.Max(0.25, percent / 100.0), 1.2);

    private void ApplyZoomToAll(int percent)
    {
        double zoom = PercentToZoom(percent);
        foreach (var tab in _tabs) tab.Browser.ZoomLevel = zoom;
    }

    private void AccountMurzila_Click(object sender, RoutedEventArgs e) => Navigate(_account.GetAuthUrl("Murzila"));
    private void AccountGoogle_Click(object sender, RoutedEventArgs e) => Navigate(_account.GetAuthUrl("Google"));

    private async Task<string> GetPageTextAsync(BrowserTab tab)
    {
        try
        {
            string text = await tab.Browser.EvaluateScriptAsync<string>("document.body.innerText");
            if (string.IsNullOrWhiteSpace(text)) text = tab.Title;
            return text.Length > 4000 ? text[..4000] : text;
        }
        catch { return tab.Title; }
    }

    private async void TranslatePage_Click(object sender, RoutedEventArgs e)
    {
        var tab = Current();
        if (tab == null) return;
        string text = await GetPageTextAsync(tab);
        string from = _translator.DetectLanguage(text) ?? "ru";
        string to = from == "ru" ? "en" : "ru";
        string? translated = await _translator.TranslateAsync(text, to);
        Dispatcher.Invoke(() =>
        {
            if (translated is null) { StatusText.Text = "перевод недоступен — нужна сеть"; return; }
            Clipboard.SetText(translated);
            StatusText.Text = "перевод скопирован (" + from + " → " + to + ")";
        });
    }

    private async void VoicePage_Click(object sender, RoutedEventArgs e)
    {
        var tab = Current();
        if (tab == null) return;
        string text = await GetPageTextAsync(tab);
        string from = _translator.DetectLanguage(text) ?? "ru";
        string to = from == "ru" ? "en" : "ru";
        string? translated = await _translator.TranslateAsync(text.Length > 500 ? text[..500] : text, to);
        if (translated is not null) _voice.Speak(translated, to);
    }

    private async void DubPage_Click(object sender, RoutedEventArgs e)
    {
        var tab = Current();
        if (tab == null) return;
        string text = await GetPageTextAsync(tab);
        string from = _translator.DetectLanguage(text) ?? "ru";
        string to = from == "ru" ? "en" : "ru";
        string? translated = await _translator.TranslateAsync(text.Length > 1500 ? text[..1500] : text, to);
        if (translated is null) return;
        var progress = new Progress<(int Done, int Total)>(p =>
            Dispatcher.Invoke(() => StatusText.Text = "живые голоса: " + p.Done + " / " + p.Total));
        await _voice.DubAsync(translated, to, progress);
        Dispatcher.Invoke(() => StatusText.Text = "живые голоса: готово");
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (ctrl && e.Key == Key.T) { AddTab("murzila://newtab", "Новая вкладка"); e.Handled = true; }
        else if (ctrl && e.Key == Key.W) { RemoveTabAt(Tabs.SelectedIndex); e.Handled = true; }
        else if (ctrl && e.Key == Key.L) { AddressBox.Focus(); AddressBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.F5) { Current()?.Browser.Reload(); e.Handled = true; }
        else if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            _prefs.DefaultZoomPercent = Math.Min(200, _prefs.DefaultZoomPercent + 10);
            ApplyZoomToAll(_prefs.DefaultZoomPercent);
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            _prefs.DefaultZoomPercent = Math.Max(50, _prefs.DefaultZoomPercent - 10);
            ApplyZoomToAll(_prefs.DefaultZoomPercent);
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Left) { Back_Click(sender, e); e.Handled = true; }
        else if (alt && e.Key == Key.Right) { Forward_Click(sender, e); e.Handled = true; }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Maximize_Click(sender, e); return; }
        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState =
        WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _prefs.Save();
        base.OnClosed(e);
    }
}
