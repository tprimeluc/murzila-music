using System.ComponentModel;
using System.Windows;
using MurzilaBrowser.App.Services;

namespace MurzilaBrowser.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly BrowserPrefs _prefs;

    public SettingsWindow(BrowserPrefs prefs)
    {
        InitializeComponent();
        _prefs = prefs;
        SearchBox.ItemsSource = BrowserPrefs.SearchEngines.Keys.ToList();
        SearchBox.SelectedItem = prefs.SearchEngine;
        LowEndCheck.IsChecked = prefs.LowEndMode;
        GpuCheck.IsChecked = prefs.HardwareAcceleration;
        FullscreenCheck.IsChecked = prefs.FullscreenOnStart;
        VoiceCheck.IsChecked = prefs.EnableVoiceTranslation;
        LiveCheck.IsChecked = prefs.EnableLiveVoices;
        UpdateCheck.IsChecked = prefs.EnableInAppUpdates;
        ZoomSlider.Value = prefs.DefaultZoomPercent;
        ZoomText.Text = prefs.DefaultZoomPercent + "%";
        ZoomSlider.ValueChanged += (_, _) => ZoomText.Text = ZoomSlider.Value + "%";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _prefs.SearchEngine = SearchBox.SelectedItem?.ToString() ?? "DuckDuckGo";
        _prefs.LowEndMode = LowEndCheck.IsChecked == true;
        _prefs.HardwareAcceleration = GpuCheck.IsChecked == true;
        _prefs.FullscreenOnStart = FullscreenCheck.IsChecked == true;
        _prefs.EnableVoiceTranslation = VoiceCheck.IsChecked == true;
        _prefs.EnableLiveVoices = LiveCheck.IsChecked == true;
        _prefs.EnableInAppUpdates = UpdateCheck.IsChecked == true;
        _prefs.DefaultZoomPercent = (int)ZoomSlider.Value;
        _prefs.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
