// Video Loader by mysik — скачивание видео и музыки с YouTube и других сайтов (на основе yt-dlp).
// WPF, собирается встроенным в Windows компилятором (C# 5, .NET Framework 4.x) — см. build.bat.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shapes = System.Windows.Shapes;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Video Loader")]
[assembly: AssemblyDescription("Video Loader by mysik — скачивание видео и музыки")]
[assembly: AssemblyCompany("mysik")]
[assembly: AssemblyProduct("Video Loader by mysik")]
[assembly: AssemblyCopyright("by mysik")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace VideoLoader
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "VideoLoaderByMysik_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Video Loader уже открыт.", "Video Loader by mysik", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | (SecurityProtocolType)192;
                ServicePointManager.DefaultConnectionLimit = 32;
                WF.Application.EnableVisualStyles();

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += (s, e) =>
                {
                    MessageBox.Show("Ошибка: " + e.Exception.Message, "Video Loader by mysik", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };
                app.Startup += (s, e) =>
                {
                    MainWindow w;
                    try { w = new MainWindow(); }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Не удалось открыть окно:\n" + ex, "Video Loader by mysik", MessageBoxButton.OK, MessageBoxImage.Error);
                        app.Shutdown();
                        return;
                    }
                    app.MainWindow = w;
                    w.Show();
                    w.Init();
                };
                app.Run();
            }
        }
    }

    // ------------------------------------------------------------------ пути и настройки

    static class Paths
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoLoader");
        public static readonly string Bin = Path.Combine(Root, "bin");
        public static readonly string Tmp = Path.Combine(Root, "tmp");
        public static string YtDlp { get { return Path.Combine(Bin, "yt-dlp.exe"); } }
        public static string Ffmpeg { get { return Path.Combine(Bin, "ffmpeg.exe"); } }
        public static string Ffprobe { get { return Path.Combine(Bin, "ffprobe.exe"); } }
        public static string Deno { get { return Path.Combine(Bin, "deno.exe"); } }
        public static string Ini { get { return Path.Combine(Root, "settings.ini"); } }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string DownloadsFolder()
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    class Settings
    {
        public string Folder;
        public bool Compat = true;
        public bool AutoPaste = true;
        public bool Cover = true;
        public int Quality = 1080;

        public void Load()
        {
            Folder = Path.Combine(Paths.DownloadsFolder(), "Video Loader");
            try
            {
                if (!File.Exists(Paths.Ini)) return;
                foreach (var raw in File.ReadAllLines(Paths.Ini, Encoding.UTF8))
                {
                    int i = raw.IndexOf('=');
                    if (i <= 0) continue;
                    string k = raw.Substring(0, i).Trim().ToLowerInvariant();
                    string v = raw.Substring(i + 1).Trim();
                    if (k == "folder" && v.Length > 0) Folder = v;
                    else if (k == "compat") Compat = v == "1";
                    else if (k == "autopaste") AutoPaste = v == "1";
                    else if (k == "cover") Cover = v == "1";
                    else if (k == "quality") int.TryParse(v, out Quality);
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.Root);
                File.WriteAllLines(Paths.Ini, new[]
                {
                    "folder=" + Folder,
                    "compat=" + (Compat ? "1" : "0"),
                    "autopaste=" + (AutoPaste ? "1" : "0"),
                    "cover=" + (Cover ? "1" : "0"),
                    "quality=" + Quality
                }, Encoding.UTF8);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ запуск процессов

    class Runner
    {
        public Process P;
        public bool Killed;

        // Запускает программу, отдаёт каждую строку stdout/stderr в onLine (из фонового потока).
        public Task<int> Run(string exe, string args, Action<string> onLine)
        {
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Paths.Bin;
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";

            var p = new Process();
            p.StartInfo = psi;
            p.OutputDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            P = p;

            return Task.Run(() =>
            {
                p.WaitForExit();   // без таймаута ждёт и окончания вывода
                int code = -1;
                try { code = p.ExitCode; } catch { }
                return code;
            });
        }

        public void Kill()
        {
            Killed = true;
            var p = P;
            if (p == null) return;
            try
            {
                if (p.HasExited) return;
                // yt-dlp запускает ffmpeg — гасим всё дерево процессов
                var k = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                k.UseShellExecute = false;
                k.CreateNoWindow = true;
                using (var kp = Process.Start(k)) kp.WaitForExit(5000);
            }
            catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        // Кавычки для командной строки Windows.
        public static string Q(string s)
        {
            if (s == null) s = "";
            if (s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return s;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in s)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', bs); sb.Append(c); }
                bs = 0;
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ yt-dlp

    class QualityOption
    {
        public int Height;      // >0 — ограничение по высоте, 0 — лучшее, -1 — только звук (MP3)
        public string Label;
        public string Sub;
        public QualityOption(int h, string label, string sub) { Height = h; Label = label; Sub = sub; }
    }

    class VideoInfo
    {
        public string Url, Title, Channel, Duration, Thumb, Id, Extractor, PlaylistTitle;
        public int PlaylistCount;
        public bool IsLive, HasListParam;
        public List<int> Resolutions = new List<int>();
        public bool HasFormatInfo;

        public bool IsPlaylistOnly { get { return PlaylistCount > 0 && !string.IsNullOrEmpty(PlaylistTitle); } }

        public List<QualityOption> Options()
        {
            var list = new List<QualityOption>();
            int[] tiers = { 2160, 1440, 1080, 720, 480, 360 };
            var have = new HashSet<int>();
            foreach (int r in Resolutions)
                foreach (int t in tiers)
                    if (r >= t * 0.9) { have.Add(t); break; }

            foreach (int t in tiers)
            {
                if (!have.Contains(t)) continue;
                if (t == 2160) list.Add(new QualityOption(t, "4K", "2160p"));
                else if (t == 1440) list.Add(new QualityOption(t, "2K", "1440p"));
                else if (t == 1080) list.Add(new QualityOption(t, "1080p", "Full HD"));
                else if (t == 720) list.Add(new QualityOption(t, "720p", "HD"));
                else list.Add(new QualityOption(t, t + "p", "SD"));
            }
            if (list.Count == 0) list.Add(new QualityOption(0, "Видео", "лучшее"));
            list.Add(new QualityOption(-1, "MP3", "только звук"));
            return list;
        }
    }

    static class YtDlp
    {
        public static string Common()
        {
            return "--ignore-config --no-colors" +
                   " --js-runtimes " + Runner.Q("deno:" + Paths.Deno) +
                   " --ffmpeg-location " + Runner.Q(Paths.Bin);
        }

        static readonly string[][] Prints =
        {
            new[] { "T", "%(title)s" },
            new[] { "U", "%(channel,uploader,creator|)s" },
            new[] { "D", "%(duration_string|)s" },
            new[] { "I", "%(thumbnail|)s" },
            new[] { "V", "%(id|)s" },
            new[] { "K", "%(extractor_key|)s" },
            new[] { "W", "%(webpage_url|)s" },
            new[] { "H", "%(formats.:.height)s" },
            new[] { "X", "%(formats.:.width)s" },
            new[] { "P", "%(playlist_title|)s" },
            new[] { "C", "%(playlist_count|)s" },
            new[] { "L", "%(is_live|)s" }
        };

        public static string AnalyzeArgs(string url)
        {
            var sb = new StringBuilder(Common());
            sb.Append(" --no-warnings --no-playlist -I 1 --skip-download");
            foreach (var p in Prints) sb.Append(" --print ").Append(Runner.Q(p[0] + ":" + p[1]));
            sb.Append(" -- ").Append(Runner.Q(url));
            return sb.ToString();
        }

        public static VideoInfo ParseInfo(string url, List<string> lines)
        {
            var v = new VideoInfo();
            v.Url = url;
            v.HasListParam = Regex.IsMatch(url, "[?&]list=", RegexOptions.IgnoreCase);
            var seen = new HashSet<string>();
            string heights = null, widths = null;

            foreach (var line in lines)
            {
                if (line.Length < 2 || line[1] != ':') continue;
                string k = line.Substring(0, 1);
                if (!"TUDIVKWHXPCL".Contains(k) || seen.Contains(k)) continue;
                seen.Add(k);
                string val = line.Substring(2).Trim();
                if (val == "NA") val = "";
                switch (k)
                {
                    case "T": v.Title = val; break;
                    case "U": v.Channel = val; break;
                    case "D": v.Duration = val; break;
                    case "I": v.Thumb = val; break;
                    case "V": v.Id = val; break;
                    case "K": v.Extractor = val; break;
                    case "W": break; // качаем по исходной ссылке: в ней может быть плейлист
                    case "H": heights = val; break;
                    case "X": widths = val; break;
                    case "P": v.PlaylistTitle = val; break;
                    case "C": int.TryParse(val, out v.PlaylistCount); break;
                    case "L": v.IsLive = val.Equals("True", StringComparison.OrdinalIgnoreCase); break;
                }
            }
            if (v.Title == null) return null;

            var hs = ParseList(heights);
            var ws = ParseList(widths);
            v.HasFormatInfo = hs.Count > 0;
            for (int i = 0; i < hs.Count; i++)
            {
                int h = hs[i];
                if (h <= 0) continue;
                int w = (ws.Count == hs.Count) ? ws[i] : 0;
                v.Resolutions.Add(w > 0 ? Math.Min(w, h) : h); // вертикальные видео: качество по меньшей стороне
            }

            if (string.Equals(v.Extractor, "Youtube", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(v.Id))
                v.Thumb = "https://i.ytimg.com/vi/" + v.Id + "/hqdefault.jpg"; // jpg — WPF не всегда умеет webp
            return v;
        }

        static List<int> ParseList(string s)
        {
            var res = new List<int>();
            if (string.IsNullOrEmpty(s) || !s.StartsWith("[")) return res;
            foreach (var part in s.Trim('[', ']').Split(','))
            {
                int n;
                res.Add(int.TryParse(part.Trim(), out n) ? n : 0);
            }
            return res;
        }

        public static string DownloadArgs(DownloadItem it, Settings st)
        {
            var sb = new StringBuilder(Common());
            sb.Append(" --newline --progress --no-mtime --embed-metadata");
            sb.Append(" --progress-template ").Append(Runner.Q(
                "download:[dl]%(info.format_id)s|%(info.vcodec)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|" +
                "%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s|%(progress.fragment_index)s|%(progress.fragment_count)s"));
            sb.Append(" --progress-template ").Append(Runner.Q("postprocess:[pp]%(progress.postprocessor)s|%(progress.status)s"));
            sb.Append(" --print ").Append(Runner.Q("before_dl:[item]%(playlist_index|)s|%(n_entries,playlist_count|)s|%(title)s"));
            sb.Append(" --print ").Append(Runner.Q("after_move:[file]%(filepath)s"));
            sb.Append(" -P ").Append(Runner.Q(it.Folder));

            if (it.Playlist)
                sb.Append(" --yes-playlist -o ").Append(Runner.Q("%(playlist_title,playlist_id).80B/%(playlist_index)03d. %(title).120B.%(ext)s"));
            else
                sb.Append(" --no-playlist -o ").Append(Runner.Q("%(title).150B.%(ext)s"));

            int h = it.Quality.Height;
            if (h < 0)
            {
                sb.Append(" -f ba/b -x --audio-format mp3 --audio-quality 0");
                if (st.Cover) sb.Append(" --embed-thumbnail --convert-thumbnails jpg");
            }
            else
            {
                string sort = h > 0 ? "res:" + h : "res";
                if (st.Compat) sort += ",+codec:avc:m4a"; // H.264 + AAC: открывается любым плеером
                sb.Append(" -f ").Append(Runner.Q("bv*+ba/b"));
                sb.Append(" -S ").Append(Runner.Q(sort));
                sb.Append(" --merge-output-format mp4/mkv");
            }

            sb.Append(" -- ").Append(Runner.Q(it.Url));
            return sb.ToString();
        }

        // Понятное объяснение ошибки yt-dlp.
        public static void Explain(string raw, out string title, out string text)
        {
            string r = raw ?? "";
            string low = r.ToLowerInvariant();
            if (low.Contains("unsupported url")) { title = "Сайт не поддерживается"; text = "Проверь, что ссылка ведёт на страницу с видео."; }
            else if (low.Contains("is not a valid url")) { title = "Это не похоже на ссылку"; text = "Скопируй адрес страницы с видео целиком."; }
            else if (low.Contains("not a bot")) { title = "YouTube просит подтверждение"; text = "YouTube решил, что запросов слишком много. Подожди пару минут. Если повторяется, включи Zapret GUI."; }
            else if (low.Contains("private video")) { title = "Видео закрыто"; text = "Автор сделал видео приватным."; }
            else if (low.Contains("confirm your age") || low.Contains("age-restricted") || low.Contains("inappropriate")) { title = "Видео 18+"; text = "Такие видео YouTube отдаёт только после входа в аккаунт."; }
            else if (low.Contains("members-only") || low.Contains("join this channel")) { title = "Только для спонсоров"; text = "Видео доступно только платным подписчикам канала."; }
            else if (low.Contains("premieres in") || low.Contains("will begin in")) { title = "Видео ещё не вышло"; text = "Это премьера или запланированный эфир. Попробуй, когда он начнётся."; }
            else if (low.Contains("requested format is not available")) { title = "Нет такого качества"; text = "Выбери другое качество и попробуй снова."; }
            else if (low.Contains("video unavailable") || low.Contains("this video is unavailable") || low.Contains("has been removed")) { title = "Видео недоступно"; text = "Его удалили или закрыли для твоей страны."; }
            else if (low.Contains("timed out") || low.Contains("unable to download webpage") || low.Contains("getaddrinfo") ||
                     low.Contains("connection") || low.Contains("ssl") || low.Contains("network")) { title = "Нет связи с сайтом"; text = "Проверь интернет. Если это YouTube, включи Zapret GUI."; }
            else if (low.Contains("no video formats")) { title = "Видео не найдено"; text = "На странице нет видео, которое можно скачать."; }
            else if (low.Contains("no space left") || low.Contains("errno 28")) { title = "Закончилось место"; text = "Освободи место на диске или выбери другую папку."; }
            else if (low.Contains("permission denied") || low.Contains("errno 13")) { title = "Нет доступа к папке"; text = "Выбери другую папку для сохранения."; }
            else
            {
                title = "Не получилось";
                string t = Regex.Replace(r, "^ERROR:\\s*", "");
                t = Regex.Replace(t, "^\\[[^\\]]+\\]\\s*[^:]{0,40}:\\s*", "");
                text = t.Length > 220 ? t.Substring(0, 217) + "…" : (t.Length > 0 ? t : "Подробности в журнале на вкладке «Настройки».");
            }
        }
    }

    // ------------------------------------------------------------------ элемент очереди

    enum ItemState { Waiting, Running, Done, Failed, Cancelled }

    class DownloadItem
    {
        public string Url, Title, Thumb, Folder;
        public QualityOption Quality;
        public bool Playlist;
        public ItemState State = ItemState.Waiting;
        public Runner Run;

        public string CurFid, LastFile, LastError;
        public int Part, FilesDone, PlIndex, PlCount;

        // элементы строки
        public Border Row;
        public TextBlock TitleText, StatusText, ActionIcon;
        public ScaleTransform Bar;
        public Border BarFill;
        public Button Action;
    }

    // ------------------------------------------------------------------ окно

    class MainWindow : Window
    {
        static readonly Color CGreen = Hex("#2FB36E");
        static readonly Color CRed = Hex("#F0525F");
        static readonly Color CAmber = Hex("#F2B33D");
        static readonly Color CIdle = Hex("#4A505C");
        static readonly Color CSub = Hex("#8B92A0");
        static readonly Color CText = Hex("#E8EAEE");
        static readonly Color CCard = Hex("#1C1F26");
        static readonly CultureInfo Ru = new CultureInfo("ru-RU");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        const string UrlYtDlp = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        const string UrlFfmpeg = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        const string UrlDeno = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

        readonly Settings settings = new Settings();
        readonly FrameworkElement root;
        readonly List<DownloadItem> queue = new List<DownloadItem>();

        VideoInfo current;
        QualityOption selectedQuality;
        Runner analyzeRunner;
        int analyzeVersion, snackVersion, signatureClicks;
        bool setupRunning, queueRunning, suppress, closingConfirmed;
        string lastClipboard, lastAnalyzedUrl, retryUrl;
        bool retryIsSetup;
        TaskCompletionSource<bool> dlgTcs;
        DispatcherTimer zapretTimer, pasteTimer;

        // элементы
        FrameworkElement titleBar, pageMain, pageQueue, pageSettings, overlay;
        FrameworkElement stateEmpty, stateLoading, stateSetup, stateError, stateInfo;
        Button minBtn, closeBtn, pasteBtn, clearBtn, retryBtn, folderBtn, downloadBtn, clearDoneBtn,
               setFolderBtn, openFolderBtn, updateBtn, dlgYes, dlgNo;
        RadioButton navMain, navQueue, navSettings;
        TextBox urlBox, logBox;
        TextBlock urlPlaceholder, loadingSub, setupSub, errorTitle, errorText, durationText, playlistBadgeText,
                  infoTitle, infoChannel, swPlaylistHint, folderText, downloadBtnText, zapretText, queueSummary,
                  queueBadgeText, setFolderText, componentsText, dlgTitle, dlgText, snackText;
        Border urlField, thumb, durationBadge, playlistBadge, queueBadge, snack, setupBar;
        Shapes.Ellipse zapretDot;
        Shapes.Path loadSpinner;
        WrapPanel qualityPanel;
        StackPanel queueList, queueEmpty;
        CheckBox swPlaylist, swAutoPaste, swCompat, swCover;
        Run signature;

        public MainWindow()
        {
            settings.Load();

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VideoLoader.MainWindow.xaml"))
                root = (FrameworkElement)XamlReader.Load(s);

            Title = "Video Loader by mysik";
            Width = 440;
            Height = 740;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Content = root;

            FindElements();
            WireEvents();
        }

        T F<T>(string name) where T : class
        {
            var o = root.FindName(name) as T;
            if (o == null) throw new Exception("В разметке нет элемента " + name);
            return o;
        }

        void FindElements()
        {
            titleBar = F<FrameworkElement>("TitleBar");
            pageMain = F<FrameworkElement>("PageMain");
            pageQueue = F<FrameworkElement>("PageQueue");
            pageSettings = F<FrameworkElement>("PageSettings");
            overlay = F<FrameworkElement>("Overlay");
            stateEmpty = F<FrameworkElement>("StateEmpty");
            stateLoading = F<FrameworkElement>("StateLoading");
            stateSetup = F<FrameworkElement>("StateSetup");
            stateError = F<FrameworkElement>("StateError");
            stateInfo = F<FrameworkElement>("StateInfo");

            minBtn = F<Button>("MinBtn");
            closeBtn = F<Button>("CloseBtn");
            pasteBtn = F<Button>("PasteBtn");
            clearBtn = F<Button>("ClearBtn");
            retryBtn = F<Button>("RetryBtn");
            folderBtn = F<Button>("FolderBtn");
            downloadBtn = F<Button>("DownloadBtn");
            clearDoneBtn = F<Button>("ClearDoneBtn");
            setFolderBtn = F<Button>("SetFolderBtn");
            openFolderBtn = F<Button>("OpenFolderBtn");
            updateBtn = F<Button>("UpdateBtn");
            dlgYes = F<Button>("DlgYes");
            dlgNo = F<Button>("DlgNo");

            navMain = F<RadioButton>("NavMain");
            navQueue = F<RadioButton>("NavQueue");
            navSettings = F<RadioButton>("NavSettings");

            urlBox = F<TextBox>("UrlBox");
            logBox = F<TextBox>("LogBox");

            urlPlaceholder = F<TextBlock>("UrlPlaceholder");
            loadingSub = F<TextBlock>("LoadingSub");
            setupSub = F<TextBlock>("SetupSub");
            errorTitle = F<TextBlock>("ErrorTitle");
            errorText = F<TextBlock>("ErrorText");
            durationText = F<TextBlock>("DurationText");
            playlistBadgeText = F<TextBlock>("PlaylistBadgeText");
            infoTitle = F<TextBlock>("InfoTitle");
            infoChannel = F<TextBlock>("InfoChannel");
            swPlaylistHint = F<TextBlock>("SwPlaylistHint");
            folderText = F<TextBlock>("FolderText");
            downloadBtnText = F<TextBlock>("DownloadBtnText");
            zapretText = F<TextBlock>("ZapretText");
            queueSummary = F<TextBlock>("QueueSummary");
            queueBadgeText = F<TextBlock>("QueueBadgeText");
            setFolderText = F<TextBlock>("SetFolderText");
            componentsText = F<TextBlock>("ComponentsText");
            dlgTitle = F<TextBlock>("DlgTitle");
            dlgText = F<TextBlock>("DlgText");
            snackText = F<TextBlock>("SnackText");

            urlField = F<Border>("UrlField");
            thumb = F<Border>("Thumb");
            durationBadge = F<Border>("DurationBadge");
            playlistBadge = F<Border>("PlaylistBadge");
            queueBadge = F<Border>("QueueBadge");
            snack = F<Border>("Snack");
            setupBar = F<Border>("SetupBar");

            zapretDot = F<Shapes.Ellipse>("ZapretDot");
            loadSpinner = F<Shapes.Path>("LoadSpinner");
            qualityPanel = F<WrapPanel>("QualityPanel");
            queueList = F<StackPanel>("QueueList");
            queueEmpty = F<StackPanel>("QueueEmpty");

            swPlaylist = F<CheckBox>("SwPlaylist");
            swAutoPaste = F<CheckBox>("SwAutoPaste");
            swCompat = F<CheckBox>("SwCompat");
            swCover = F<CheckBox>("SwCover");
            signature = F<Run>("Signature");
        }

        void WireEvents()
        {
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
            minBtn.Click += (s, e) => WindowState = WindowState.Minimized;
            closeBtn.Click += (s, e) => Close();

            navMain.Checked += (s, e) => ShowPage(pageMain);
            navQueue.Checked += (s, e) => ShowPage(pageQueue);
            navSettings.Checked += (s, e) => ShowPage(pageSettings);

            urlBox.TextChanged += OnUrlChanged;
            urlBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Analyze(urlBox.Text.Trim(), true); } };
            urlBox.GotKeyboardFocus += (s, e) => urlField.BorderBrush = Br(CGreen);
            urlBox.LostKeyboardFocus += (s, e) => urlField.BorderBrush = Br(CCard);

            pasteBtn.Click += (s, e) =>
            {
                string t = ReadClipboard();
                if (string.IsNullOrEmpty(t)) { Toast("В буфере обмена нет текста"); return; }
                lastClipboard = t;
                urlBox.Text = t.Trim();
                Analyze(urlBox.Text, true);
            };
            clearBtn.Click += (s, e) => { urlBox.Text = ""; ResetMain(); urlBox.Focus(); };
            retryBtn.Click += async (s, e) =>
            {
                if (retryIsSetup) { if (await EnsureComponents(false) && retryUrl != null) Analyze(retryUrl, true); }
                else if (retryUrl != null) Analyze(retryUrl, true);
            };

            folderBtn.Click += (s, e) => ChooseFolder();
            setFolderBtn.Click += (s, e) => ChooseFolder();
            openFolderBtn.Click += (s, e) => OpenFolder(settings.Folder, null);
            downloadBtn.Click += (s, e) => AddCurrentToQueue();
            clearDoneBtn.Click += (s, e) => ClearDone();
            updateBtn.Click += async (s, e) => await UpdateYtDlp();

            swPlaylist.Checked += (s, e) => UpdateDownloadButton();
            swPlaylist.Unchecked += (s, e) => UpdateDownloadButton();
            swAutoPaste.Checked += (s, e) => { if (!suppress) { settings.AutoPaste = true; settings.Save(); } };
            swAutoPaste.Unchecked += (s, e) => { if (!suppress) { settings.AutoPaste = false; settings.Save(); } };
            swCompat.Checked += (s, e) => { if (!suppress) { settings.Compat = true; settings.Save(); } };
            swCompat.Unchecked += (s, e) => { if (!suppress) { settings.Compat = false; settings.Save(); } };
            swCover.Checked += (s, e) => { if (!suppress) { settings.Cover = true; settings.Save(); } };
            swCover.Unchecked += (s, e) => { if (!suppress) { settings.Cover = false; settings.Save(); } };

            dlgYes.Click += (s, e) => CloseDialog(true);
            dlgNo.Click += (s, e) => CloseDialog(false);
            overlay.MouseLeftButtonDown += (s, e) => { if (ReferenceEquals(e.OriginalSource, overlay)) CloseDialog(false); };
            KeyDown += (s, e) => { if (e.Key == Key.Escape && overlay.Visibility == Visibility.Visible) CloseDialog(false); };

            signature.MouseLeftButtonDown += (s, e) =>
            {
                signatureClicks++;
                Toast(signatureClicks % 5 == 0 ? "Ты нашёл пасхалку. Привет от mysik!" : "Video Loader by mysik");
            };

            Activated += (s, e) => { UpdateZapret(); TryAutoPaste(); };

            pasteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            pasteTimer.Tick += (s, e) =>
            {
                pasteTimer.Stop();
                string t = urlBox.Text.Trim();
                if (LooksLikeUrl(t) && t != lastAnalyzedUrl) Analyze(t, false);
            };
        }

        // -------------------------------------------------- старт

        public async void Init()
        {
            suppress = true;
            swAutoPaste.IsChecked = settings.AutoPaste;
            swCompat.IsChecked = settings.Compat;
            swCover.IsChecked = settings.Cover;
            suppress = false;
            UpdateFolderTexts();
            UpdateQueueUi();

            zapretTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            zapretTimer.Tick += (s, e) => UpdateZapret();
            zapretTimer.Start();
            UpdateZapret();

            Log("Video Loader by mysik. Компоненты: " + Paths.Bin);
            RefreshComponents();

            if (!ComponentsReady())
            {
                bool yes = await Ask("Нужно скачать компоненты",
                    "Для работы нужны yt-dlp, ffmpeg и deno — около 200 МБ. Они скачиваются с GitHub один раз и потом обновляются сами.",
                    "Скачать", "Позже");
                if (yes) await EnsureComponents(false);
            }
            TryAutoPaste();
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (!closingConfirmed && queue.Any(i => i.State == ItemState.Running || i.State == ItemState.Waiting))
            {
                e.Cancel = true;
                bool yes = await Ask("Загрузки ещё идут", "Если закрыть программу, незаконченные загрузки остановятся.", "Закрыть", "Остаться");
                if (!yes) return;
                closingConfirmed = true;
                foreach (var it in queue) if (it.Run != null) it.Run.Kill();
                Close();
                return;
            }
            if (analyzeRunner != null) analyzeRunner.Kill();
            base.OnClosing(e);
        }

        void ShowPage(FrameworkElement page)
        {
            foreach (var p in new[] { pageMain, pageQueue, pageSettings })
                p.Visibility = ReferenceEquals(p, page) ? Visibility.Visible : Visibility.Collapsed;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        void ShowState(FrameworkElement state)
        {
            foreach (var p in new[] { stateEmpty, stateLoading, stateSetup, stateError, stateInfo })
                p.Visibility = ReferenceEquals(p, state) ? Visibility.Visible : Visibility.Collapsed;

            var rot = loadSpinner.RenderTransform as RotateTransform;
            if (rot != null)
            {
                if (ReferenceEquals(state, stateLoading))
                    rot.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
                else rot.BeginAnimation(RotateTransform.AngleProperty, null);
            }
            state.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        void ResetMain()
        {
            analyzeVersion++;
            if (analyzeRunner != null) { analyzeRunner.Kill(); analyzeRunner = null; }
            current = null;
            lastAnalyzedUrl = null;
            ShowState(stateEmpty);
        }

        // -------------------------------------------------- компоненты

        bool ComponentsReady()
        {
            return File.Exists(Paths.YtDlp) && File.Exists(Paths.Ffmpeg) && File.Exists(Paths.Deno);
        }

        async void RefreshComponents()
        {
            if (!File.Exists(Paths.YtDlp)) { componentsText.Text = "Не установлены"; updateBtn.Content = "Скачать"; return; }
            updateBtn.Content = "Обновить";
            string ver = "";
            try
            {
                var r = new Runner();
                var lines = new List<string>();
                await r.Run(Paths.YtDlp, "--version", l => { lock (lines) lines.Add(l); });
                ver = lines.FirstOrDefault(l => Regex.IsMatch(l, "^\\d{4}\\.")) ?? "";
            }
            catch { }
            componentsText.Text = (ver.Length > 0 ? "Версия " + ver : "Установлен") +
                                  (File.Exists(Paths.Ffmpeg) ? " · ffmpeg" : " · нет ffmpeg") +
                                  (File.Exists(Paths.Deno) ? " · deno" : " · нет deno");
        }

        async Task<bool> EnsureComponents(bool quiet)
        {
            if (ComponentsReady()) return true;
            if (setupRunning) return false;
            setupRunning = true;
            navMain.IsChecked = true;
            ShowState(stateSetup);
            SetSetupProgress(0);
            string error = null;

            try
            {
                Directory.CreateDirectory(Paths.Bin);
                Directory.CreateDirectory(Paths.Tmp);

                if (!File.Exists(Paths.YtDlp))
                {
                    string tmp = Path.Combine(Paths.Tmp, "yt-dlp.exe.part");
                    error = await Fetch(UrlYtDlp, tmp, "yt-dlp");
                    if (error == null) MoveOver(tmp, Paths.YtDlp);
                }
                if (error == null && !File.Exists(Paths.Deno))
                {
                    string tmp = Path.Combine(Paths.Tmp, "deno.zip");
                    error = await Fetch(UrlDeno, tmp, "deno");
                    if (error == null)
                    {
                        setupSub.Text = "deno · распаковываю…";
                        error = await Task.Run(() => ExtractFiles(tmp, new[] { "deno.exe" }));
                    }
                }
                if (error == null && (!File.Exists(Paths.Ffmpeg) || !File.Exists(Paths.Ffprobe)))
                {
                    string tmp = Path.Combine(Paths.Tmp, "ffmpeg.zip");
                    error = await Fetch(UrlFfmpeg, tmp, "ffmpeg");
                    if (error == null)
                    {
                        setupSub.Text = "ffmpeg · распаковываю…";
                        error = await Task.Run(() => ExtractFiles(tmp, new[] { "ffmpeg.exe", "ffprobe.exe" }));
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; }

            setupRunning = false;
            try { Directory.Delete(Paths.Tmp, true); } catch { }
            RefreshComponents();

            if (error != null || !ComponentsReady())
            {
                Log("Установка компонентов: " + (error ?? "не все файлы на месте"));
                retryIsSetup = true;
                ShowError("Не удалось скачать компоненты",
                    "Проверь интернет и попробуй снова. Если файлы пропадают сразу после загрузки, добавь папку %LOCALAPPDATA%\\VideoLoader в исключения антивируса.");
                return false;
            }

            Log("Компоненты установлены.");
            if (!quiet) Toast("Всё готово — вставляй ссылку");
            ShowState(stateEmpty);
            return true;
        }

        async Task<string> Fetch(string url, string dest, string name)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "VideoLoader";
                    wc.DownloadProgressChanged += (s, e) =>
                    {
                        double total = e.TotalBytesToReceive;
                        string got = FmtBytes(e.BytesReceived);
                        setupSub.Text = total > 0
                            ? name + " · " + got + " из " + FmtBytes(total)
                            : name + " · " + got;
                        SetSetupProgress(total > 0 ? e.BytesReceived / total : 0);
                    };
                    setupSub.Text = name + " · подключаюсь…";
                    SetSetupProgress(0);
                    await wc.DownloadFileTaskAsync(new Uri(url), dest);
                }
                return null;
            }
            catch (Exception ex) { return name + ": " + ex.Message; }
        }

        static string ExtractFiles(string zip, string[] names)
        {
            try
            {
                using (var za = ZipFile.OpenRead(zip))
                {
                    foreach (string n in names)
                    {
                        var entry = za.Entries.FirstOrDefault(e =>
                            e.FullName.Replace('\\', '/').EndsWith("/" + n, StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.Equals(n, StringComparison.OrdinalIgnoreCase));
                        if (entry == null) return "в архиве нет " + n;
                        string tmp = Path.Combine(Paths.Bin, n + ".part");
                        entry.ExtractToFile(tmp, true);
                        MoveOver(tmp, Path.Combine(Paths.Bin, n));
                    }
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
            finally { try { File.Delete(zip); } catch { } }
        }

        static void MoveOver(string from, string to)
        {
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }

        void SetSetupProgress(double p)
        {
            var st = setupBar.RenderTransform as ScaleTransform;
            if (st != null) st.ScaleX = Math.Max(0, Math.Min(1, p));
        }

        async Task UpdateYtDlp()
        {
            if (!ComponentsReady()) { await EnsureComponents(false); return; }
            if (queue.Any(i => i.State == ItemState.Running)) { Toast("Дождись окончания загрузок"); return; }

            updateBtn.IsEnabled = false;
            updateBtn.Content = "Проверяю…";
            var lines = new List<string>();
            int code = -1;
            try
            {
                code = await new Runner().Run(Paths.YtDlp, "-U", l => { lock (lines) lines.Add(l); UI(() => Log("yt-dlp: " + l)); });
            }
            catch (Exception ex) { lines.Add(ex.Message); }
            updateBtn.IsEnabled = true;
            RefreshComponents();

            string all = string.Join("\n", lines);
            if (all.Contains("up to date")) Toast("Уже последняя версия yt-dlp");
            else if (all.Contains("Updated yt-dlp")) Toast("yt-dlp обновлён");
            else if (code == 0) Toast("Готово");
            else Toast("Обновить не получилось — подробности в журнале");
        }

        // -------------------------------------------------- ссылка и анализ

        static bool LooksLikeUrl(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 2000) return false;
            return Regex.IsMatch(s.Trim(), "^(https?://)?([\\p{L}\\p{N}-]+\\.)+\\p{L}{2,}(:\\d+)?(/\\S*)?$", RegexOptions.IgnoreCase) &&
                   s.Trim().IndexOf(' ') < 0 && s.Contains("/");
        }

        static string ReadClipboard()
        {
            for (int i = 0; i < 3; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch { Thread.Sleep(40); } // буфер может быть занят другой программой
            }
            return null;
        }

        void TryAutoPaste()
        {
            if (!settings.AutoPaste || setupRunning || overlay.Visibility == Visibility.Visible) return;
            string t = ReadClipboard();
            if (t == null) return;
            t = t.Trim();
            if (t == lastClipboard) return;
            lastClipboard = t;
            if (!LooksLikeUrl(t) || t == lastAnalyzedUrl) return;
            urlBox.Text = t;
            Analyze(t, false);
        }

        void OnUrlChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = urlBox.Text.Length == 0;
            urlPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            clearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            pasteTimer.Stop();
            if (!empty) pasteTimer.Start();
        }

        async void Analyze(string url, bool explicitRequest)
        {
            pasteTimer.Stop();
            url = (url ?? "").Trim();
            if (url.Length == 0) return;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            if (!LooksLikeUrl(url))
            {
                if (explicitRequest) { retryIsSetup = false; retryUrl = null; ShowError("Это не похоже на ссылку", "Скопируй адрес страницы с видео целиком, например https://youtu.be/…"); }
                return;
            }
            if (!explicitRequest && url == lastAnalyzedUrl) return;

            navMain.IsChecked = true;
            if (!ComponentsReady())
            {
                bool ok = await EnsureComponents(true);
                if (!ok) { retryUrl = url; return; }
            }

            int my = ++analyzeVersion;
            if (analyzeRunner != null) analyzeRunner.Kill();
            lastAnalyzedUrl = url;
            retryUrl = url;
            retryIsSetup = false;

            loadingSub.Text = "Обычно это занимает пару секунд";
            ShowState(stateLoading);
            Log("Ищу: " + url);

            var lines = new List<string>();
            string lastError = null;
            var r = new Runner();
            analyzeRunner = r;
            int code;
            try
            {
                var slowHint = Task.Delay(6000).ContinueWith(_t => UI(() =>
                {
                    if (my == analyzeVersion && stateLoading.Visibility == Visibility.Visible)
                        loadingSub.Text = "Сайт отвечает медленно. Если это YouTube, включи Zapret GUI";
                }));
                code = await r.Run(Paths.YtDlp, YtDlp.AnalyzeArgs(url), l =>
                {
                    lock (lines) lines.Add(l);
                    if (l.StartsWith("ERROR:")) lastError = l;
                });
            }
            catch (Exception ex) { code = -1; lastError = ex.Message; }

            if (my != analyzeVersion || r.Killed) return; // уже ищем что-то другое
            analyzeRunner = null;

            VideoInfo info;
            lock (lines) info = YtDlp.ParseInfo(url, lines);
            if (info == null)
            {
                string t, x;
                YtDlp.Explain(lastError, out t, out x);
                Log("Ошибка: " + (lastError ?? ("код " + code)));
                ShowError(t, x);
                return;
            }
            ShowInfo(info);
        }

        void ShowError(string title, string text)
        {
            errorTitle.Text = title;
            errorText.Text = text;
            ShowState(stateError);
        }

        void ShowInfo(VideoInfo info)
        {
            current = info;
            infoTitle.Text = info.IsPlaylistOnly ? info.PlaylistTitle : info.Title;
            infoChannel.Text = info.IsPlaylistOnly
                ? Plural(info.PlaylistCount, "видео", "видео", "видео") + (string.IsNullOrEmpty(info.Channel) ? "" : " · " + info.Channel)
                : (string.IsNullOrEmpty(info.Channel) ? (info.Extractor ?? "") : info.Channel);

            bool showDuration = !info.IsPlaylistOnly && (info.IsLive || !string.IsNullOrEmpty(info.Duration));
            durationBadge.Visibility = showDuration ? Visibility.Visible : Visibility.Collapsed;
            durationText.Text = info.IsLive ? "● Эфир" : info.Duration;

            playlistBadge.Visibility = info.IsPlaylistOnly ? Visibility.Visible : Visibility.Collapsed;
            playlistBadgeText.Text = "Плейлист · " + info.PlaylistCount;

            swPlaylist.Visibility = (info.HasListParam && !info.IsPlaylistOnly) ? Visibility.Visible : Visibility.Collapsed;
            swPlaylist.IsChecked = false;
            swPlaylistHint.Text = "Видео сохранятся в отдельную папку";

            thumb.Background = Br(CCard);
            SetThumb(thumb, info.Thumb);
            BuildQualityPills(info);
            UpdateFolderTexts();
            UpdateDownloadButton();
            ShowState(stateInfo);
        }

        void BuildQualityPills(VideoInfo info)
        {
            qualityPanel.Children.Clear();
            var opts = info.Options();
            var pillStyle = (Style)root.FindResource("Pill");

            QualityOption pick = opts.FirstOrDefault(o => o.Height == settings.Quality);
            if (pick == null) pick = opts.FirstOrDefault(o => o.Height > 0 && o.Height <= 1080);
            if (pick == null) pick = opts[0];

            foreach (var o in opts)
            {
                var opt = o;
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = o.Label, FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = o.Sub, FontSize = 10.5, Foreground = Br(CSub), HorizontalAlignment = HorizontalAlignment.Center });
                var rb = new RadioButton { Style = pillStyle, Content = sp };
                rb.IsChecked = ReferenceEquals(o, pick);
                rb.Checked += (s, e) => { selectedQuality = opt; UpdateDownloadButton(); };
                qualityPanel.Children.Add(rb);
            }
            selectedQuality = pick;
        }

        void UpdateDownloadButton()
        {
            if (current == null || selectedQuality == null) return;
            bool pl = current.IsPlaylistOnly || swPlaylist.IsChecked == true;
            string q = selectedQuality.Height < 0 ? "MP3" : (selectedQuality.Height > 0 ? selectedQuality.Label : "видео");
            downloadBtnText.Text = pl ? "Скачать плейлист · " + q : "Скачать " + q;
        }

        void SetThumb(Border target, string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(url);
                bi.DecodePixelWidth = 480;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                Action apply = () => target.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                if (bi.IsDownloading) bi.DownloadCompleted += (s, e) => apply();
                else apply();
            }
            catch { }
        }

        void ChooseFolder()
        {
            using (var d = new WF.FolderBrowserDialog())
            {
                d.Description = "Куда сохранять видео";
                d.ShowNewFolderButton = true;
                try { Directory.CreateDirectory(settings.Folder); d.SelectedPath = settings.Folder; } catch { }
                if (d.ShowDialog() == WF.DialogResult.OK && !string.IsNullOrEmpty(d.SelectedPath))
                {
                    settings.Folder = d.SelectedPath;
                    settings.Save();
                    UpdateFolderTexts();
                    Toast("Видео будут сохраняться в " + Path.GetFileName(settings.Folder.TrimEnd('\\')));
                }
            }
        }

        void UpdateFolderTexts()
        {
            folderText.Text = ShortPath(settings.Folder);
            setFolderText.Text = settings.Folder;
        }

        static string ShortPath(string p)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && p.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                return "~" + p.Substring(home.Length);
            return p;
        }

        void OpenFolder(string folder, string file)
        {
            try
            {
                if (file != null && File.Exists(file)) { Process.Start("explorer.exe", "/select,\"" + file + "\""); return; }
                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex) { Log("Не удалось открыть папку: " + ex.Message); }
        }

        // -------------------------------------------------- очередь

        void AddCurrentToQueue()
        {
            if (current == null || selectedQuality == null) return;
            try { Directory.CreateDirectory(settings.Folder); }
            catch { Toast("Не получается создать папку — выбери другую"); return; }

            var it = new DownloadItem();
            it.Url = current.Url;
            it.Playlist = current.IsPlaylistOnly || swPlaylist.IsChecked == true;
            it.Title = current.IsPlaylistOnly ? current.PlaylistTitle : current.Title;
            it.Thumb = current.Thumb;
            it.Folder = settings.Folder;
            it.Quality = selectedQuality;
            if (selectedQuality.Height != 0) { settings.Quality = selectedQuality.Height; settings.Save(); }

            BuildRow(it);
            queue.Add(it);
            queueList.Children.Insert(0, it.Row);
            UpdateQueueUi();
            Log("В очередь: " + it.Title + " (" + selectedQuality.Label + ")");

            Toast("Добавлено в загрузки");
            urlBox.Text = "";
            ResetMain();
            ProcessQueue();
        }

        void BuildRow(DownloadItem it)
        {
            var icons = (FontFamily)root.FindResource("Icons");
            var iconBtn = (Style)root.FindResource("IconBtn");

            var row = new Border { Background = Br(CCard), CornerRadius = new CornerRadius(14), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var th = new Border { Width = 96, Height = 54, CornerRadius = new CornerRadius(8), Background = Br(Hex("#262A33")), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            if (it.Quality.Height < 0 && string.IsNullOrEmpty(it.Thumb))
                th.Child = new TextBlock { Text = "", FontFamily = icons, FontSize = 18, Foreground = Br(CSub), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            SetThumb(th, it.Thumb);
            g.Children.Add(th);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(mid, 1);
            it.TitleText = new TextBlock { Text = it.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            it.StatusText = new TextBlock { Text = "В очереди · " + it.Quality.Label, Foreground = Br(CSub), FontSize = 11.5, Margin = new Thickness(0, 3, 0, 7), TextTrimming = TextTrimming.CharacterEllipsis };
            var track = new Grid { Height = 4 };
            track.Children.Add(new Border { Background = Br(Hex("#2A2E37")), CornerRadius = new CornerRadius(2) });
            it.Bar = new ScaleTransform(0, 1);
            it.BarFill = new Border { Background = Br(CGreen), CornerRadius = new CornerRadius(2), RenderTransform = it.Bar, RenderTransformOrigin = new Point(0, 0.5) };
            track.Children.Add(it.BarFill);
            mid.Children.Add(it.TitleText);
            mid.Children.Add(it.StatusText);
            mid.Children.Add(track);
            g.Children.Add(mid);

            it.ActionIcon = new TextBlock { Text = "", FontFamily = icons, FontSize = 12 };
            it.Action = new Button { Style = iconBtn, Content = it.ActionIcon, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Отменить" };
            Grid.SetColumn(it.Action, 2);
            it.Action.Click += (s, e) => OnItemAction(it);
            g.Children.Add(it.Action);

            row.Child = g;
            it.Row = row;
        }

        void OnItemAction(DownloadItem it)
        {
            switch (it.State)
            {
                case ItemState.Waiting:
                    it.State = ItemState.Cancelled;
                    SetItemFinal(it, "Отменено", CSub, 0);
                    break;
                case ItemState.Running:
                    if (it.Run != null) it.Run.Kill();
                    break;
                case ItemState.Done:
                    OpenFolder(it.Playlist && it.LastFile != null ? Path.GetDirectoryName(it.LastFile) : it.Folder, it.Playlist ? null : it.LastFile);
                    break;
                case ItemState.Failed:
                case ItemState.Cancelled:
                    ResetItem(it);
                    it.State = ItemState.Waiting;
                    ProcessQueue();
                    break;
            }
            UpdateQueueUi();
        }

        void ResetItem(DownloadItem it)
        {
            it.CurFid = null; it.LastFile = null; it.LastError = null;
            it.Part = 0; it.FilesDone = 0; it.PlIndex = 0; it.PlCount = 0;
            it.StatusText.Text = "В очереди · " + it.Quality.Label;
            it.StatusText.Foreground = Br(CSub);
            it.BarFill.Background = Br(CGreen);
            it.Bar.ScaleX = 0;
            it.ActionIcon.Text = "";
            it.Action.ToolTip = "Отменить";
        }

        void SetItemFinal(DownloadItem it, string status, Color color, double bar)
        {
            it.StatusText.Text = status;
            it.StatusText.Foreground = Br(color);
            it.Bar.ScaleX = bar;
            it.BarFill.Background = Br(color == CSub ? CIdle : color);
            if (it.State == ItemState.Done) { it.ActionIcon.Text = ""; it.Action.ToolTip = "Показать в папке"; }
            else { it.ActionIcon.Text = ""; it.Action.ToolTip = "Скачать заново"; }
        }

        async void ProcessQueue()
        {
            if (queueRunning) return;
            queueRunning = true;
            try
            {
                while (true)
                {
                    var it = queue.FirstOrDefault(i => i.State == ItemState.Waiting);
                    if (it == null) break;
                    await RunItem(it);
                    UpdateQueueUi();
                }
            }
            finally { queueRunning = false; }
        }

        async Task RunItem(DownloadItem it)
        {
            if (!ComponentsReady() && !await EnsureComponents(true))
            {
                it.State = ItemState.Failed;
                SetItemFinal(it, "Нет компонентов для загрузки", CRed, 1);
                return;
            }

            it.State = ItemState.Running;
            it.StatusText.Text = "Подключаюсь…";
            it.StatusText.Foreground = Br(CSub);
            it.ActionIcon.Text = "";
            it.Action.ToolTip = "Отменить";
            UpdateQueueUi();

            string args = YtDlp.DownloadArgs(it, settings);
            Log("Скачиваю: " + it.Title);
            it.Run = new Runner();
            int code;
            try
            {
                code = await it.Run.Run(Paths.YtDlp, args, line => UI(() => OnItemLine(it, line)));
            }
            catch (Exception ex) { code = -1; it.LastError = ex.Message; }

            // дать UI дочитать последние строки
            await Task.Delay(150);

            if (it.Run.Killed)
            {
                it.State = ItemState.Cancelled;
                SetItemFinal(it, "Отменено", CSub, 0);
                Log("Отменено: " + it.Title);
                return;
            }

            if (code == 0 || (it.Playlist && it.FilesDone > 0))
            {
                it.State = ItemState.Done;
                string what;
                if (it.Playlist)
                    what = code == 0 ? "Готово · " + Plural(it.FilesDone, "файл", "файла", "файлов")
                                     : "Готово частично · " + Plural(it.FilesDone, "файл", "файла", "файлов");
                else
                {
                    long size = 0;
                    try { if (it.LastFile != null && File.Exists(it.LastFile)) size = new FileInfo(it.LastFile).Length; } catch { }
                    what = "Готово" + (size > 0 ? " · " + FmtBytes(size) : "") +
                           (it.LastFile != null ? " · " + Path.GetExtension(it.LastFile).TrimStart('.').ToUpperInvariant() : "");
                }
                SetItemFinal(it, what, CGreen, 1);
                Log("Готово: " + (it.LastFile ?? it.Title));
                ToastIfHidden("Скачано: " + it.Title);
            }
            else
            {
                it.State = ItemState.Failed;
                string t, x;
                YtDlp.Explain(it.LastError, out t, out x);
                SetItemFinal(it, t + ". " + x, CRed, 1);
                Log("Ошибка: " + (it.LastError ?? ("код " + code)));
            }
        }

        void OnItemLine(DownloadItem it, string line)
        {
            if (line.StartsWith("[dl]"))
            {
                var f = line.Substring(4).Split('|');
                if (f.Length < 9) return;
                if (f[0] != it.CurFid) { it.CurFid = f[0]; it.Part++; }
                bool audioPart = f[1] == "none";
                double dl = Num(f[2]), total = Num(f[3]), est = Num(f[4]), speed = Num(f[5]), eta = Num(f[6]), fi = Num(f[7]), fc = Num(f[8]);
                if (total <= 0) total = est;
                double p = total > 0 && dl >= 0 ? dl / total : (fc > 0 && fi >= 0 ? fi / fc : -1);

                var parts = new List<string>();
                if (it.Playlist && it.PlCount > 0) parts.Add(it.PlIndex + " из " + it.PlCount);
                if (it.Quality.Height >= 0) parts.Add(audioPart ? "Звук" : "Видео");
                if (p >= 0) parts.Add(((int)Math.Floor(p * 100)) + "%");
                if (total > 0) parts.Add(FmtBytes(total));
                if (speed > 0) parts.Add(FmtBytes(speed) + "/с");
                if (eta > 0) parts.Add("осталось " + FmtEta(eta));
                it.StatusText.Text = string.Join(" · ", parts);
                it.StatusText.Foreground = Br(CSub);
                if (p >= 0) it.Bar.ScaleX = Math.Min(1, p);
                it.BarFill.Background = Br(CGreen);
            }
            else if (line.StartsWith("[pp]"))
            {
                var f = line.Substring(4).Split('|');
                string name = f[0];
                if (f.Length > 1 && f[1] != "started") return;
                string text;
                if (name.StartsWith("Merger")) text = "Склеиваю видео и звук…";
                else if (name.Contains("ExtractAudio")) text = "Делаю MP3…";
                else if (name.Contains("EmbedThumbnail") || name.Contains("ThumbnailsConvertor")) text = "Добавляю обложку…";
                else if (name.Contains("Metadata")) text = "Записываю название и автора…";
                else if (name.Contains("MoveFiles")) text = "Сохраняю…";
                else text = "Обработка…";
                if (it.Playlist && it.PlCount > 0) text = it.PlIndex + " из " + it.PlCount + " · " + text;
                it.StatusText.Text = text;
                it.Bar.ScaleX = 1;
                it.BarFill.Background = Br(CAmber);
            }
            else if (line.StartsWith("[item]"))
            {
                var f = line.Substring(6).Split(new[] { '|' }, 3);
                int a, b;
                if (f.Length >= 2 && int.TryParse(f[0], out a)) it.PlIndex = a;
                if (f.Length >= 2 && int.TryParse(f[1], out b)) it.PlCount = b;
                it.CurFid = null;
                it.Part = 0;
                if (it.Playlist && f.Length == 3) it.TitleText.ToolTip = f[2];
            }
            else if (line.StartsWith("[file]"))
            {
                it.LastFile = line.Substring(6).Trim();
                it.FilesDone++;
            }
            else if (line.StartsWith("ERROR:"))
            {
                it.LastError = line;
                Log(line);
            }
            else if (line.StartsWith("WARNING:"))
            {
                Log(line);
            }
        }

        void ClearDone()
        {
            foreach (var it in queue.Where(i => i.State == ItemState.Done || i.State == ItemState.Failed || i.State == ItemState.Cancelled).ToList())
            {
                queue.Remove(it);
                queueList.Children.Remove(it.Row);
            }
            UpdateQueueUi();
        }

        void UpdateQueueUi()
        {
            int active = queue.Count(i => i.State == ItemState.Running || i.State == ItemState.Waiting);
            int done = queue.Count(i => i.State == ItemState.Done);
            int finished = queue.Count(i => i.State == ItemState.Done || i.State == ItemState.Failed || i.State == ItemState.Cancelled);

            queueBadge.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
            queueBadgeText.Text = active.ToString();
            queueEmpty.Visibility = queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            clearDoneBtn.Visibility = finished > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (queue.Count == 0) queueSummary.Text = "Пока ничего не скачано";
            else if (active > 0) queueSummary.Text = "Скачивается " + active + (done > 0 ? " · готово " + done : "");
            else queueSummary.Text = "Готово " + done + " из " + queue.Count;
        }

        // -------------------------------------------------- Zapret

        void UpdateZapret()
        {
            bool on = false;
            try { on = Process.GetProcessesByName("winws").Length > 0; } catch { }
            zapretDot.Fill = Br(on ? CGreen : CIdle);
            zapretText.Text = on
                ? "Zapret включён — YouTube качается на полной скорости"
                : "Если YouTube качается медленно, включи Zapret GUI";
        }

        // -------------------------------------------------- диалоги, уведомления, журнал

        Task<bool> Ask(string title, string text, string yes, string no)
        {
            if (dlgTcs != null) { var old = dlgTcs; dlgTcs = null; old.TrySetResult(false); }
            dlgTitle.Text = title;
            dlgText.Text = text ?? "";
            dlgYes.Content = yes;
            dlgNo.Visibility = no != null ? Visibility.Visible : Visibility.Collapsed;
            if (no != null) dlgNo.Content = no;
            Grid.SetColumn(dlgYes, no == null ? 0 : 2);
            Grid.SetColumnSpan(dlgYes, no == null ? 3 : 1);

            overlay.Visibility = Visibility.Visible;
            overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            dlgTcs = new TaskCompletionSource<bool>();
            return dlgTcs.Task;
        }

        void CloseDialog(bool result)
        {
            overlay.Visibility = Visibility.Collapsed;
            var t = dlgTcs;
            dlgTcs = null;
            if (t != null) t.TrySetResult(result);
        }

        async void Toast(string msg)
        {
            int my = ++snackVersion;
            snackText.Text = msg;
            snack.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            await Task.Delay(3000);
            if (my == snackVersion)
                snack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        }

        void ToastIfHidden(string msg)
        {
            if (!navQueue.IsChecked.GetValueOrDefault()) Toast(msg);
        }

        void UI(Action a)
        {
            try { Dispatcher.BeginInvoke(a); } catch { }
        }

        void Log(string msg)
        {
            if (logBox.Text.Length > 60000) logBox.Text = logBox.Text.Substring(30000);
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
            logBox.ScrollToEnd();
        }

        // -------------------------------------------------- форматирование

        static double Num(string s)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, Inv, out d) ? d : -1;
        }

        static string FmtBytes(double b)
        {
            if (b < 1024) return ((int)b) + " Б";
            if (b < 1024 * 1024) return (b / 1024).ToString("0", Ru) + " КБ";
            if (b < 1024.0 * 1024 * 1024) return (b / 1024 / 1024).ToString(b < 100 * 1024 * 1024 ? "0.0" : "0", Ru) + " МБ";
            return (b / 1024 / 1024 / 1024).ToString("0.00", Ru) + " ГБ";
        }

        static string FmtEta(double sec)
        {
            var t = TimeSpan.FromSeconds(Math.Round(sec));
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                                     : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        static string Plural(int n, string one, string few, string many)
        {
            int a = Math.Abs(n) % 100, b = a % 10;
            string w = (a > 10 && a < 20) ? many : b == 1 ? one : (b >= 2 && b <= 4) ? few : many;
            return n + " " + w;
        }

        static Color Hex(string s) { return (Color)ColorConverter.ConvertFromString(s); }

        static SolidColorBrush Br(Color c) { return new SolidColorBrush(c); }
    }
}
