using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace VanishFF
{
    enum FileStatus { Waiting, Queued, Working, Done, Error, Skipped }

    // Строка списка файлов: путь + кэш ffprobe + статус + задание-снимок.
    class FileEntry
    {
        public string Path;
        public ProbeInfo Probe;
        public FileStatus Status = FileStatus.Waiting;
        public string Note = "";       // текст после статуса (сводка/ошибка)
        public AudioSettings Job;      // снимок настроек (null = нет задания)
        public double DoneSeconds;     // время обработки
        public int BatchIndex = 1;     // номер в пачке для тегов {#}

        public string Name { get { return System.IO.Path.GetFileName(Path); } }
    }

    // Снимок настроек вкладки «Аудио» в момент постановки (ТЗ 2.4).
    class AudioSettings
    {
        public string Format = "opus";     // opus/mp3/aac/flac/wav
        public int BitrateKbps = 64;       // opus/mp3/aac при CBR
        public bool VbrMode;               // mp3/aac: VBR вместо CBR
        public int VbrQuality = 4;         // mp3: V0..V9; aac: 1..5
        public string OpusApp = "audio";   // audio (авто) / voip (речь)
        public bool OpusVbr = true;
        public int FlacLevel = 5;
        public int FlacBits;               // 0 = как в исходнике, 16, 24
        public int WavBits;                // 0 = как в исходнике, 16, 24, 32
        public int SampleRate;             // 0 = авто
        public string Channels = "original"; // original/mono/stereo
        public List<int> Tracks = new List<int>(); // отмеченные, 0-based
        public int LeftTrack, RightTrack = 1;      // для «по ушам», 0-based
        public bool Normalize = true;
        public int Lufs = -16;
        public string NameTemplate = "{имя}";
        public string OutputDir = "";      // "" = рядом с исходником
        public bool Overwrite;
        public string ExtraArgs = "";
        public bool Verbose;

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(Format);
            if (Format == "opus" || Format == "mp3" || Format == "aac")
            {
                if (VbrMode && Format == "mp3") sb.Append(" V" + VbrQuality);
                else if (VbrMode && Format == "aac") sb.Append(" q" + VbrQuality);
                else sb.Append(" " + BitrateKbps + "k");
            }
            sb.Append(Channels == "mono" ? ", моно"
                    : Channels == "stereo" ? ", по ушам" : "");
            if (Normalize) sb.Append(", норм. " + Lufs);
            return sb.ToString();
        }
    }

    // Контекст выполнения задания: лог и отмена (всё потокобезопасно,
    // маршалинг в UI делает QueueManager).
    class JobContext
    {
        public Action<string> Log;
        public Func<bool> Cancelled;
        public string TempDir;
    }

    // Порт listening.py: извлечение дорожек, двухпроходный loudnorm,
    // сведение, кодирование.
    static class AudioJob
    {
        public static void Run(FileEntry entry, JobContext ctx)
        {
            AudioSettings s = entry.Job;
            ProbeInfo probe = entry.Probe;
            if (probe == null || probe.Audio.Count == 0)
                throw new Exception("в файле нет аудиодорожек");

            var tracks = new List<int>();
            foreach (int t in s.Tracks)
                if (t < probe.Audio.Count) tracks.Add(t);
            if (tracks.Count == 0) tracks.Add(0);

            string outDir = string.IsNullOrEmpty(s.OutputDir)
                ? Path.GetDirectoryName(entry.Path) : s.OutputDir;
            Directory.CreateDirectory(outDir);

            ctx.Log(string.Format(
                "Файл: {0} | дорожек: {1}, берём: {2} | {3} | нормализация: {4}",
                entry.Name, probe.Audio.Count, JoinTracks(tracks),
                s.Summary(), s.Normalize ? "да" : "нет"));

            if (s.Channels == "original" && tracks.Count > 1)
            {
                // каждая отмеченная дорожка — отдельным файлом (ТЗ 3.4)
                int done = 0;
                foreach (int idx in tracks)
                {
                    string outPath = BuildOutPath(entry, s, outDir, idx, true);
                    if (outPath == null) continue;
                    ctx.Log(string.Format("Дорожка {0} [{1}/{2}]:",
                        idx + 1, done + 1, tracks.Count));
                    ConvertSingle(entry, s, idx, outPath, ctx);
                    done++;
                }
            }
            else if (s.Channels == "stereo")
            {
                string outPath = BuildOutPath(entry, s, outDir, -1, false);
                if (outPath == null) return;
                int li = Math.Min(s.LeftTrack, probe.Audio.Count - 1);
                int ri = Math.Min(s.RightTrack, probe.Audio.Count - 1);
                ctx.Log(string.Format(
                    "Стерео «голоса по ушам»: дорожка {0} -> L, {1} -> R",
                    li + 1, ri + 1));
                string wl = ExtractNormalized(entry, s, li, true, ctx, "L");
                string wr = ExtractNormalized(entry, s, ri, true, ctx, "R");
                Encode(s, "[0:a][1:a]join=inputs=2:channel_layout=stereo[out]",
                       new string[] { wl, wr }, outPath, entry, ctx);
                ReportSize(entry, outPath, ctx);
            }
            else if (s.Channels == "mono" && tracks.Count > 1)
            {
                string outPath = BuildOutPath(entry, s, outDir, -1, false);
                if (outPath == null) return;
                var wavs = new List<string>();
                for (int n = 0; n < tracks.Count; n++)
                {
                    ctx.Log(string.Format("Дорожка {0} [{1}/{2}]:",
                        tracks[n] + 1, n + 1, tracks.Count));
                    wavs.Add(ExtractNormalized(entry, s, tracks[n], true, ctx,
                                               "t" + n));
                }
                ctx.Log("Сведение в моно...");
                var fg = new StringBuilder();
                for (int i = 0; i < wavs.Count; i++)
                    fg.Append("[" + i + ":a]");
                fg.Append("amix=inputs=" + wavs.Count + ":normalize=0[out]");
                Encode(s, fg.ToString(), wavs.ToArray(), outPath, entry, ctx);
                ReportSize(entry, outPath, ctx);
            }
            else
            {
                // одна дорожка (или одна отмеченная)
                string outPath = BuildOutPath(entry, s, outDir, -1, false);
                if (outPath == null) return;
                ConvertSingle(entry, s, tracks[0], outPath, ctx);
            }
        }

        // ── шаги конвейера ───────────────────────────────────────────

        static void ConvertSingle(FileEntry entry, AudioSettings s, int idx,
                                  string outPath, JobContext ctx)
        {
            bool toMono = s.Channels == "mono";
            string wav = ExtractNormalized(entry, s, idx, toMono, ctx,
                                           "s" + idx);
            Encode(s, null, new string[] { wav }, outPath, entry, ctx);
            ReportSize(entry, outPath, ctx);
        }

        // Извлечь дорожку в WAV (+ двухпроходная нормализация при галке).
        static string ExtractNormalized(FileEntry entry, AudioSettings s,
                                        int idx, bool mono, JobContext ctx,
                                        string tag)
        {
            int rate = EffRate(s, entry.Probe, idx);
            string wav = Path.Combine(ctx.TempDir, tag + ".wav");
            string map = "-map 0:a:" + idx;
            string ac = mono ? " -ac 1" : "";

            RunStep(string.Format("-i {0} {1}{2} -ar {3} {4}",
                    FF.Quote(entry.Path), map, ac, rate, FF.Quote(wav)),
                    true, entry.Probe.Duration, "извлечение", s, ctx);

            if (!s.Normalize) return wav;

            string measured = Measure(wav, entry.Probe.Duration, s, ctx);
            string wavN = Path.Combine(ctx.TempDir, tag + "n.wav");
            RunStep(string.Format("-i {0} -af {1}{2} -ar {3} {4}",
                    FF.Quote(wav), Loudnorm(s), measured, rate, FF.Quote(wavN)),
                    true, entry.Probe.Duration, "нормализация", s, ctx);
            try { File.Delete(wav); } catch { }
            return wavN;
        }

        static string Loudnorm(AudioSettings s)
        {
            return "loudnorm=I=" + s.Lufs + ":TP=-1.5:LRA=11";
        }

        // Первый проход loudnorm: измерение. Возвращает ":measured_..." хвост.
        static string Measure(string wav, double duration, AudioSettings s,
                              JobContext ctx)
        {
            string stderr;
            string args = string.Format("-i {0} -af {1}:print_format=json -f null -",
                                        FF.Quote(wav), Loudnorm(s));
            LogCmd(args, s, ctx);
            int code = FF.Run(args, false, duration,
                MakePct("измерение громкости", ctx),
                s.Verbose ? MakeVerbose(ctx) : (Action<string>)null,
                ctx.Cancelled, out stderr);
            CheckCancel(ctx);
            if (code != 0)
                throw new Exception("ffmpeg (измерение): " + Tail(stderr));

            var m = Regex.Match(stderr, "\\{[^{}]*\"input_i\"[^{}]*\\}",
                                RegexOptions.Singleline);
            if (!m.Success)
            {
                ctx.Log("      (не удалось измерить — нормализация одним проходом)");
                return "";
            }
            var d = new System.Web.Script.Serialization.JavaScriptSerializer()
                .Deserialize<Dictionary<string, string>>(m.Value);
            return string.Format(
                ":measured_I={0}:measured_TP={1}:measured_LRA={2}" +
                ":measured_thresh={3}:offset={4}:linear=true",
                d["input_i"], d["input_tp"], d["input_lra"],
                d["input_thresh"], d["target_offset"]);
        }

        static void Encode(AudioSettings s, string filtergraph, string[] inputs,
                           string outPath, FileEntry entry, JobContext ctx)
        {
            var sb = new StringBuilder();
            foreach (string p in inputs)
                sb.Append("-i " + FF.Quote(p) + " ");
            if (filtergraph != null)
                sb.Append("-filter_complex \"" + filtergraph + "\" -map \"[out]\" ");

            switch (s.Format)
            {
                case "opus":
                    sb.Append("-c:a libopus -b:a " + s.BitrateKbps + "k");
                    sb.Append(" -application " + s.OpusApp);
                    if (!s.OpusVbr) sb.Append(" -vbr off");
                    break;
                case "mp3":
                    sb.Append("-c:a libmp3lame ");
                    sb.Append(s.VbrMode ? "-q:a " + s.VbrQuality
                                        : "-b:a " + s.BitrateKbps + "k");
                    break;
                case "aac":
                    sb.Append("-c:a aac ");
                    // VBR-качество 1..5 -> -q:a 0.3..1.9
                    sb.Append(s.VbrMode
                        ? "-q:a " + (0.3 + (s.VbrQuality - 1) * 0.4)
                              .ToString("0.0", CultureInfo.InvariantCulture)
                        : "-b:a " + s.BitrateKbps + "k");
                    break;
                case "flac":
                    sb.Append("-c:a flac -compression_level " + s.FlacLevel);
                    if (s.FlacBits == 16) sb.Append(" -sample_fmt s16");
                    else if (s.FlacBits == 24) sb.Append(" -sample_fmt s32");
                    break;
                case "wav":
                    sb.Append(s.WavBits == 24 ? "-c:a pcm_s24le"
                            : s.WavBits == 32 ? "-c:a pcm_f32le"
                            : "-c:a pcm_s16le");
                    break;
            }

            int rate = EffRate(s, entry.Probe,
                               s.Tracks.Count > 0 ? s.Tracks[0] : 0);
            sb.Append(" -ar " + rate);
            if (!string.IsNullOrEmpty(s.ExtraArgs))
                sb.Append(" " + s.ExtraArgs.Trim());
            sb.Append(" " + FF.Quote(outPath));

            RunStep(sb.ToString(), true, entry.Probe.Duration, "кодирование",
                    s, ctx);
        }

        // ── вспомогательное ──────────────────────────────────────────

        static int EffRate(AudioSettings s, ProbeInfo probe, int trackIdx)
        {
            if (s.SampleRate > 0) return s.SampleRate;
            if (s.Format == "opus" || s.Format == "aac") return 48000;
            if (s.Format == "mp3") return 44100;
            // flac/wav: как в исходнике
            if (trackIdx < probe.Audio.Count &&
                probe.Audio[trackIdx].SampleRate > 0)
                return probe.Audio[trackIdx].SampleRate;
            return 48000;
        }

        static void RunStep(string args, bool quiet, double duration,
                            string label, AudioSettings s, JobContext ctx)
        {
            LogCmd(args, s, ctx);
            string stderr;
            int code = FF.Run(args, quiet, duration, MakePct(label, ctx),
                s.Verbose ? MakeVerbose(ctx) : (Action<string>)null,
                ctx.Cancelled, out stderr);
            CheckCancel(ctx);
            if (code != 0)
                throw new Exception("ffmpeg (" + label + "): " + Tail(stderr));
        }

        static void LogCmd(string args, AudioSettings s, JobContext ctx)
        {
            // полная команда в консоль (ТЗ 2.3.5)
            ctx.Log(">>> ffmpeg -y " + args);
        }

        static Action<int> MakePct(string label, JobContext ctx)
        {
            int last = -25;
            return delegate(int pct)
            {
                if (pct - last >= 25 || pct == 100)
                {
                    last = pct;
                    ctx.Log("      " + label + ": " + pct + "%");
                }
            };
        }

        static Action<string> MakeVerbose(JobContext ctx)
        {
            return delegate(string line) { ctx.Log("  " + line); };
        }

        static void CheckCancel(JobContext ctx)
        {
            if (ctx.Cancelled()) throw new JobCancelledException();
        }

        static string Tail(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length > 400) text = text.Substring(text.Length - 400);
            return text;
        }

        static string JoinTracks(List<int> t)
        {
            var parts = new List<string>();
            foreach (int i in t) parts.Add((i + 1).ToString());
            return string.Join(",", parts.ToArray());
        }

        static void ReportSize(FileEntry entry, string outPath, JobContext ctx)
        {
            double outMb = new FileInfo(outPath).Length / 1048576.0;
            double srcMb = new FileInfo(entry.Path).Length / 1048576.0;
            ctx.Log(string.Format("Готово: {0}", outPath));
            ctx.Log(string.Format("Размер: {0:0.0} МБ -> {1:0.0} МБ" +
                (outMb > 0 && srcMb / outMb >= 1.05
                    ? " (сжатие в " + (srcMb / outMb).ToString("0.0") + " раза)"
                    : ""),
                srcMb, outMb));
        }

        // Имя результата по шаблону; null = пропустить (уже существует).
        // trackIdx >= 0 — режим «каждая дорожка отдельным файлом».
        static string BuildOutPath(FileEntry entry, AudioSettings s,
                                   string outDir, int trackIdx, bool perTrack)
        {
            string baseName = Path.GetFileNameWithoutExtension(entry.Path);
            DateTime mtime = File.GetLastWriteTime(entry.Path);
            int n = Math.Max(1, entry.BatchIndex);

            string tpl = string.IsNullOrEmpty(s.NameTemplate)
                ? "{имя}" : s.NameTemplate;
            string name = tpl
                .Replace("{имя}", baseName).Replace("{name}", baseName)
                .Replace("{дата}", mtime.ToString("yyyy-MM-dd"))
                .Replace("{date}", mtime.ToString("yyyy-MM-dd"))
                .Replace("{время_чмс}", mtime.ToString("HH-mm-ss"))
                .Replace("{time_hms}", mtime.ToString("HH-mm-ss"))
                .Replace("{время_чм}", mtime.ToString("HH-mm"))
                .Replace("{time_hm}", mtime.ToString("HH-mm"))
                .Replace("{сегодня}", DateTime.Today.ToString("yyyy-MM-dd"))
                .Replace("{today}", DateTime.Today.ToString("yyyy-MM-dd"))
                .Replace("{###}", n.ToString("000"))
                .Replace("{##}", n.ToString("00"))
                .Replace("{#}", n.ToString())
                .Trim();

            if (perTrack)
            {
                string trackTag = "дорожка " + (trackIdx + 1);
                if (name.Contains("{дорожка}") || name.Contains("{track}"))
                    name = name.Replace("{дорожка}", trackTag)
                               .Replace("{track}", trackTag);
                else
                    name += " (" + trackTag + ")";
            }
            else
                name = name.Replace("{дорожка}", "").Replace("{track}", "")
                           .Trim();

            if (name.Length == 0) name = baseName;
            name = Regex.Replace(name, "[<>:\"/\\\\|?*]", "_");
            string outPath = Path.Combine(outDir, name + "." + s.Format);

            if (string.Equals(Path.GetFullPath(outPath),
                              Path.GetFullPath(entry.Path),
                              StringComparison.OrdinalIgnoreCase))
                throw new Exception(
                    "имя результата совпадает с исходником — измените шаблон");

            if (File.Exists(outPath) && !s.Overwrite)
            {
                throw new JobSkippedException(
                    "результат уже существует: " + Path.GetFileName(outPath) +
                    " (включите «Перезаписывать готовые»)");
            }
            return outPath;
        }
    }

    class JobCancelledException : Exception { }
    class JobSkippedException : Exception
    {
        public JobSkippedException(string msg) : base(msg) { }
    }
}
