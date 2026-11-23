using System.Windows;
using System.Windows.Controls;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class RefundWindow : Window
{
    private sealed class OrderView
    {
        public ServerOrder Order { get; set; } = new();
        public string DisplayText { get; set; } = "";
    }

    public RefundWindow(IEnumerable<ServerOrder> orders)
    {
        InitializeComponent();
        OrdersListBox.ItemsSource = orders.Select(o => new OrderView
        {
            Order = o,
            DisplayText = $"{o.ProductName} · {o.Amount:0} ₽ · {o.Method}"
        }).ToList();
        if (OrdersListBox.Items.Count > 0) OrdersListBox.SelectedIndex = 0;
    }

    private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (OrdersListBox.SelectedItem is not OrderView view)
        {
            TbStatus.Text = "Выбери заказ.";
            return;
        }
        string reason = (CbReason.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "other";
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            TbStatus.Text = Txt.StoreNotFoundLocal;
            return;
        }
        BtnSubmit.IsEnabled = false;
        try
        {
            await ApiClient.CreateRefundAsync(token, view.Order.Id, reason, TbComment.Text.Trim());
            DialogResult = true;
            Close();
        }
        catch (ApiException ex) { TbStatus.Text = ex.Message; }
        catch { TbStatus.Text = Txt.StoreNotFoundLocal; }
        finally { BtnSubmit.IsEnabled = true; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
