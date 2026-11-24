using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MurzilaMusic.App.Controls;
using MurzilaMusic.App.Converters;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;
using MurzilaMusic.App.Windows;

namespace MurzilaMusic.App;

public partial class MainWindow : Window
{
    private readonly LibraryService _library = new();
    private readonly CloudSyncService _sync;
    private readonly PlaybackService _player = new();
    private readonly LyricsService _lyricsService = new();
    private readonly ObservableCollection<TrackItem> _view = new();
    private readonly PathToImageConverter _coverConverter = new();
    private readonly DispatcherTimer _timer;

    private TrackItem? _current;
    private LyricsDocument? _lyrics;
    private string _filter = "all";
    private bool _draggingProgress;
    private bool _refreshingPlaylists;
    private bool _showRemaining;
    private double _volumeBeforeMute = 0.75;
    private readonly Stack<TrackItem> _history = new();
    private VideoWindow? _videoWindow;
    private bool _videoWasPlaying;
    private int _busyDepth;

    public MainWindow()
    {
        InitializeComponent();
        IconSearch.Data = AppIcons.Get("search");
        // Файлы, которые уже лежат в Tracks, но потерялись в library.json,
        // возвращаем в список — трек не должен «исчезать» из приложения.
        _library.ReconcileFromDisk();
        _sync = new CloudSyncService(_library);

        NpLyrics.ApplySettings();
        NpLyrics.SeekRequested += (_, time) =>
        {
            if (_current is null) return;
            _player.Seek(time);
            // Перемотка доступна и на паузе — сразу обновляем ползунок, время и текст.
            if (!_draggingProgress)
                SldProgress.Value = Math.Clamp(_player.PositionSeconds / Math.Max(0.1, _player.DurationSeconds), 0, 1);
            UpdateTimeLabels();
            NpLyrics.Update(_player.PositionSeconds);
        };
        Season.Refresh();
        UpdateAudioOutputButton();

        Icon = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "murzila-icon.png"), UriKind.Absolute));
        LbTracks.ItemsSource = _view;

        SldVolume.Value = SettingsService.Current.RememberVolume
            ? Math.Clamp(SettingsService.Current.Volume, 0, 1)
            : 0.75;
        _showRemaining = SettingsService.Current.ShowRemainingTime;
        _volumeBeforeMute = SldVolume.Value > 0 ? SldVolume.Value : 0.75;
        _player.Volume = SettingsService.Current.Volume;
        BtnShuffle.Opacity = SettingsService.Current.Shuffle ? 1 : 0.45;
        UpdateRepeatButton();
        UpdateMuteButton();
        _player.PlaybackEnded += Player_PlaybackEnded;
        _player.StateChanged += Player_StateChanged;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Normal, OnTick, Dispatcher);
        _timer.Start();

        RefreshTracks();
        ApplyLanguage();
        ApplyViewOptions();
        UpdatePlayerUi();

        Txt.Changed += OnLanguageChanged;
        _ = InitializeStoreAsync();
        _ = AutoSyncAsync();

        AllowDrop = true;
        Drop += MainWindow_Drop;
        DragOver += MainWindow_DragOver;
        Closed += MainWindow_Closed;
    }

    private void ShowBusy(string text)
    {
        _busyDepth++;
        BusyText.Text = text;
        BusyProgress.IsIndeterminate = true;
        BusyOverlay.Visibility = Visibility.Visible;
    }

    private void HideBusy()
    {
        _busyDepth = Math.Max(0, _busyDepth - 1);
        if (_busyDepth == 0) BusyOverlay.Visibility = Visibility.Collapsed;
    }

    // ---------- магазин, бэкапы и импорт ----------

    private async Task InitializeStoreAsync()
    {
        await CatalogService.RefreshAsync();
        SettingsService.Current.CatalogVersion = CatalogService.Current.Version;
        if (!SettingsService.Current.LoggedIn || string.IsNullOrWhiteSpace(SettingsService.Current.AccessToken)) return;
        try
        {
            var state = await ApiClient.GetAccountStateAsync(SettingsService.Current.AccessToken);
            CatalogService.Apply(state);
        }
        catch
        {
            // Офлайн: локальные настройки и Built-in каталог продолжают работать.
        }
    }

    /// <summary>
    /// При запуске подтягиваем свои треки из аккаунта: трек, загруженный через веб,
    /// должен появиться в приложении без ручного нажатия «Треки на других устройствах».
    /// </summary>
    private async Task AutoSyncAsync()
    {
        if (!SettingsService.Current.LoggedIn
            || string.IsNullOrWhiteSpace(SettingsService.Current.AccessToken)) return;
        try
        {
            int recovered = _library.ReconcileFromDisk();
            if (recovered > 0) RefreshTracks();
            var result = await _sync.RunAsync();
            try
            {
                string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MurzilaMusic", "sync.log");
                File.AppendAllText(log,
                    $"{DateTime.Now:O} url={ApiClient.BaseUrl} up={result.Uploaded} down={result.Downloaded} skip={result.Skipped} {result.Message}\n");
            }
            catch { }
            if (result.Downloaded > 0 || result.Uploaded > 0 || recovered > 0)
            {
                RefreshTracks();
                TbStatus.Text = $"Свои треки обновлены: скачано {result.Downloaded}, загружено {result.Uploaded}.";
            }
        }
        catch (Exception ex)
        {
            // Офлайн — локальная библиотека продолжает работать, но причину пишем в лог.
            try
            {
                string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MurzilaMusic", "sync-error.log");
                File.AppendAllText(log, $"{DateTime.Now:O} url={ApiClient.BaseUrl} {ex}\n");
            }
            catch { }
        }
    }

    /// <summary>После добавления сразу отправляем трек в свой сервер, чтобы он был в вебе.</summary>
    private async Task SyncAfterAddAsync(string? addedMessage)
    {
        if (!SettingsService.Current.LoggedIn
            || string.IsNullOrWhiteSpace(SettingsService.Current.AccessToken)) return;
        try
        {
            var result = await _sync.RunAsync();
            RefreshTracks();
            string sync = result.Uploaded > 0
                ? $" В сервер загружено: {result.Uploaded}."
                : "";
            if (!string.IsNullOrWhiteSpace(addedMessage)) TbStatus.Text = addedMessage + sync;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(addedMessage))
                TbStatus.Text = addedMessage + " Синхронизация не удалась: " + ex.Message;
        }
    }

    private async void BtnImportPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Txt.ImportPlaylistDialogTitle,
            Filter = Txt.ImportPlaylistFilter
        };
        if (dialog.ShowDialog() != true) return;
        TbStatus.Text = Txt.ImportPlaylistStatus;
        ShowBusy(Txt.ImportPlaylistBusy);
        try
        {
            var result = await PlaylistImportService.ImportAsync(_library, dialog.FileName);
            RefreshTracks();
            TbStatus.Text = Txt.PlaylistImported(result.PlaylistName, result.Added, result.Matched, result.Skipped);
        }
        catch (Exception ex) { TbStatus.Text = ex.Message; }
        finally { HideBusy(); }
    }

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SettingsService.Current.AccessToken))
        {
            TbStatus.Text = Txt.SyncLoginHint;
            return;
        }
        BtnSync.IsEnabled = false;
        ShowBusy(Txt.SyncBusy);
        var progress = new Progress<string>(text =>
        {
            BusyText.Text = text;
            TbStatus.Text = text;
        });
        try
        {
            var result = await _sync.RunAsync(progress);
            RefreshTracks();
            TbStatus.Text = result.Message;
        }
        catch (Exception ex)
        {
            TbStatus.Text = ex.Message;
        }
        finally
        {
            HideBusy();
            BtnSync.IsEnabled = true;
        }
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        var window = new BackupWindow(_library) { Owner = this };
        if (window.ShowDialog() == true)
        {
            RefreshTracks();
            ApplyLanguage();
            ApplyViewOptions();
            UpdatePlayerUi();
            TbStatus.Text = Txt.LibraryRestored;
        }
    }

    private void MenuComment_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTrack() is not { } track) return;
        var window = new CommentWindow(track) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _library.SetComment(track.Id, window.Comment);
            RefreshTracks();
            UpdatePlayerUi();
            TbStatus.Text = Txt.CommentSaved;
        }
    }

    // ---------- библиотека и список ----------

    private void RefreshTracks()
    {
        _view.Clear();
        IEnumerable<TrackItem> source = _filter switch
        {
            "favorites" => _library.Tracks.Where(t => t.IsFavorite),
            "all" => _library.Tracks,
            _ => _library.Playlists.Where(p => p.Id == _filter)
                    .SelectMany(p => p.TrackIds)
                    .Select(id => _library.Find(id))
                    .OfType<TrackItem>()
        };

        string query = TbSearch.Text.Trim();
        if (query.Length > 0)
            source = source.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                                  || t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
                                  || t.Album.Contains(query, StringComparison.OrdinalIgnoreCase));

        IEnumerable<TrackItem> ordered = CbSort.SelectedIndex switch
        {
            1 => source.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
            2 => source.OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
                      .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
            3 => source.OrderByDescending(t => t.PlayCount).ThenByDescending(t => t.AddedAt),
            _ => source.OrderByDescending(t => t.AddedAt)
        };
        foreach (var track in ordered) _view.Add(track);

        if (_view.Count == 0)
        {
            TbStatus.Text = SettingsService.Current.LoggedIn
                ? "Своих треков пока нет — добавь файлы или нажми «Треки на других устройствах»."
                : "Своих треков пока нет. Войди в аккаунт и нажми «Треки на других устройствах» — свои треки скачаются с сервера.";
        }
        else
            TbStatus.Text = Txt.TrackCount(_view.Count, _library.Data.TotalPlayed);

        _refreshingPlaylists = true;
        LbPlaylists.ItemsSource = _library.Playlists.ToList();
        _refreshingPlaylists = false;
    }

    /// <summary>
    /// Спрашивает у пользователя данные трека перед добавлением: название, артиста, альбом.
    /// Так в библиотеке не остаются имена файлов вроде «01 - track». Отмена — не добавляем.
    /// </summary>
    private TrackInfoDraft? AskTrackInfo(string[] files)
    {
        bool multiple = files.Length > 1;
        string header = multiple ? "Информация о выбранных треках" : "Информация о треке";
        string sub = multiple
            ? $"Выбрано файлов: {files.Length}. Можно заполнить общие данные для всех или пропустить."
            : Path.GetFileName(files[0]);

        var draft = new TrackInfoDraft
        {
            Title = multiple ? "" : Path.GetFileNameWithoutExtension(files[0]),
            ApplyToAll = multiple
        };
        return TrackInfoWindow.Ask(this, header, sub, draft, allowApplyAll: multiple, allowSkip: true);
    }

    private void MenuInfo_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTrack() is not { } track) return;
        var draft = new TrackInfoDraft
        {
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            Comment = track.Comment,
            AllowExternal = track.AllowExternal
        };
        var result = TrackInfoWindow.Ask(this, "Информация о треке",
            Path.GetFileName(track.FileName), draft);
        if (result is null) return;
        _library.UpdateInfo(track, result);
        RefreshTracks();
        TbStatus.Text = Txt.TrackInfoSaved;
    }

    private async void BtnAddTracks_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Txt.AddTracksDialogTitle,
            Multiselect = true,
            Filter = Txt.AudioFilter
        };
        if (dialog.ShowDialog() != true) return;

        var info = AskTrackInfo(dialog.FileNames);
        if (info is null) return;

        TbStatus.Text = Txt.AddingTracks;
        int total = dialog.FileNames.Length;
        int done = 0;
        ShowBusy(Txt.AddingTracks);
        var progress = new Progress<string>(name =>
        {
            done++;
            string text = $"{Txt.Adding(name)} ({done}/{total})";
            TbStatus.Text = text;
            BusyText.Text = text;
        });
        try
        {
            int added = await _library.AddFilesAsync(dialog.FileNames, progress, info);
            _filter = "all";
            RefreshTracks();
            string message = added > 0 ? Txt.AddedTracks(added) : Txt.NoNewTracks;
            TbStatus.Text = message;
            if (added > 0) await SyncAfterAddAsync(message);
        }
        finally { HideBusy(); }
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
        => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        // Перетащенный .lrc сразу прикрепляется к текущему треку.
        string? lrc = files.FirstOrDefault(f =>
            f.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase));
        if (lrc is not null)
        {
            AttachLyricsFile(lrc);
            return;
        }

        var audio = files.Where(LibraryService.IsAudio).ToList();
        if (audio.Count == 0) return;
        var info = AskTrackInfo(audio.ToArray());
        if (info is null) return;
        TbStatus.Text = Txt.AddingTracks;
        ShowBusy(Txt.AddingTracks);
        try
        {
            int added = await _library.AddFilesAsync(audio, null, info);
            RefreshTracks();
            string message = Txt.AddedTracks(added);
            TbStatus.Text = message;
            if (added > 0) await SyncAfterAddAsync(message);
        }
        finally { HideBusy(); }
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        // ComboBox инициализируется раньше, чем остальные поля окна.
        if (TbStatus is null) return;
        RefreshTracks();
    }

    private TrackItem? SelectedTrack() => LbTracks.SelectedItem as TrackItem;

    private void MenuPlay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTrack() is { } track) PlayTrack(track);
    }

    private void MenuFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTrack() is not { } track) return;
        _library.ToggleFavorite(track.Id);
        RefreshTracks();
        UpdateHeart();
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTrack() is not { } track) return;
        if (SettingsService.Current.ConfirmBeforeDelete
            && MessageBox.Show(Txt.DeleteTrackConfirm(track.Title), Txt.DeleteTrack,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _library.RemoveFromLibrary(track.Id);
        RefreshTracks();
        TbStatus.Text = Txt.TrackDeleted;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshTracks();

    private void NavAll_Click(object sender, RoutedEventArgs e)
    {
        _filter = "all";
        _refreshingPlaylists = true;
        LbPlaylists.SelectedItem = null;
        _refreshingPlaylists = false;
        RefreshTracks();
    }

    private void NavHome_Click(object sender, RoutedEventArgs e)
    {
        TbSearch.Text = "";
        _filter = "all";
        _refreshingPlaylists = true;
        LbPlaylists.SelectedItem = null;
        _refreshingPlaylists = false;
        RefreshTracks();
        if (_view.Count > 0) LbTracks.ScrollIntoView(_view[0]);
        TbStatus.Text = Txt.HomeTip;
    }

    private void NavFavorites_Click(object sender, RoutedEventArgs e)
    {
        _filter = "favorites";
        RefreshTracks();
    }

    private void NavQuick_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Txt.QuickTitle,
            Multiselect = false,
            Filter = Txt.AudioVideoFilter
        };
        if (dialog.ShowDialog() != true) return;
        PlayLocalFile(dialog.FileName);
    }

    /// <summary>Быстрое прослушивание файла с диска без копирования в библиотеку.</summary>
    public void PlayLocalFile(string path)
    {
        if (!File.Exists(path)) return;

        var quick = new TrackItem
        {
            Id = "quick-" + Guid.NewGuid().ToString("N"),
            Title = Path.GetFileNameWithoutExtension(path),
            Artist = Txt.LocalFileArtist,
            FileName = path
        };

        try
        {
            LibraryService.ReadTags(path, quick);
        }
        catch
        {
            // без тегов тоже можно слушать
        }

        if (string.IsNullOrWhiteSpace(quick.Artist))
            quick.Artist = Txt.LocalFileArtist;

        if (LibraryService.IsVideo(path)) quick.VideoFile = path;

        TbStatus.Text = Txt.LocalFileStatus(Path.GetFileName(path));
        PlayTrack(quick);
    }

    private void LbPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingPlaylists) return;
        if (e.AddedItems.OfType<Playlist>().FirstOrDefault() is { } playlist)
        {
            _filter = playlist.Id;
            RefreshTracks();
        }
    }

    private void LbTracks_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LbTracks.SelectedItem is TrackItem track) PlayTrack(track);
    }

    private void RowFavorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id) return;
        _library.ToggleFavorite(id);
        RefreshTracks();
        UpdateHeart();
    }

    // ---------- воспроизведение ----------

    private void PlayTrack(TrackItem track, bool rememberInHistory = true)
    {
        if (_videoWindow is not null)
        {
            _videoWasPlaying = false;
            _videoWindow.Close();
        }

        if (rememberInHistory && _current is not null && !ReferenceEquals(_current, track))
            _history.Push(_current);

        var decision = AccessPolicy.Check(track,
            SettingsService.Current.UnlockedTracks.Contains(track.Id),
            _library.Data, SettingsService.Current);

        if (!decision.Allowed)
        {
            var paywall = new PaywallWindow(track, decision) { Owner = this };
            if (paywall.ShowDialog() != true) return;
            decision = AccessPolicy.Check(track,
                SettingsService.Current.UnlockedTracks.Contains(track.Id),
                _library.Data, SettingsService.Current);
            if (!decision.Allowed) return;
        }

        try
        {
            _player.Play(track);
        }
        catch
        {
            TbStatus.Text = Txt.FileOpenError;
            return;
        }

        _current = track;
        _lyrics = null;
        NpLyrics.SetDocument(null, Txt.LyricsNotFound);
        TbLyricsStatus.Text = SettingsService.Current.AutoLoadLyrics
            ? Txt.LyricsLoading
            : Txt.LyricsNotFoundShort;
        if (_library.Find(track.Id) is not null) _library.RegisterPlay(track.Id);
        UpdatePlayerUi();
        _ = LoadLyricsAsync(track);
    }

    private async Task LoadLyricsAsync(TrackItem track)
    {
        if (!SettingsService.Current.AutoLoadLyrics) return;
        await FindLyricsAsync(track, false);
    }

    private async Task FindLyricsAsync(TrackItem? track = null, bool force = true)
    {
        track ??= _current;
        if (track is null) return;

        BtnFindLyrics.IsEnabled = false;
        TbLyricsStatus.Text = Txt.LyricsLoading;
        ShowBusy(Txt.LyricsLoading);
        try
        {
            var document = await _lyricsService.LoadAsync(track, force);
            if (_current != track) return;
            _lyrics = document;
            NpLyrics.SetDocument(document, Txt.LyricsNotFound);
            TbLyricsStatus.Text = document is null
                ? Txt.LyricsNotFoundShort
                : Txt.LyricsFound(document.Source);
        }
        catch
        {
            if (_current == track)
            {
                _lyrics = null;
                NpLyrics.SetDocument(null, Txt.LyricsNotFound);
                TbLyricsStatus.Text = Txt.LyricsNotFoundShort;
            }
        }
        finally
        {
            BtnFindLyrics.IsEnabled = true;
            HideBusy();
        }
    }

    private void BtnFindLyrics_Click(object sender, RoutedEventArgs e) => _ = FindLyricsAsync();

    private void AttachLyricsFile(string path)
    {
        if (_current is null)
        {
            TbStatus.Text = Txt.NothingPlaying;
            return;
        }
        _current.LyricsFile = path;
        if (_library.Find(_current.Id) is not null) _library.Save();
        LocalLyricsCatalog.Default.Refresh();
        TbStatus.Text = Txt.LyricsAttached(Path.GetFileName(path));
        _ = FindLyricsAsync(_current, true);
    }

    private void BtnOpenLyricsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Lyrics) { UseShellExecute = true });
        }
        catch { /* Проводник может быть недоступен — не критично. */ }
    }

    private void BtnEditLyrics_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            TbStatus.Text = Txt.NothingPlaying;
            return;
        }
        var editor = new LyricsEditorWindow(_current, path => AttachLyricsFile(path)) { Owner = this };
        editor.ShowDialog();
    }

    private void Player_PlaybackEnded(object? sender, EventArgs e)
        => Dispatcher.Invoke(() => NextTrack(fromEnding: true));

    private void Player_StateChanged(object? sender, EventArgs e)
        => Dispatcher.Invoke(UpdatePlayButton);

    private void UpdatePlayButton() => BtnPlay.Content = AppIcons.Create(_player.IsPlaying ? "pause" : "play", 22);

    private void UpdatePlayerUi()
    {
        if (_current is null)
        {
            CurrentTitle.Text = Txt.NothingPlaying;
            CurrentArtist.Text = "—";
            NpTitle.Text = "—";
            NpArtist.Text = "—";
            NpComment.Text = "";
            NpCover.Source = _coverConverter.Convert(null, typeof(ImageSource), null, System.Globalization.CultureInfo.CurrentCulture) as ImageSource;
            CurrentCover.Source = NpCover.Source;
            TbTime.Text = "0:00";
            TbTotal.Text = "0:00";
            SldProgress.Value = 0;
            LbTracks.SelectedItem = null;
            UpdateHeart();
            return;
        }

        var cover = _coverConverter.Convert(_current.CoverFile, typeof(ImageSource), null,
            System.Globalization.CultureInfo.CurrentCulture) as ImageSource;
        CurrentCover.Source = cover;
        NpCover.Source = cover;
        CurrentTitle.Text = _current.Title;
        CurrentArtist.Text = _current.Artist;
        NpTitle.Text = _current.Title;
        NpArtist.Text = _current.Artist;
        NpComment.Text = _current.Comment;
        TbTotal.Text = FormatTime(_current.DurationSeconds);
        if (_view.Contains(_current)) LbTracks.SelectedItem = _current;
        UpdateHeart();
    }

    private void UpdateHeart()
    {
        bool favorite = _current is not null && _current.IsFavorite;
        BtnPlayerFavorite.Content = AppIcons.Create(favorite ? "heart" : "heartOutline", 20);
        BtnPlayerFavorite.Foreground = favorite ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Color.FromRgb(155, 155, 168));
    }

    private static string FormatTime(double seconds)
    {
        int s = (int)Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:D2}";
    }

    private void UpdateTimeLabels()
    {
        double position = _player.PositionSeconds;
        double duration = _player.DurationSeconds;
        TbTime.Text = _showRemaining
            ? "−" + FormatTime(Math.Max(0, duration - position))
            : FormatTime(position);
        TbTotal.Text = FormatTime(duration);
    }

    private void TbTime_Click(object sender, MouseButtonEventArgs e)
    {
        _showRemaining = !_showRemaining;
        TbTime.ToolTip = Txt.TimeToggleTip;
        UpdateTimeLabels();
    }

    private void BtnShuffle_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.Shuffle = !SettingsService.Current.Shuffle;
        SettingsService.Save();
        BtnShuffle.Opacity = SettingsService.Current.Shuffle ? 1 : 0.45;
        TbStatus.Text = SettingsService.Current.Shuffle ? Txt.ShuffleOn : Txt.ShuffleOff;
    }

    private void BtnRepeat_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.RepeatMode = (SettingsService.Current.RepeatMode + 1) % 3;
        SettingsService.Save();
        UpdateRepeatButton();
    }

    private void UpdateRepeatButton()
    {
        int mode = Math.Clamp(SettingsService.Current.RepeatMode, 0, 2);
        BtnRepeat.Content = AppIcons.Create(mode == 2 ? "repeatOne" : "repeat", 19);
        BtnRepeat.Opacity = mode == 0 ? 0.45 : 1;
        BtnRepeat.ToolTip = mode switch
        {
            1 => Txt.RepeatAllTip,
            2 => Txt.RepeatOneTip,
            _ => Txt.RepeatTip
        };
    }

    private void BtnAudioOutput_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        if (SettingsService.Current.BluetoothHint)
            menu.Items.Add(new MenuItem { Header = Txt.AudioOutputTip, IsEnabled = false });
        foreach (var device in AudioOutputService.GetDevices())
        {
            var item = new MenuItem
            {
                Header = device.Name,
                Tag = device.Number,
                IsCheckable = true,
                IsChecked = device.Number == SettingsService.Current.OutputDeviceNumber
            };
            item.Click += AudioDevice_Click;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var settingsItem = new MenuItem { Header = Txt.OpenSoundSettings };
        settingsItem.Click += (_, _) => AudioOutputService.OpenSoundSettings();
        menu.Items.Add(settingsItem);

        BtnAudioOutput.ContextMenu = menu;
        menu.PlacementTarget = BtnAudioOutput;
        menu.IsOpen = true;
    }

    private void AudioDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int number }) return;

        SwitchAudioOutput(number);
    }

    private void SwitchAudioOutput(int number)
    {
        double position = _player.PositionSeconds;
        bool wasPlaying = _player.IsPlaying;
        SettingsService.Current.OutputDeviceNumber = number;
        SettingsService.Save();
        UpdateAudioOutputButton();

        if (_current is null) return;
        try
        {
            _player.Play(_current);
            if (position > 0) _player.Seek(position);
            if (!wasPlaying) _player.Pause();
        }
        catch
        {
            TbStatus.Text = Txt.FileOpenError;
        }
    }

    private void UpdateAudioOutputButton()
    {
        BtnAudioOutput.ToolTip = Txt.AudioOutputCurrent(
            AudioOutputService.NameOf(SettingsService.Current.OutputDeviceNumber));
    }

    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        if (SldVolume.Value > 0.001)
        {
            _volumeBeforeMute = SldVolume.Value;
            SldVolume.Value = 0;
        }
        else
        {
            SldVolume.Value = _volumeBeforeMute > 0 ? _volumeBeforeMute : 0.75;
        }
    }

    private void UpdateMuteButton()
    {
        bool muted = SldVolume.Value <= 0.001;
        BtnMute.Content = AppIcons.Create(muted ? "mute" : "volume", 18);
        BtnMute.ToolTip = muted ? Txt.UnmuteTip : Txt.MuteTip;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Позиция, время и текст обновляются и на паузе — иначе перемотка
        // по клику выглядела бы так, будто ничего не произошло.
        double position = _player.PositionSeconds;
        double duration = Math.Max(0.1, _player.DurationSeconds);
        if (!_draggingProgress) SldProgress.Value = Math.Clamp(position / duration, 0, 1);
        UpdateTimeLabels();
        NpLyrics.Update(position);

        if (_player.IsPlaying && NpVisualizer.Visibility == Visibility.Visible)
        {
            var bands = new float[64];
            _player.GetBands(bands);
            NpVisualizer.Update(bands);
        }
        else
        {
            NpVisualizer.Reset();
        }
    }

    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            if (_view.Count == 0)
            {
                TbStatus.Text = Txt.FirstAddTracks;
                return;
            }
            PlayTrack(_view[0]);
            return;
        }
        _player.TogglePause();
    }

    private void NextTrack(bool fromEnding = false)
    {
        if (_view.Count == 0) return;
        int mode = Math.Clamp(SettingsService.Current.RepeatMode, 0, 2);

        // Повтор одного трека: перемотка на начало и продолжение.
        if (fromEnding && mode == 2)
        {
            _player.Seek(0);
            if (!_player.IsPlaying) _player.Resume();
            return;
        }

        if (fromEnding && !SettingsService.Current.AutoPlayNext)
        {
            _player.Stop();
            UpdatePlayButton();
            return;
        }

        int index = _current is null ? -1 : _view.IndexOf(_current);
        if (index < 0) { PlayTrack(_view[0]); return; }

        if (SettingsService.Current.Shuffle && _view.Count > 1)
        {
            int next = Random.Shared.Next(_view.Count - 1);
            if (next >= index) next++;
            PlayTrack(_view[next]);
            return;
        }

        // Без повтора на последнем треке просто останавливаемся.
        if (fromEnding && mode == 0 && index == _view.Count - 1)
        {
            _player.Stop();
            UpdatePlayButton();
            return;
        }

        PlayTrack(_view[(index + 1) % _view.Count]);
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e) => NextTrack();

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_view.Count == 0) return;

        // Сначала возвращаемся по истории, включая перемешанные переходы.
        while (_history.Count > 0)
        {
            var previous = _history.Pop();
            if (!_view.Contains(previous)) continue;
            PlayTrack(previous, rememberInHistory: false);
            return;
        }

        int index = _current is null ? -1 : _view.IndexOf(_current);
        if (index < 0) { PlayTrack(_view[0]); return; }
        PlayTrack(_view[(index - 1 + _view.Count) % _view.Count]);
    }

    private void SldProgress_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _draggingProgress = true;

    private void SldProgress_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _draggingProgress = false;
        _player.Seek(SldProgress.Value * _player.DurationSeconds);
    }

    private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double volume = SldVolume.Value;
        _player.Volume = volume;
        if (SettingsService.Current.RememberVolume) SettingsService.Current.Volume = volume;
        if (volume > 0) _volumeBeforeMute = volume;
        UpdateMuteButton();
        SettingsService.Save();
    }

    private void BtnPlayerFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_library.Find(_current.Id) is null)
        {
            TbStatus.Text = Txt.QuickFavorite;
            return;
        }
        _library.ToggleFavorite(_current.Id);
        RefreshTracks();
        UpdateHeart();
    }

    private void Artist_Click(object sender, MouseButtonEventArgs e)
    {
        string artist = _current?.Artist ?? CurrentArtist.Text;
        if (string.IsNullOrWhiteSpace(artist)) return;
        var tracks = _library.Tracks.Where(t => t.Artist == artist).ToList();
        var window = new ArtistWindow(artist, tracks, track => PlayTrack(track)) { Owner = this };
        window.Show();
    }

    // ---------- текст, визуализатор, видео ----------

    private void BtnLyrics_Click(object sender, RoutedEventArgs e)
    {
        bool show = NowPlayingOverlay.Visibility != Visibility.Visible;
        NowPlayingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && _current is not null && _lyrics is null) _ = LoadLyricsAsync(_current);
    }

    private void CloseNowPlaying_Click(object sender, RoutedEventArgs e)
        => NowPlayingOverlay.Visibility = Visibility.Collapsed;

    private void BtnVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_videoWindow is not null)
        {
            _videoWindow.Activate();
            return;
        }

        // Аудио ставится на паузу, чтобы дорожки не играли одновременно.
        _videoWasPlaying = _player.IsPlaying;
        _player.Pause();

        _videoWindow = new VideoWindow(_current, _current.VideoFile, _lyrics,
            OnVideoAttached, OnVideoStateChanged)
        {
            Owner = this
        };
        _videoWindow.Closed += VideoWindow_Closed;
        _videoWindow.Show();
    }

    /// <summary>Видео включается через аудио-кнопки только когда видео реально открылось, а не по клику.</summary>
    private void OnVideoStateChanged(bool videoLoaded)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_videoWindow is null) return;
            BtnVideo.Visibility = videoLoaded ? Visibility.Collapsed : Visibility.Visible;
            BtnHeadphones.Visibility = videoLoaded ? Visibility.Visible : Visibility.Collapsed;
        }));
    }

    private void OnVideoAttached(string path)
    {
        if (_current is null) return;
        _current.VideoFile = path;
        if (_library.Find(_current.Id) is not null) _library.Save();
        TbStatus.Text = Txt.VideoAttached(Path.GetFileName(path));
    }

    private void VideoWindow_Closed(object? sender, EventArgs e)
    {
        _videoWindow = null;
        BtnVideo.Visibility = Visibility.Visible;
        BtnHeadphones.Visibility = Visibility.Collapsed;

        bool resume = _videoWasPlaying;
        _videoWasPlaying = false;
        if (resume) _player.Resume();
    }

    private void BtnHeadphones_Click(object sender, RoutedEventArgs e) => _videoWindow?.Close();

    // ---------- скачать, плейлист, публикация ----------

    private void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var dialog = new SaveFileDialog
        {
            Title = Txt.SaveTrackTitle,
            FileName = _current.Title + Path.GetExtension(_current.FileName),
            Filter = Txt.SaveTrackFilter
        };
        if (dialog.ShowDialog() == true)
        {
            try { File.Copy(_current.FileName, dialog.FileName, true); TbStatus.Text = Txt.TrackSaved; }
            catch { TbStatus.Text = Txt.TrackSaveError; }
        }
    }

    private void BtnPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_library.Find(_current.Id) is null)
        {
            TbStatus.Text = Txt.QuickPlaylist;
            return;
        }
        var picker = new PlaylistPickerWindow(_library) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPlaylistId is { } id)
        {
            _library.AddToPlaylist(id, _current.Id);
            RefreshTracks();
            TbStatus.Text = Txt.AddedToPlaylist;
        }
    }

    // ---------- аккаунт, эквалайзер, настройки ----------

    private void BtnAccount_Click(object sender, RoutedEventArgs e)
    {
        var account = new AccountWindow { Owner = this };
        account.ShowDialog();
        UpdateAccountButton();
        if (_current is null) UpdatePlayerUi();
    }

    private void BtnEqualizer_Click(object sender, RoutedEventArgs e)
    {
        var equalizer = new EqualizerWindow(() => _player.ApplyEqualizer()) { Owner = this };
        equalizer.ShowDialog();
    }

    private void UpdateAccountButton()
    {
        bool loggedIn = SettingsService.Current.LoggedIn;
        BtnAccount.Content = AppIcons.WithText(
            "user",
            loggedIn ? SettingsService.Current.DisplayName : Txt.Login,
            17);
        BtnAccount.ToolTip = loggedIn ? Txt.Profile : Txt.AccountTitle;
    }

    private void SetSortItem(int index, string text)
    {
        if (CbSort.Items.Count > index && CbSort.Items[index] is ComboBoxItem item)
            item.Content = text;
    }

    /// <summary>Применить настройки внешнего вида без перезапуска приложения.</summary>
    private void ApplyViewOptions()
    {
        var settings = SettingsService.Current;
        LbTracks.ItemTemplate = (DataTemplate)FindResource(
            settings.CompactMode ? "TrackTemplateCompact" : "TrackTemplate");
        NpCover.Visibility = settings.ShowNowPlayingCover ? Visibility.Visible : Visibility.Collapsed;
        NpVisualizer.Visibility = settings.ShowVisualizer ? Visibility.Visible : Visibility.Collapsed;
        _showRemaining = settings.ShowRemainingTime;
        UpdateTimeLabels();
        LbTracks.Items.Refresh();
    }

    private void ApplyLanguage()
    {
        TbGreeting.Text = Txt.Greeting;
        TbSearch.ToolTip = Txt.SearchTip;
        TbOnlineBadge.Text = Txt.HeaderOnlineBadge;
        BtnSettings.ToolTip = Txt.SettingsTip;

        // Промо-карточка
        TbPromoBadge.Text = Txt.PromoBadge;
        TbPromoTitle.Text = Txt.PromoTitle;
        TbPromoText.Text = Txt.PromoText;
        BtnPromoFeatures.Content = Txt.PromoButton;

        // Боковая панель
        TbMenuCaption.Text = Txt.MenuCaption;
        NavHome.Content = AppIcons.WithText("home", Txt.NavHome);
        NavHome.ToolTip = Txt.NavHomeTip;
        NavAll.Content = AppIcons.WithText("music", Txt.NavAll);
        NavFavorites.Content = AppIcons.WithText("heart", Txt.NavFavorites);
        TbPlaylistsCaption.Text = Txt.PlaylistsCaption;
        BtnImportPlaylist.Content = AppIcons.WithText("import", Txt.ImportPlaylist);
        BtnImportPlaylist.ToolTip = Txt.ImportPlaylistTip;
        BtnBackup.Content = AppIcons.WithText("backup", Txt.Backup);
        BtnSync.Content = AppIcons.WithText("cloud", Txt.SyncTracks);
        BtnSync.ToolTip = Txt.SyncTip;
        BtnFeatures.Content = AppIcons.WithText("sparkle", Txt.FeaturesNav);
        BtnFeatures.ToolTip = Txt.FeaturesTip;
        TbOnlyOwn.Text = Txt.OnlyOwnBanner;

        // Герой-баннер и заголовки разделов
        TbHeroKicker.Text = Txt.HeroKicker;
        TbHeroTitle.Text = Txt.HeroTitle;
        TbHeroText.Text = Txt.HeroText;
        BtnHeroAdd.Content = AppIcons.WithText("plus", Txt.HeroAdd);
        BtnHeroFeatures.Content = AppIcons.WithText("sparkle", Txt.HeroFeatures);
        TbMyMusicTitle.Text = Txt.MyMusicTitle;
        TbSortHint.Text = Txt.SortHint;
        TbLyricsTitle.Text = Txt.LyricsTitle;
        BtnBackToPlayer.Content = AppIcons.WithText("back", Txt.BackToPlayer);

        // Подсказки внутри шаблонов строк обновляются через DynamicResource
        if (Application.Current is not null)
        {
            Application.Current.Resources["RowFavoriteTip"] = Txt.RowFavoriteTip;
            Application.Current.Resources["RowPublishTip"] = Txt.RowPublishTip;
        }

        NavQuick.Content = AppIcons.WithText("headphones", Txt.QuickTitle);
        NavQuick.ToolTip = Txt.QuickTip;
        BtnAddTracks.Content = AppIcons.WithText("plus", Txt.AddTracks);
        BtnSettings.Content = AppIcons.Create("settings", 20);
        BtnShuffle.Content = AppIcons.Create("shuffle", 19);
        BtnPrev.Content = AppIcons.Create("previous", 20);
        BtnNext.Content = AppIcons.Create("next", 20);
        BtnAudioOutput.Content = AppIcons.Create("volume", 18);
        BtnLyrics.Content = AppIcons.Create("lyrics", 18);
        BtnVideo.Content = AppIcons.Create("video", 18);
        BtnHeadphones.Content = AppIcons.Create("headphones", 18);
        BtnEqualizer.Content = AppIcons.Create("equalizer", 18);
        BtnDownload.Content = AppIcons.Create("download", 18);
        BtnPlaylist.Content = AppIcons.Create("plus", 18);
        BtnLyrics.ToolTip = Txt.LyricsAndVisualizer;
        BtnVideo.ToolTip = Txt.VideoClip;
        BtnEqualizer.ToolTip = Txt.EqualizerTitle;
        BtnDownload.ToolTip = Txt.Download;
        BtnPlaylist.ToolTip = Txt.AddToPlaylist;
        BtnHeadphones.ToolTip = Txt.BackToAudio;
        BtnFindLyrics.Content = Txt.FindLyrics;
        BtnShuffle.ToolTip = Txt.ShuffleTip;
        BtnOpenLyricsFolder.ToolTip = Txt.LyricsFolderTip;
        BtnOpenLyricsFolder.Content = AppIcons.Create("folder", 18);
        BtnEditLyrics.ToolTip = Txt.EditLyricsTip;
        BtnEditLyrics.Content = AppIcons.Create("edit", 18);
        TbTime.ToolTip = Txt.TimeToggleTip;
        UpdateRepeatButton();
        UpdateMuteButton();
        UpdateHeart();
        UpdatePlayButton();
        TbSortLabel.Text = Txt.SortLabel.ToUpperInvariant();
        SetSortItem(0, Txt.SortNewest);
        SetSortItem(1, Txt.SortTitle);
        SetSortItem(2, Txt.SortArtist);
        SetSortItem(3, Txt.SortPlays);
        MenuPlayItem.Header = Txt.PlayFromMenu;
        MenuFavoriteItem.Header = Txt.FavoriteMenu;
        MenuDeleteItem.Header = Txt.DeleteTrack;
        MenuCommentItem.Header = Txt.CommentMenu;
        TbLyricsStatus.Text = _lyrics is not null
            ? Txt.LyricsFound(_lyrics.Source)
            : Txt.LyricsNotFoundShort;
        if (_current is null) CurrentTitle.Text = Txt.NothingPlaying;
        UpdateAccountButton();
    }

    private void OnLanguageChanged()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyLanguage();
            if (_current is null) UpdatePlayerUi();
            else if (_lyrics is null) NpLyrics.SetDocument(null, Txt.LyricsNotFound);
        }));
    }

    private void BtnFeatures_Click(object sender, RoutedEventArgs e)
    {
        new FeaturesWindow { Owner = this }.ShowDialog();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        // Читаем свежий файл: другие окна/процессы могли изменить настройки.
        SettingsService.Reload();
        var settings = new SettingsWindow(() =>
        {
            ApplyLanguage();
            ApplyViewOptions();
            if (SettingsService.Current.RememberVolume)
                SldVolume.Value = Math.Clamp(SettingsService.Current.Volume, 0, 1);
            NpVisualizer.Visibility = SettingsService.Current.ShowVisualizer ? Visibility.Visible : Visibility.Collapsed;
            NpLyrics.ApplySettings();
            Season.Refresh();
            UpdateAudioOutputButton();
        }, SwitchAudioOutput) { Owner = this };
        settings.ShowDialog();
        UpdateAccountButton();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Txt.Changed -= OnLanguageChanged;
        _timer.Stop();
        _videoWindow?.Close();
        _player.Dispose();
        SettingsService.Save();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (!SettingsService.Current.EnableHotkeys) return;
        if (e.OriginalSource is TextBox or PasswordBox) return;
        int step = Math.Clamp(SettingsService.Current.SeekSeconds, 1, 30);

        switch (e.Key)
        {
            case Key.Space:
                BtnPlay_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Left:
                _player.Seek(Math.Max(0, _player.PositionSeconds - step));
                e.Handled = true;
                break;
            case Key.Right:
                _player.Seek(_player.PositionSeconds + step);
                e.Handled = true;
                break;
            case Key.Up:
                SldVolume.Value = Math.Clamp(SldVolume.Value + 0.05, 0, 1);
                e.Handled = true;
                break;
            case Key.Down:
                SldVolume.Value = Math.Clamp(SldVolume.Value - 0.05, 0, 1);
                e.Handled = true;
                break;
            case Key.M:
                BtnMute_Click(sender, e);
                e.Handled = true;
                break;
            case Key.L:
                BtnLyrics_Click(sender, e);
                e.Handled = true;
                break;
            case Key.S:
                BtnShuffle_Click(sender, e);
                e.Handled = true;
                break;
            case Key.R:
                BtnRepeat_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Delete:
                if (SelectedTrack() is { } selected) MenuDelete_Click(sender, e);
                e.Handled = true;
                break;
        }
    }
}
