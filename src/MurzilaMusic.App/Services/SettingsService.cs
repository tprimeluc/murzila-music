using System.Text.Json;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App.Services;

public sealed class AppSettings
{
    /// <summary>Обращение в интерфейсе: "Ты" по умолчанию, можно заменить на "Вы".</summary>
    public string Pronoun { get; set; } = "Ты";

    public double Volume { get; set; } = 0.75;
    public bool AutoLoadLyrics { get; set; } = true;
    public bool ShowVisualizer { get; set; } = true;

    /// <summary>Перемешивание и повтор: 0 — выкл, 1 — весь список, 2 — один трек.</summary>
    public bool Shuffle { get; set; }
    public int RepeatMode { get; set; }
    public Tariff Tariff { get; set; } = Tariff.Free;
    public bool LoggedIn { get; set; }
    public string DisplayName { get; set; } = "Гость";
    public string AccountEmail { get; set; } = "";
    public bool DemoPayments { get; set; } = true;
    public int SharePort { get; set; } = 8787;

    /// <summary>Резервный адрес онлайн-сервиса. В интерфейсе не редактируется.</summary>
    public string ApiBaseUrl { get; set; } = ApiClient.DefaultOnlineUrl;

    /// <summary>Токен сессии реального серверного аккаунта.</summary>
    public string AccessToken { get; set; } = "";
    public string AccountId { get; set; } = "";

    /// <summary>true, если вход выполнен через сервер, а не через локальный демо-режим.</summary>
    public bool ServerMode { get; set; }

    /// <summary>Треки, разблокированные разовым доступом 5 ₽ (демо).</summary>
    public List<string> UnlockedTracks { get; set; } = new();

    // Свой магазин
    public string ActivePlanId { get; set; } = "free";
    public string AdsLevel { get; set; } = "full";
    public int BalanceMinor { get; set; }
    public string CatalogVersion { get; set; } = "";
    public List<string> ActiveFeatureIds { get; set; } = new();

    /// <summary>Эквалайзер: 10 полос, значения в дБ от -12 до +12.</summary>
    public bool EqualizerEnabled { get; set; } = true;
    public List<double> EqualizerBands { get; set; } = EqualizerPresets.Flat();
    public string EqualizerPreset { get; set; } = "Ровно";

    /// <summary>Номер звукового устройства WaveOut. 0 — системное по умолчанию.</summary>
    public int OutputDeviceNumber { get; set; }

    // Визуал и сезонные эффекты
    /// <summary>Тема оформления: Тёмная, Светлая или Ночная.</summary>
    public string Theme { get; set; } = ThemeService.Dark;
    public bool SeasonalEffects { get; set; } = true;
    public string SeasonMode { get; set; } = "Авто";
    public int ParticleDensity { get; set; } = 28;
    public bool CompactMode { get; set; }
    public bool ShowNowPlayingCover { get; set; } = true;

    // Текст и караоке
    public bool LyricsClickToSeek { get; set; } = true;
    public bool KaraokeAnimation { get; set; } = true;
    public bool LyricsGlow { get; set; } = true;
    public int LyricsFontSize { get; set; } = 25;
    public bool PreferEnhancedLyrics { get; set; } = true;
    public bool UseLrclib { get; set; } = true;
    public bool UseGenius { get; set; }
    public bool UseMusixmatch { get; set; }
    public string GeniusToken { get; set; } = "";
    public string MusixmatchToken { get; set; } = "";
    public bool SaveLyricsCache { get; set; } = true;

    // Поведение плеера
    public bool AutoPlayNext { get; set; } = true;
    public bool ShowRemainingTime { get; set; }
    public bool RememberVolume { get; set; } = true;
    public bool EnableHotkeys { get; set; } = true;
    public int SeekSeconds { get; set; } = 5;
    public bool ConfirmBeforeDelete { get; set; } = true;
    public bool ShowPlayCounts { get; set; } = true;

    // Сеть и публикация
    public bool ShareOverLan { get; set; } = true;
    public bool BluetoothHint { get; set; } = true;

