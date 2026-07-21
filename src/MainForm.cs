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

        Button[] tabButtons;
        Panel[] pages;
        int activeTab;
        QueueManager queueMgr;
        TabAudio tabAudio;

        public MainForm()
        {
            Text = L.AppTitle;
            Font = new Font("Segoe UI", 10.5f);
            MinimumSize = new Size(900, 600);
            try { Icon = new Icon(System.IO.Path.Combine(Program.AppDir, "V.ico")); }
            catch { }

            // геометрия из настроек (ТЗ 2.1)
            int w = Settings.GetI("win_w", 1040), h = Settings.GetI("win_h", 700);
            int x = Settings.GetI("win_x", int.MinValue);
            int y = Settings.GetI("win_y", int.MinValue);
            Size = new Size(w, h);
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
            new ToolTip().SetToolTip(themeBtn, L.ThemeToggleTip);

            tabsFlow = new FlowLayoutPanel();
            tabsFlow.Dock = DockStyle.Fill;
            tabsFlow.WrapContents = false;
            tabsFlow.Padding = new Padding(6, 0, 0, 0);
            tabsFlow.Paint += PaintTabUnderline;

            header.Controls.Add(tabsFlow);
            header.Controls.Add(themeBtn);

            // статусная полоса (ТЗ 2.1)
            statusBar = new Panel();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 28;
            statusBar.Tag = "panel";
            statusBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Current.Border))
                    e.Graphics.DrawLine(pen, 0, 0, statusBar.Width, 0);
            };
            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(10, 5);
            statusLabel.Tag = "dim";
            statusLabel.Text = L.StatusIdle;
            statusBar.Controls.Add(statusLabel);

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

            Controls.Add(content);
            Controls.Add(statusBar);
            Controls.Add(header);
            content.BringToFront();
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
            Invalidate(true);
        }

        public void SetStatus(string text)
        {
            statusLabel.Text = text;
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
