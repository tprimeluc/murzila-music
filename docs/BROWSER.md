# Murzila Browser

Свой браузер Murzila: оболочка на **C# / WPF**, движок **Chromium через CEF (CefSharp)**, локальный помощник на **Python**, сборка внутренних страниц на **Node.js**. WebView2 не используется вообще.

## Зачем

- Любые сайты на полноценном движке Chromium (JavaScript, Web Audio, Canvas, Range-стрим — всё работает).
- Две свои вкладки прямо в коде: **Главная** (`murzila://home`) и **Музыка** (`murzila://music`, веб-плеер Murzila).
- Свой тёмный интерфейс, свои векторные иконки, вкладки, адресная строка, история, закладки.

## Архитектура

```
src/MurzilaBrowser/
├── MurzilaBrowser.App/        C# WPF: окно, вкладки, адресная строка, CEF
│   ├── MainWindow.xaml        хром браузера (все иконки — SVG-пути, не Unicode)
│   ├── Services/
│   │   ├── BrowserPrefs.cs    настройки в %APPDATA%\MurzilaBrowser
│   │   └── PythonBridge.cs    HTTP-клиент к локальному помощнику
│   ├── Scheme/
│   │   └── MurzilaSchemeHandlerFactory.cs  внутренние страницы murzila://
│   └── Resources/             home.html, music.html, newtab.html, error.html, style.css
├── sidecar/server.py          Python: история, закладки, перевод (stdlib, без pip)
└── tools/                     Node.js: манифест внутренних страниц
```

Потоки данных:

1. C# при старте поднимает CEF с кастомной схемой `murzila://` и открывает вкладки.
2. C# запускает `python sidecar/server.py 6137` и показывает статус в строке состояния.
3. История и закладки пишутся в `%APPDATA%\MurzilaBrowser\*.json` через Python.
4. Node.js скрипт `npm run build` пересобирает `Assets/pages.manifest.json`.

## Сборка и запуск

```powershell
cd D:\Murzila-Music
dotnet build src/MurzilaBrowser/MurzilaBrowser.App/MurzilaBrowser.App.csproj -c Release

# exe лежит в bin/Release/net10.0-windows/win-x64/
.\src\MurzilaBrowser\MurzilaBrowser.App\bin\Release\net10.0-windows\win-x64\MurzilaBrowser.exe

# можно сразу открыть адрес
.\...\MurzilaBrowser.exe http://localhost:6001/player
```

Python нужен в `PATH` (проверка: `python --version`). Если его нет, браузер работает, но история/закладки/перевод отключаются.

## Управление

| Клавиши | Действие |
|---|---|
| Ctrl+T | новая вкладка |
| Ctrl+W | закрыть вкладку |
| Ctrl+L | фокус на адресной строке |
| F5 | обновить |
| Alt+Лево / Alt+Право | назад / вперёд |

Ввод без схемы: `localhost:6001` → `http://`, `example.com` → `http://`, anything with space → поиск DuckDuckGo.

## Релиз 01.12.2026

- [ ] Собрать Release x64, убрать отладочные логи.
- [ ] Сделать MSI (как у Murzila Music) и ZIP-архив с портативной версией.
- [ ] Добавить ярлык на рабочий стол и свою иконку.
- [ ] Загрузить ассеты в GitHub Release `v3.7.0`.
- [ ] Проверить запуск на чистой машине без installed Python (браузер должен работать).

## Известные ограничения

- CEF-рантайм занимает ~180 МБ в папке приложения — это нормально для полноценного Chromium.
- Внутренние страницы `murzila://` отдаются из встроенных ресурсов C#, отдельный веб-сервер для них не нужен.
- Перевод в Python-помощнике использует публичные API и может быть недоступен без интернета.
