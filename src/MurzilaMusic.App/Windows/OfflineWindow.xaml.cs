using System.Windows;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class OfflineWindow : Window
{
    private string _code;

    public OfflineWindow(string code)
    {
        InitializeComponent();
        _code = code;
        UpdateText();
    }

    private void UpdateText()
    {
        TbMessage.Text = Txt.Offline;
        TbHint.Text = Txt.OfflineHint + " " + Txt.Pick("Тексты появятся после подключения.", "Тексты появятся после подключения.");
        TbCode.Text = "Код Ошибки: " + _code;
        BtnRetry.Content = Txt.Retry;
        BtnOffline.Content = Txt.OfflineMode;
    }

    private async void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        BtnRetry.IsEnabled = false;
        BtnRetry.Content = "Проверка...";
        var (ok, code) = await ConnectivityService.CheckAsync();
        _code = code;
        if (ok)
        {
            DialogResult = true;
            Close();
            return;
        }
        UpdateText();
        BtnRetry.IsEnabled = true;
    }

    private void BtnOffline_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
