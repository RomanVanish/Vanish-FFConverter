using System;
using System.Drawing;
using System.Windows.Forms;

namespace VanishFF
{
    class MainForm : Form
    {
        FlowLayoutPanel tabsFlow;
        Button themeBtn;
        Panel content;
        Panel statusBar;
        Label statusLabel;
        Label overallLabel;
        Button logToggle;
        Panel logDrawer;
        LogBox logConsole;
        ThinProgress progress;
        bool logOpen;
        int contentFixedWidth;
        string logFilePath;
        string summaryPath;
        string lastOverallText = L.StatusIdle;
        int lastOverallKind;
        const int drawerWidth = 620;

        Button[] tabButtons;
        Panel[] pages;
        int activeTab;
        QueueManager queueMgr;
        TabAudio tabAudio;
        bool hadSavedSize;
        bool sizedOnce;

        public MainForm()
        {
            Text = L.AppTitle;
            Font = new Font("Segoe UI", 10.5f);
            AutoScaleMode = AutoScaleMode.Font;
            try { Icon = new Icon(System.IO.Path.Combine(Program.AppDir, "V.ico")); }
            catch { }

            // геометрия из настроек (ТЗ 2.1). Сохранённый размер уже в
            // пикселях экрана; размеры по умолчанию масштабируются по DPI
            // в OnShown (иначе при 125% окно мало и всё обрезается).
            hadSavedSize = Settings.GetI("win_w", -1) != -1;
            int x = Settings.GetI("win_x", int.MinValue);
            int y = Settings.GetI("win_y", int.MinValue);
            if (hadSavedSize)
                Size = new Size(Settings.GetI("win_w", 1060),
                                Settings.GetI("win_h", 860));
            if (x != int.MinValue && y != int.MinValue &&
                Screen.FromPoint(new Point(x, y)).WorkingArea.Contains(x, y))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(x, y);
            }
            else StartPosition = FormStartPosition.CenterScreen;
            if (Settings.GetB("win_max", false))
                WindowState = FormWindowState.Maximized;

            BuildLayout();

            // глобальная очередь (ТЗ 2.4) + первая рабочая вкладка
            queueMgr = new QueueManager(this);
            QueueManager.I = queueMgr;
            queueMgr.Status = SetStatus;
            tabAudio = new TabAudio(queueMgr);
            tabAudio.SetStatusBar = SetStatus;
            tabAudio.LogSink = AppendLog;
            tabAudio.ProgressSink = SetProgress;
            tabAudio.OverallSink = SetOverall;
            tabAudio.ShowLog = OpenLog;
            tabAudio.NewLog = StartNewLog;
            tabAudio.SummarySink = WriteSummary;
            tabAudio.ShowSummary = OpenSummary;
            tabAudio.ShowLogsFolder = OpenLogsFolder;
            tabAudio.EnsureHeight = EnsureTabHeight;
            pages[0].Controls.Clear();
            pages[0].Controls.Add(tabAudio);

            ApplyTheme();
            SelectTab(Settings.GetI("active_tab", 0));

            FormClosing += OnClosing;
        }

