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

            // Проверка при старте (ТЗ 2.5): без ffmpeg программа бессмысленна
            if (!File.Exists(FFmpeg) || !File.Exists(FFprobe))
            {
                MessageBox.Show(L.ErrNoFFmpeg, L.AppTitle,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Settings.Load();
            Theme.Current = Settings.GetB("theme_dark", true) ? Theme.Dark : Theme.Light;

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
