using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VanishFF
{
    // Тёмный тултип со значком ⓘ (как в транскрибере: жёлтый значок,
    // подсказка при наведении). Отрисовка своя — системный тултип светлый.
    static class Tips
    {
        static readonly Font TipFont = new Font("Segoe UI", 9.5f);
        public static readonly ToolTip T = MakeTip();

        static ToolTip MakeTip()
        {
            var t = new ToolTip();
            t.AutoPopDelay = 40000;
            t.InitialDelay = 300;
            t.ReshowDelay = 120;
            t.ShowAlways = true;
            t.OwnerDraw = true;
            t.Popup += delegate(object s, PopupEventArgs e)
            {
                string txt = T.GetToolTip(e.AssociatedControl);
                Size sz = TextRenderer.MeasureText(txt, TipFont);
                e.ToolTipSize = new Size(sz.Width + 18, sz.Height + 14);
            };
            t.Draw += delegate(object s, DrawToolTipEventArgs e)
            {
                Palette p = Theme.Current;
                using (var bg = new SolidBrush(p.ConsoleBg))
                    e.Graphics.FillRectangle(bg, e.Bounds);
                using (var pen = new Pen(p.StQueued))
                    e.Graphics.DrawRectangle(pen, 0, 0,
                        e.Bounds.Width - 1, e.Bounds.Height - 1);
                var r = e.Bounds; r.X += 8; r.Y += 6;
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, TipFont, r,
                    p.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };
            return t;
        }

        public static void Set(Control c, string text)
        {
            T.SetToolTip(c, text);
        }

        // Значок ⓘ с подсказкой — ставится рядом с меткой блока.
        public static Label Icon(string text)
        {
            var l = new Label();
            l.Text = "ⓘ";
            l.AutoSize = true;
            l.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            l.Margin = new Padding(3, 5, 4, 0);
            l.Cursor = Cursors.Help;
            l.Tag = "info";
            Set(l, text);
            return l;
        }
    }

    // Консоль лога: авто-прокрутка вниз ТОЛЬКО если пользователь уже внизу.
    // Если он прокрутил вверх читать — новые строки не сбрасывают позицию.
    // RichTextBox (а не TextBox) — ровнее рисует строки, без обрезки верхней.
    class LogBox : RichTextBox
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, ref RECT r);
        const int EM_GETFIRSTVISIBLELINE = 0xCE;
        const int EM_LINESCROLL = 0xB6;
        const int EM_SETRECT = 0xB3;

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        public LogBox()
        {
            ReadOnly = true;
            WordWrap = true;
            BorderStyle = BorderStyle.None;
            ScrollBars = RichTextBoxScrollBars.Vertical;
        }

        // Внутренний отступ текстовой области — иначе первая строка рисуется
        // впритык к верхней границе и выглядит обрезанной.
        void UpdateTextRect()
        {
            if (!IsHandleCreated) return;
            var r = new RECT { Left = 4, Top = 6,
                Right = ClientSize.Width - 4, Bottom = ClientSize.Height };
            SendMessage(Handle, EM_SETRECT, IntPtr.Zero, ref r);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateTextRect();
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            UpdateTextRect();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) UpdateTextRect();
        }

        public void AppendLine(string line)
        {
            bool atBottom = AtBottom();
            int firstBefore = FirstVisible();
            Select(TextLength, 0);
            SelectedText = line + "\r\n";
            if (atBottom)
                ScrollBottomWhole();   // прокрутка по целым строкам — верхняя целая
            else
            {
                int delta = firstBefore - FirstVisible();
                if (delta != 0)
                    SendMessage(Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
            }
        }

        // Прокрутить к низу так, чтобы верхняя видимая строка была ЦЕЛОЙ
        // (сдвигаем область просмотра на целое число строк).
        void ScrollBottomWhole()
        {
            int lineH = Font.Height > 0 ? Font.Height : 16;
            int visible = Math.Max(1, ClientSize.Height / lineH);
            int total = GetLineFromCharIndex(TextLength);   // индекс последней строки
            int target = Math.Max(0, total - visible + 1);
            int delta = target - FirstVisible();
            if (delta != 0)
                SendMessage(Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
        }

        int FirstVisible()
        {
            return SendMessage(Handle, EM_GETFIRSTVISIBLELINE,
                               IntPtr.Zero, IntPtr.Zero).ToInt32();
        }

        bool AtBottom()
        {
            if (TextLength == 0) return true;
            int first = FirstVisible();
            int total = GetLineFromCharIndex(TextLength);
            int lineH = Font.Height > 0 ? Font.Height : 16;
            int visible = Math.Max(1, ClientSize.Height / lineH);
            return first + visible >= total;
        }
    }

    // Тонкий прогресс-бар терракотой (ТЗ 2.6): системный ProgressBar не
    // темнеет, поэтому свой.
    class ThinProgress : Control
    {
        int val;
        public bool ShowText;
        static readonly Color Fill = Theme.FromHex("2E9E44");   // зелёный (Rufus)

        public ThinProgress()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 6;
        }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(0, Math.Min(100, value));
                if (v != val) { val = v; Invalidate(); }
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            using (var b = new SolidBrush(p.IsDark
                ? Theme.Blend(p.PanelBg, p.WindowBg, 0.4f) : p.PanelBg))
                e.Graphics.FillRectangle(b, ClientRectangle);
            int w = (int)((long)Width * val / 100);
            if (w > 0)
                using (var b = new SolidBrush(Fill))
                    e.Graphics.FillRectangle(b, 0, 0, w, Height);
            if (ShowText)
            {
                string t = val + "%";
                // белый на зелёной части, обычный текст на пустой
                TextRenderer.DrawText(e.Graphics, t, Font, ClientRectangle,
                    val >= 50 ? Color.White : p.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            using (var pen = new Pen(p.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // Тёмный ComboBox: WinForms сам не красит выпадайки (в Qt-транскрибере
    // они тёмные из коробки). Рисуем и закрытый бокс, и список пунктов.
    class DarkCombo : ComboBox
    {
        public DarkCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 22;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Palette p = Theme.Current;
            // неактивный combo — заметно приглушённый фон и текст
            Color panel = Enabled ? p.PanelBg
                : Theme.Blend(p.PanelBg, p.WindowBg, 0.6f);
            Color text = Enabled ? p.Text : p.TextDim;

            if (e.Index < 0)
            {
                using (var b = new SolidBrush(panel))
                    e.Graphics.FillRectangle(b, e.Bounds);
                return;
            }
            bool sel = (e.State & DrawItemState.Selected) != 0 && Enabled;
            Color bg = sel ? Theme.Blend(p.PanelBg, p.Accent, p.IsDark ? 0.38f : 0.22f)
                           : panel;
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);
            var r = e.Bounds; r.X += 5;
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]),
                Font, r, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis);
        }

        // Enabled сам не перерисовывает owner-draw combo — форсируем.
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }
    }

    // Обёртка с рамкой нашего цвета: WinForms рисует поля/списки системной
    // белой рамкой, которая «вырви глаз» в тёмной теме. Прячем родную,
    // даём свою через Panel(Padding=1, Tag="border").
    static class Bordered
    {
        public static Panel Wrap(Control inner)
        {
            var tb = inner as TextBox;
            if (tb != null) tb.BorderStyle = BorderStyle.None;
            var lv = inner as ListView;
            if (lv != null) lv.BorderStyle = BorderStyle.None;

            bool fill = inner.Dock == DockStyle.Fill;
            var p = new Panel();
            p.Tag = "border";
            p.Margin = inner.Margin;
            inner.Margin = Padding.Empty;

            if (fill)
            {
                p.Padding = new Padding(1);
                inner.Dock = DockStyle.Fill;
                p.Dock = DockStyle.Fill;
                p.Controls.Add(inner);
            }
            else
            {
                // Однострочное поле: высота берётся из реального размера
                // контрола (он авто-подстраивается под шрифт/DPI), поэтому
                // рамка не обрезает текст «наполовину». Панель следует за
                // размером поля через SizeChanged.
                int w = inner.Width;
                inner.Location = new Point(1, 1);
                p.Controls.Add(inner);
                EventHandler sync = delegate
                {
                    p.Size = new Size(inner.Width + 2, inner.Height + 2);
                };
                inner.SizeChanged += sync;
                p.HandleCreated += sync;
                p.Size = new Size(w + 2, inner.Height + 2);
            }
            return p;
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
            Dock = DockStyle.Top;

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
                if (Content.Visible == value)
                {
                    Header.Text = (value ? "▾ " : "▸ ") + title;
                    return;
                }
                // высота содержимого — чтобы вырастить окно на неё, а не
                // выпихивать кнопки за край (обратная связь)
                int delta = Content.GetPreferredSize(Size.Empty).Height;
                Content.Visible = value;
                Header.Text = (value ? "▾ " : "▸ ") + title; // ▾/▸

                Control p = this;
                while (p != null) { p.PerformLayout(); p = p.Parent; }

                var f = FindForm();
                if (f != null && f.WindowState == FormWindowState.Normal
                    && delta > 0)
                {
                    var wa = Screen.FromControl(f).WorkingArea;
                    int target = f.Height + (value ? delta : -delta);
                    f.Height = Math.Max(f.MinimumSize.Height,
                                        Math.Min(target, wa.Height));
                }
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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // LVM_SETEXTENDEDLISTVIEWSTYLE + LVS_EX_DOUBLEBUFFER — убирает
            // мерцание и глюки перерисовки owner-draw
            SendMessage(Handle, 0x1000 + 54, (IntPtr)0x00010000, (IntPtr)0x00010000);
        }

        public FileListView()
        {
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            MultiSelect = false;
            AllowDrop = true;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            BorderStyle = BorderStyle.None;
            OwnerDraw = true;   // сами красим шапку и строки (ТЗ 2.6)
            Columns.Add("#", 36);
            Columns.Add("", 42);          // 1 значок статуса
            Columns.Add("Статус", 230);   // 2 текст статуса/параметры
            Columns.Add("Файл", 220);
            Columns.Add("Длит.", 66);
            Columns.Add("Размер", 78);
            Columns.Add("Инфо", 220);
            ShowItemToolTips = true;   // полный статус/ошибка во всплывашке
            MouseClick += OnMouseClickList;
            DrawColumnHeader += OnDrawHeader;
            DrawItem += OnDrawItem;              // «хвост» строки
            DrawSubItem += OnDrawSubItem;

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            ItemDrag += OnItemDrag;
            DragOver += OnDragOver;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
            };
            SelectedIndexChanged += delegate
            {
                if (SelectionChangedCb != null)
                    SelectionChangedCb(SelectedEntry);
            };
            // ресайз окна — последняя колонка ровно заполняет остаток;
            // пользователь тянет колонку — последнюю только ДОРАЩИВАЕМ
            // (при расширении колонок появляется горизонтальный скролл).
            Resize += delegate { FillLast(true); };
            ColumnWidthChanged += delegate { FillLast(false); };
        }

        public FileEntry SelectedEntry
        {
            get
            {
                return SelectedItems.Count > 0
                    ? (FileEntry)SelectedItems[0].Tag : null;
            }
        }

        // Нумерация строк (как в XXL): колонка «#».
        public void Renumber()
        {
            for (int i = 0; i < Items.Count; i++)
                Items[i].Text = (i + 1).ToString();
        }

        bool adjustingCols;

        // Последнюю колонку только РАСШИРЯЕМ до правого края (закрыть пустое
        // место — нет белой полосы). НЕ сжимаем: если колонки не влезают по
        // ширине, появляется родной горизонтальный скролл.
        void FillLast(bool exact)
        {
            if (adjustingCols || Columns.Count < 2 || ClientSize.Width < 50)
                return;
            adjustingCols = true;
            int used = 0;
            for (int i = 0; i < Columns.Count - 1; i++)
                used += Columns[i].Width;
            int avail = ClientSize.Width - used - 2;
            var last = Columns[Columns.Count - 1];
            if (exact)
                last.Width = Math.Max(140, avail);   // ресайз окна: ровно
            else if (avail > last.Width)
                last.Width = avail;                  // тянут колонку: только рост
            // если avail < last.Width (колонки шире окна) — не трогаем,
            // появляется горизонтальный скролл
            adjustingCols = false;
        }

        // ── owner-draw ───────────────────────────────────────────────

        void OnDrawHeader(object s, DrawListViewColumnHeaderEventArgs e)
        {
            Palette p = Theme.Current;
            using (var bg = new SolidBrush(p.PanelBg))
                e.Graphics.FillRectangle(bg, e.Bounds);
            // нижняя граница + светлый вертикальный разделитель справа —
            // подсказка, что колонки можно тянуть
            using (var pen = new Pen(p.Border))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1,
                                    e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4,
                                    e.Bounds.Right - 1, e.Bounds.Bottom - 4);
            }
            var r = e.Bounds; r.X += 6;
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, r, p.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        // Красим ТОЛЬКО «хвост» справа от последней колонки (иначе там
        // белая полоса). Всю строку заливать нельзя — при частичной
        // перерисовке это затирало текст ячеек (баг «инфа исчезает»).
        void OnDrawItem(object s, DrawListViewItemEventArgs e)
        {
            Palette p = Theme.Current;
            Color bg = e.Item.Selected
                ? Blend(p.PanelBg, p.Accent, p.IsDark ? 0.32f : 0.22f)
                : p.PanelBg;
            int right = 0;
            foreach (ColumnHeader c in Columns) right += c.Width;
            if (right < ClientSize.Width)
                using (var b = new SolidBrush(bg))
                    e.Graphics.FillRectangle(b, new Rectangle(right,
                        e.Bounds.Top, ClientSize.Width - right, e.Bounds.Height));
        }

        void OnDrawSubItem(object s, DrawListViewSubItemEventArgs e)
        {
            Palette p = Theme.Current;
            bool sel = e.Item.Selected;
            Color bg = sel
                ? Blend(p.PanelBg, p.Accent, p.IsDark ? 0.32f : 0.22f)
                : p.PanelBg;
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);

            // ВСЯ строка окрашивается в цвет статуса файла; «в работе» —
            // жирным. Колонка «Статус» никогда не режется многоточием.
            Color fg; bool bold;
            RowStyle(((FileEntry)e.Item.Tag).Status, p, out fg, out bold);
            Font f = bold ? BoldFont : Font;
            bool center = e.ColumnIndex == 0 || e.ColumnIndex == 1;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | (center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            var r = e.Bounds;
            if (!center) { r.X += 6; r.Width -= 8; }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, f, r, fg, flags);
        }

        Font boldFont;
        Font BoldFont
        {
            get
            {
                if (boldFont == null || boldFont.FontFamily != Font.FontFamily
                    || boldFont.Size != Font.Size)
                    boldFont = new Font(Font, FontStyle.Bold);
                return boldFont;
            }
        }

        static void RowStyle(FileStatus st, Palette p, out Color col, out bool bold)
        {
            bold = false;
            switch (st)
            {
                case FileStatus.Queued: col = p.StQueued; break;
                case FileStatus.Working: col = p.StWorking; bold = true; break;
                case FileStatus.Done: col = p.StDone; break;
                case FileStatus.Error: col = p.StError; break;
                case FileStatus.Skipped: col = p.TextDim; break;
                default: col = p.Text; break;   // Waiting
            }
        }

        static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
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
                var item = new ListViewItem("");   // 0 № — заполнит Renumber
                item.SubItems.Add("");                        // 1 значок
                item.SubItems.Add("");                        // 2 текст статуса
                item.SubItems.Add(entry.Name);                // 3 Файл
                item.SubItems.Add(FmtDuration(probe.Duration)); // 4 Длит.
                item.SubItems.Add(FmtSize(probe.SizeBytes));  // 5 Размер
                item.SubItems.Add(InfoText(probe));           // 6 Инфо
                item.Tag = entry;
                Items.Add(item);
                UpdateEntry(entry);
            }
            Renumber();
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
            Renumber();
            if (ListChanged != null) ListChanged();
        }

        public void ClearAll()
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var e = (FileEntry)Items[i].Tag;
                if (e.Status != FileStatus.Working) Items.RemoveAt(i);
            }
            Renumber();
            if (ListChanged != null) ListChanged();
        }

        public void UpdateEntry(FileEntry e)
        {
            foreach (ListViewItem it in Items)
            {
                if (it.Tag != e) continue;
                string icon, detail;
                StatusParts(e, out icon, out detail);
                it.SubItems[1].Text = icon;
                it.SubItems[2].Text = detail;
                it.ToolTipText = e.Status == FileStatus.Done
                    ? "Нажмите на значок папки, чтобы открыть папку с файлом"
                    : e.Status == FileStatus.Error ? "ошибка: " + e.Note : detail;
                Invalidate(it.Bounds);   // форсируем полную перерисовку строки
                return;
            }
        }

        // Краткая инфо-строка о дорожках (как в XXL): «N дор.: codec/Nch».
        static string InfoText(ProbeInfo p)
        {
            if (p.Audio.Count == 0)
                return p.HasVideo ? "видео без звука" : "нет медиа";
            var parts = new List<string>();
            foreach (var a in p.Audio)
                parts.Add(a.Codec + "/" + Ch(a.Channels));
            string s = p.Audio.Count + " дор.: " + string.Join(", ", parts.ToArray());
            if (p.HasVideo) s = "видео + " + s;
            return s;
        }

        static string Ch(int n)
        {
            return n == 1 ? "моно" : n == 2 ? "2ch" : n + "ch";
        }

        public static string FmtSize(long bytes)
        {
            if (bytes <= 0) return "";
            double mb = bytes / 1048576.0;
            return mb >= 1000
                ? (mb / 1024).ToString("0.0") + " ГБ"
                : mb.ToString("0.#") + " МБ";
        }

        public void RefreshTheme()
        {
            foreach (ListViewItem it in Items)
                UpdateEntry((FileEntry)it.Tag);
        }

        // Значок (колонка 1) + текст статуса/параметры (колонка 2).
        static void StatusParts(FileEntry e, out string icon, out string detail)
        {
            switch (e.Status)
            {
                case FileStatus.Queued:
                    icon = "⏸"; detail = e.Job.Summary(); break;
                case FileStatus.Working:
                    icon = "►"; detail = "в работе..."; break;
                case FileStatus.Done:
                    icon = "📂";
                    detail = "✓ " + (e.Job != null ? e.Job.Summary() : "готово")
                        + "  (" + FmtElapsed(e.DoneSeconds) + ")";
                    break;
                case FileStatus.Error:
                    icon = "✗"; detail = "ошибка: " + e.Note; break;
                case FileStatus.Skipped:
                    icon = "⏭"; detail = e.Note; break;
                default:
                    icon = "[...]"; detail = "в ожидании настроек"; break;
            }
        }

        // Клик по значку папки готового файла — открыть папку с ним.
        void OnMouseClickList(object sender, MouseEventArgs e)
        {
            var hit = HitTest(e.Location);
            if (hit.Item == null) return;
            var entry = (FileEntry)hit.Item.Tag;
            if (entry.Status != FileStatus.Done) return;
            if (hit.Item.SubItems.Count < 2
                || hit.SubItem != hit.Item.SubItems[1]) return;  // колонка значка
            string path = entry.OutputPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe",
                    "/select,\"" + path + "\"");
            }
            catch { }
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
            Renumber();
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
