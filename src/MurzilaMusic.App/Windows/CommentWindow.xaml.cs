using System.Windows;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App;

public partial class CommentWindow : Window
{
    public CommentWindow(TrackItem track)
    {
        InitializeComponent();
        TbTrack.Text = $"{track.Title} — {track.Artist}";
        TbComment.Text = track.Comment;
        TbComment.Focus();
    }

    public string Comment => TbComment.Text.Trim();

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
