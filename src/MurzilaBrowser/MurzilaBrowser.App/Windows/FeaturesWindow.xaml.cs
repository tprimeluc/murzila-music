using System.Windows;
using System.Windows.Media;
using MurzilaBrowser.App.Services;

namespace MurzilaBrowser.App.Windows;

public sealed record FeatureRow(BrowserFeature Feature, int Number)
{
    public string Status => Feature.Ready ? "готово" : "скоро";
    public SolidColorBrush StatusColor => new(Feature.Ready
        ? Color.FromRgb(0x4f, 0x8c, 0xff)
        : Color.FromRgb(0x9a, 0xa4, 0xb7));
}

public partial class FeaturesWindow : Window
{
    private readonly LocalDataService _data;
    private readonly UpdateService _updates;
    private readonly VoiceService _voice;

    public FeaturesWindow(LocalDataService data, UpdateService updates, VoiceService voice)
    {
        InitializeComponent();
        _data = data;
        _updates = updates;
        _voice = voice;
        Header.Text = "50 возможностей Murzila Browser";
        FeatureList.ItemsSource = FeatureCatalog.All.Select((f, i) => new FeatureRow(f, i + 1)).ToList();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        var info = await _updates.CheckAsync();
        CheckUpdateBtn.IsEnabled = true;
        if (info is null) MessageBox.Show("Не удалось проверить обновление — нужна сеть.", "Обновление");
        else MessageBox.Show("Текущая: " + info.CurrentVersion + "\nПоследняя: " + info.LatestTag
            + "\n" + (info.IsNewer ? "Доступно обновление!" : "У тебя актуальная версия."), "Обновление");
    }

    private void Voice_Click(object sender, RoutedEventArgs e)
    {
        var names = _voice.Voices;
        MessageBox.Show(names.Count > 0
            ? "Установленные голоса:\n" + string.Join("\n", names.Take(12))
            : "Системные голоса не найдены.", "Голос");
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _data.ClearHistory();
        MessageBox.Show("История очищена.", "История");
    }
}
