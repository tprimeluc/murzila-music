using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class SettingsWindow : Window
{
    private readonly Action _onChanged;
    private readonly Action<int>? _onOutputDeviceChanged;
    private bool _loading = true;

    public SettingsWindow(Action onChanged, Action<int>? onOutputDeviceChanged = null)
    {
        _onChanged = onChanged;
        _onOutputDeviceChanged = onOutputDeviceChanged;
        InitializeComponent();

        RbTy.IsChecked = SettingsService.Current.Pronoun == "Ты";
        RbVy.IsChecked = SettingsService.Current.Pronoun == "Вы";
        TbSettingsSubtitle.Text = Txt.SettingsSubtitle;
        TbLyricsSourcesHint.Text = Txt.LyricsSourcesHint;
        BtnEqualizer.Content = AppIcons.WithText("equalizer", Txt.EqualizerTitle);
        TbThemeLabel.Text = Txt.ThemeLabel;
        TbThemeHint.Text = Txt.ThemeHint;
        CbTheme.Items.Clear();
        foreach (string name in Txt.ThemeNames)
            CbTheme.Items.Add(new ComboBoxItem { Content = name });
        ThemeService.Apply(SettingsService.Current.Theme);
        LoadControls();
        LoadOutputDevices();
        _loading = false;

        UpdatePronounHint();
        UpdateSliderHints();
        UpdateAccountText();
        UpdateEqText();
        UpdateTariffText();
        _ = CheckServerAsync();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Resources["WindowIntro"] is System.Windows.Media.Animation.Storyboard intro)
            intro.Begin(this);
    }

    /// <summary>
    /// WPF создаёт содержимое TabItem только при первом открытии, поэтому содержимое
    /// новых вкладок надо загружать из AppSettings повторно, иначе галочки «пропадают».
    /// </summary>
    private void Tabs_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SettingsTabs is null) return;

        if (Resources["TabsFlip"] is System.Windows.Media.Animation.Storyboard flip)
        {
            flip.Stop(this);
            flip.Begin(this, true);
        }

        _loading = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            LoadControls();
            LoadOutputDevices();
            UpdateSliderHints();
            UpdatePronounHint();
            UpdateAccountText();
            UpdateEqText();
            UpdateTariffText();
            _loading = false;
            if (TbOnlineStatus is not null && string.IsNullOrEmpty(TbOnlineStatus.Text))
                _ = CheckServerAsync();
        }));
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        // Собираем значения со всех вкладок: часть событий mappings может не сработать,
        // если пользователь переключил вкладку и сразу нажал «Готово».
        CollectControls();
        SettingsService.Save();
        DialogResult = true;
        Close();
    }

    private void CollectControls()
    {
        foreach (var element in FindVisualChildren<FrameworkElement>(this))
        {
            if (element.Tag is not string key) continue;
            var property = Property(key);
            if (property is null) continue;

            object? value = element switch
            {
                CheckBox check => check.IsChecked == true,
                Slider slider => (int)Math.Round(slider.Value),
                TextBox text => text.Text,
                ComboBox combo => combo.SelectedItem switch
                {
                    ComboBoxItem item when item.Tag is not null => item.Tag,
                    ComboBoxItem item => item.Content,
                    _ => null
                },
                _ => null
            };
            if (value is null) continue;

            try
            {
                if (property.PropertyType == typeof(bool)) property.SetValue(SettingsService.Current, Convert.ToBoolean(value));
                else if (property.PropertyType == typeof(int)) property.SetValue(SettingsService.Current, Convert.ToInt32(value));
                else if (property.PropertyType == typeof(string)) property.SetValue(SettingsService.Current, value.ToString() ?? "");
            }
            catch { }
        }
    }

    private async Task CheckServerAsync()
    {
        bool up = await ApiClient.IsAvailableAsync();
        if (up)
        {
            try
            {
                var info = await ApiClient.GetInfoAsync();
                SettingsService.Current.DemoPayments = !info.RealPayments;
                SettingsService.Save();
            }
            catch { }
        }
        if (TbOnlineDetails is not null)
            TbOnlineDetails.Text = up ? Txt.OnlineDetailsOnline : Txt.OnlineDetailsOffline;
        if (TbOnlineStatus is not null)
        {
            TbOnlineStatus.Text = up ? Txt.ServerRunning : Txt.ServerUnavailable;
            TbOnlineStatus.Foreground = new SolidColorBrush(up
                ? Color.FromRgb(0x7E, 0xE0, 0x81)
                : Color.FromRgb(0xFF, 0x8B, 0x8B));
        }
        UpdateAccountText();
        UpdateTariffText();
    }

    // ---------- универсальное сохранение 30+ настроек ----------

    private static PropertyInfo? Property(string name) => typeof(AppSettings).GetProperty(name);

    private void LoadControls()
    {
        foreach (var element in FindVisualChildren<FrameworkElement>(this))
        {
            if (element.Tag is not string key) continue;
            var property = Property(key);
            if (property is null) continue;
            object? value = property.GetValue(SettingsService.Current);

            switch (element)
            {
                case CheckBox check:
                    check.IsChecked = value is true;
                    break;
                case Slider slider:
                    slider.Value = Convert.ToDouble(value ?? 0);
                    break;
                case TextBox text:
                    text.Text = value?.ToString() ?? "";
                    break;
                case ComboBox combo:
                    string wanted = value?.ToString() ?? "";
                    foreach (var item in combo.Items.OfType<ComboBoxItem>())
                    {
                        string candidate = item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
                        if (string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            combo.SelectedItem = item;
                            break;
                        }
                    }
                    break;
            }
        }
    }

    private void Generic_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is not FrameworkElement element || element.Tag is not string key) return;
        var property = Property(key);
        if (property is null) return;

        object? value = element switch
        {
            CheckBox check => check.IsChecked == true,
            Slider slider => (int)Math.Round(slider.Value),
            TextBox text => text.Text,
            ComboBox combo => combo.SelectedItem switch
            {
                ComboBoxItem item when item.Tag is not null => item.Tag,
                ComboBoxItem item => item.Content,
                _ => combo.SelectedItem
            },
            _ => null
        };
        if (value is null) return;

        try
        {
            if (property.PropertyType == typeof(bool)) property.SetValue(SettingsService.Current, Convert.ToBoolean(value));
            else if (property.PropertyType == typeof(int)) property.SetValue(SettingsService.Current, Convert.ToInt32(value));
            else if (property.PropertyType == typeof(string)) property.SetValue(SettingsService.Current, value.ToString() ?? "");
            else return;
        }
        catch { return; }

        SettingsService.Save();
        if (key == nameof(AppSettings.Theme))
            ThemeService.Apply(SettingsService.Current.Theme);
        UpdateSliderHints();
        _onChanged?.Invoke();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) yield return result;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    private void UpdateSliderHints()
    {
        TbDensity.Text = SettingsService.Current.ParticleDensity.ToString();
        TbSeek.Text = SettingsService.Current.SeekSeconds.ToString();
        TbFont.Text = SettingsService.Current.LyricsFontSize.ToString();
    }

    // ---------- звуковое устройство ----------

    private void LoadOutputDevices()
    {
        CbOutput.Items.Clear();
        foreach (var device in AudioOutputService.GetDevices())
            CbOutput.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Number });

        int current = SettingsService.Current.OutputDeviceNumber;
        foreach (var item in CbOutput.Items.OfType<ComboBoxItem>())
            if (item.Tag is int number && number == current) { CbOutput.SelectedItem = item; break; }
    }

    private void Output_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CbOutput.SelectedItem is not ComboBoxItem { Tag: int number }) return;
        SettingsService.Current.OutputDeviceNumber = number;
        SettingsService.Save();
        _onOutputDeviceChanged?.Invoke(number);
        _onChanged?.Invoke();
    }

    private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
        => AudioOutputService.OpenSoundSettings();

    // ---------- остальные действия ----------

    private void Pronoun_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || RbTy is null || RbVy is null) return;
        SettingsService.Current.Pronoun = RbVy.IsChecked == true ? "Вы" : "Ты";
        SettingsService.Save();
        UpdatePronounHint();
        Txt.NotifyChanged();
        _onChanged?.Invoke();
    }

    private void Tariff_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsService.Current.ServerMode)
        {
            TbTariff.Text = Txt.ServerTariffOnly;
            return;
        }
        if (sender is not Button button || button.Tag is not string value) return;
        SettingsService.Current.Tariff = (Tariff)int.Parse(value);
        if (SettingsService.Current.Tariff > Tariff.Free) SettingsService.Current.LoggedIn = true;
        SettingsService.Save();
        UpdateTariffText();
        _onChanged?.Invoke();
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        var account = new AccountWindow { Owner = this };
        account.ShowDialog();
        UpdateAccountText();
        UpdateTariffText();
        _onChanged?.Invoke();
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        AccountService.Logout();
        UpdateAccountText();
        UpdateTariffText();
        _onChanged?.Invoke();
    }

    private void Eq_Click(object sender, RoutedEventArgs e)
    {
        var equalizer = new EqualizerWindow(() => _onChanged?.Invoke()) { Owner = this };
        equalizer.ShowDialog();
        UpdateEqText();
    }

    private void UpdatePronounHint()
    {
        TbPronounHint.Text = SettingsService.Current.Pronoun == "Вы"
            ? "Сейчас обращение «Вы». Все надписи обновляются сразу."
            : "Сейчас обращение «Ты». Все надписи обновляются сразу.";
    }

    private void UpdateAccountText()
    {
        if (SettingsService.Current.LoggedIn)
        {
            TbAccountStatus.Text = Txt.LoggedInAs(DisplayName())
                                   + (string.IsNullOrWhiteSpace(SettingsService.Current.AccountEmail)
                                       ? ""
                                       : " · " + SettingsService.Current.AccountEmail)
                                   + " · " + (SettingsService.Current.ServerMode
                                       ? Txt.ServerAccountBadge
                                       : Txt.DemoAccountBadge);
            BtnAccountAction.Content = "Сменить аккаунт";
            BtnLogout.Visibility = Visibility.Visible;
        }
        else
        {
            TbAccountStatus.Text = Txt.Guest + ". " + Txt.AccountSubtitle;
            BtnAccountAction.Content = "Войти или зарегистрироваться";
            BtnLogout.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateEqText()
    {
        string state = SettingsService.Current.EqualizerEnabled ? "включён" : "выключен";
        TbEqStatus.Text = $"Эквалайзер {state} · пресет: {SettingsService.Current.EqualizerPreset}";
    }

    private void UpdateTariffText()
    {
        string name = Txt.TariffStatus(SettingsService.Current.Tariff);
        string status = SettingsService.Current.LoggedIn ? "вход выполнен" : "гость";
        TbTariff.Text = $"Текущий тариф: {name} · статус: {status}";
    }

    private static string DisplayName()
    {
        string name = SettingsService.Current.DisplayName;
        if (!string.IsNullOrWhiteSpace(name) && name != Txt.Guest) return name;
        string email = SettingsService.Current.AccountEmail;
        int at = email.IndexOf('@');
        return at > 0 ? email[..at] : Txt.Guest;
    }

    private void OpenWebPlayer_Click(object sender, RoutedEventArgs e)
        => OpenUrl("http://localhost:6001/player");

    private void OpenApiToken_Click(object sender, RoutedEventArgs e)
        => OpenUrl("http://localhost:6001/api/token");

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* браузер мог быть недоступен — адрес показан в подсказке */ }
    }
}
