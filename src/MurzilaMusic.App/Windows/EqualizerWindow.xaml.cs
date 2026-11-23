using System.Windows;
using System.Windows.Controls;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class EqualizerWindow : Window
{
    private readonly Action? _onChanged;
    private Slider[] _sliders = Array.Empty<Slider>();
    private TextBlock[] _labels = Array.Empty<TextBlock>();
    private bool _ready;
    private string _preset = "Ровно";

    public EqualizerWindow(Action? onChanged = null)
    {
        _onChanged = onChanged;
        InitializeComponent();

        _sliders = new[] { Eq0, Eq1, Eq2, Eq3, Eq4, Eq5, Eq6, Eq7, Eq8, Eq9 };
        _labels = new[] { Eq0Value, Eq1Value, Eq2Value, Eq3Value, Eq4Value,
                          Eq5Value, Eq6Value, Eq7Value, Eq8Value, Eq9Value };

        var bands = SettingsService.Current.EqualizerBands;
        for (int i = 0; i < _sliders.Length; i++)
            _sliders[i].Value = i < bands.Count ? Math.Clamp(bands[i], -12, 12) : 0;

        ChkEnabled.IsChecked = SettingsService.Current.EqualizerEnabled;
        _preset = string.IsNullOrWhiteSpace(SettingsService.Current.EqualizerPreset)
            ? "Ровно"
            : SettingsService.Current.EqualizerPreset;

        _ready = true;
        UpdateLabels();
    }

    private void Eq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _preset = "Свой";
        Apply();
    }

    private void Enabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Apply();
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string name) return;
        _preset = name;
        var gains = EqualizerPresets.Get(name);

        _ready = false;
        for (int i = 0; i < _sliders.Length; i++)
            _sliders[i].Value = i < gains.Count ? gains[i] : 0;
        _ready = true;

        Apply();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _preset = "Ровно";
        _ready = false;
        foreach (var slider in _sliders) slider.Value = 0;
        _ready = true;
        Apply();
    }

    private void Apply()
    {
        SettingsService.Current.EqualizerEnabled = ChkEnabled.IsChecked == true;
        SettingsService.Current.EqualizerBands = _sliders
            .Select(s => Math.Round(s.Value))
            .ToList();
        SettingsService.Current.EqualizerPreset = _preset;
        SettingsService.Save();

        UpdateLabels();
        _onChanged?.Invoke();
    }

    private void UpdateLabels()
    {
        for (int i = 0; i < _sliders.Length; i++)
        {
            double value = Math.Round(_sliders[i].Value);
            _labels[i].Text = value switch
            {
                > 0 => $"+{value:0} дБ",
                < 0 => $"{value:0} дБ",
                _ => "0 дБ"
            };
        }
        TbPreset.Text = $"Пресет: {_preset}";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Apply();
        Close();
    }
}
