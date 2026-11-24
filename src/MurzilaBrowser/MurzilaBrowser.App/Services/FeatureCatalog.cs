namespace MurzilaBrowser.App.Services;

public sealed record BrowserFeature(string Name, string Description, bool Ready = true);

/// <summary>50 возможностей браузера — показываются в окне «Возможности».</summary>
public static class FeatureCatalog
{
    public static readonly IReadOnlyList<BrowserFeature> All = new[]
    {
        new BrowserFeature("Вкладки", "Несколько страниц в одном окне"),
        new BrowserFeature("Новая вкладка для поиска", "Никогда не перезаписывает текущую страницу"),
        new BrowserFeature("Адресная строка", "URL, поиск и murzila://"),
        new BrowserFeature("Назад", "Alt+Лево"),
        new BrowserFeature("Вперёд", "Alt+Право"),
        new BrowserFeature("Обновить", "F5"),
        new BrowserFeature("Главная", "Своя стартовая страница"),
        new BrowserFeature("Новая вкладка", "Ctrl+T"),
        new BrowserFeature("Закрыть вкладку", "Ctrl+W"),
        new BrowserFeature("Фокус адреса", "Ctrl+L"),
        new BrowserFeature("История", "Хранится локально в C#, без Python"),
        new BrowserFeature("Очистка истории", "Одна кнопка"),
        new BrowserFeature("Закладки", "Локальный JSON"),
        new BrowserFeature("Удаление закладки", "По URL"),
        new BrowserFeature("Экспорт истории", "JSON в Downloads"),
        new BrowserFeature("Импорт закладок", "JSON из файла"),
        new BrowserFeature("Переводчик", "Google + MyMemory, без ключей"),
        new BrowserFeature("Пакетный перевод", "До 120 строк за раз"),
        new BrowserFeature("Автоопределение языка", "RU/EN"),
        new BrowserFeature("Кэш перевода", "В translations.json"),
        new BrowserFeature("Голосовой перевод", "Системный TTS"),
        new BrowserFeature("Живые голоса", "Несколько голосов, минуты на обработку"),
        new BrowserFeature("Субтитры", "Перевод текста видео"),
        new BrowserFeature("Синхронизация субтитров", "По таймкодам LRC/VTT"),
        new BrowserFeature("Аккаунт Murzila", "Вход через сайт"),
        new BrowserFeature("GitHub-вход", "OAuth через сайт"),
        new BrowserFeature("Google-вход", "OAuth через сайт"),
        new BrowserFeature("2FA", "Через сайт Murzila"),
        new BrowserFeature("Профиль", "Отдельная папка в AppData"),
        new BrowserFeature("Обновления в приложении", "Проверка GitHub Releases"),
        new BrowserFeature("Скачивание обновления", "MSI в Downloads"),
        new BrowserFeature("Уведомление о версии", "В окне возможностей"),
        new BrowserFeature("Статус сети", "Строка состояния"),
        new BrowserFeature("Индикатор загрузки", "Progress bar"),
        new BrowserFeature("Монохромная тема", "Нейтральная тема браузера"),
        new BrowserFeature("Настройки поиска", "6 поисковых систем в настройках"),
        new BrowserFeature("Свой курсор", "Стандартный системный"),
        new BrowserFeature("Свои иконки", "SVG-пути, не Unicode"),
        new BrowserFeature("Блокировщик рекламы", "Базовый список доменов", false),
        new BrowserFeature("Менеджер расширений", "Каркас, включение/выключение", false),
        new BrowserFeature("Менеджер загрузок", "Список файлов", false),
        new BrowserFeature("Полный экран", "F11"),
        new BrowserFeature("Масштаб", "Ctrl +/-"),
        new BrowserFeature("Скриншот вкладки", "PNG в Downloads", false),
        new BrowserFeature("Инкогнито", "Отдельный контекст", false),
        new BrowserFeature("Аппаратное ускорение", "Включается в настройках"),
        new BrowserFeature("Автозаполнение", "Формы", false),
        new BrowserFeature("Медиа-клавиши", "Play/Pause", false),
        new BrowserFeature("Жесты мышью", "Свайпы назад/вперёд", false),
        new BrowserFeature("Экономичный режим", "Меньше лагов, 4K легче")
    };
}
