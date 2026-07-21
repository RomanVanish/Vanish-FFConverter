using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;

namespace VanishFF
{
    class TrackInfo
    {
        public int Index;         // номер среди аудиодорожек, 0-based
        public string Codec = "";
        public int Channels;
        public int SampleRate;
        public int BitrateKbps;   // 0 = неизвестен
        public string Title = "";
        public string Language = "";

        public string Describe()
        {
            string ch = Channels == 1 ? "моно"
                      : Channels == 2 ? "стерео"
                      : Channels + " кан.";
            string s = string.Format("№{0} {1} {2}", Index + 1, Codec, ch);
            if (SampleRate > 0) s += " " + (SampleRate / 1000.0).ToString("0.#") + "кГц";
            if (BitrateKbps > 0) s += " " + BitrateKbps + "k";
            if (!string.IsNullOrEmpty(Language) && Language != "und")
                s += " (" + Language + ")";
            if (!string.IsNullOrEmpty(Title)) s += " — " + Title;
            return s;
        }
    }

    class ProbeInfo
    {
        public double Duration;
        public long SizeBytes;
        public bool HasVideo;
        public List<TrackInfo> Audio = new List<TrackInfo>();
    }

    // Обвязка ffmpeg/ffprobe: probe с кэшем не здесь (кэш — в FileEntry),
    // запуск с прогрессом и отменой.
    static class FF
    {
        public static string Quote(string path)
        {
            return "\"" + path + "\"";
        }

        static string GetStr(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
                return v.ToString();
            return "";
        }

        public static ProbeInfo Probe(string path)
        {
            var psi = new ProcessStartInfo(Program.FFprobe,
                "-v quiet -print_format json -show_streams -show_format " + Quote(path));
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;

            string json;
            using (var p = Process.Start(psi))
            {
                json = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            }

            Dictionary<string, object> data;
            try
            {
                data = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(json);
            }
            catch { return null; }
            if (data == null) return null;

            var info = new ProbeInfo();
            object fmtObj;
            if (data.TryGetValue("format", out fmtObj))
            {
                var fmt = fmtObj as Dictionary<string, object>;
                double dur;
                if (double.TryParse(GetStr(fmt, "duration"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out dur))
                    info.Duration = dur;
                long sz;
                if (long.TryParse(GetStr(fmt, "size"), out sz))
                    info.SizeBytes = sz;
            }

            object streamsObj;
            if (!data.TryGetValue("streams", out streamsObj)) return null;
            var streams = streamsObj as ArrayList;
            if (streams == null) return null;

            int audioIdx = 0;
            foreach (object sObj in streams)
            {
                var s = sObj as Dictionary<string, object>;
                if (s == null) continue;
                string type = GetStr(s, "codec_type");
                if (type == "video")
                {
                    // обложки mp3 приходят как video (attached_pic) — не видео
                    string disp = "";
                    object dObj;
                    if (s.TryGetValue("disposition", out dObj))
                        disp = GetStr(dObj as Dictionary<string, object>, "attached_pic");
                    if (disp != "1") info.HasVideo = true;
                }
                else if (type == "audio")
                {
                    var t = new TrackInfo();
                    t.Index = audioIdx++;
                    t.Codec = GetStr(s, "codec_name");
                    int ch; int.TryParse(GetStr(s, "channels"), out ch);
                    t.Channels = ch;
                    int sr; int.TryParse(GetStr(s, "sample_rate"), out sr);
                    t.SampleRate = sr;
                    long br; long.TryParse(GetStr(s, "bit_rate"), out br);
                    t.BitrateKbps = (int)(br / 1000);
                    object tagsObj;
                    if (s.TryGetValue("tags", out tagsObj))
                    {
                        var tags = tagsObj as Dictionary<string, object>;
                        t.Title = GetStr(tags, "title");
                        t.Language = GetStr(tags, "language");
                    }
                    info.Audio.Add(t);
                }
            }
            return info;
        }

        // Запуск ffmpeg.
        //   args      — аргументы без "-y" (он добавляется всегда)
        //   quiet     — добавить -loglevel error (для measure-прохода нельзя:
        //               loudnorm печатает свой json в stderr на уровне info)
        //   duration  — для прогресса в %; 0 = без прогресса
        //   onPct     — колбэк процентов (может быть null)
        //   onErrLine — колбэк каждой строки stderr (подробный вывод; null)
        //   isCancelled — опрос отмены; при true процесс убивается
        // Возвращает exit code; stderr целиком — в out-параметре.
        public static int Run(string args, bool quiet, double duration,
                              Action<int> onPct, Action<string> onErrLine,
                              Func<bool> isCancelled, out string stderrText)
        {
            var sb = new StringBuilder();
            sb.Append("-y -hide_banner ");
            if (quiet) sb.Append("-loglevel error ");
            if (duration > 0) sb.Append("-progress pipe:1 -nostats ");
            sb.Append(args);

            var psi = new ProcessStartInfo(Program.FFmpeg, sb.ToString());
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            var errAll = new StringBuilder();
            int lastPct = -1;

            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null || onPct == null || duration <= 0) return;
                    if (e.Data.StartsWith("out_time_ms=") ||
                        e.Data.StartsWith("out_time_us="))
                    {
                        long us;
                        if (long.TryParse(e.Data.Substring(12), out us))
                        {
                            int pct = (int)Math.Min(100, us / 1000000.0 / duration * 100);
                            if (pct > lastPct)
                            {
                                lastPct = pct;
                                onPct(pct);
                            }
                        }
                    }
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (errAll) errAll.AppendLine(e.Data);
                    if (onErrLine != null && e.Data.Length > 0) onErrLine(e.Data);
                };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                while (!p.WaitForExit(200))
                {
                    if (isCancelled != null && isCancelled())
                    {
                        try { p.Kill(); } catch { }
                        p.WaitForExit(3000);
                        stderrText = errAll.ToString();
                        return -1;
                    }
                }
                p.WaitForExit(); // дождаться асинхронных читателей
                stderrText = errAll.ToString();
                return p.ExitCode;
            }
        }
    }
}
