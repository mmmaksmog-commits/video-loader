# Video Loader by mysik

Скачивает видео и музыку с YouTube, VK Видео, RuTube, TikTok и сотен других сайтов. Скопировали ссылку, открыли программу — видео уже найдено.

**Страница программы и скачивание:** https://mysik-portfolio.vercel.app/soft/video-loader.html

## Что умеет

- **Ссылка из буфера** — программа сама подхватывает скопированную ссылку.
- **Качество на выбор** — 4K, 2K, 1080p, 720p… или только звук в MP3.
- **Плейлисты целиком** — каждый в свою папку.
- **Очередь загрузок** — скорость, сколько осталось, отмена и повтор.
- **Для любого плеера** — mP4 в H.264 открывается везде, даже на старом телевизоре.
- **MP3 с обложкой** — с названием и автором, как в музыкальном сервисе.

## Как начать

Нужна только Windows 10 или 11 — компилятор .NET Framework 4.x уже встроен в систему.

1. Скачайте репозиторий: зелёная кнопка **Code → Download ZIP**, и распакуйте.
2. Запустите `build.bat` — через пару секунд рядом появится `VideoLoader.exe`.
3. Откройте `VideoLoader.exe`. Права администратора не нужны.
4. **Скачайте компоненты.** При первом запуске программа один раз загрузит yt-dlp, ffmpeg и deno (~200 МБ) с их официальных страниц.
5. **Скопируйте ссылку на видео.** Вернитесь в программу, выберите качество и нажмите «Скачать».

## Полезно знать

- YouTube качается медленно — включите Zapret GUI.
- Перестал работать какой-то сайт — в «Настройках» нажмите «Обновить» у yt-dlp.
- Скачивайте только то, что можно: свои видео, открытые для загрузки или для личного просмотра.

Файлы: `MainWindow.xaml`, `README.txt`, `VideoLoader.cs`, `app.ico`, `app.manifest`, `build.bat`.

## Другие программы by mysik

- [Zapret GUI](https://mysik-zapret.netlify.app) — Возвращает Discord, YouTube и Steam одной кнопкой. Без VPN: скорость и IP остаются вашими.
- [DNS Switch](https://mysik-portfolio.vercel.app/soft/dns-switch.html) — Меняет DNS в один клик и сам проверяет, какой быстрее именно у вас.
- [Пинг](https://mysik-portfolio.vercel.app/soft/ping.html) — Следит за интернетом каждую секунду и подсказывает, где проблема: Wi-Fi, роутер или провайдер.
- [Передача](https://mysik-portfolio.vercel.app/soft/peredacha.html) — Файлы, фото и тексты между компьютером и телефоном по QR-коду. Без проводов и облаков.
- [Converter](https://mysik-portfolio.vercel.app/soft/converter.html) — Видео, музыка и картинки в нужный формат, включая HEIC с iPhone. Умеет сжимать перед отправкой.
- [Снимок](https://mysik-portfolio.vercel.app/soft/snimok.html) — Скриншот области по PrintScreen, стрелки, размытие и нумерация. Сразу в буфер.
- [Win Cleaner](https://mysik-portfolio.vercel.app/soft/win-cleaner.html) — Чистит мусор, убирает рекламу и Copilot, наводит порядок в автозагрузке. Всё можно вернуть.
- [Диск](https://mysik-portfolio.vercel.app/soft/disk.html) — Карта диска за минуту: видно, какие папки и файлы съели всё место.
- [Раскладка](https://mysik-portfolio.vercel.app/soft/raskladka.html) — Напечатали «ghbdtn»? Одна клавиша — и слово исправлено, раскладка переключена.
- [Буфер](https://mysik-portfolio.vercel.app/soft/bufer.html) — Помнит всё, что вы копировали: текст, ссылки, картинки. Ctrl+Shift+V — и вставлено.
- [Визитка](https://mysik-portfolio.vercel.app/soft/vizitka.html) — Конструктор сайтов для мастеров: маникюр, барбер, массаж. Сайт под ключ за 15–30 минут.

Все программы — в портфолио: https://mysik-portfolio.vercel.app/#programs  
Сделано [mysik](https://mysik-portfolio.vercel.app).