    // Аккаунты
    public bool RequireEmailCode { get; set; } = true;
    public bool GoogleSignIn { get; set; } = true;
}

public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "settings.json");
    private static readonly object SaveLock = new();
    private static AppSettings _current = Load();

    public static AppSettings Current => _current;

    public static void Reload()
    {
        lock (SaveLock) _current = Load();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (loaded is not null)
                {
                    if (loaded.EqualizerBands is null || loaded.EqualizerBands.Count != EqualizerPresets.Frequencies.Length)
                    {
                        loaded.EqualizerBands = EqualizerPresets.Flat();
                        loaded.EqualizerPreset = "Ровно";
                    }
                    loaded.LyricsFontSize = Math.Clamp(loaded.LyricsFontSize, 16, 40);
                    loaded.ParticleDensity = Math.Clamp(loaded.ParticleDensity, 0, 80);
                    loaded.SeekSeconds = Math.Clamp(loaded.SeekSeconds, 1, 30);
                    loaded.OutputDeviceNumber = Math.Max(0, loaded.OutputDeviceNumber);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // Битые настройки не должны мешать запуску, но копию сохраняем.
            App.WriteCrash(ex, "Settings.Load");
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        return new AppSettings();
    }

    /// <summary>Атомарная запись: при сбое старый файл остаётся нетронутым.</summary>
    public static void Save()
    {
        lock (SaveLock)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_current,
                    new JsonSerializerOptions { WriteIndented = true }));
                if (File.Exists(FilePath))
                {
                    // File.Replace падает, если файл backup уже существует — убираем старый.
                    string backup = FilePath + ".bak";
                    try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                    File.Replace(temp, FilePath, backup);
                }
                else File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                App.WriteCrash(ex, "Settings.Save");
            }
        }
    }
}

/// <summary>Все пользовательские тексты с двумя формами обращения.</summary>
public static partial class Txt
{
    /// <summary>Срабатывает после смены «Ты»/«Вы», чтобы открытые окна обновили надписи.</summary>
    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    private static bool Formal => SettingsService.Current.Pronoun == "Вы";

    public static string Pick(string ty, string vy) => Formal ? vy : ty;

    public static string Greeting => Pick("Что хочешь послушать?", "Что хотите послушать?");
    public static string Offline => Pick("Извини, проверь свою сеть и переподключись.",
                                         "Извините, проверьте свою сеть и переподключитесь.");
    public static string OfflineHint => Pick("Без сети недоступны тексты песен и публикация треков.",
                                             "Без сети недоступны тексты песен и публикация треков.");
    public static string Retry => Pick("Повтори", "Повторите");
    public static string OfflineMode => "Работать офлайн";
    public static string Welcome => "Добро пожаловать";
    public static string LoginNeeded => "Нужно войти";
    public static string AddTracks => "Добавить треки";
    public static string Loading => "Загрузка....";
    public static string AddingTracks => "Добавляем треки...";
    public static string Adding(string name) => "Добавляем: " + name;
    public static string TrackInfoSaved => Pick("Информация о треке сохранена.", "Информация о треке сохранена.");

    public static string SearchTip => Pick("Поиск по твоим трекам", "Поиск по вашим трекам");
    public static string SettingsTitle => "Настройки";
    public static string AccountTitle => "Аккаунт";
    public static string AddTracksDialogTitle => Pick("Выбери свои треки", "Выберите свои треки");
    public static string QuickTitle => "Прослушать локальный файл";
    public static string QuickTip => "MP3, FLAC, M4A, MP4, AAC, OGG, WAV, WMA и другие";
    public static string OnlyOwn => "Только свои треки: никаких загрузок с других сайтов.";
    public static string FirstAddTracks => Pick("Сначала добавь свои треки.", "Сначала добавьте свои треки.");
    public static string FileOpenError => Pick(
        "Не удалось открыть аудиофайл. Проверь, что файл есть на диске.",
        "Не удалось открыть аудиофайл. Проверьте, что файл есть на диске.");
    public static string NothingPlaying => "Ничего не играет";
    public static string LocalFileArtist => "Локальный файл";
    public static string LyricsTitle => "Текст";
    public static string FindLyrics => "Найти текст";
    public static string LyricsLoading => "Ищем текст...";
    public static string LyricsNotFound => Pick(
        "Текст не найден. Положи .lrc рядом с файлом или нажми «Найти текст».",
        "Текст не найден. Положите .lrc рядом с файлом или нажмите «Найти текст».");
    public static string LyricsNotFoundShort => "Текст не найден";
    public static string LyricsFromLrc => "Локальный .lrc";
    public static string LyricsFromEmbedded => "Текст из тегов файла";
    public static string LyricsFromLrclib => "lrclib · синхронный текст";
    public static string LyricsPlain => "lrclib · обычный текст";
    public static string LyricsFound(string source) => $"Найдено: {source}";

