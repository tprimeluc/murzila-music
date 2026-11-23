using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class VideoWindow : Window
{
    private readonly TrackItem _track;
    private readonly Action<string>? _onVideoChanged;
    private readonly Action<bool>? _onStateChanged;
    private readonly LyricsService _lyricsService = new();
    private readonly DispatcherTimer _timer;

    private LyricsDocument? _lyrics;
    private string? _videoPath;
    private TimeSpan _duration;
    private bool _dragging;
    private bool _autoPlay;
    private bool _hasMedia;
    private bool _isVideoPlaying;
    private bool _isFullscreen;
    private WindowState _previousState;
    private WindowStyle _previousStyle;

    public VideoWindow(TrackItem track, string? videoPath, LyricsDocument? lyrics,
        Action<string>? onVideoChanged = null, Action<bool>? onStateChanged = null)
    {
        InitializeComponent();
        VideoLyrics.ApplySettings();
        VideoLyrics.SeekRequested += (_, time) =>
        {
            if (_hasMedia) Player.Position = TimeSpan.FromSeconds(Math.Max(0, time));
        };
        _track = track;
        _onVideoChanged = onVideoChanged;
        _onStateChanged = onStateChanged;

        Title = $"{track.Title} — клип";
        TbVideoTitle.Text = track.Title;
        TbVideoArtist.Text = track.Artist;
        TbNoVideo.Text = Txt.VideoMissing;
        TbVideoError.Text = Txt.VideoError;
        BtnFindLyrics.Content = Txt.FindLyrics;
        BtnCenterPlay.Content = AppIcons.Create("play", 28);
        BtnPlayPause.Content = AppIcons.Create("pause", 20);

        SldVolume.Value = Math.Clamp(SettingsService.Current.Volume, 0, 1);
        Player.Volume = SldVolume.Value;

        LoadVideo(videoPath);
        if (lyrics is not null)
        {
            _lyrics = lyrics;
            VideoLyrics.SetDocument(lyrics, Txt.LyricsNotFound);
            TbLyricsStatus.Text = Txt.LyricsFound(lyrics.Source);
        }
        else
        {
            _ = LoadLyricsAsync(false);
        }

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Normal,
            OnTick, Dispatcher);
        _timer.Start();

        Txt.Changed += OnLanguageChanged;
        Closed += OnClosed;
    }

    private void LoadVideo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _hasMedia = false;
            _isVideoPlaying = false;
            _videoPath = null;
            Player.Visibility = Visibility.Collapsed;
            TbNoVideo.Visibility = Visibility.Visible;
            BtnCenterPlay.Visibility = Visibility.Collapsed;
            BtnPlayPause.IsEnabled = false;
            _onStateChanged?.Invoke(false);
            return;
        }

        _videoPath = path;
        _hasMedia = true;
        _isVideoPlaying = false;
        _autoPlay = true;
        TbNoVideo.Visibility = Visibility.Collapsed;
        TbVideoErrorBorder.Visibility = Visibility.Collapsed;
        BtnPlayPause.IsEnabled = true;
        Player.Visibility = Visibility.Visible;
        Player.Stop();
        Player.Source = new Uri(path, UriKind.Absolute);
        UpdatePlayButton();
    }

    private async Task LoadLyricsAsync(bool force)
    {
        TbLyricsStatus.Text = Txt.LyricsLoading;
        BtnFindLyrics.IsEnabled = false;
        try
        {
            var document = await _lyricsService.LoadAsync(_track, force);
            _lyrics = document;
            VideoLyrics.SetDocument(document, Txt.LyricsNotFound);
            TbLyricsStatus.Text = document is null
                ? Txt.LyricsNotFoundShort
                : Txt.LyricsFound(document.Source);
        }
        catch
        {
            VideoLyrics.SetDocument(null, Txt.LyricsNotFound);
            TbLyricsStatus.Text = Txt.LyricsNotFoundShort;
        }
        finally
        {
            BtnFindLyrics.IsEnabled = true;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_hasMedia || _duration <= TimeSpan.Zero) return;

        var position = Player.Position;
        if (!_dragging)
        {
            double fraction = position.TotalSeconds / Math.Max(0.1, _duration.TotalSeconds);
            SldPosition.Value = Math.Clamp(fraction, 0, 1);
        }

        TbPosition.Text = FormatTime(position);
        TbDuration.Text = FormatTime(_duration);
        VideoLyrics.Update(position.TotalSeconds);
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        _hasMedia = true;
        _duration = Player.NaturalDuration.HasTimeSpan
            ? Player.NaturalDuration.TimeSpan
            : TimeSpan.Zero;
        TbDuration.Text = FormatTime(_duration);
        if (_autoPlay)
        {
            Player.Play();
            _isVideoPlaying = true;
            _autoPlay = false;
        }
        UpdatePlayButton();
        _onStateChanged?.Invoke(true);
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        _autoPlay = false;
        _isVideoPlaying = false;
        SldPosition.Value = 1;
        TbPosition.Text = FormatTime(_duration);
        UpdatePlayButton();
    }

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _hasMedia = false;
        _isVideoPlaying = false;
        TbVideoErrorBorder.Visibility = Visibility.Visible;
        TbVideoError.Text = Txt.VideoError;
        BtnCenterPlay.Visibility = Visibility.Collapsed;
        BtnPlayPause.IsEnabled = false;
        UpdatePlayButton();
        _onStateChanged?.Invoke(false);
    }

    private void Player_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_hasMedia) PlayPause_Click(sender, e);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasMedia) return;

        if (_isVideoPlaying)
        {
            Player.Pause();
            _isVideoPlaying = false;
        }
        else
        {
            if (_duration > TimeSpan.Zero && Player.Position >= _duration - TimeSpan.FromMilliseconds(300))
                TrySeek(TimeSpan.Zero);
            Player.Play();
            _isVideoPlaying = true;
        }
        UpdatePlayButton();
    }

    private void UpdatePlayButton()
    {
        bool playing = _isVideoPlaying;
        BtnPlayPause.Content = AppIcons.Create(playing ? "pause" : "play", 20);
        BtnCenterPlay.Visibility = playing || !_hasMedia ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SldPosition_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _dragging = true;

    private void SldPosition_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        if (!_hasMedia) return;
        double seconds = SldPosition.Value * Math.Max(0.1, _duration.TotalSeconds);
        TrySeek(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, _duration.TotalSeconds - 0.1))));
    }

    private void TrySeek(TimeSpan position)
    {
        try { Player.Position = position; }
        catch { /* поток ещё не готов к перемотке */ }
    }

    private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Player.Volume = Math.Clamp(e.NewValue, 0, 1);
        SettingsService.Current.Volume = e.NewValue;
    }

    private async void FindLyrics_Click(object sender, RoutedEventArgs e)
    {
        await LoadLyricsAsync(true);
    }

    private void PickVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Txt.ChooseVideoTitle,
            Multiselect = false,
            Filter = "Видео|*.mp4;*.m4v;*.wmv;*.avi;*.mkv;*.mov|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        _track.VideoFile = dialog.FileName;
        _onVideoChanged?.Invoke(dialog.FileName);
        LoadVideo(dialog.FileName);
    }

    private void BackToAudio_Click(object sender, RoutedEventArgs e) => Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            WindowStyle = _previousStyle;
            WindowState = _previousState;
            _isFullscreen = false;
        }
        else
        {
            _previousStyle = WindowStyle;
            _previousState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                PlayPause_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Left:
                SeekBy(-5);
                e.Handled = true;
                break;
            case Key.Right:
                SeekBy(5);
                e.Handled = true;
                break;
            case Key.Escape:
                if (_isFullscreen) ToggleFullscreen();
                else Close();
                e.Handled = true;
                break;
        }
    }

    private void SeekBy(double seconds)
    {
        if (!_hasMedia || _duration <= TimeSpan.Zero) return;
        double target = Math.Clamp(Player.Position.TotalSeconds + seconds, 0, _duration.TotalSeconds - 0.1);
        TrySeek(TimeSpan.FromSeconds(target));
    }

    private void SeekBack_Click(object sender, RoutedEventArgs e) => SeekBy(-5);

    private void SeekForward_Click(object sender, RoutedEventArgs e) => SeekBy(5);

    private void OnLanguageChanged()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TbNoVideo.Text = Txt.VideoMissing;
            TbVideoError.Text = Txt.VideoError;
            BtnFindLyrics.Content = Txt.FindLyrics;
            BtnPickVideo.Content = Txt.VideoChoose;
            BtnBackToAudio.Content = Txt.BackToAudio;
            if (_lyrics is null) VideoLyrics.SetDocument(null, Txt.LyricsNotFound);
        }));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        Txt.Changed -= OnLanguageChanged;
        _onStateChanged?.Invoke(false);
        _isVideoPlaying = false;
        SettingsService.Save();
        Player.Stop();
        Player.Close();
    }

    private static string FormatTime(TimeSpan time)
    {
        int seconds = (int)Math.Max(0, time.TotalSeconds);
        return $"{seconds / 60}:{seconds % 60:D2}";
    }
}
