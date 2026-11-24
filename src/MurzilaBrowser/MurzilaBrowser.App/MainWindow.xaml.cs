using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CefSharp;
using CefSharp.Wpf;
using MurzilaBrowser.App.Services;

namespace MurzilaBrowser.App;

public sealed class BrowserTab
{
    public required TabItem Header { get; init; }
    public required TabItem Page { get; init; }
    public required ChromiumWebBrowser Browser { get; init; }
    public string Title { get; set; } = "Новая вкладка";
    public bool IsBuiltIn { get; set; }
}

public partial class MainWindow : Window
{
    private readonly BrowserPrefs _settings;
    private readonly PythonBridge _bridge;
    private readonly List<BrowserTab> _tabs = new();
    private bool _syncing;

    public MainWindow(string startupUrl)
    {
        InitializeComponent();
        _settings = BrowserPrefs.Load();
        _bridge = new PythonBridge(_settings);

        AddTab(_settings.HomeUrl, "Главная", builtIn: true);
        AddTab("murzila://music", "Музыка", builtIn: true);
        if (!string.IsNullOrWhiteSpace(startupUrl) && startupUrl != _settings.HomeUrl)
            AddTab(startupUrl);

        Tabs.SelectedIndex = 0;
        PageTabs.SelectedIndex = 0;
        Loaded += MainWindow_Loaded;
        KeyDown += MainWindow_KeyDown;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _bridge.StartAsync();
        SidecarText.Text = _bridge.Connected ? "python: подключён" : "python: недоступен";
        await _bridge.AddHistoryAsync(Current()?.Browser.Address ?? _settings.HomeUrl, Current()?.Title ?? "Главная");
    }

    private BrowserTab? Current()
    {
        int i = PageTabs.SelectedIndex;
        return i >= 0 && i < _tabs.Count ? _tabs[i] : null;
    }

    private void AddTab(string url, string? title = null, bool builtIn = false)
    {
        var browser = new ChromiumWebBrowser(url)
        {
            Focusable = true
        };

        string initialTitle = title ?? TitleFromUrl(url);
        var header = new TabItem { Header = initialTitle };
        var page = new TabItem { Content = browser };
        var tab = new BrowserTab { Header = header, Page = page, Browser = browser, Title = initialTitle, IsBuiltIn = builtIn };

        browser.TitleChanged += (_, args) => Dispatcher.Invoke(() =>
        {
            string title = args.NewValue?.ToString() ?? "";
            tab.Title = string.IsNullOrWhiteSpace(title) ? "Без названия" : title;
            header.Header = tab.Title;
        });
        browser.AddressChanged += (_, args) => Dispatcher.Invoke(() =>
        {
            string address = args.NewValue?.ToString() ?? "";
            if (Current() == tab) AddressBox.Text = address;
            _ = _bridge.AddHistoryAsync(address, tab.Title);
        });
        browser.LoadingStateChanged += (_, args) => Dispatcher.Invoke(() =>
        {
            if (Current() != tab) return;
            LoadProgress.Value = args.IsLoading ? 50 : 100;
            StatusText.Text = args.IsLoading ? "загрузка…" : "готово";
            if (!args.IsLoading && tab.Title == "Загрузка…")
            {
                tab.Title = TitleFromUrl(tab.Browser.Address);
                header.Header = tab.Title;
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
            if (Current() == tab && !string.IsNullOrWhiteSpace(status))
                StatusText.Text = status;
        });

        _tabs.Add(tab);
        Tabs.Items.Add(header);
        PageTabs.Items.Add(page);
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

        if (_tabs.Count == 0) AddTab(_settings.HomeUrl, "Главная", builtIn: true);
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

    private void Navigate(string url)
    {
        var tab = Current();
        if (tab == null) return;
        string target = NormalizeUrl(url);
        tab.Title = target;
        tab.Header.Header = target;
        tab.Browser.Load(target);
        AddressBox.Text = target;
    }

    private static string TitleFromUrl(string url)
    {
        if (url.Contains("murzila://home", StringComparison.OrdinalIgnoreCase)) return "Главная";
        if (url.Contains("murzila://music", StringComparison.OrdinalIgnoreCase)) return "Музыка";
        if (url.Contains("murzila://newtab", StringComparison.OrdinalIgnoreCase)) return "Новая вкладка";
        try { return new Uri(NormalizeUrl(url)).Host; } catch { return "Загрузка…"; }
    }

    private static string NormalizeUrl(string input)
    {
        string value = input.Trim();
        if (value.Length == 0) return "murzila://newtab";
        if (value.StartsWith("murzila://", StringComparison.OrdinalIgnoreCase)) return value;
        if (value.Contains("://", StringComparison.Ordinal)) return value;
        if (value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || value.StartsWith("127.0.0.1", StringComparison.Ordinal))
            return "http://" + value;
        if (value.Contains(' ') || !value.Contains('.'))
            return "https://duckduckgo.com/?q=" + Uri.EscapeDataString(value);
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
    private void Home_Click(object sender, RoutedEventArgs e) => Navigate(_settings.HomeUrl);
    private void NewTab_Click(object sender, RoutedEventArgs e) => AddTab("murzila://newtab", "Новая вкладка");

    private async void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        var tab = Current();
        if (tab == null) return;
        await _bridge.AddBookmarkAsync(tab.Browser.Address, tab.Title);
        StarIcon.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xd3, 0x6e));
        StatusText.Text = "закладка добавлена: " + tab.Title;
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (ctrl && e.Key == Key.T) { AddTab("murzila://newtab", "Новая вкладка"); e.Handled = true; }
        else if (ctrl && e.Key == Key.W) { RemoveTabAt(Tabs.SelectedIndex); e.Handled = true; }
        else if (ctrl && e.Key == Key.L) { AddressBox.Focus(); AddressBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.F5) { Current()?.Browser.Reload(); e.Handled = true; }
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
        _bridge.Dispose();
        _settings.Save();
        base.OnClosed(e);
    }
}
