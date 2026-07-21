using System;
using System.IO;
using System.Windows.Forms;

namespace VanishFF
{
    static class Program
    {
        public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string FFmpeg = Path.Combine(AppDir, "ffmpeg", "ffmpeg.exe");
        public static readonly string FFprobe = Path.Combine(AppDir, "ffmpeg", "ffprobe.exe");
        public static readonly string FFplay = Path.Combine(AppDir, "ffmpeg", "ffplay.exe");

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Settings.Load();
            Theme.Current = Settings.GetB("theme_dark", true) ? Theme.Dark : Theme.Light;

            // Проверка при старте: без ffmpeg предлагаем скачать/взять вручную
            // (или выйти). Скрипты для этого больше не нужны.
            if (!FFmpegSetup.Ensure()) return;

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(AppDir, "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                        ex + "\r\n\r\n");
                }
                catch { }
                throw;
            }
        }
    }
}
