# Video Loader by mysik

Программа для Windows, которая скачивает видео и музыку с YouTube, VK Видео, RuTube, TikTok и сотен других сайтов. Скопировали ссылку, открыли программу — видео уже найдено.

## Что умеет

- **Ссылка из буфера** — программа сама подхватывает скопированную ссылку
- **Качество на выбор** — 4K, 2K, 1080p, 720p… или только звук в MP3
- **Плейлисты целиком** — в отдельную папку
- **Очередь загрузок** — скорость, сколько осталось, отмена и повтор
- **Видео для любого плеера** — MP4 в H.264, открывается везде
- **MP3 с обложкой**, названием и автором
- **Работает вместе с Zapret GUI** — YouTube качается на полной скорости

## Первый запуск

1. Запустите `VideoLoader.exe` — права администратора не нужны.
2. Программа предложит один раз скачать компоненты (~200 МБ) с их официальных страниц на GitHub:
   [yt-dlp](https://github.com/yt-dlp/yt-dlp) скачивает видео, [ffmpeg](https://github.com/yt-dlp/FFmpeg-Builds) склеивает картинку со звуком, [deno](https://github.com/denoland/deno) нужен для YouTube.
   Они хранятся в `%LOCALAPPDATA%\VideoLoader`.
3. Видео сохраняются в «Загрузки\Video Loader», папку можно поменять в «Настройках».

## Если что-то не работает

- YouTube качается медленно или не находит видео — включите [Zapret GUI](https://mysik-zapret.netlify.app).
- Перестал работать какой-то сайт — в «Настройках» нажмите «Обновить» у yt-dlp.
- «YouTube просит подтверждение» — подождите пару минут и попробуйте снова.
- Антивирус удалил компоненты — добавьте `%LOCALAPPDATA%\VideoLoader` в исключения.

> Скачивайте только то, что можно: свои видео, открытые для загрузки или для личного просмотра.

## Сборка

Нужна только Windows 10 или 11 — компилятор .NET Framework 4.x уже встроен в систему.

1. Скачайте репозиторий: зелёная кнопка **Code → Download ZIP**, и распакуйте.
2. Запустите `build.bat` — за несколько секунд рядом появится `VideoLoader.exe`.
3. Перенесите `VideoLoader.exe` куда удобно и запускайте.

Файлы в репозитории: `VideoLoader.cs`, `MainWindow.xaml`, `app.manifest`, `app.ico`, `build.bat`.

## Другие программы by mysik

- [Zapret GUI](https://mysik-zapret.netlify.app) — возвращает Discord, YouTube и Steam без VPN
- [Video Loader](https://github.com/mmmaksmog-commits/video-loader) — скачивание видео и музыки
- [Converter](https://github.com/mmmaksmog-commits/converter) — конвертер видео, музыки и картинок
- [Win Cleaner](https://github.com/mmmaksmog-commits/win-cleaner) — очистка и настройка Windows
- [DNS Switch](https://github.com/mmmaksmog-commits/dns-switch) — смена DNS в один клик

Все программы — на сайте **[mysik-zapret.netlify.app](https://mysik-zapret.netlify.app)**.
Сделано [mysik](https://mysik-portfolio.vercel.app).
