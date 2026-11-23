using System.Windows;
using System.Windows.Controls;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App;

public partial class ArtistWindow : Window
{
    private readonly Action<TrackItem> _onPlay;

    public ArtistWindow(string artist, IEnumerable<TrackItem> tracks, Action<TrackItem> onPlay)
    {
        InitializeComponent();
        _onPlay = onPlay;
        TbArtist.Text = artist;
        LbTracks.ItemsSource = tracks.ToList();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void LbTracks_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.OfType<TrackItem>().FirstOrDefault() is { } track)
        {
            _onPlay(track);
            LbTracks.SelectedItem = null;
        }
    }
}
