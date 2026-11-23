using System.Windows;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App.Windows;

/// <summary>Простой редактор собственного текста: результат сохраняется как .lrc в папке текстов.</summary>
public sealed partial class LyricsEditorWindow : Window
{
    private readonly TrackItem _track;
    private readonly Action<string> _onSaved;

    public LyricsEditorWindow(TrackItem track, Action<string> onSaved)
    {
        InitializeComponent();
        _track = track;
        _onSaved = onSaved;
        Title = Txt.LyricsEditorTitle;
        ApplyLanguage();
        Txt.Changed += OnLanguageChanged;
        Closed += (_, _) => Txt.Changed -= OnLanguageChanged;

        string source = FindExistingLrc();
        TbLrc.Text = source.Length > 0
            ? source
            : $"[ti:{track.Title}]\n[ar:{track.Artist}]\n[00:00.00]{track.Title}\n";
    }

    private string FindExistingLrc()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_track.LyricsFile)
            && _track.LyricsFile.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
            candidates.Add(_track.LyricsFile);
        if (!string.IsNullOrWhiteSpace(_track.FileName))
            candidates.Add(Path.ChangeExtension(_track.FileName, ".lrc"));
        candidates.Add(Path.Combine(AppPaths.Lyrics, _track.Id + ".lrc"));

        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { if (File.Exists(path)) return File.ReadAllText(path); }
            catch { /* следующий кандидат */ }
        }
        return "";
    }

    private void ApplyLanguage()
    {
        TbTitle.Text = Txt.LyricsEditorTitle;
        TbHint.Text = Txt.LyricsEditorHint;
        BtnSave.Content = Txt.Save;
        BtnCancel.Content = Txt.Cancel;
    }

    private void OnLanguageChanged() => Dispatcher.BeginInvoke(ApplyLanguage);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string text = TbLrc.Text.Trim();
        if (text.Length == 0)
        {
            MessageBox.Show(Txt.LyricsEmpty, Txt.LyricsEditorTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Метаданные помогают локальному каталогу найти текст по «Артист - Название».
        if (!text.Contains("[ti:", StringComparison.OrdinalIgnoreCase))
            text = $"[ti:{_track.Title}]\n" + text;
        if (!text.Contains("[ar:", StringComparison.OrdinalIgnoreCase))
            text = $"[ar:{_track.Artist}]\n" + text;

        try
        {
            AppPaths.Ensure();
            string name = SafeName($"{_track.Artist} - {_track.Title}");
            string path = Path.Combine(AppPaths.Lyrics, name + ".lrc");
            File.WriteAllText(path, text);
            LocalLyricsCatalog.Default.Refresh();
            _onSaved(path);
            Close();
        }
        catch
        {
            MessageBox.Show(Txt.LyricsSaveError, Txt.LyricsEditorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string result = new string(chars).Trim();
        return result.Length == 0 ? "lyrics" : result;
    }
}
