# Murzila Browser v2

Обычный браузер: вкладки, адресная строка, поиск, история, закладки. Свой движок — **Chromium/CEF (Blink + V8)**. Не WebView2, не PyWebEngine, **Python полностью убран из браузера**.

## Что изменилось в v2

- Обычный внешний вид: нейтральная синяя тема, стартовая страница без music-брендинга.
- Любой поиск/ввод адреса **всегда открывает новую вкладку** — текущая не перезаписывается.
- Python-помощник удалён. История, закладки, перевод, обновления и голос — на C#.
- Кнопка «50» в панели открывает окно со списком из 50 возможностей.
- Контекстное меню вкладки: перевод страницы, обычный голос, живые голоса, вход через Murzila/Google.
- Обновления проверяются прямо в приложении через GitHub Releases (нужен интернет).
- Обход 18+ за 200 ₽ **не делаем** (нет платёжных ключей, механизмы YouTube меняются).

## Архитектура

```
src/MurzilaBrowser/
├── MurzilaBrowser.App/
│   ├── MainWindow.xaml(.cs)     хром браузера, вкладки, меню, проверка обновлений
│   ├── App.xaml.cs              CEF + кастомная схема murzila://
│   ├── Scheme/                  внутренние страницы
│   ├── Services/
│   │   ├── BrowserPrefs.cs      настройки
│   │   ├── LocalDataService.cs  история/закладки (JSON, C#)
│   │   ├── TranslationService.cs Google + MyMemory + кэш
│   │   ├── VoiceService.cs      системный TTS + «живые голоса»
│   │   ├── UpdateService.cs     GitHub Releases
│   │   ├── AccountService.cs    Murzila / GitHub / Google через сайт
│   │   └── FeatureCatalog.cs    50 возможностей
│   ├── Windows/FeaturesWindow   окно возможностей
│   └── Resources/               murzila://home, newtab, music, error
└── tools/                       Node.js: манифест внутренних страниц
```

Движок: CEF 156 = Chromium с Blink + V8. JavaScript, Web Audio, Canvas и современные сайты работают.

## Сборка

```powershell
dotnet build src/MurzilaBrowser/MurzilaBrowser.App/MurzilaBrowser.App.csproj -c Release
.\src\MurzilaBrowser\MurzilaBrowser.App\bin\Release\net10.0-windows\win-x64\MurzilaBrowser.exe
```

## Windows 7

Текущая сборка требует Windows 10/11: .NET 10 и CEF 156 на Win7 не запускаются. Для Win7 нужна отдельная legacy-ветка: .NET Framework 4.8.1 + CefSharp 109 (последний CEF с поддержкой Win7). Тогда часть современных сайтов будет работать хуже. Это отдельный этап после релиза 01.12.
