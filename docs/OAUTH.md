# Настройка входа через Google и GitHub

Сервер использует `MURZILA_SERVER_URL` как публичный адрес для callback-ов.

## Важно про адрес
Сейчас твой сайт живёт на `https://murzila-music.onrender.com`.
Поэтому в Render → Environment выставь:
```
MURZILA_SERVER_URL=https://murzila-music.onrender.com
```
Когда подключишь свой домен, поменяешь на `https://murzila-music.cc.cd` и добавишь в OAuth-приложения **вторые** callback-адреса с доменом.

---

## 1. GitHub OAuth

1. Открой https://github.com/settings/developers
2. **OAuth Apps → New OAuth App**
3. Заполни:
   - **Application name:** `Murzila Music`
   - **Homepage URL:** `https://murzila-music.onrender.com`
   - **Authorization callback URL:** `https://murzila-music.onrender.com/api/auth/github/callback`
   - Description: `Вход в Murzila Music через GitHub`
4. **Register application**
5. Скопируй **Client ID**, нажми **Generate a new client secret**, скопируй secret.
6. В Render → твой сервис → **Environment → Add Environment Variable**:
   - `GITHUB_CLIENT_ID` = Client ID
   - `GITHUB_CLIENT_SECRET` = Client secret
7. Render сам сделает redeploy. Проверь кнопку «Войти через GitHub» в плеере.

---

## 2. Google OAuth

1. Открой https://console.cloud.google.com/
2. Создай проект (или выбери существующий), например `Murzila Music`.
3. **APIs & Services → OAuth consent screen**:
   - User Type: **External**
   - App name: `Murzila Music`
   - User support email: твой email
   - Developer contact: твой email
   - Scopes: оставь `email`, `profile`, `openid`
   - **Test users** → добавь `tprimeluc@gmail.com` (иначе вход будет заблокирован, пока приложение не проверено)
   - Authorized domains: `murzila-music.onrender.com` (если Google потребует подтверждение домена — пока просто используй test user)
4. **APIs & Services → Credentials → Create Credentials → OAuth client ID**:
   - Application type: **Web application**
   - Name: `Murzila Music Web`
   - **Authorized redirect URIs** → добавь:
     ```
     https://murzila-music.onrender.com/api/auth/google/callback
     ```
5. Скопируй **Client ID** и **Client secret**.
6. В Render → Environment:
   - `GOOGLE_CLIENT_ID` = Client ID
   - `GOOGLE_CLIENT_SECRET` = Client secret
7. Redeploy → проверь кнопку «Войти через Google».

---

## 3. После подключения домена
Добавь в оба OAuth-приложения вторые redirect/callback URI:
```
https://murzila-music.cc.cd/api/auth/github/callback
https://murzila-music.cc.cd/api/auth/google/callback
```
И поменяй `MURZILA_SERVER_URL` на `https://murzila-music.cc.cd`.

---

## Проверка
- `GET /api/auth/google/start` → 200 с полем `url`, если ключи заданы.
- `GET /api/auth/github/start` → 200 с полем `url`, если ключи заданы.
- Без ключей endpoint вернёт 400 с понятным текстом «не заданы ...».
