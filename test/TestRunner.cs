using System;
using System.Collections.Generic;
using System.IO;

namespace VanishFF
{
    // Консольный прогон конвейера AudioJob без GUI.
    // Сборка: build_test.bat; запуск: test.exe <папка с медиа> <папка выхода>
    static class TestRunner
    {
        static int failed;

        static void Main(string[] args)
        {
            string mediaDir = args[0];
            string outDir = args[1];
            Directory.CreateDirectory(outDir);

            string mp3 = Path.Combine(mediaDir, "тестовая запись 1.mp3");
            string mka = Path.Combine(mediaDir, "мультитрек OBS.mka");
            string mp4 = Path.Combine(mediaDir, "видео с звуком.mp4");

            // 1. mp3 -> opus, моно, нормализация
            var s1 = Base(outDir);
            s1.Format = "opus"; s1.BitrateKbps = 64; s1.Channels = "mono";
            Run("mp3->opus моно+норм", mp3, s1);
            Check("1a: opus существует", File.Exists(P(outDir, "тестовая запись 1.opus")));
            var p1 = FF.Probe(P(outDir, "тестовая запись 1.opus"));
            Check("1b: длительность ~10с", p1 != null && Math.Abs(p1.Duration - 10) < 0.7);
            Check("1c: моно", p1 != null && p1.Audio[0].Channels == 1);

            // 2. мультитрек -> моно-сведение трёх дорожек
            var s2 = Base(outDir);
            s2.Format = "opus"; s2.Channels = "mono";
            s2.Tracks = new List<int> { 0, 1, 2 };
            s2.NameTemplate = "{имя} (сведение)";
            Run("mka 3 дорожки -> моно", mka, s2);
            var p2 = FF.Probe(P(outDir, "мультитрек OBS (сведение).opus"));
            Check("2a: сведение готово", p2 != null);
            Check("2b: моно", p2 != null && p2.Audio[0].Channels == 1);

            // 3. стерео «по ушам»
            var s3 = Base(outDir);
            s3.Format = "opus"; s3.Channels = "stereo";
            s3.LeftTrack = 0; s3.RightTrack = 1;
            s3.NameTemplate = "{имя} (по ушам)";
            Run("mka -> стерео по ушам", mka, s3);
            var p3 = FF.Probe(P(outDir, "мультитрек OBS (по ушам).opus"));
            Check("3a: стерео готово", p3 != null);
            Check("3b: 2 канала", p3 != null && p3.Audio[0].Channels == 2);

            // 4. «как в исходнике», две отмеченные -> два отдельных файла
            var s4 = Base(outDir);
            s4.Format = "mp3"; s4.Channels = "original";
            s4.Tracks = new List<int> { 0, 2 };
            s4.Normalize = false;
            Run("mka дорожки 1,3 -> отдельные mp3", mka, s4);
            Check("4a: дорожка 1", File.Exists(P(outDir, "мультитрек OBS (дорожка 1).mp3")));
            Check("4b: дорожка 3", File.Exists(P(outDir, "мультитрек OBS (дорожка 3).mp3")));

            // 5. видео -> звук в wav 16 бит
            var s5 = Base(outDir);
            s5.Format = "wav"; s5.WavBits = 16; s5.Normalize = false;
            Run("mp4 -> wav", mp4, s5);
            var p5 = FF.Probe(P(outDir, "видео с звуком.wav"));
            Check("5a: wav из видео", p5 != null && p5.Audio.Count == 1);
            Check("5b: pcm_s16le", p5 != null && p5.Audio[0].Codec == "pcm_s16le");

            // 6. flac + теги имени
            var s6 = Base(outDir);
            s6.Format = "flac"; s6.Normalize = false;
            s6.NameTemplate = "{имя} [{##}]";
            Run("mp3 -> flac с тегами", mp3, s6);
            Check("6a: flac с номером", File.Exists(P(outDir, "тестовая запись 1 [01].flac")));

            // 7. защита от перезаписи: повтор без overwrite -> Skipped
            bool skipped = false;
            try { AudioJob.Run(Entry(mp3, s1), Ctx(outDir)); }
            catch (JobSkippedException) { skipped = true; }
            Check("7: повтор без overwrite пропущен", skipped);

            Console.WriteLine(failed == 0
                ? "\nВСЕ ТЕСТЫ ПРОЙДЕНЫ"
                : "\nПРОВАЛОВ: " + failed);
            Environment.Exit(failed == 0 ? 0 : 1);
        }

        static string P(string dir, string name)
        {
            return Path.Combine(dir, name);
        }

        static AudioSettings Base(string outDir)
        {
            var s = new AudioSettings();
            s.OutputDir = outDir;
            s.Tracks = new List<int> { 0 };
            s.NameTemplate = "{имя}";
            return s;
        }

        static FileEntry Entry(string path, AudioSettings s)
        {
            var e = new FileEntry();
            e.Path = path;
            e.Probe = FF.Probe(path);
            e.Job = s;
            return e;
        }

        static JobContext Ctx(string outDir)
        {
            var ctx = new JobContext();
            ctx.TempDir = Path.Combine(outDir, "tmp_" +
                Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(ctx.TempDir);
            ctx.Cancelled = delegate { return false; };
            ctx.Log = delegate(string line) { Console.WriteLine("  " + line); };
            return ctx;
        }

        static void Run(string label, string path, AudioSettings s)
        {
            Console.WriteLine("\n=== " + label + " ===");
            try { AudioJob.Run(Entry(path, s), Ctx(s.OutputDir)); }
            catch (Exception ex)
            {
                Console.WriteLine("  ИСКЛЮЧЕНИЕ: " + ex.Message);
                failed++;
            }
        }

        static void Check(string label, bool ok)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
            if (!ok) failed++;
        }
    }
}