        void BuildLayout()
        {
            // строка вкладок + кнопка темы
            var header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 46;

            themeBtn = new Button();
            themeBtn.Dock = DockStyle.Right;
            themeBtn.Width = 46;
            themeBtn.Font = new Font("Segoe UI", 12f);
            themeBtn.TabStop = false;
            themeBtn.Click += delegate { ToggleTheme(); };
            Tips.Set(themeBtn, L.ThemeToggleTip);

            tabsFlow = new FlowLayoutPanel();
            tabsFlow.Dock = DockStyle.Fill;
            tabsFlow.WrapContents = false;
            tabsFlow.Padding = new Padding(6, 0, 0, 0);
            tabsFlow.Paint += PaintTabUnderline;

            header.Controls.Add(tabsFlow);
            header.Controls.Add(themeBtn);

            // нижняя область: верхняя строка — жёлтый детальный статус
            // «Файл N из M · имя (0:42): этап»; нижняя — общий статус
            // «Готово (3:05)» / «▶ 1:23» + кнопка «Консоль».
            statusBar = new Panel();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 56;
            statusBar.Tag = "panel";
            statusBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Current.Border))
                    e.Graphics.DrawLine(pen, 0, 0, statusBar.Width, 0);
            };

            statusLabel = new Label();   // жёлтая жирная детальная строка
            statusLabel.Dock = DockStyle.Top;
            statusLabel.Height = 28;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(12, 0, 0, 0);
            statusLabel.Font = new Font("Segoe UI Semibold", 11f);
            statusLabel.Tag = "status";
            statusLabel.Text = "";

            var botRow = new Panel();
            botRow.Dock = DockStyle.Fill;
            botRow.Tag = "panel";

            logToggle = new Button();
            logToggle.Dock = DockStyle.Right;
            logToggle.Width = 120;
            logToggle.FlatStyle = FlatStyle.Flat;
            logToggle.TabStop = false;
            logToggle.Text = "Консоль »";
            logToggle.Click += delegate { ToggleLog(); };
            Tips.Set(logToggle, "Показать/скрыть консоль (выезжает справа).");

            overallLabel = new Label();   // общий статус программы (жирный)
            overallLabel.Dock = DockStyle.Fill;
            overallLabel.TextAlign = ContentAlignment.MiddleLeft;
            overallLabel.Padding = new Padding(12, 0, 0, 0);
            overallLabel.Font = new Font("Segoe UI Semibold", 10.5f);
            overallLabel.Text = L.StatusIdle;

            botRow.Controls.Add(overallLabel);
            botRow.Controls.Add(logToggle);
            statusBar.Controls.Add(botRow);
            statusBar.Controls.Add(statusLabel);

            // выезжающая панель консоли справа (модель Rufus)
            logDrawer = new Panel();
            logDrawer.Dock = DockStyle.Right;
            logDrawer.Width = drawerWidth;
            logDrawer.Visible = false;
            logDrawer.Tag = "panel";
            var logHead = new Panel();
            logHead.Dock = DockStyle.Top;
            logHead.Height = 34;
            var logTitle = new Label();
            logTitle.Text = "Консоль";
            logTitle.AutoSize = true;
            logTitle.Location = new Point(8, 8);
            logTitle.Font = new Font("Segoe UI Semibold", 10.5f);
            var logClear = new Button();
            logClear.Text = "Очистить";
            logClear.Dock = DockStyle.Right;
            logClear.Width = 92;
            logClear.FlatStyle = FlatStyle.Flat;
            logClear.TabStop = false;
            logClear.Click += delegate { logConsole.Clear(); };
            var logFileBtn = new Button();
            logFileBtn.Text = "Файл";
            logFileBtn.Dock = DockStyle.Right;
            logFileBtn.Width = 64;
            logFileBtn.FlatStyle = FlatStyle.Flat;
            logFileBtn.TabStop = false;
            logFileBtn.Click += delegate { OpenLogFile(); };
            Tips.Set(logFileBtn, "Открыть полный лог-файл этой сессии.");
            logHead.Controls.Add(logTitle);
            logHead.Controls.Add(logFileBtn);
            logHead.Controls.Add(logClear);
            logConsole = new LogBox();
            logConsole.Dock = DockStyle.Fill;
            logConsole.Tag = "console";
            var lmono = new Font("Cascadia Mono", 9.5f);
            if (lmono.Name != "Cascadia Mono") lmono = new Font("Consolas", 9.5f);
            logConsole.Font = lmono;
            var logInner = new Panel();
            logInner.Dock = DockStyle.Fill;
            logInner.Padding = new Padding(1, 2, 0, 0);  // левая граница + малый зазор
            logInner.Tag = "border";
            logInner.Controls.Add(logConsole);

            // прогресс текущего файла — зелёная полоса с % под консолью
            progress = new ThinProgress();
            progress.Dock = DockStyle.Bottom;
            progress.Height = 24;
            progress.ShowText = true;
            progress.Visible = false;

            logDrawer.Controls.Add(progress);   // Bottom
            logDrawer.Controls.Add(logHead);    // Top
            logDrawer.Controls.Add(logInner);   // Fill (последним = внутренний)

            content = new Panel();
            content.Dock = DockStyle.Fill;

            // вкладки-страницы; пока заглушки, наполняются по фазам (ТЗ 9)
            string[] names = { L.TabAudio, L.TabVideo, L.TabRemux,
                               L.TabCut, L.TabInspect, L.TabTools };
            string[] phases = { L.StubPhase1, L.StubPhase2, L.StubPhase1,
                                L.StubPhase2, L.StubPhase1, L.StubPhase3 };
            tabButtons = new Button[names.Length];
            pages = new Panel[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var b = new Button();
                b.Text = names[i];
                b.Font = new Font("Segoe UI Semibold", 11f);
                b.AutoSize = true;
                b.Margin = new Padding(2, 8, 2, 6);
                b.Padding = new Padding(6, 1, 6, 1);
                b.TabStop = false;
                int idx = i;
                b.Click += delegate { SelectTab(idx); };
                tabsFlow.Controls.Add(b);
                tabButtons[i] = b;

                var page = new Panel();
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                var stub = new Label();
                stub.Dock = DockStyle.Fill;
                stub.TextAlign = ContentAlignment.MiddleCenter;
                stub.Font = new Font("Segoe UI Semibold", 12.5f);
                stub.Tag = "dim";
                stub.Text = string.Format(L.StubPhase, names[i], phases[i]);
                page.Controls.Add(stub);
                pages[i] = page;
                content.Controls.Add(page);
            }

            Controls.Add(content);     // Fill — центр
            Controls.Add(logDrawer);   // Right — выезжает
            Controls.Add(statusBar);   // Bottom
            Controls.Add(header);      // Top
            content.BringToFront();
        }

        void ToggleLog()
        {
            SetLogOpen(!logOpen);
        }

        public void OpenLog()
        {
            if (!logDrawer.Visible) SetLogOpen(true);
        }

        void SetLogOpen(bool open)
        {
            if (logDrawer.Visible == open) return;   // источник правды — панель
            logOpen = open;
            logToggle.Text = open ? "Консоль «" : "Консоль »";

            if (open)
            {
                // основное окно фиксируется по текущей ширине, консоль
                // забирает остаток — при расширении окна растёт консоль
                contentFixedWidth = content.Width;
                content.Dock = DockStyle.Left;
                content.Width = contentFixedWidth;
                logDrawer.Visible = true;
                logDrawer.Dock = DockStyle.Fill;
                logDrawer.BringToFront();
                if (WindowState == FormWindowState.Normal)
                {
                    var wa = Screen.FromControl(this).WorkingArea;
                    Width += drawerWidth;
                    if (Right > wa.Right)
                        Left = Math.Max(wa.Left, wa.Right - Width);
                }
            }
            else
            {
                int cw = logDrawer.Width;
                logDrawer.Visible = false;
                content.Dock = DockStyle.Fill;
                if (WindowState == FormWindowState.Normal)
                    Width -= cw;
            }
        }

        public void AppendLog(string line)
        {
            logConsole.AppendLine(line);
            WriteLogFile(line);
        }

        void WriteLogFile(string line)
        {
            try
            {
                if (logFilePath == null)
                {
                    string dir = System.IO.Path.Combine(Program.AppDir, "logs");
                    System.IO.Directory.CreateDirectory(dir);
                    logFilePath = System.IO.Path.Combine(dir,
                        DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
                        + "_console.log");
                }
                System.IO.File.AppendAllText(logFilePath, line + "\r\n");
            }
            catch { }
        }

        void OpenLogFile()
        {
            if (logFilePath != null && System.IO.File.Exists(logFilePath))
            {
                try { System.Diagnostics.Process.Start(logFilePath); }
                catch { }
            }
            else
                ThemedDialog.Info(this,
                    "Лог-файл появится, когда пойдёт обработка.");
        }

        void OpenLogsFolder()
        {
            try
            {
                string dir = System.IO.Path.Combine(Program.AppDir, "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(dir);
            }
            catch { }
        }

        // Новый цикл: консоль и полный лог-файл начинаются заново.
        public void StartNewLog()
        {
            logConsole.Clear();
            logFilePath = null;
        }

        public void WriteSummary(string text)
        {
            try
            {
                string dir = System.IO.Path.Combine(Program.AppDir, "logs");
                System.IO.Directory.CreateDirectory(dir);
                summaryPath = System.IO.Path.Combine(dir,
                    DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
                    + "_summary.txt");
                System.IO.File.WriteAllText(summaryPath, text);
            }
            catch { }
        }

        public void OpenSummary()
        {
            if (summaryPath != null && System.IO.File.Exists(summaryPath))
            {
                try { System.Diagnostics.Process.Start(summaryPath); }
                catch { }
            }
            else
                ThemedDialog.Info(this,
                    "Краткий итог появится после завершения обработки.");
        }

        public void SetProgress(int pct)
        {
            progress.Visible = pct > 0 && pct < 100;
            progress.Value = pct;
        }

        // kind: 0 простой · 1 работа (фиолетовый) · 2 готово (зелёный)
        //       3 готово с ошибками (красный)
        public void SetOverall(string text, int kind)
        {
            lastOverallText = text;
            lastOverallKind = kind;
            ApplyOverallColor();
        }

        void ApplyOverallColor()
        {
            overallLabel.Text = lastOverallText;
            Palette p = Theme.Current;
            overallLabel.ForeColor =
                lastOverallKind == 1 ? p.StWorking :
                lastOverallKind == 2 ? p.StDone :
                lastOverallKind == 3 ? p.StError : p.TextDim;
        }

        void SelectTab(int idx)
        {
            if (idx < 0 || idx >= pages.Length) idx = 0;
            activeTab = idx;
            for (int i = 0; i < pages.Length; i++)
                pages[i].Visible = (i == idx);
            StyleTabs();
            tabsFlow.Invalidate();
        }

        // активная вкладка: текст акцентом + терракотовая полоса снизу
        void PaintTabUnderline(object sender, PaintEventArgs e)
        {
            var b = tabButtons[activeTab];
            using (var br = new SolidBrush(Theme.Current.Accent))
                e.Graphics.FillRectangle(br, b.Left, b.Bottom + 1, b.Width, 3);
        }

        void StyleTabs()
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                var b = tabButtons[i];
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Theme.Current.WindowBg;
                b.ForeColor = (i == activeTab)
                    ? Theme.Current.Accent : Theme.Current.TextDim;
                b.FlatAppearance.MouseOverBackColor = Theme.Current.PanelBg;
                b.FlatAppearance.MouseDownBackColor = Theme.Current.PanelBg;
            }
        }

        void ToggleTheme()
        {
            Theme.Current = Theme.Current.IsDark ? Theme.Light : Theme.Dark;
            Settings.Set("theme_dark", Theme.Current.IsDark);
            ApplyTheme();
        }

        void ApplyTheme()
        {
            Theme.Apply(this);
            StyleTabs();
            themeBtn.Text = Theme.Current.IsDark ? "☀" : "☾";
            themeBtn.FlatAppearance.BorderSize = 0;
            themeBtn.BackColor = Theme.Current.WindowBg;
            themeBtn.ForeColor = Theme.Current.TextDim;
            Theme.ApplyTitleBar(this);
            if (tabAudio != null) tabAudio.RefreshTheme();
            ApplyOverallColor();   // не дать теме сбросить цвет статуса в серый
            Invalidate(true);
        }

        public void SetStatus(string text)
        {
            bool has = !string.IsNullOrEmpty(text);
            statusLabel.Text = text;
            statusLabel.Visible = has;
            // без детальной строки — полоса в одну строку (нет пустого места)
            statusBar.Height = has ? 56 : 32;
        }

        // Окно растёт вниз, если контенту вкладки не хватает высоты
        // (напр. opus-настройки выше mp3), а не сжимает список файлов.
        public void EnsureTabHeight(int needed)
        {
            if (WindowState != FormWindowState.Normal) return;
            int deficit = needed - content.Height;
            if (deficit > 8)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                Height = Math.Min(wa.Height, Height + deficit);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (sizedOnce) return;
            sizedOnce = true;
            // масштаб под фактический DPI монитора
            float f = DeviceDpi / 96f;
            MinimumSize = new Size((int)(940 * f), (int)(860 * f));
            if (!hadSavedSize && WindowState == FormWindowState.Normal)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                Size = new Size(
                    Math.Min((int)(1060 * f), wa.Width),
                    Math.Min((int)(940 * f), wa.Height));
                CenterToScreen();
            }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            var bounds = (WindowState == FormWindowState.Normal)
                ? Bounds : RestoreBounds;
            Settings.Set("win_w", bounds.Width);
            Settings.Set("win_h", bounds.Height);
            Settings.Set("win_x", bounds.X);
            Settings.Set("win_y", bounds.Y);
            Settings.Set("win_max", WindowState == FormWindowState.Maximized);
            Settings.Set("active_tab", activeTab);
            if (tabAudio != null) tabAudio.SaveSettings();
            Settings.Save();
        }
    }
}
