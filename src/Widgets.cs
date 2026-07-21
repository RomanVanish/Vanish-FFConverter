using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VanishFF
{
    // Общий ToolTip с долгим показом (тексты длинные).
    static class Tips
    {
        public static readonly ToolTip T = MakeTip();
        static ToolTip MakeTip()
        {
            var t = new ToolTip();
            t.AutoPopDelay = 30000;
            t.InitialDelay = 400;
            t.ReshowDelay = 200;
            return t;
        }
        public static void Set(Control c, string text)
        {
            T.SetToolTip(c, text);
        }
    }

    // Раскрывающийся блок «▸ Расширенно» (ТЗ: одна механика на все вкладки).
    class Expander : Panel
    {
        public readonly Button Header = new Button();
        public readonly Panel Content = new Panel();
        readonly string title;

        public Expander(string titleText)
        {
            title = titleText;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Fill;

            Header.Text = "▸ " + title;   // ▸
            Header.AutoSize = true;
            Header.TabStop = false;
            Header.Dock = DockStyle.Top;
            Header.TextAlign = ContentAlignment.MiddleLeft;
            Header.Click += delegate { Toggle(); };

            Content.AutoSize = true;
            Content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Content.Dock = DockStyle.Top;
            Content.Visible = false;
            Content.Padding = new Padding(16, 4, 0, 4);

            Controls.Add(Content);
            Controls.Add(Header);
        }

        public bool Expanded
        {
            get { return Content.Visible; }
            set
            {
                Content.Visible = value;
                Header.Text = (value ? "▾ " : "▸ ") + title; // ▾/▸
            }
        }

        public void Toggle() { Expanded = !Expanded; }
    }

    // Список файлов с очередью: статусы, цвета, DnD-добавление,
    // перетаскивание строк (порядок списка = порядок обработки, ТЗ 2.4).
    class FileListView : ListView
    {
        public Action<FileEntry> SelectionChangedCb;
        public Action ListChanged;

        public FileListView()
        {
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            MultiSelect = false;
            AllowDrop = true;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            BorderStyle = BorderStyle.FixedSingle;
            Columns.Add("Файл", 320);
            Columns.Add("Длительность", 110);
            Columns.Add("Статус", 340);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            ItemDrag += OnItemDrag;
            DragOver += OnDragOver;
            SelectedIndexChanged += delegate
            {
                if (SelectionChangedCb != null)
                    SelectionChangedCb(SelectedEntry);
            };
            Resize += delegate
            {
                // колонка файла тянется
                int w = ClientSize.Width - Columns[1].Width - Columns[2].Width - 4;
                if (w > 120) Columns[0].Width = w;
            };
        }

        public FileEntry SelectedEntry
        {
            get
            {
                return SelectedItems.Count > 0
                    ? (FileEntry)SelectedItems[0].Tag : null;
            }
        }

        public List<FileEntry> Entries
        {
            get
            {
                var list = new List<FileEntry>();
                foreach (ListViewItem it in Items)
                    list.Add((FileEntry)it.Tag);
                return list;
            }
        }

        public void AddPaths(string[] paths, Action<string> log)
        {
            foreach (string p in paths)
            {
                string full = Path.GetFullPath(p);
                bool dup = false;
                foreach (ListViewItem it in Items)
                    if (string.Equals(((FileEntry)it.Tag).Path, full,
                                      StringComparison.OrdinalIgnoreCase))
                    { dup = true; break; }
                if (dup) continue;
                if (Directory.Exists(full)) continue;

                ProbeInfo probe = FF.Probe(full);
                if (probe == null || (probe.Audio.Count == 0 && !probe.HasVideo))
                {
                    if (log != null)
                        log("Не медиа-файл, пропущен: " + Path.GetFileName(full));
                    continue;
                }

                var entry = new FileEntry();
                entry.Path = full;
                entry.Probe = probe;
                var item = new ListViewItem(entry.Name);
                item.Tag = entry;
                item.SubItems.Add(FmtDuration(probe.Duration));
                item.SubItems.Add("");
                Items.Add(item);
                UpdateEntry(entry);
            }
            if (ListChanged != null) ListChanged();
        }

        public void RemoveSelected()
        {
            foreach (ListViewItem it in SelectedItems)
            {
                var e = (FileEntry)it.Tag;
                if (e.Status == FileStatus.Working) continue; // в работе — нельзя
                Items.Remove(it);
            }
            if (ListChanged != null) ListChanged();
        }

        public void ClearAll()
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var e = (FileEntry)Items[i].Tag;
                if (e.Status != FileStatus.Working) Items.RemoveAt(i);
            }
            if (ListChanged != null) ListChanged();
        }

        public void UpdateEntry(FileEntry e)
        {
            foreach (ListViewItem it in Items)
            {
                if (it.Tag != e) continue;
                string text; Color color;
                StatusText(e, out text, out color);
                it.SubItems[2].Text = text;
                it.UseItemStyleForSubItems = false;
                it.SubItems[2].ForeColor = color;
                it.ForeColor = Theme.Current.Text;
                return;
            }
        }

        public void RefreshTheme()
        {
            foreach (ListViewItem it in Items)
                UpdateEntry((FileEntry)it.Tag);
        }

        static void StatusText(FileEntry e, out string text, out Color color)
        {
            Palette p = Theme.Current;
            switch (e.Status)
            {
                case FileStatus.Queued:
                    text = "⏳ в очереди · " + e.Job.Summary();
                    color = p.StQueued; break;
                case FileStatus.Working:
                    text = "► в работе...";
                    color = p.Accent; break;
                case FileStatus.Done:
                    text = "✓ готово (" + FmtElapsed(e.DoneSeconds) + ")";
                    color = p.StDone; break;
                case FileStatus.Error:
                    text = "✗ ошибка: " + e.Note;
                    color = p.StError; break;
                case FileStatus.Skipped:
                    text = "⏭ " + e.Note;
                    color = p.TextDim; break;
                default:
                    text = "— ожидает";
                    color = p.TextDim; break;
            }
        }

        public static string FmtDuration(double sec)
        {
            if (sec <= 0) return "";
            int s = (int)sec;
            return s >= 3600
                ? string.Format("{0}:{1:00}:{2:00}", s / 3600, s % 3600 / 60, s % 60)
                : string.Format("{0}:{1:00}", s / 60, s % 60);
        }

        public static string FmtElapsed(double sec)
        {
            int s = (int)sec;
            return s >= 3600
                ? string.Format("{0}:{1:00}:{2:00}", s / 3600, s % 3600 / 60, s % 60)
                : string.Format("{0}:{1:00}", s / 60, s % 60);
        }

        // ── перетаскивание ───────────────────────────────────────────

        void OnDragEnter(object s, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : e.Data.GetDataPresent(typeof(ListViewItem))
                    ? DragDropEffects.Move : DragDropEffects.None;
        }

        void OnItemDrag(object s, ItemDragEventArgs e)
        {
            DoDragDrop(e.Item, DragDropEffects.Move);
        }

        void OnDragOver(object s, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(ListViewItem))
                ? DragDropEffects.Move
                : e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object s, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (FilesDropped != null) FilesDropped(files);
                return;
            }
            var item = e.Data.GetData(typeof(ListViewItem)) as ListViewItem;
            if (item == null || item.ListView != this) return;
            Point pt = PointToClient(new Point(e.X, e.Y));
            ListViewItem target = GetItemAt(pt.X, pt.Y);
            int idx = target != null ? target.Index : Items.Count - 1;
            Items.Remove(item);
            Items.Insert(Math.Min(idx, Items.Count), item);
            if (ListChanged != null) ListChanged();
        }

        public Action<string[]> FilesDropped;
    }

    // Свои диалоги в палитре (системный MessageBox не темнеет, ТЗ 2.6).
    static class ThemedDialog
    {
        public static bool Confirm(IWin32Window owner, string text,
                                   string checkboxText, out bool checkboxOn)
        {
            using (var f = new Form())
            {
                f.Text = L.AppTitle;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(460, checkboxText != null ? 170 : 140);
                f.Font = new Font("Segoe UI", 10.5f);

                var lbl = new Label();
                lbl.Text = text;
                lbl.SetBounds(20, 18, 420, 70);
                f.Controls.Add(lbl);

                CheckBox cb = null;
                if (checkboxText != null)
                {
                    cb = new CheckBox();
                    cb.Text = checkboxText;
                    cb.SetBounds(20, 92, 420, 26);
                    f.Controls.Add(cb);
                }

                var ok = new Button();
                ok.Text = "Да";
                ok.Tag = "accent";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(f.ClientSize.Width - 210, f.ClientSize.Height - 44,
                             95, 32);
                var cancel = new Button();
                cancel.Text = "Отмена";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(f.ClientSize.Width - 108,
                                 f.ClientSize.Height - 44, 95, 32);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                Theme.Apply(f);
                f.Shown += delegate { Theme.ApplyTitleBar(f); };

                bool result = f.ShowDialog(owner) == DialogResult.OK;
                checkboxOn = cb != null && cb.Checked;
                return result;
            }
        }

        public static void Info(IWin32Window owner, string text)
        {
            bool dummy;
            Confirm(owner, text, null, out dummy);
        }
    }
}
