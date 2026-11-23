using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;
using MurzilaMusic.Shared;

namespace MurzilaMusic.App;

public partial class PaywallWindow : Window
{
    private readonly TrackItem _track;
    private readonly AccessDecision _decision;
    private string? _lastOrderId;
    private bool _lastUnlockTrack;
    private bool _closed;

    public PaywallWindow(TrackItem track, AccessDecision decision)
    {
        InitializeComponent();
        _track = track;
        _decision = decision;
        TbReason.Text = decision.Reason;
        TbTrack.Text = $"{track.Title} — {track.Artist} · {track.DisplayDuration}";

        PlansList.ItemsSource = CatalogService.Current.Plans
            .Where(p => p.PriceRub > 0)
            .OrderBy(p => p.SortOrder)
            .ToList();

        var addon = CatalogService.Current.Addons.FirstOrDefault(a => a.Kind == "trackUnlock");
        if (addon is not null)
        {
            BtnSingle.Content = $"{addon.Name} — {addon.PriceRub} ₽";
            BtnSingle.Tag = addon.Id;
        }
        else BtnSingle.Visibility = Visibility.Collapsed;

        UpdateUserStatus();
    }

    private void UpdateUserStatus()
    {
        var settings = SettingsService.Current;
        TbUserStatus.Text = settings.LoggedIn
            ? Txt.LoggedInAs(settings.DisplayName)
            : Txt.AccountNeedLogin;
        BtnLogin.Content = settings.LoggedIn ? Txt.Profile : Txt.Login + " / " + Txt.Register;
        TbBalance.Text = Txt.StoreBalance(settings.BalanceMinor / 100m);
        TbPaymentMode.Text = settings.ServerMode
            ? (settings.DemoPayments ? Txt.PaymentTestMode : Txt.PaymentRealMode)
            : Txt.LocalFallbackMode;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        PlansList.IsEnabled = !busy;
        BtnSingle.IsEnabled = !busy;
        BtnRestore.IsEnabled = !busy;
        BtnLogin.IsEnabled = !busy;
        BtnRetry.IsEnabled = !busy;
        TbPromoCode.IsEnabled = !busy;
        if (status is not null) TbPaymentStatus.Text = status;
    }

    private async Task<bool> EnsureLoginAsync()
    {
        if (SettingsService.Current.LoggedIn) return true;
        var account = new AccountWindow { Owner = this };
        if (account.ShowDialog() == true)
        {
            UpdateUserStatus();
            return true;
        }
        TbUserStatus.Text = Txt.AccountNeedLogin;
        return false;
    }

    /// <summary>Сервер не запущен: каталог и лимиты всё равно работают локально.</summary>
    private void GrantLocally(string productId, bool unlockThisTrack)
    {
        var plan = CatalogService.Current.FindPlan(productId);
        var addon = CatalogService.Current.Addons.FirstOrDefault(a => a.Id == productId);
        if (plan is not null && plan.LegacyTariff > (int)SettingsService.Current.Tariff)
            SettingsService.Current.Tariff = (Tariff)plan.LegacyTariff;
        if (addon is not null)
            SettingsService.Current.ActivePlanId = "single5";
        else if (plan is not null)
            SettingsService.Current.ActivePlanId = plan.Id;
        if (unlockThisTrack && !SettingsService.Current.UnlockedTracks.Contains(_track.Id))
            SettingsService.Current.UnlockedTracks.Add(_track.Id);
        SettingsService.Save();
        TbPaymentStatus.Text = Txt.PaymentSuccess;
        DialogResult = true;
        Close();
    }

