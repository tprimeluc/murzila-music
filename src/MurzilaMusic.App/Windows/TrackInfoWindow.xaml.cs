using System.Windows;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App;

/// <summary>
/// Окно заполнения информации о треке. Открывается при добавлении файлов
/// и при редактировании существующего трека — чтобы в библиотеке были
/// нормальные названия, артисты и альбомы, а не имена файлов.
/// </summary>
public partial class TrackInfoWindow : Window
{
    private readonly bool _allowApplyAll;

    public TrackInfoDraft? Result { get; private set; }
    public bool Skipped { get; private set; }

    private TrackInfoWindow(string header, string subHeader, TrackInfoDraft draft,
        bool allowApplyAll, bool allowSkip)
    {
        InitializeComponent();
        _allowApplyAll = allowApplyAll;

        TbHeader.Text = header;
        TbSubHeader.Text = subHeader;
        TbTitle.Text = draft.Title;
        TbArtist.Text = draft.Artist;
        TbAlbum.Text = draft.Album;
        TbComment.Text = draft.Comment;

        CbAllowExternal.IsChecked = draft.AllowExternal;
        CbApplyAll.IsChecked = draft.ApplyToAll && allowApplyAll;
        CbApplyAll.Visibility = allowApplyAll ? Visibility.Visible : Visibility.Collapsed;
        BtnSkip.Visibility = allowSkip ? Visibility.Visible : Visibility.Collapsed;
        BtnSkip.Content = allowApplyAll ? "Пропустить" : "Без изменений";

        Loaded += (_, _) =>
        {
            TbTitle.Focus();
            TbTitle.SelectAll();
        };
    }

    /// <summary>Спрашивает данные трека. Возвращает null, если пользователь отменил добавление.</summary>
    public static TrackInfoDraft? Ask(Window owner, string header, string subHeader,
        TrackInfoDraft draft, bool allowApplyAll = false, bool allowSkip = false)
    {
        var window = new TrackInfoWindow(header, subHeader, draft, allowApplyAll, allowSkip)
        {
            Owner = owner
        };
        window.ShowDialog();
        if (window.Skipped) return new TrackInfoDraft();
        return window.Result;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = new TrackInfoDraft
        {
            Title = TbTitle.Text.Trim(),
            Artist = TbArtist.Text.Trim(),
            Album = TbAlbum.Text.Trim(),
            Comment = TbComment.Text.Trim(),
            ApplyToAll = _allowApplyAll && CbApplyAll.IsChecked == true,
            AllowExternal = CbAllowExternal.IsChecked == true
        };
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Skipped = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }
}
