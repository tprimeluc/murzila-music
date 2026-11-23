namespace MurzilaMusic.App.Services;

/// <summary>Тексты обновлённого интерфейса beta 1.3 (шапка, боковая панель, баннер, плеер).</summary>
public static partial class Txt
{
    // Шапка
    public static string HeaderOnlineBadge => "  ·  онлайн";
    public static string SettingsTip => "Настройки";

    // Строки трека (используются через DynamicResource в шаблонах)
    public static string RowFavoriteTip => "В любимое";
    public static string RowPublishTip => "Опубликовать и скопировать ссылку";

    // Промо-карточка
    public static string PromoBadge => "БЕСПЛАТНО НАВСЕГДА";
    public static string PromoTitle => "Музыка без границ";
    public static string PromoText => Pick(
        "48 онлайн-возможностей. Свои треки всегда с тобой.",
        "48 онлайн-возможностей. Свои треки всегда с вами.");
    public static string PromoButton => Pick("Смотреть возможности", "Смотреть возможности");

    // Боковая панель
    public static string MenuCaption => "МЕНЮ";
    public static string NavHome => "Главная";
    public static string NavHomeTip => Pick("Вся музыка и чистый поиск", "Вся музыка и чистый поиск");
    public static string NavAll => "Моя музыка";
    public static string NavFavorites => "Любимое";
    public static string PlaylistsCaption => "ПЛЕЙЛИСТЫ";
    public static string ImportPlaylist => "Импорт плейлиста";
    public static string ImportPlaylistTip => Pick(
        "M3U, JSON или XML со списком твоих локальных файлов",
        "M3U, JSON или XML со списком ваших локальных файлов");
    public static string Backup => "Резервная копия";
    public static string SyncTracks => "Треки на других устройствах";
    public static string SyncTip => "Загрузить опубликованные треки в свой сервер и скачать их в этом аккаунте";
    public static string FeaturesNav => "48 онлайн-возможностей";
    public static string FeaturesTip => "Что уже работает в бесплатном онлайн-сервисе";
    public static string OnlyOwnBanner => "Только свои треки. Никаких чужих загрузок.";

    // Герой-баннер
    public static string HeroKicker => Pick("ТВОЯ МУЗЫКА. ВСЕГДА С ТОБОЙ", "ТВОЯ МУЗЫКА. ВСЕГДА С ВАМИ");
    public static string HeroTitle => Pick("Слушай своё — без границ и чужого.", "Слушайте своё — без границ и чужого.");
    public static string HeroText => Pick(
        "Твои локальные треки, тексты, видео и облако в одном красивом плеере.",
        "Ваши локальные треки, тексты, видео и облако в одном красивом плеере.");
    public static string HeroAdd => Pick("Добавить музыку", "Добавить музыку");
    public static string HeroFeatures => Pick("48 возможностей", "48 возможностей");

    // Разделы и «Сейчас играет»
    public static string MyMusicTitle => "Моя музыка";
    public static string SortHint => "Сортировка";
    public static string BackToPlayer => Pick("К плееру", "К плееру");

    // Восстановление пароля
    public static string ForgotEmailMismatch(string email) => Pick(
        $"В аккаунте указана почта {email}. Проверь адрес.",
        $"В аккаунте указана почта {email}. Проверьте адрес.");

    // Тема оформления
    public static string ThemeLabel => "Тема оформления";
    public static string ThemeHint => Pick(
        "Меняй тему прямо здесь — перезапуск не нужен.",
        "Меняйте тему прямо здесь — перезапуск не нужен.");
    public static IReadOnlyList<string> ThemeNames => ThemeService.Names;

    // Индикация загрузки
    public static string BusyDefault => "Загрузка…";

    // Импорт плейлиста
    public static string ImportPlaylistDialogTitle => Pick(
        "Импорт своего плейлиста (только локальные файлы)",
        "Импорт своего плейлиста (только локальные файлы)");
    public static string ImportPlaylistFilter => "Плейлисты (*.m3u;*.m3u8;*.json;*.xml;*.pls)|*.m3u;*.m3u8;*.json;*.xml;*.pls";
    public static string ImportPlaylistStatus => Pick("Импорт плейлиста…", "Импорт плейлиста…");
    public static string ImportPlaylistBusy => Pick(
        "Импорт своего плейлиста…", "Импорт своего плейлиста…");
    public static string PlaylistImported(string name, int added, int matched, int skipped) => Pick(
        $"Плейлист «{name}»: добавлено {added}, найдено {matched}, пропущено {skipped}.",
        $"Плейлист «{name}»: добавлено {added}, найдено {matched}, пропущено {skipped}.");

    // Синхронизация
    public static string SyncLoginHint => Pick(
        "Чтобы забрать свои треки на другом устройстве, войди в тот же аккаунт.",
        "Чтобы забрать свои треки на другом устройстве, войдите в тот же аккаунт.");
    public static string SyncBusy => Pick(
        "Синхронизация своих треков…", "Синхронизация своих треков…");

    // Статусы и диалоги
    public static string LibraryRestored => Pick(
        "Библиотека восстановлена из резервной копии.",
        "Библиотека восстановлена из резервной копии.");
    public static string CommentSaved => Pick("Комментарий сохранён.", "Комментарий сохранён.");
    public static string TrackCount(int count, long played) => $"Треков: {count} · прослушано: {played}";
    public static string AudioFilter => "Аудио (*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma)|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma|Все файлы (*.*)|*.*";
    public static string AudioVideoFilter => "Аудио и видео|*.mp3;*.flac;*.m4a;*.m4b;*.mp4;*.m4v;*.aac;*.ogg;*.opus;*.wav;*.wma;*.aiff;*.aif;*.wmv;*.avi;*.mkv;*.mov|Все файлы (*.*)|*.*";
    public static string SaveTrackTitle => Pick("Сохранить трек", "Сохранить трек");
    public static string SaveTrackFilter => "Аудио (*.*)|*.*";
    public static string PublishDisabled => Pick(
        "Публикация в сети выключена в настройках.",
        "Публикация в сети выключена в настройках.");
}
