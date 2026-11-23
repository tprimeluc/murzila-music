using System.Diagnostics;
using System.Windows;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class BackupWindow : Window
{
    private readonly LibraryService _library;

    public BackupWindow(LibraryService library)
    {
        InitializeComponent();
        _library = library;
        Refresh();
    }

    private void Refresh()
    {
        BackupsList.ItemsSource = BackupService.GetBackups();
        if (BackupsList.Items.Count > 0) BackupsList.SelectedIndex = 0;
    }

    private void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        string folder = BackupService.CreateBackup();
        TbStatus.Text = "Копия создана: " + folder;
        Refresh();
    }

    private void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not string folder)
        {
            TbStatus.Text = "Выбери копию для восстановления.";
            return;
        }
        var answer = MessageBox.Show("Восстановить выбранную копию? Текущая библиотека будет заменена.",
            "Murzila-Music", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            BackupService.Restore(folder, _library);
            TbStatus.Text = "Готово. Библиотека и настройки восстановлены.";
            DialogResult = true;
        }
        catch (Exception ex) { TbStatus.Text = ex.Message; }
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.Backup) { UseShellExecute = true });
        }
        catch (Exception ex) { TbStatus.Text = ex.Message; }
    }
}
