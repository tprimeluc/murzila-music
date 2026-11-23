using System.Windows;
using System.Windows.Input;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class PlaylistPickerWindow : Window
{
    private readonly LibraryService _library;
    public string? SelectedPlaylistId { get; private set; }

    public PlaylistPickerWindow(LibraryService library)
    {
        InitializeComponent();
        _library = library;
        Refresh();
    }

    private void Refresh() => LbPlaylists.ItemsSource = _library.Playlists.ToList();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (LbPlaylists.SelectedItem is not Playlist playlist) return;
        SelectedPlaylistId = playlist.Id;
        DialogResult = true;
        Close();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        _library.CreatePlaylist(TbName.Text);
        Refresh();
        LbPlaylists.SelectedIndex = LbPlaylists.Items.Count - 1;
    }

    private void LbPlaylists_DoubleClick(object sender, MouseButtonEventArgs e) => Add_Click(sender, e);
}
