using System.Windows;

namespace MurzilaMusic.App;

public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();

    /// <summary>Показываем реальный этап запуска, а не фиксированную задержку.</summary>
    public void SetProgress(int percent, string step)
    {
        SplashProgress.Value = Math.Clamp(percent, 0, 100);
        SplashStep.Text = step;
    }
}