    public static string QuickFavorite => Pick(
        "Это быстрый файл. Чтобы сохранить в любимое, добавь его в библиотеку.",
        "Это быстрый файл. Чтобы сохранить в любимое, добавьте его в библиотеку.");
    public static string QuickPlaylist => Pick(
        "Это быстрый файл. Чтобы добавить в плейлист, сначала добавь его в библиотеку.",
        "Это быстрый файл. Чтобы добавить в плейлист, сначала добавьте его в библиотеку.");
    public static string QuickShare => Pick(
        "Это быстрый файл. Чтобы поделиться, сначала добавь его в библиотеку.",
        "Это быстрый файл. Чтобы поделиться, сначала добавьте его в библиотеку.");
    public static string ShareError => Pick(
        "Не удалось запустить локальную публикацию. Проверь порт и брандмауэр.",
        "Не удалось запустить локальную публикацию. Проверьте порт и брандмауэр.");
    public static string LinkCopied(string url) => "Ссылка скопирована: " + url;
    public static string LinkLocal(string url) =>
        "Ссылка только для этого ПК: " + url + " (для сети разрешите URL ACL — см. README)";
    public static string TrackSaved => "Трек сохранён.";
    public static string TrackSaveError => "Не удалось сохранить файл.";
    public static string AddedToPlaylist => "Добавлено в плейлист.";
    public static string NoNewTracks => "Новых треков не найдено.";
    public static string AddedTracks(int count) => $"Добавлено треков: {count}";
    public static string LocalFileStatus(string name) => "Локальный файл: " + name;
    public static string ChooseVideoTitle => Pick("Выбери видео", "Выберите видео");
    public static string VideoMissing => Pick(
        "Для этого трека нет видеоклипа. Выбери видеофайл.",
        "Для этого трека нет видеоклипа. Выберите видеофайл.");
    public static string VideoChoose => "Выбрать видео";
    public static string BackToAudio => "Вернуться к аудио";
    public static string VideoError => "Не удалось воспроизвести видео. Windows не поддерживает этот кодек или файл повреждён.";
    public static string VideoAttached(string name) => "Видео прикреплено: " + name;
    public static string EqualizerTitle => "Эквалайзер";
    public static string EqualizerApplied => "Эквалайзер применён.";
    public static string LyricsAndVisualizer => "Текст и визуализатор";
    public static string VideoClip => "Видеоклип";
    public static string Download => "Скачать трек";
    public static string AddToPlaylist => "Добавить в плейлист";
    public static string Share => "Поделиться ссылкой";
    public static string VizHint => "FFT · 64 полосы";
    public static string HomeTip => Pick("Главная — вся музыка", "Главная — вся музыка");
    public static string AudioOutputTip => Pick(
        "Куда играть: колонка, наушники, Bluetooth",
        "Куда играть: колонка, наушники, Bluetooth");
    public static string AudioOutputCurrent(string name) => Pick(
        "Звук: " + name + " — нажми, чтобы выбрать другое устройство",
        "Звук: " + name + " — нажмите, чтобы выбрать другое устройство");
    public static string OpenSoundSettings => "Настройки звука и Bluetooth…";
    public static string LyricsSeekTip => Pick(
        "Нажми на слово — перемотаю к этому моменту",
        "Нажмите на слово — перемотаю к этому моменту");
    public static string SeasonalTip => "Сезонные эффекты";

