# Главная страница MursikT7 Corporation

Статический сайт в папке `site/` — **не на Render**. Работает на любом бесплатном статическом хостинге.

## Деплой (бесплатно)
- **Cloudflare Pages**: подключи GitHub-репозиторий `tprimeluc/murzila-music`, Build command пустой, Output directory = `site`.
- **GitHub Pages**: Settings → Pages → Deploy from a branch → `main` / `docs` или скопируй содержимое `site/` в `docs/`.
- **Netlify**: New site from Git → Base directory `site`, без сборки.

## Домен
Нужна зона для `mursikt7-corp.<зона домена>`. После получения зоны:
1. В хостинге добавить Custom domain `mursikt7-corp.<зона>`.
2. У DNS-провайдера сделать CNAME на адрес, который покажет хостинг.
3. В `site/index.html` ссылки на плеер можно заменить с `murzila-music.onrender.com` на свой адрес плеера.

Сайт ч/б, со своим курсором, анимацией иконки и пасхалкой в подвале (5 секунд наведения).
