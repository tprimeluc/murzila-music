# Murzila Browser Native (C++)

Каркас перехода браузера на C++. Компилятор уже стоит: MSVC 14.44 из Visual Studio 2022 Build Tools (`vcvars64.bat`).

Сборка:
```
build.bat
```

Сейчас это Win32-окно. Для полноценного браузера нужно подключить **CEF SDK** (заголовки `include/`, `libcef.lib`, `libcef_dll_wrapper`) — это ~[large] загрузка, и она делается отдельным этапом. Дальше: движок CEF (Blink + V8) + WebGL/WebGPU-рендер для миллионов объектов, как описано в предложенной архитектуре.

Murzila Music, pet-speech и Discord-бот не трогаем — они работают на C#/Python и остаются как есть.