    // Регистрация и вход с подтверждением
    public static string CodeSent => Pick(
        "Отправил код на почту. Введи 6 цифр ниже.",
        "Отправил код на почту. Введите 6 цифр ниже.");
    public static string CodeTest(string code) =>
        "Тестовый режим SMTP: код " + code + " (письмо не отправлялось).";
    public static string CodeEmail => "Код подтверждения";
    public static string CodeVerify => "Подтвердить и войти";
    public static string CodeResend => "Запросить новый код";
    public static string LoginWithCode => "Войти по коду из письма";
    public static string GoogleButton => "Войти через Google";
    public static string GoogleNotReady => Pick(
        "Вход через Google пока недоступен. Войди по почте — всё работает и там.",
        "Вход через Google пока недоступен. Войдите по почте — всё работает и там.");
    public static string GoogleWaiting => Pick(
        "Окно Google открыто. Жду подтверждения… Закрой это окно, когда войдёшь.",
        "Окно Google открыто. Жду подтверждения… Закройте это окно, когда войдете.");
    public static string GoogleNotReadyDetail(string issue, string redirect) => Pick(
        "Вход через Google пока недоступен. Войди по почте — аккаунт и все функции на месте.",
        "Вход через Google пока недоступен. Войдите по почте — аккаунт и все функции на месте.");
    public static string GoogleCheckAgain => Pick("Я подтвердил, проверить снова", "Я подтвердил, проверить снова");
    public static string GoogleLinkHint => Pick(
        "Чтобы привязать Google: войди в аккаунт, открой «Связанные сервисы» и нажми «Привязать Google».",
        "Чтобы привязать Google: войдите в аккаунт, откройте «Связанные сервисы» и нажмите «Привязать Google».");

    // Восстановление пароля, смена данных и связанные сервисы.
    public static string ForgotPassword => Pick("Забыл(-и) пароль?", "Забыли пароль?");
    public static string ForgotHint => Pick(
        "Введи почту аккаунта. Если он есть, придёт код для нового пароля.",
        "Введите почту аккаунта. Если он есть, придёт код для нового пароля.");
    public static string ForgotSent(string email) => Pick(
        $"Если аккаунт есть, код отправлен на {email}. Код действует 10 минут.",
        $"Если аккаунт есть, код отправлен на {email}. Код действует 10 минут.");
    public static string CodeSentTo(string email, int seconds) => Pick(
        $"Код отправлен на {email}. Действует {seconds / 60} мин.",
        $"Код отправлен на {email}. Действует {seconds / 60} мин.");
    public static string CodeCopy => "Копировать код";
    public static string CodePaste => "Вставить код";
    public static string NewPassword => "Новый пароль";
    public static string ResetPasswordDone => Pick(
        "Пароль сменён. Ты уже вошёл в аккаунт.",
        "Пароль сменён. Вы уже вошли в аккаунт.");
    public static string SecuritySection => "Безопасность и вход";
    public static string ChangePassword => "Сменить пароль";
    public static string ChangeEmail => "Сменить почту";
    public static string BindPhone => "Привязать телефон";
    public static string UnbindPhone => "Отвязать телефон";
    public static string LinkGoogle => "Привязать Google";
    public static string UnlinkGoogle => "Отвязать Google";
    public static string LinkedServices => "Связанные сервисы";
    public static string LinkedNone => "не привязан";
    public static string CurrentPassword => "Текущий пароль";
    public static string PhoneLabel => "Телефон";
    public static string CodeFromSms => Pick("Код из SMS", "Код из SMS");
    public static string SmsSent(string phone) => Pick(
        $"Код отправлен на {phone}. Если SMS-провайдер не настроен, код показан ниже.",
        $"Код отправлен на {phone}. Если SMS-провайдер не настроен, код показан ниже.");
    public static string SmsNotConfigured => Pick(
        "Код появится прямо здесь — ждать SMS не нужно.",
        "Код появится прямо здесь — ждать SMS не нужно.");
    public static string Done => "Готово";
    public static string EnterCode => Pick("Введи код из 6 цифр.", "Введите код из 6 цифр.");
    public static string InvalidPhone => Pick("Введи номер телефона полностью.", "Введите номер телефона полностью.");
    public static string PasswordTooShort => Pick("Пароль должен быть не короче 6 символов.", "Пароль должен быть не короче 6 символов.");
    public static string PasswordChanged => Pick("Пароль сменён. Другие устройства отключены.", "Пароль сменён. Другие устройства отключены.");
    public static string EmailChanged => Pick("Почта сменена.", "Почта сменена.");
    public static string PhoneLinked => Pick("Телефон привязан.", "Телефон привязан.");
    public static string PhoneUnlinked => Pick("Телефон отвязан.", "Телефон отвязан.");
    public static string GoogleLinked => Pick("Google привязан к аккаунту.", "Google привязан к аккаунту.");
    public static string GoogleUnlinked => Pick("Google отвязан.", "Google отвязан.");
    public static string PhoneLogin => Pick("Войти по SMS", "Войти по SMS");
    public static string LoginByPhoneHint => Pick(
        "Если номер привязан к аккаунту, придёт код. SMS.ru без ключа — код показан в окне.",
        "Если номер привязан к аккаунту, придёт код. SMS.ru без ключа — код показан в окне.");
    public static string NeedLoginForSecurity => Pick("Для управления безопасностью войди в аккаунт.", "Для управления безопасностью войдите в аккаунт.");

