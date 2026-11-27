# Murzila Music — деплой в интернет

> **Переходим на свой хостинг.** Инструкция по собственному серверу (порт 80, все сайты на одном IP) — в [OWN_HOSTING.md](OWN_HOSTING.md). Раздел ниже про Render оставлен как запасной вариант.

Стек: C# / ASP.NET Core 10 + Docker + Render (бесплатный Web Service) + DNS на my.dnshe.com.

## 1. GitHub (бесплатно, без карты)
1. Зарегистрируйся на https://github.com.
2. Создай **пустой** репозиторий, например `murzila-music` (без README, без .gitignore).
3. В PowerShell открыть `D:\Murzila-Music` и выполнить (подставь свой логин):

```powershell
git init
git config user.email "твоя-почта@example.com"
git config user.name "твой-логин"
git add .
git commit -m "Murzila Music beta 1.3 - cloud"
git branch -M main
git remote add origin https://github.com/ТВОЙ_ЛОГИН/murzila-music.git
git push -u origin main
```

## 2. Render (бесплатно, без карты)
1. https://render.com → «Sign up with GitHub».
2. **New → Web Service** → выбери репозиторий `murzila-music`.
3. Render увидит `render.yaml` и `Dockerfile` — выбери **Docker**, план **Free**.
4. В разделе **Environment** добавь секреты (не в GitHub!):
   - `MURZILA_SERVER_URL` = `https://murzila-music.cc.cd`
   - `MURZILA_BOT_KEY` = тот же ключ, что в `.env` бота
   - `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM` — из твоего локального `.env` сервера
   - `GENIUS_ACCESS_TOKEN` — токен Genius
5. Create Web Service. Через несколько минут получишь адрес вида `https://murzila-music.onrender.com`.
6. Проверь `https://murzila-music.onrender.com/api/health` → должен быть `{"status":"ok"}`.

## 3. Домен (у тебя уже есть murzila-music.cc.cd)
1. В Render: **Settings → Custom Domains → Add Custom Domain** → `murzila-music.cc.cd`.
2. Render покажет CNAME-запись (обычно `murzila-music.onrender.com`).
3. В my.dnshe.com удали старую A-запись и добавь **CNAME**:
   - Название: `@`
   - Содержимое: `murzila-music.onrender.com`
4. Подожди 5–30 минут. Render сам выпустит бесплатный HTTPS-сертификат.

## 4. Бот
В `.env` бота поменяй:
```
MURZILA_API_URL=https://murzila-music.cc.cd
```
Остальные токены остаются теми же. Перезапусти бота.

## Альтернатива Render: Koyeb (если не хочешь Render)
1. https://app.koyeb.com →_sign up с GitHub.
2. **Create App → Deploy from GitHub** → репозиторий `murzila-music`, выбери **Dockerfile**.
3. Порт: `8080` (Koyeb сам задаст env `PORT`, сервер его понимает).
4. В Environment добавь те же секреты, что в разделе Render.
5. В **Domains** добавь `murzila-music.cc.cd`, Koyeb покажет CNAME → `*.koyeb.app`.
6. В dnshe замени A-запись на CNAME `@ → <показанный koyeb-адрес>`.
Если Koyeb попросит карту — возвращайся к Render, это единственный бесплатный вариант без карты, которому я могу доверять для C# + домена.

## Важное
- Render Free/Koyeb Free засыпают после простоя — первый запрос может идти ~1 минуту.
- База данных в контейнере **новая и пустая**: после деплоя зарегистрируйся на сайте и добавь треки через «Добавить треки», либо укажи приложению `MURZILA_SERVER_URL=https://murzila-music.cc.cd` и нажми «Синхронизировать».
- Секреты (SMTP, API-ключи) не коммить в GitHub — только в Environment на Render.
- Свой DNS-сервер на C# для этого не нужен: публичный домен работает через DNS хостинга/регистратора.