    private async Task BuyAsync(string productId, bool unlockThisTrack)
    {
        if (!await EnsureLoginAsync()) return;
        var settings = SettingsService.Current;
        if (!settings.ServerMode || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            GrantLocally(productId, unlockThisTrack);
            return;
        }

        string token = settings.AccessToken;
        SetBusy(true, Txt.PaymentStarting);
        try
        {
            var product = CatalogService.Current.FindPlan(productId);
            int priceMinor = (product?.PriceRub
                ?? CatalogService.Current.Addons.FirstOrDefault(a => a.Id == productId)?.PriceRub ?? 0) * 100;
            string promo = TbPromoCode.Text.Trim();
            var info = await ApiClient.GetInfoAsync();

            string method;
            if (promo.Length > 0) method = "promo";
            else if (settings.BalanceMinor >= priceMinor) method = "balance";
            else if (info.TestPayments) method = "test";
            else method = "manual";

            var order = await ApiClient.CreateStoreOrderAsync(token, productId, method,
                promo.Length > 0 ? promo : null, unlockThisTrack ? _track.Id : null, null);
            _lastOrderId = order.Id;
            _lastUnlockTrack = unlockThisTrack;

            if (order.Status == "paid")
            {
                await ApplyServerStateAsync(token);
                TbPaymentStatus.Text = Txt.PaymentSuccess;
                DialogResult = true;
                Close();
                return;
            }

            if (order.Status == "awaiting_confirm")
            {
                string url = string.IsNullOrWhiteSpace(order.ConfirmationUrl)
                    ? "" : ApiClient.AbsoluteUrl(order.ConfirmationUrl);
                if (url.Length > 0)
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch { TbPaymentStatus.Text = "Открой вручную: " + url; }
                }
                TbPaymentStatus.Text = Txt.StoreManualSent;
                BtnRetry.Visibility = Visibility.Visible;
                await PollOrderAsync(token, order.Id, unlockThisTrack);
                return;
            }

            TbPaymentStatus.Text = "Заказ " + order.Status + ". Проверь статус ещё раз.";
            BtnRetry.Visibility = Visibility.Visible;
        }
        catch (ApiException ex) { TbPaymentStatus.Text = ex.Message; }
        catch (HttpRequestException) { TbPaymentStatus.Text = Txt.PaymentFailed; }
        catch (TaskCanceledException) { TbPaymentStatus.Text = Txt.PaymentFailed; }
        finally { if (!_closed) SetBusy(false); }
    }

    private async Task ApplyServerStateAsync(string token)
    {
        var state = await ApiClient.GetAccountStateAsync(token);
        CatalogService.Apply(state);
        UpdateUserStatus();
    }

    private async Task PollOrderAsync(string token, string orderId, bool unlockThisTrack)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline && !_closed)
        {
            await Task.Delay(2000);
            if (_closed) return;
            TbPaymentStatus.Text = Txt.PaymentChecking;
            ServerOrder status;
            try { status = await ApiClient.StoreOrderStatusAsync(token, orderId); }
            catch { continue; }

            if (status.Status == "paid")
            {
                await ApplyServerStateAsync(token);
                if (unlockThisTrack && !SettingsService.Current.UnlockedTracks.Contains(_track.Id))
                {
                    SettingsService.Current.UnlockedTracks.Add(_track.Id);
                    SettingsService.Save();
                }
                TbPaymentStatus.Text = Txt.PaymentSuccess;
                DialogResult = true;
                Close();
                return;
            }
            if (status.Status == "canceled")
            {
                TbPaymentStatus.Text = Txt.PaymentCancelled;
                return;
            }
            TbPaymentStatus.Text = Txt.StoreManualSent;
        }
        if (!_closed) TbPaymentStatus.Text = Txt.PaymentTimeout;
    }

    private void PlanBuy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string planId }) _ = BuyAsync(planId, false);
    }

    private void SingleBuy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string productId }) _ = BuyAsync(productId, true);
    }

    private async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureLoginAsync()) return;
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) { TbPaymentStatus.Text = Txt.StoreNotFoundLocal; return; }
        SetBusy(true, Txt.PaymentChecking);
        try
        {
            var result = await ApiClient.RestorePurchasesAsync(token);
            await ApplyServerStateAsync(token);
            TbPaymentStatus.Text = Txt.StoreRestored(result.Message);
            if (SettingsService.Current.Tariff > _decision.RequiredTariff
                || SettingsService.Current.UnlockedTracks.Contains(_track.Id))
            {
                DialogResult = true;
                Close();
            }
        }
        catch (ApiException ex) { TbPaymentStatus.Text = ex.Message; }
        catch { TbPaymentStatus.Text = Txt.StoreNotFoundLocal; }
        finally { if (!_closed) SetBusy(false); }
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        if (await EnsureLoginAsync()) UpdateUserStatus();
    }

    private async void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOrderId is null || _closed) return;
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return;
        SetBusy(true, Txt.PaymentChecking);
        try { await PollOrderAsync(token, _lastOrderId, _lastUnlockTrack); }
        finally { if (!_closed) SetBusy(false); }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closed = true;
        base.OnClosing(e);
    }
}