    public static string ShuffleTip => Pick("Перемешать треки", "Перемешать треки");
    public static string RepeatTip => Pick("Повтор: выключен", "Повтор: выключен");
    public static string RepeatAllTip => Pick("Повтор: весь список", "Повтор: весь список");
    public static string RepeatOneTip => Pick("Повтор: один трек", "Повтор: один трек");
    public static string MuteTip => Pick("Выключить звук", "Выключить звук");
    public static string UnmuteTip => Pick("Включить звук", "Включить звук");
    public static string RemainingTip => Pick("Нажми, чтобы показать остаток", "Нажмите, чтобы показать остаток");
    public static string TimeToggleTip => Pick("Нажми, чтобы переключить время", "Нажмите, чтобы переключить время");
    public static string ShuffleOn => "Перемешивание включено.";
    public static string ShuffleOff => "Перемешивание выключено.";
    public static string SortLabel => Pick("Сортировка", "Сортировка");
    public static string SortNewest => Pick("Сначала новые", "Сначала новые");
    public static string SortTitle => "По названию";
    public static string SortArtist => "По артисту";
    public static string SortPlays => "По прослушиваниям";
    public static string LyricsFolderTip => Pick("Открыть папку текстов", "Открыть папку текстов");
    public static string EditLyrics => Pick("Свой текст", "Свой текст");
    public static string EditLyricsTip => Pick("Написать или вставить свой .lrc", "Написать или вставить свой .lrc");
    public static string LyricsAttached(string name) => "Текст прикреплён: " + name;
    public static string LyricsSaved => "Свой текст сохранён.";
    public static string LyricsSaveError => Pick(
        "Не удалось сохранить текст. Проверь папку Lyrics.",
        "Не удалось сохранить текст. Проверьте папку Lyrics.");
    public static string LrcDropped(string name) => "Текст добавлен: " + name;
    public static string DeleteTrack => "Удалить из библиотеки";
    public static string DeleteTrackConfirm(string title) => Pick(
        $"Убрать «{title}» из библиотеки? Файл останется на диске.",
        $"Убрать «{title}» из библиотеки? Файл останется на диске.");
    public static string TrackDeleted => "Трек удалён.";
    public static string PlayFromMenu => "Играть";
    public static string FavoriteMenu => "В любимое";
    public static string CommentMenu => "Комментарий";
    public static string PublishMenu => "Опубликовать";
    public static string Save => "Сохранить";
    public static string Cancel => "Отмена";
    public static string LyricsEmpty => Pick("Впиши текст или вставь .lrc.", "Впишите текст или вставьте .lrc.");
    public static string LyricsEditorTitle => "Свой текст";
    public static string LyricsEditorHint => Pick(
        "Вставь свой текст с метками .lrc. Пример: [00:12.34]Строка",
        "Вставьте свой текст с метками .lrc. Пример: [00:12.34]Строка");
    public static string Login => "Войти";
    public static string Register => "Зарегистрироваться";
    public static string Guest => "Гость";
    public static string Profile => "Профиль";
    public static string LoggedInAs(string name) => Pick($"Ты вошёл как {name}", $"Вы вошли как {name}");
    public static string AccountSubtitle => Pick(
        "Войди или создай аккаунт — так открываются длинные треки и тарифы.",
        "Войдите или создайте аккаунт — так открываются длинные треки и тарифы.");
    public static string AccountGuestHint => Pick(
        "Аккаунт хранится в твоём онлайн-профиле. Пароль сохраняется только хешем.",
        "Аккаунт хранится в вашем онлайн-профиле. Пароль сохраняется только хешем.");
    public static string AccountNoAccount => Pick(
        "Нет аккаунта? Открой вкладку «Регистрация».",
        "Нет аккаунта? Откройте вкладку «Регистрация».");
    public static string RepeatPassword => Pick("Повтори пароль", "Повторите пароль");
    public static string InvalidEmail => Pick("Введи корректную почту.", "Введите корректную почту.");
    public static string PasswordsMismatch => "Пароли не совпадают.";
    public static string AccountNeedLogin => Pick(
        "Сначала войди или зарегистрируйся.",
        "Сначала войдите или зарегистрируйтесь.");
    public static string AccountLoggedOut => "Вы вышли из аккаунта.";
    public static string AccountWrong => "Неверная почта или пароль.";
    public static string AccountExists => "Аккаунт с такой почтой уже существует.";
    public static string AccountCreated => "Аккаунт создан.";
    public static string AccountLoggedIn(string name) => $"Вход выполнен: {name}";
    public static string AccountStoredLocal => Pick(
        "Данные аккаунта хранятся в твоём профиле. Пароль не отправляется в открытом виде.",
        "Данные аккаунта хранятся в вашем профиле. Пароль не отправляется в открытом виде.");
    public static string TariffStatus(Tariff tariff) => tariff switch
    {
        Tariff.Single5 => "Разовый доступ 5 ₽",
        Tariff.Pro50 => "Про 50 ₽",
        Tariff.Premium105 => "Премиум 105 ₽",
        Tariff.Ultra200 => "Ультра 200 ₽",
        _ => "Бесплатный 0 ₽"
    };

