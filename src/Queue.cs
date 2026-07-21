using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VanishFF
{
    // Глобальная очередь (ТЗ 2.4): один воркер, один ffmpeg за раз.
    // Порядок — порядок списка вкладки (владелец сообщает индексы).
    class QueueManager
    {
        public static QueueManager I;

        readonly Control ui;                 // для маршалинга в UI-поток
        Thread worker;
        volatile bool stopNow;               // Стоп: убить текущий, пауза
        volatile bool skipCurrent;           // Пропустить текущее задание
        public volatile bool PauseAfter;     // Пауза после текущего
        volatile bool running;

        public FileEntry Current { get; private set; }

        // владелец вкладки отдаёт очередь и принимает события
        public Func<List<FileEntry>> GetQueued;         // в порядке списка
        public Action<FileEntry> EntryChanged;          // статус обновился
        public Action<string> Status;                   // статусная полоса
        public Action<FileEntry, string> Log;           // строка в консоль
        public Action QueueFinished;

        public QueueManager(Control uiControl)
        {
            ui = uiControl;
        }

        public bool Running { get { return running; } }

        public void Start()
        {
            stopNow = false;
            skipCurrent = false;
            PauseAfter = false;
            if (running) return;
            running = true;
            worker = new Thread(WorkLoop);
            worker.IsBackground = true;
            worker.Start();
        }

        public void StopNow()
        {
            stopNow = true;
        }

        public void SkipCurrent()
        {
            skipCurrent = true;
        }

        void OnUi(Action a)
        {
            try
            {
                if (ui.IsHandleCreated) ui.BeginInvoke(a);
            }
            catch { }
        }

        void WorkLoop()
        {
            try
            {
                while (true)
                {
                    if (stopNow || PauseAfter) break;

                    FileEntry next = null;
                    var list = new List<FileEntry>();
                    var ev = new ManualResetEvent(false);
                    OnUi(delegate { list.AddRange(GetQueued()); ev.Set(); });
                    ev.WaitOne(3000);
                    foreach (var e in list)
                        if (e.Status == FileStatus.Queued) { next = e; break; }
                    if (next == null) break;

                    RunOne(next);
                }
            }
            finally
            {
                running = false;
                Current = null;
                OnUi(delegate
                {
                    if (QueueFinished != null) QueueFinished();
                });
            }
        }

        void RunOne(FileEntry entry)
        {
            Current = entry;
            skipCurrent = false;
            var t0 = DateTime.Now;

            OnUi(delegate
            {
                entry.Status = FileStatus.Working;
                entry.Note = "";
                if (EntryChanged != null) EntryChanged(entry);
                if (Status != null) Status("В работе: " + entry.Name);
            });

            string temp = Path.Combine(Program.AppDir, "temp",
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(temp);

            var ctx = new JobContext();
            ctx.TempDir = temp;
            ctx.Cancelled = delegate { return stopNow || skipCurrent; };
            ctx.Log = delegate(string line)
            {
                OnUi(delegate { if (Log != null) Log(entry, line); });
            };

            FileStatus result = FileStatus.Done;
            string note = "";
            try
            {
                AudioJob.Run(entry, ctx);
            }
            catch (JobCancelledException)
            {
                result = FileStatus.Skipped;
                note = stopNow ? "остановлено" : "пропущено";
            }
            catch (JobSkippedException ex)
            {
                result = FileStatus.Skipped;
                note = ex.Message;
            }
            catch (Exception ex)
            {
                result = FileStatus.Error;
                note = ex.Message;
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }

            double secs = (DateTime.Now - t0).TotalSeconds;
            OnUi(delegate
            {
                entry.Status = result;
                entry.Note = note;
                entry.DoneSeconds = secs;
                if (result != FileStatus.Done && Log != null)
                    Log(entry, result == FileStatus.Error
                        ? "ОШИБКА: " + note : ">>> " + note);
                if (EntryChanged != null) EntryChanged(entry);
            });
        }
    }
}
