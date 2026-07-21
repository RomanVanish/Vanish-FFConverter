using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace VanishFF
{
    // Проверка/установка ffmpeg при старте: если его нет — предложить
    // скачать, открыть страницу загрузки (BtbN / gyan.dev) или выйти.
    // Всё на штатных сборках .NET — без сторонних зависимостей.
    static class FFmpegSetup
    {
        const string DownloadUrl =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/" +
            "ffmpeg-master-latest-win64-gpl.zip";
        const string SiteBtbn = "https://github.com/BtbN/FFmpeg-Builds/releases";
        const string SiteGyan = "https://www.gyan.dev/ffmpeg/builds/";

        static string FFDir { get { return Path.Combine(Program.AppDir, "ffmpeg"); } }

        static bool Present()
        {
            return File.Exists(Program.FFmpeg) && File.Exists(Program.FFprobe);
        }

        // true — ffmpeg на месте (можно запускаться), false — выходим.
        public static bool Ensure()
        {
            if (Present()) return true;
            while (true)
            {
                switch (AskDialog())
                {
                    case Choice.Download:
                        using (var d = new DownloadForm(DownloadUrl, FFDir))
                        {
                            d.ShowDialog();
                            if (d.Ok && Present()) return true;
                            // не удалось (в т.ч. битая ссылка) — ручная страница
                            ManualDialog(d.Err);
                        }
                        if (Present()) return true;
                        break;

                    case Choice.Manual:
                        ManualDialog(null);
                        if (Present()) return true;
                        break;

                    default:
                        return false;   // выход
                }
            }
        }

        enum Choice { Download, Manual, Exit }

        static Choice AskDialog()
        {
            using (var f = NewForm("Vanish-FFConverter — нужен ffmpeg", 470, 210))
            {
                var lbl = new Label();
                lbl.Text = "Для работы программе нужен ffmpeg — его нет рядом " +
                    "с программой.\n\nМожно скачать автоматически (~180 МБ, один " +
                    "раз) или взять вручную с сайта. ffmpeg ляжет в папку " +
                    "«ffmpeg» рядом с программой.";
                lbl.SetBounds(18, 16, 434, 80);
                f.Controls.Add(lbl);

                var bDl = MkBtn(f, "Скачать (~180 МБ)", 18, 120, 180, true);
                var bSite = MkBtn(f, "Открыть страницу загрузки", 206, 120, 180, false);
                var bExit = MkBtn(f, "Выход", 18, 160, 120, false);

                Choice result = Choice.Exit;
                bDl.Click += delegate { result = Choice.Download; f.Close(); };
                bSite.Click += delegate { result = Choice.Manual; f.Close(); };
                bExit.Click += delegate { result = Choice.Exit; f.Close(); };
                f.AcceptButton = bDl;
                f.CancelButton = bExit;

                Theme.Apply(f);
                f.Shown += delegate { Theme.ApplyTitleBar(f); };
                f.ShowDialog();
                return result;
            }
        }

        static void ManualDialog(string errorMsg)
        {
            using (var f = NewForm("Скачать ffmpeg вручную", 520, 300))
            {
                var lbl = new Label();
                string head = errorMsg != null
                    ? "Автоматически скачать не получилось (" + errorMsg + ").\n\n"
                    : "";
                lbl.Text = head +
                    "Скачайте сборку ffmpeg для Windows 64-bit с одного из " +
                    "сайтов ниже (подойдёт любая):\n" +
                    "  • BtbN — на GitHub, файл вида ...win64-gpl.zip\n" +
                    "  • gyan.dev — «release» build\n\n" +
                    "В архиве в папке bin лежат ffmpeg.exe, ffprobe.exe, " +
                    "ffplay.exe — положите эти три файла в папку «ffmpeg» " +
                    "рядом с программой и запустите Vanish-FFConverter снова.";
                lbl.SetBounds(18, 14, 484, 150);
                f.Controls.Add(lbl);

                var bBtbn = MkBtn(f, "Сайт BtbN", 18, 176, 150, false);
                var bGyan = MkBtn(f, "Сайт gyan.dev", 176, 176, 150, false);
                var bFolder = MkBtn(f, "Открыть папку «ffmpeg»", 334, 176, 168, false);
                var bClose = MkBtn(f, "Закрыть", 18, 220, 120, true);

                bBtbn.Click += delegate { OpenUrl(SiteBtbn); };
                bGyan.Click += delegate { OpenUrl(SiteGyan); };
                bFolder.Click += delegate
                {
                    try { Directory.CreateDirectory(FFDir); Process.Start(FFDir); }
                    catch { }
                };
                bClose.Click += delegate { f.Close(); };
                f.AcceptButton = bClose;
                f.CancelButton = bClose;

                Theme.Apply(f);
                f.Shown += delegate { Theme.ApplyTitleBar(f); };
                f.ShowDialog();
            }
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch { }
        }

        // ── помощники ────────────────────────────────────────────────

        static Form NewForm(string title, int w, int h)
        {
            var f = new Form();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = true;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.ClientSize = new Size(w, h);
            f.Font = new Font("Segoe UI", 10f);
            try { f.Icon = new Icon(Path.Combine(Program.AppDir, "V.ico")); }
            catch { }
            return f;
        }

        static Button MkBtn(Form f, string text, int x, int y, int w, bool accent)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            if (accent) b.Tag = "accent";
            f.Controls.Add(b);
            return b;
        }

        // Окно загрузки: скачивает и распаковывает ffmpeg с прогрессом.
        class DownloadForm : Form
        {
            public bool Ok;
            public string Err;

            WebClient wc;
            readonly ThinProgress bar;
            readonly Label lbl;
            readonly Button cancel;
            readonly string url, ffDir, zip, ex;

            public DownloadForm(string url, string ffDir)
            {
                this.url = url;
                this.ffDir = ffDir;
                zip = Path.Combine(ffDir, "_ffmpeg.zip");
                ex = Path.Combine(ffDir, "_extract");

                Text = "Загрузка ffmpeg";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false; MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(460, 130);
                Font = new Font("Segoe UI", 10f);
                try { Icon = new Icon(Path.Combine(Program.AppDir, "V.ico")); }
                catch { }

                lbl = new Label();
                lbl.Text = "Скачиваю ffmpeg (~180 МБ)...";
                lbl.SetBounds(16, 16, 428, 24);
                Controls.Add(lbl);

                bar = new ThinProgress();
                bar.ShowText = true;
                bar.SetBounds(16, 48, 428, 24);
                Controls.Add(bar);

                cancel = new Button();
                cancel.Text = "Отмена";
                cancel.SetBounds(344, 86, 100, 30);
                cancel.Click += delegate
                {
                    try { if (wc != null) wc.CancelAsync(); } catch { }
                };
                Controls.Add(cancel);

                Theme.Apply(this);
                Shown += delegate { Theme.ApplyTitleBar(this); Begin(); };
            }

            void Begin()
            {
                try
                {
                    Directory.CreateDirectory(ffDir);
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls12;
                    wc = new WebClient();
                    wc.DownloadProgressChanged += OnProgress;
                    wc.DownloadFileCompleted += OnDone;
                    wc.DownloadFileAsync(new Uri(url), zip);
                }
                catch (Exception e)
                {
                    Ok = false; Err = e.Message; Close();
                }
            }

            void OnProgress(object s, DownloadProgressChangedEventArgs e)
            {
                bar.Value = e.ProgressPercentage;
                if (e.TotalBytesToReceive > 0)
                    lbl.Text = string.Format("Скачиваю ffmpeg: {0} / {1} МБ",
                        e.BytesReceived / 1048576, e.TotalBytesToReceive / 1048576);
            }

            void OnDone(object s, AsyncCompletedEventArgs e)
            {
                if (e.Cancelled) { Ok = false; Err = "отменено"; Close(); return; }
                if (e.Error != null)
                {
                    Ok = false; Err = e.Error.Message; Close(); return;
                }
                // распаковка — в фоне, чтобы окно не «висло»
                lbl.Text = "Распаковываю...";
                bar.Value = 100;
                cancel.Enabled = false;
                var t = new Thread(delegate ()
                {
                    try { Extract(); Ok = true; }
                    catch (Exception ex2) { Ok = false; Err = ex2.Message; }
                    try { BeginInvoke((Action)Close); } catch { }
                });
                t.IsBackground = true;
                t.Start();
            }

            void Extract()
            {
                if (Directory.Exists(ex)) Directory.Delete(ex, true);
                ZipFile.ExtractToDirectory(zip, ex);
                var found = Directory.GetFiles(ex, "ffmpeg.exe",
                    SearchOption.AllDirectories);
                if (found.Length == 0)
                    throw new Exception("в архиве не найден ffmpeg.exe");
                string binDir = Path.GetDirectoryName(found[0]);
                foreach (string src in Directory.GetFiles(binDir, "*.exe"))
                    File.Copy(src, Path.Combine(ffDir, Path.GetFileName(src)), true);
                try { File.Delete(zip); } catch { }
                try { Directory.Delete(ex, true); } catch { }
            }
        }
    }
}