    // Онлайн-сервис: пользователь не должен видеть адреса, порты и инструкции по запуску.
    public static string ServerUnavailable => Pick(
        "Онлайн пока недоступен. Музыка, тексты и плейлисты работают с этого ПК — повтори позже.",
        "Онлайн пока недоступен. Музыка, тексты и плейлисты работают с этого ПК — повторите позже.");
    public static string ServerRunning => "Онлайн подключён: аккаунт, синхронизация и магазин работают.";
    public static string OnlineDetailsOnline => Pick(
        "Вход по почте и кодам, свои треки на других устройствах, магазин без карты — всё уже включено.",
        "Вход по почте и кодам, свои треки на других устройствах, магазин без карты — всё уже включено.");
    public static string OnlineDetailsOffline => Pick(
        "Своя музыка, тексты и настройки работают всегда. Онлайн вернётся сам, когда появится связь.",
        "Своя музыка, тексты и настройки работают всегда. Онлайн вернётся сам, когда появится связь.");
    public static string SettingsSubtitle => Pick(
        "Всё сохраняется сразу. 48 онлайн-возможностей — без перезапуска плеера.",
        "Всё сохраняется сразу. 48 онлайн-возможностей — без перезапуска плеера.");
    public static string LyricsSourcesHint => Pick(
        "Тексты находятся сами. Никакие ключи и токены вводить не нужно.",
        "Тексты находятся сами. Никакие ключи и токены вводить не нужно.");
    public static string LocalFallbackMode => Pick(
        "Работаешь офлайн. Своя музыка доступна всегда; онлайн подключится сам, когда появится связь.",
        "Работаете офлайн. Своя музыка доступна всегда; онлайн подключится сам, когда появится связь.");
    public static string ServerAccountBadge => "Онлайн-аккаунт";
    public static string DemoAccountBadge => "Локальный аккаунт · только этот ПК";
    public static string ServerTariffOnly => "Тариф и баланс доступны в окне магазина — без карты и реальных списаний.";
    public static string PaymentStarting => "Создаём платёж...";
    public static string PaymentWaiting => "Ожидаем оплату в браузере. Не закрывай это окно.";
    public static string PaymentBrowserOpened => Pick(
        "Открыл страницу оплаты. После оплаты вернись сюда — доступ подключится сам.",
        "Открыли страницу оплаты. После оплаты вернитесь сюда — доступ подключится сам.");
    public static string PaymentChecking => "Проверяем оплату...";
    public static string PaymentSuccess => "Оплата прошла. Тариф активирован.";
    public static string PaymentFailed => Pick(
        "Не удалось открыть магазин. Проверь соединение и попробуй ещё раз.",
        "Не удалось открыть магазин. Проверьте соединение и попробуйте ещё раз.");
    public static string PaymentTimeout => Pick(
        "Время ожидания вышло. Открой окно ещё раз — тариф подтянется сам.",
        "Время ожидания вышло. Откройте окно ещё раз — тариф подтянется сам.");
    public static string PaymentCancelled => "Оплата отменена.";
    public static string PaymentTestMode => "Тестовый режим: реальные деньги не списываются.";
    public static string PaymentRealMode => "Оплата через ЮKassa. Данные карты вводятся на защищённой странице.";
    public static string PaymentRetry => "Ещё раз проверить";

