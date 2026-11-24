using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class AccountWindow : Window
{
    private bool _busy;
    private bool _serverRequiresCode = true;
    private bool _googleConfigured;
    private bool _smsConfigured;
    private string _googleIssue = Txt.OnlineDetailsOffline;
    private string _googleRedirect = "—";
    private string _codeMode = "register";
    private string _codeEmail = "";
    private string _forgotEmail = "";
    private string _newEmail = "";
    private string _bindPhone = "";
    private string _loginPhone = "";
    private DispatcherTimer? _googleTimer;
    private int _googleAttempts;
    private ServerAccountState? _storeState;

    private sealed class OrderView
    {
        public string DisplayText { get; set; } = "";
        public ServerOrder Order { get; set; } = new();
    }

    public AccountWindow()
    {
        InitializeComponent();
        UpdateView();
        TbLoginEmail.Focus();
        _ = CheckServerAsync();
    }

    private async Task CheckServerAsync()
    {
        if (!await ApiClient.IsAvailableAsync()) return;
        try
        {
            var info = await ApiClient.GetInfoAsync();
            SettingsService.Current.DemoPayments = !info.RealPayments;
            SettingsService.Save();
            _serverRequiresCode = info.RequireEmailCode && SettingsService.Current.RequireEmailCode;
            _googleConfigured = info.GoogleConfigured && SettingsService.Current.GoogleSignIn;
            _smsConfigured = info.SmsConfigured;
            _googleIssue = string.IsNullOrWhiteSpace(info.GoogleIssue) ? "" : info.GoogleIssue;
            _googleRedirect = string.IsNullOrWhiteSpace(info.GoogleRedirectUri) ? "—" : info.GoogleRedirectUri;

            BtnGoogle.IsEnabled = _googleConfigured;
            BtnGoogle.ToolTip = _googleConfigured ? "" : Txt.GoogleNotReady;
            BtnLinkGoogle.IsEnabled = info.GoogleConfigured;
            BtnLinkGoogle.ToolTip = _googleConfigured ? "" : Txt.GoogleNotReadyDetail(_googleIssue, _googleRedirect);

            if (!_smsConfigured) TbSecurityStatus.Text = Txt.SmsNotConfigured;
            if (!info.GoogleConfigured)
                TbLinkedStatus.Text = Txt.GoogleNotReadyDetail(_googleIssue, _googleRedirect);
            if (!SettingsService.Current.LoggedIn)
                TbHint.Text = Txt.AccountGuestHint + " " + Txt.AccountStoredLocal;
        }
        catch { /* сервер ответил, но /api/info недоступен — не критично */ }
    }

    private void UpdateView()
    {
        bool loggedIn = SettingsService.Current.LoggedIn;
        GuestPanel.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        ProfilePanel.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;

        TbTitle.Text = loggedIn ? Txt.Profile : Txt.AccountTitle;
        TbSubtitle.Text = loggedIn ? Txt.LoggedInAs(DisplayName()) : Txt.AccountSubtitle;
        TbHint.Text = Txt.AccountGuestHint + " "
                      + (SettingsService.Current.ServerMode ? Txt.AccountStoredLocal : Txt.LocalFallbackMode);
        TbNoAccount.Text = Txt.AccountNoAccount;
        TbRepeatPassword.Text = Txt.RepeatPassword;
        TbCodeTitle.Text = Txt.CodeEmail;
        TbCodeSent.Text = Txt.CodeSent;
        BtnVerifyCode.Content = Txt.CodeVerify;
        BtnResendCode.Content = Txt.CodeResend;
        BtnLoginCode.Content = Txt.LoginWithCode;
        BtnPhoneLogin.Content = Txt.PhoneLogin;
        TbPhoneLoginInfo.Text = Txt.LoginByPhoneHint;
        TbForgotInfo.Text = Txt.ForgotHint;
        BtnGoogle.Content = Txt.GoogleButton;
        BtnForgot.Content = Txt.ForgotPassword;

        if (loggedIn)
        {
            string name = DisplayName();
            TbProfileName.Text = name;
            TbProfileEmail.Text = SettingsService.Current.AccountEmail;
            TbInitial.Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
            TbProfileTariff.Text = Txt.TariffStatus(SettingsService.Current.Tariff);
            var plan = CatalogService.FindPlan(SettingsService.Current.ActivePlanId);
            TbProfilePlan.Text = $"План: {plan.Name} · {Txt.StoreFeatures(plan.FeatureCount)} · {Txt.StoreAds(SettingsService.Current.AdsLevel)}";
            TbProfileBalance.Text = Txt.StoreBalance(SettingsService.Current.BalanceMinor / 100m);
            TbProfileHint.Text = (SettingsService.Current.ServerMode
                                     ? Txt.ServerAccountBadge
                                     : Txt.DemoAccountBadge)
                                 + "\n"
                                 + (SettingsService.Current.DemoPayments
                                     ? Txt.PaymentTestMode
                                     : Txt.PaymentRealMode);

            if (SettingsService.Current.ServerMode && !string.IsNullOrWhiteSpace(SettingsService.Current.AccessToken))
            {
                _ = LoadStoreStateAsync();
                _ = LoadLinkedAsync();
            }
        }
    }

    private static string DisplayName()
    {
        string name = SettingsService.Current.DisplayName;
        if (!string.IsNullOrWhiteSpace(name) && name != Txt.Guest) return name;
        string email = SettingsService.Current.AccountEmail;
        int at = email.IndexOf('@');
        return at > 0 ? email[..at] : Txt.Guest;
    }

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (LoginPanel is null || RegisterPanel is null) return;
        bool login = RbLoginTab.IsChecked == true;
        LoginPanel.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
        RegisterPanel.Visibility = login ? Visibility.Collapsed : Visibility.Visible;
        CodePanel.Visibility = Visibility.Collapsed;
        ForgotPanel.Visibility = Visibility.Collapsed;
        PhoneLoginPanel.Visibility = Visibility.Collapsed;
        TbError.Text = "";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BtnLogin.IsEnabled = !busy;
        BtnRegister.IsEnabled = !busy;
        BtnLogout.IsEnabled = !busy;
        BtnLoginCode.IsEnabled = !busy;
        BtnPhoneLogin.IsEnabled = !busy;
        BtnVerifyCode.IsEnabled = !busy;
        BtnResendCode.IsEnabled = !busy;
        BtnGoogle.IsEnabled = !busy && _googleConfigured;
        BtnForgotRequest.IsEnabled = !busy;
        BtnForgotReset.IsEnabled = !busy;
        BtnPhoneLoginRequest.IsEnabled = !busy;
        BtnPhoneLoginVerify.IsEnabled = !busy;
        BtnChangePassword.IsEnabled = !busy;
        BtnChangeEmailRequest.IsEnabled = !busy;
        BtnChangeEmailVerify.IsEnabled = !busy;
        BtnPhoneRequest.IsEnabled = !busy;
        BtnPhoneVerify.IsEnabled = !busy;
        BtnPhoneUnlink.IsEnabled = !busy;
        BtnLinkGoogle.IsEnabled = !busy && _googleConfigured;
        BtnUnlinkGoogle.IsEnabled = !busy;
        BtnStore.IsEnabled = !busy;
        BtnRestore.IsEnabled = !busy;
        BtnRefund.IsEnabled = !busy;
    }

    private static void ShowError(TextBlock block, Exception ex)
        => block.Text = ex is ApiException api ? api.Message : Txt.ServerUnavailable;

    private void ApplyServerAuth(ServerAuth auth)
    {
        SettingsService.Current.LoggedIn = true;
        SettingsService.Current.ServerMode = true;
        SettingsService.Current.AccessToken = auth.Token;
        SettingsService.Current.AccountId = auth.User.Id;
        SettingsService.Current.DisplayName = auth.User.Name;
        SettingsService.Current.AccountEmail = auth.User.Email;
        SettingsService.Current.Tariff = (Tariff)auth.User.Tariff;
        SettingsService.Save();
    }

    private static void UseLocalFallback()
    {
        SettingsService.Current.ServerMode = false;
        SettingsService.Current.AccessToken = "";
        SettingsService.Current.AccountId = "";
        SettingsService.Save();
    }

    private void ShowCodeStep(string mode, string email, ServerCodeResult result)
    {
        _codeMode = mode;
        _codeEmail = email;
        CodePanel.Visibility = Visibility.Visible;
        TbCode.Text = "";
        TbCodeSent.Text = Txt.CodeSent;
        TbCodeHint.Text = result.TestMode && !string.IsNullOrWhiteSpace(result.TestCode)
            ? Txt.CodeTest(result.TestCode!)
            : "";
        TbError.Text = "";
        TbCode.Focus();
    }

    // ---------- обычный вход и регистрация ----------

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        TbError.Text = "";
        string email = TbLoginEmail.Text.Trim();
        string password = PbLoginPassword.Password;
        SetBusy(true);
        try
        {
            if (await ApiClient.IsAvailableAsync())
            {
                var auth = await ApiClient.LoginAsync(email, password);
                if (auth.Requires2Fa)
                {
                    string twoFaEmail = string.IsNullOrWhiteSpace(auth.Email) ? email : auth.Email!;
                    ShowCodeStep("2fa", twoFaEmail, new ServerCodeResult
                    {
                        Email = twoFaEmail,
                        TestMode = !string.IsNullOrWhiteSpace(auth.TestCode),
                        TestCode = auth.TestCode
                    });
                    return;
                }
                ApplyServerAuth(auth);
            }
            else
            {
                UseLocalFallback();
                if (!AccountService.Default.Login(email, password, out string error))
                {
                    TbError.Text = error + " " + Txt.LocalFallbackMode;
                    return;
                }
            }

            DialogResult = true;
            Close();
        }
        catch (ApiException ex) { TbError.Text = ex.Message; }
        catch (HttpRequestException) { TbError.Text = Txt.ServerUnavailable; }
        catch (TaskCanceledException) { TbError.Text = Txt.ServerUnavailable; }
        finally { SetBusy(false); }
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        TbError.Text = "";
        if (PbRegPassword.Password != PbRegConfirm.Password)
        {
            TbError.Text = Txt.PasswordsMismatch;
            return;
        }

        string name = TbRegName.Text.Trim();
        string email = TbRegEmail.Text.Trim();
        string password = PbRegPassword.Password;
        SetBusy(true);
        try
        {
            bool server = await ApiClient.IsAvailableAsync();
            if (server && _serverRequiresCode)
            {
                var result = await ApiClient.RequestRegisterCodeAsync(name, email, password);
                ShowCodeStep("register", result.Email, result);
                return;
            }

            if (server) ApplyServerAuth(await ApiClient.RegisterAsync(name, email, password));
            else
            {
                UseLocalFallback();
                if (!AccountService.Default.Register(name, email, password, out string error))
                {
                    TbError.Text = error + " " + Txt.LocalFallbackMode;
                    return;
                }
            }

            DialogResult = true;
            Close();
        }
        catch (ApiException ex) { TbError.Text = ex.Message; }
        catch (HttpRequestException) { TbError.Text = Txt.ServerUnavailable; }
        catch (TaskCanceledException) { TbError.Text = Txt.ServerUnavailable; }
        finally { SetBusy(false); }
    }

    private async void LoginCode_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string email = TbLoginEmail.Text.Trim();
        string password = PbLoginPassword.Password;
        if (!await ApiClient.IsAvailableAsync())
        {
            TbError.Text = Txt.ServerUnavailable;
            return;
        }

        SetBusy(true);
        try
        {
            var result = await ApiClient.RequestLoginCodeAsync(email, password);
            ShowCodeStep("login", result.Email, result);
        }
        catch (ApiException ex) { TbError.Text = ex.Message; }
        catch (HttpRequestException) { TbError.Text = Txt.ServerUnavailable; }
        catch (TaskCanceledException) { TbError.Text = Txt.ServerUnavailable; }
        finally { SetBusy(false); }
    }

    private async void ResendCode_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(_codeEmail)) return;
        SetBusy(true);
        try
        {
            ServerCodeResult result = _codeMode == "register"
                ? await ApiClient.RequestRegisterCodeAsync(TbRegName.Text.Trim(), _codeEmail, PbRegPassword.Password)
                : await ApiClient.RequestLoginCodeAsync(_codeEmail, PbLoginPassword.Password);
            ShowCodeStep(_codeMode, _codeEmail, result);
        }
        catch (ApiException ex) { TbError.Text = ex.Message; }
        catch (HttpRequestException) { TbError.Text = Txt.ServerUnavailable; }
        catch (TaskCanceledException) { TbError.Text = Txt.ServerUnavailable; }
        finally { SetBusy(false); }
    }

    private async void VerifyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string code = TbCode.Text.Trim();
        if (code.Length != 6) { TbError.Text = Txt.EnterCode; return; }

        SetBusy(true);
        try
        {
            ServerAuth auth = _codeMode == "register"
                ? await ApiClient.VerifyRegisterCodeAsync(_codeEmail, code)
                : _codeMode == "2fa"
                    ? await ApiClient.VerifyLogin2FaAsync(_codeEmail, code)
                    : await ApiClient.VerifyLoginCodeAsync(_codeEmail, code);
            ApplyServerAuth(auth);
            DialogResult = true;
            Close();
        }
        catch (ApiException ex) { TbError.Text = ex.Message; }
        catch (HttpRequestException) { TbError.Text = Txt.ServerUnavailable; }
        catch (TaskCanceledException) { TbError.Text = Txt.ServerUnavailable; }
        finally { SetBusy(false); }
    }

    // ---------- забытый пароль ----------

    private void Forgot_Click(object sender, RoutedEventArgs e)
    {
        ForgotPanel.Visibility = Visibility.Visible;
        PhoneLoginPanel.Visibility = Visibility.Collapsed;
        CodePanel.Visibility = Visibility.Collapsed;
        // Подставляем почту текущего аккаунта: пользователи часто путают её с SMTP-адресом.
        string account = SettingsService.Current.AccountEmail;
        TbForgotEmail.Text = !string.IsNullOrWhiteSpace(account)
            ? account
            : TbLoginEmail.Text.Trim();
        TbForgotInfo.Text = Txt.ForgotHint;
        TbForgotError.Text = "";
        TbForgotHint.Text = "";
        ForgotCodeGroup.Visibility = Visibility.Collapsed;
        TbForgotEmail.Focus();
    }

    private void ForgotBack_Click(object sender, RoutedEventArgs e)
        => ForgotPanel.Visibility = Visibility.Collapsed;

    private async void ForgotRequest_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string email = TbForgotEmail.Text.Trim();
        if (!email.Contains('@') || !email.Contains('.'))
        {
            TbForgotError.Text = "Введи корректную почту.";
            return;
        }
        TbForgotError.Text = "";
        string account = SettingsService.Current.AccountEmail.Trim();
        TbForgotHint.Text = !string.IsNullOrWhiteSpace(account)
            && !string.Equals(email, account, StringComparison.OrdinalIgnoreCase)
                ? Txt.ForgotEmailMismatch(account)
                : "";
        SetBusy(true);
        try
        {
            var result = await ApiClient.ForgotRequestCodeAsync(email);
            _forgotEmail = email;
            ForgotCodeGroup.Visibility = Visibility.Visible;
            TbForgotInfo.Text = Txt.ForgotSent(string.IsNullOrWhiteSpace(result.MaskedEmail) ? result.Email : result.MaskedEmail);
            if (result.TestMode && !string.IsNullOrWhiteSpace(result.TestCode))
                TbForgotHint.Text = Txt.CodeTest(result.TestCode!);
            TbForgotCode.Focus();
        }
        catch (Exception ex) { ShowError(TbForgotError, ex); }
        finally { SetBusy(false); }
    }

    private async void ForgotReset_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string code = TbForgotCode.Text.Trim();
        string password = PbForgotNew.Password;
        if (code.Length != 6) { TbForgotError.Text = Txt.EnterCode; return; }
        if (password.Length < 6) { TbForgotError.Text = Txt.PasswordTooShort; return; }

        TbForgotError.Text = "";
        SetBusy(true);
        try
        {
            var auth = await ApiClient.ResetPasswordAsync(_forgotEmail, code, password);
            ApplyServerAuth(auth);
            MessageBox.Show(Txt.ResetPasswordDone, "Murzila-Music", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(TbForgotError, ex); }
        finally { SetBusy(false); }
    }

    // ---------- вход по SMS ----------

    private void PhoneLogin_Click(object sender, RoutedEventArgs e)
    {
        PhoneLoginPanel.Visibility = Visibility.Visible;
        ForgotPanel.Visibility = Visibility.Collapsed;
        CodePanel.Visibility = Visibility.Collapsed;
        TbPhoneLoginPhone.Text = "";
        TbPhoneLoginError.Text = "";
        TbPhoneLoginHint.Text = "";
        PhoneLoginCodeGroup.Visibility = Visibility.Collapsed;
        TbPhoneLoginPhone.Focus();
    }

    private void PhoneLoginBack_Click(object sender, RoutedEventArgs e)
        => PhoneLoginPanel.Visibility = Visibility.Collapsed;

    private async void PhoneLoginRequest_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string phone = TbPhoneLoginPhone.Text.Trim();
        if (phone.Length < 10) { TbPhoneLoginError.Text = Txt.InvalidPhone; return; }
        TbPhoneLoginError.Text = "";
        SetBusy(true);
        try
        {
            var result = await ApiClient.PhoneRequestCodeAsync(phone, null, "login");
            _loginPhone = result.Phone;
            PhoneLoginCodeGroup.Visibility = Visibility.Visible;
            TbPhoneLoginInfo.Text = Txt.SmsSent(string.IsNullOrWhiteSpace(result.MaskedPhone) ? result.Phone : result.MaskedPhone);
            TbPhoneLoginHint.Text = result.TestMode && !string.IsNullOrWhiteSpace(result.TestCode)
                ? Txt.CodeTest(result.TestCode!) : "";
            TbPhoneLoginCode.Focus();
        }
        catch (Exception ex) { ShowError(TbPhoneLoginError, ex); }
        finally { SetBusy(false); }
    }

    private async void PhoneLoginVerify_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string code = TbPhoneLoginCode.Text.Trim();
        if (code.Length != 6) { TbPhoneLoginError.Text = Txt.EnterCode; return; }
        TbPhoneLoginError.Text = "";
        SetBusy(true);
        try
        {
            var auth = await ApiClient.PhoneVerifyAsync(_loginPhone, code, null, "login");
            ApplyServerAuth(auth);
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(TbPhoneLoginError, ex); }
        finally { SetBusy(false); }
    }

    // ---------- копирование / вставка кода ----------

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TextBox source } && !string.IsNullOrWhiteSpace(source.Text))
        {
            try { Clipboard.SetText(source.Text.Trim()); } catch { }
        }
    }

    private void PasteCode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TextBox target })
        {
            try
            {
                if (Clipboard.ContainsText()) target.Text = Clipboard.GetText().Trim();
            }
            catch { }
        }
    }

    // ---------- безопасность ----------

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) { TbSecurityError.Text = Txt.NeedLoginForSecurity; return; }
        string current = PbCurrentPassword.Password;
        string next = PbNewPassword.Password;
        if (next.Length < 6) { TbSecurityError.Text = Txt.PasswordTooShort; return; }

        TbSecurityError.Text = "";
        SetBusy(true);
        try
        {
            await ApiClient.ChangePasswordAsync(token, current, next);
            PbCurrentPassword.Clear();
            PbNewPassword.Clear();
            TbSecurityStatus.Text = Txt.PasswordChanged;
        }
        catch (Exception ex) { ShowError(TbSecurityError, ex); }
        finally { SetBusy(false); }
    }

    private async void ChangeEmailRequest_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) { TbSecurityError.Text = Txt.NeedLoginForSecurity; return; }
        string email = TbNewEmail.Text.Trim();
        if (!email.Contains('@') || !email.Contains('.')) { TbSecurityError.Text = "Введи корректную почту."; return; }

        TbSecurityError.Text = "";
        SetBusy(true);
        try
        {
            var result = await ApiClient.ChangeEmailRequestCodeAsync(token, email);
            _newEmail = email;
            EmailCodeGroup.Visibility = Visibility.Visible;
            TbSecurityStatus.Text = Txt.CodeSentTo(string.IsNullOrWhiteSpace(result.MaskedEmail) ? result.Email : result.MaskedEmail, result.ExpiresIn);
            if (result.TestMode && !string.IsNullOrWhiteSpace(result.TestCode))
                TbSecurityStatus.Text += " " + Txt.CodeTest(result.TestCode!);
            TbEmailCode.Focus();
        }
        catch (Exception ex) { ShowError(TbSecurityError, ex); }
        finally { SetBusy(false); }
    }

    private async void ChangeEmailVerify_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) { TbSecurityError.Text = Txt.NeedLoginForSecurity; return; }
        string code = TbEmailCode.Text.Trim();
        if (code.Length != 6) { TbSecurityError.Text = Txt.EnterCode; return; }

        TbSecurityError.Text = "";
        SetBusy(true);
        try
        {
            await ApiClient.ChangeEmailVerifyAsync(token, _newEmail, code);
            SettingsService.Current.AccountEmail = _newEmail;
            SettingsService.Save();
            EmailCodeGroup.Visibility = Visibility.Collapsed;
            TbSecurityStatus.Text = Txt.EmailChanged;
            UpdateView();
            _ = LoadLinkedAsync();
        }
        catch (Exception ex) { ShowError(TbSecurityError, ex); }
        finally { SetBusy(false); }
    }

    private async void PhoneRequest_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) { TbSecurityError.Text = Txt.NeedLoginForSecurity; return; }
        string phone = TbPhone.Text.Trim();
        if (phone.Length < 10) { TbSecurityError.Text = Txt.InvalidPhone; return; }

        TbSecurityError.Text = "";
        SetBusy(true);
        try
        {
            var result = await ApiClient.PhoneRequestCodeAsync(phone, token, "bind");
            _bindPhone = result.Phone;
            PhoneCodeGroup.Visibility = Visibility.Visible;
            TbSecurityStatus.Text = Txt.SmsSent(string.IsNullOrWhiteSpace(result.MaskedPhone) ? result.Phone : result.MaskedPhone);
            if (result.TestMode && !string.IsNullOrWhiteSpace(result.TestCode))
                TbSecurityStatus.Text += " " + Txt.CodeTest(result.TestCode!);
            TbPhoneCode.Focus();
        }
        catch (Exception ex) { ShowError(TbSecurityError, ex); }
        finally { SetBusy(false); }
    }

    private async void PhoneVerify_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) { TbSecurityError.Text = Txt.NeedLoginForSecurity; return; }
        string code = TbPhoneCode.Text.Trim();
        if (code.Length != 6) { TbSecurityError.Text = Txt.EnterCode; return; }

        TbSecurityError.Text = "";
        SetBusy(true);
        try
        {
            var auth = await ApiClient.PhoneVerifyAsync(_bindPhone, code, token, "bind");
            ApplyServerAuth(auth);
            PhoneCodeGroup.Visibility = Visibility.Collapsed;
            TbSecurityStatus.Text = Txt.PhoneLinked;
            _ = LoadLinkedAsync();
        }
        catch (Exception ex) { ShowError(TbSecurityError, ex); }
        finally { SetBusy(false); }
    }

    private async void PhoneUnlink_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) { TbSecurityError.Text = Txt.NeedLoginForSecurity; return; }
        TbSecurityError.Text = "";
        SetBusy(true);
        try
        {
            await ApiClient.PhoneUnlinkAsync(token);
            PhoneCodeGroup.Visibility = Visibility.Collapsed;
            TbSecurityStatus.Text = Txt.PhoneUnlinked;
            _ = LoadLinkedAsync();
        }
        catch (Exception ex) { ShowError(TbSecurityError, ex); }
        finally { SetBusy(false); }
    }

    // ---------- связанные сервисы ----------

    private async Task LoadLinkedAsync()
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var linked = await ApiClient.LinkedAsync(token);
            foreach (var item in linked.Items)
            {
                string text = string.IsNullOrWhiteSpace(item.Value) ? Txt.LinkedNone : item.Value;
                if (item.Verified) text += " ✓";
                switch (item.Provider)
                {
                    case "email": TbLinkedEmail.Text = "Почта: " + text; break;
                    case "google":
                        TbLinkedGoogle.Text = "Google: " + text;
                        BtnUnlinkGoogle.Visibility = item.Verified ? Visibility.Visible : Visibility.Collapsed;
                        BtnLinkGoogle.Visibility = item.Verified ? Visibility.Collapsed : Visibility.Visible;
                        break;
                    case "phone":
                        TbLinkedPhone.Text = "Телефон: " + text;
                        BtnUnlinkPhone.Visibility = item.Verified ? Visibility.Visible : Visibility.Collapsed;
                        break;
                }
            }
        }
        catch { /* сеть не обязательна для показа профиля */ }
    }

    private async void LinkGoogle_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) { TbLinkedStatus.Text = Txt.NeedLoginForSecurity; return; }
        if (!_googleConfigured)
        {
            TbLinkedStatus.Text = Txt.GoogleNotReadyDetail(_googleIssue, _googleRedirect);
            return;
        }
        try
        {
            TbLinkedStatus.Text = Txt.GoogleWaiting;
            var start = await ApiClient.GoogleStartAsync(link: true, token: token);
            Process.Start(new ProcessStartInfo(start.Url) { UseShellExecute = true });
            PollGoogle(start.State, link: true);
        }
        catch (Exception ex) { ShowError(TbLinkedStatus, ex); }
    }

    private async void UnlinkGoogle_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return;
        SetBusy(true);
        try
        {
            await ApiClient.UnlinkAsync(token, "google");
            TbLinkedStatus.Text = Txt.GoogleUnlinked;
            _ = LoadLinkedAsync();
        }
        catch (Exception ex) { ShowError(TbLinkedStatus, ex); }
        finally { SetBusy(false); }
    }

    private void PollGoogle(string state, bool link)
    {
        TextBlock status = link ? TbLinkedStatus : TbError;
        _googleAttempts = 0;
        _googleTimer?.Stop();
        _googleTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1500),
            DispatcherPriority.Normal, async (_, _) =>
            {
                if (_googleAttempts++ > 60)
                {
                    _googleTimer?.Stop();
                    status.Text = "Google не подтверждён. Попробуй ещё раз.";
                    status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x8B));
                    return;
                }
                try
                {
                    var result = await ApiClient.GoogleStatusAsync(state);
                    if (!result.Ready) return;
                    _googleTimer?.Stop();
                    if (link)
                    {
                        await LoadLinkedAsync();
                        status.Text = Txt.GoogleLinked;
                    }
                    else
                    {
                        ApplyServerAuth(new ServerAuth { Token = result.Token ?? "", User = result.User ?? new ServerUser() });
                        DialogResult = true;
                        Close();
                    }
                }
                catch { /* пользователь ещё не подтвердил */ }
            }, Dispatcher);
        _googleTimer.Start();
    }

    private async void Google_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        TbError.Text = "";
        if (!_googleConfigured)
        {
            TbError.Text = Txt.GoogleNotReadyDetail(_googleIssue, _googleRedirect);
            return;
        }
        try
        {
            var start = await ApiClient.GoogleStartAsync();
            Process.Start(new ProcessStartInfo(start.Url) { UseShellExecute = true });
            TbError.Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xE0, 0x81));
            TbError.Text = Txt.GoogleWaiting;
            PollGoogle(start.State, link: false);
        }
        catch (Exception ex)
        {
            TbError.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x8B));
            ShowError(TbError, ex);
        }
    }

    // ---------- магазин и выход ----------

    private async Task LoadStoreStateAsync()
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var state = await ApiClient.GetAccountStateAsync(token);
            _storeState = state;
            CatalogService.Apply(state);
            var plan = CatalogService.FindPlan(state.ActivePlanId);
            TbProfileTariff.Text = Txt.TariffStatus((Tariff)state.User.Tariff);
            TbProfilePlan.Text = $"План: {plan.Name} · {Txt.StoreFeatures(plan.FeatureCount)} · {Txt.StoreAds(state.AdsLevel)}";
            TbProfileBalance.Text = Txt.StoreBalance(state.Balance);
            OrdersList.ItemsSource = state.Orders
                .Where(o => o.Status is "paid" or "awaiting_confirm")
                .Select(o => new OrderView
                {
                    Order = o,
                    DisplayText = $"{o.ProductName} · {o.Amount:0} ₽ · {o.Status} · {o.Method}"
                }).ToList();
        }
        catch { TbProfileStatus.Text = Txt.StoreNotFoundLocal; }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return;
        SetBusy(true);
        try
        {
            var result = await ApiClient.RestorePurchasesAsync(token);
            await LoadStoreStateAsync();
            TbProfileStatus.Text = Txt.StoreRestored(result.Message);
        }
        catch (ApiException ex) { TbProfileStatus.Text = ex.Message; }
        catch { TbProfileStatus.Text = Txt.StoreNotFoundLocal; }
        finally { SetBusy(false); }
    }

    private void Store_Click(object sender, RoutedEventArgs e)
    {
        var fakeTrack = new TrackItem { Title = "Магазин", Artist = "Murzila-Music" };
        var decision = new AccessDecision
        {
            NeedsPayment = true,
            Reason = "Выбери план или введи промокод."
        };
        var store = new PaywallWindow(fakeTrack, decision) { Owner = this };
        store.ShowDialog();
        _ = LoadStoreStateAsync();
    }

    private void Refund_Click(object sender, RoutedEventArgs e)
    {
        if (_storeState is null) { _ = LoadStoreStateAsync(); return; }
        var dialog = new RefundWindow(_storeState.Orders.Where(o => o.Status == "paid").ToList()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            TbProfileStatus.Text = Txt.StoreRefundDone;
            _ = LoadStoreStateAsync();
        }
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        string token = SettingsService.Current.AccessToken;
        if (!string.IsNullOrWhiteSpace(token) && await ApiClient.IsAvailableAsync())
        {
            try { await ApiClient.LogoutAsync(token); }
            catch { /* локальный выход всё равно выполняем */ }
        }

        AccountService.Logout();
        TbError.Text = Txt.AccountLoggedOut;
        UpdateView();
        RbLoginTab.IsChecked = true;
        TbLoginEmail.Focus();
    }

    private void LoginPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Login_Click(sender, e);
    }

    private void RegisterConfirm_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Register_Click(sender, e);
    }

    private void Code_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) VerifyCode_Click(sender, e);
    }

    private void ForgotCode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ForgotReset_Click(sender, e);
    }

    private void PhoneLoginCode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) PhoneLoginVerify_Click(sender, e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _googleTimer?.Stop();
        base.OnClosed(e);
    }
}