    // Свой магазин и восстановление покупок.
    public static string StoreTitle => "Свой магазин Murzila-Music";
    public static string StorePromoHint => Pick("Промокод (если есть)", "Промокод (если есть)");
    public static string StoreUsePromo => Pick("Купить по промокоду", "Купить по промокоду");
    public static string StoreUseBalance => Pick("Купить с баланса", "Купить с баланса");
    public static string StoreRestore => Pick("Восстановить покупки", "Восстановить покупки");
    public static string StoreRestored(string text) => "Готово: " + text;
    public static string StoreBalance(decimal amount) => $"Баланс: {amount:0.00} ₽";
    public static string StoreFeatures(int count) => $"{count} функций";
    public static string StoreBuy(string name, decimal price) => $"{name} — {price:0} ₽";
    public static string StoreManualSent => Pick(
        "Заказ создан. Администратор подтвердит его — доступ подключится сам.",
        "Заказ создан. Администратор подтвердит его — доступ подключится сам.");
    public static string StorePromoNeeded => Pick("Введи промокод.", "Введите промокод.");
    public static string StoreNotFoundLocal => Pick(
        "Магазин временно недоступен. Тарифы и промокоды вернутся при подключении к онлайну.",
        "Магазин временно недоступен. Тарифы и промокоды вернутся при подключении к онлайну.");
    public static string StoreRefund => Pick("Вернуть на баланс", "Вернуть на баланс");
    public static string StoreRefundDone => Pick(
        "Возврат зачислен на баланс. Не подходящий доступ отключён.",
        "Возврат зачислен на баланс. Не подходящий доступ отключён.");
    public static string StoreAds(string level) => level switch
    {
        "none" => "без рекламы",
        "low" => "мало рекламы",
        _ => "с рекламой"
    };

    public static string ApiUnauthorized => "Неверная почта или пароль.";
    public static string ApiConflict => "Аккаунт с такой почтой уже существует.";
    public static string ApiBadRequest => Pick("Проверь данные и попробуй ещё раз.", "Проверьте данные и попробуйте ещё раз.");
    public static string ApiUnavailable => "Онлайн-сервис пока недоступен. Попробуй позже.";
    public static string ApiUnknown(int code, string reason) => $"Не удалось выполнить запрос ({code}). {reason}";
}
