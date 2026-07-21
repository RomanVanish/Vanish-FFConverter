using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VanishFF
{
    // Вкладка «Аудио» (ТЗ 3): сжатие/конвертация звука.
    class TabAudio : UserControl
    {
        readonly QueueManager queue;

        FileListView files;
        ComboBox fmt, quality, channels, lufs, sampleRate;
        CheckBox normalize, overwrite, verbose;
        Label lufsLabel;
        TextBox nameTpl, outDir, extraArgs;
        RadioButton outNear, outToDir;
        Panel outWrap;
        Button bBrowse;
        Button bQueue, bQueueAll, bStart, bStop, bStopMenu;
        Label suffix;
        Expander adv;
        Timer timer;
        DateTime? t0, tFile;
        ContextMenuStrip stopMenu;
        ToolStripMenuItem miPause;

        // вывод наружу (общий лог/прогресс/время в MainForm — модель Rufus)
        public Action<string> LogSink;
        public Action<int> ProgressSink;
        public Action<string, int> OverallSink;   // общий статус внизу окна
        public Action ShowLog;
        public Action NewLog;                // очистить консоль/начать новый файл
        public Action<string> SummarySink;   // записать краткий итог
        public Action ShowSummary;           // открыть краткий итог
        public Action ShowLogsFolder;        // открыть папку логов

        void Log(string line) { if (LogSink != null) LogSink(line); }
        void Overall(string s, int kind)
        { if (OverallSink != null) OverallSink(s, kind); }

        string curStage;   // текущий этап («кодирование» и т.п.)
        int runTotal, runDone;   // позиция «Файл N из M» в текущем прогоне
        string lastTotal;  // время последнего завершённого прогона

        // дорожки
        FlowLayoutPanel tracksPanel;
        Panel stereoPanel;
        ComboBox leftEar, rightEar;
        readonly List<CheckBox> trackChecks = new List<CheckBox>();

        // расширенный режим
        Panel pOpus, pMp3, pAac, pFlac, pWav;
        TrackBar opusBitrate;
        Label opusBrLabel;
        RadioButton opusAuto, opusVoip;
        CheckBox opusVbr;
        RadioButton mp3Cbr, mp3Vbr, aacCbr, aacVbr;
        Label mp3ValLbl, aacValLbl;
        ComboBox mp3Val, aacVal;
        bool mp3IsVbr, aacIsVbr;
        int mp3BrIdx = 2, mp3QIdx = 1, aacBrIdx = 3, aacQIdx = 2;
        static readonly string[] Mp3Br = { "64","96","128","160","192","256","320" };
        static readonly string[] Mp3Q = { "V0 — лучшее","V2","V4 — среднее","V6","V9 — худшее" };
        static readonly string[] AacBr = { "48","64","96","128","192","256","320" };
        static readonly string[] AacQ = { "1 — хуже/меньше","2","3 — среднее","4","5 — лучше/больше" };
        ComboBox flacLevel, flacBits, wavBits;

        TableLayoutPanel grid;
        public Action<int> EnsureHeight;   // просьба к окну вырасти под контент
        bool syncing; // защита от рекурсии пресет<->ползунки

        static readonly string[] PresetKeys = { "compact", "standard", "high", "music", "custom" };
        // столбцы: opus / mp3 / aac
        static readonly int[,] PresetBr = {
            {  48,  96,  64 },   // компакт
            {  64, 128,  96 },   // стандарт
            {  96, 192, 128 },   // высокое
            { 256, 320, 256 } }; // максимум

        public TabAudio(QueueManager q)
        {
            queue = q;
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            BuildUi();
            LoadSettings();
            WireQueue();
            UpdateFormatUi();
            UpdateQueueButton();
        }

        // ── интерфейс ────────────────────────────────────────────────

        void BuildUi()
        {
            // Ряды: 0 заголовок | 1 список | 2 кнопки списка | 3 разделитель
            // 4 настройки | 5 расширенно | 6 разделитель | 7 имя/папка
            // 8 разделитель | 9 кнопки запуска | 10 консоль
            grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.Padding = new Padding(12, 8, 12, 6);
            for (int i = 0; i < 10; i++)
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // консоли на вкладке больше нет (общий лог справа), поэтому
            // «резиновый» — список: растёт с окном, остальное фиксировано
            grid.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
            Controls.Add(grid);

            // 0: заголовок списка + ⓘ
            var head = Row();
            head.Margin = new Padding(0, 0, 0, 5);
            head.Controls.Add(MkLbl("Файлы (любой аудио/видео формат; из видео берётся звук):"));
            head.Controls.Add(Tips.Icon(
                "Значки статуса:\n" +
                "  [...]  ждёт настроек     ⏸  в очереди\n" +
                "  ►  в работе              📂  готово (клик — открыть папку)\n" +
                "  ✗  ошибка                ⏭  пропущено\n" +
                "\n" +
                "Файлы обрабатываются в порядке списка;\n" +
                "строки можно перетаскивать мышью.\n" +
                "Добавить: кнопкой или перетаскиванием в окно."));
            grid.Controls.Add(head, 0, 0);

            // 1: список
            files = new FileListView();
            files.Dock = DockStyle.Fill;
            files.Margin = new Padding(0);
            files.FilesDropped = delegate(string[] paths) { AddFiles(paths); };
            files.SelectionChangedCb = delegate
            {
                UpdateQueueButton();
                RebuildTracks();   // дорожки — по выбранному файлу
            };
            files.ListChanged = delegate { OnListChanged(); };
            var filesWrap = Bordered.Wrap(files);
            filesWrap.MinimumSize = new Size(0, 120);
            grid.Controls.Add(filesWrap, 0, 1);

            // контекстное меню по правому клику
            var fileMenu = new ContextMenuStrip();
            var miRunOne = new ToolStripMenuItem("Запустить только это задание", null,
                delegate { RunSelectedOnly(); });
            var miCancel = new ToolStripMenuItem("Отменить задание", null,
                delegate { CancelSelectedJob(); });
            var miRemove = new ToolStripMenuItem("Удалить из списка", null,
                delegate { files.RemoveSelected(); });
            var miOpen = new ToolStripMenuItem("Открыть папку с результатом", null,
                delegate { OpenResultFolder(); });
            fileMenu.Items.Add(miRunOne);
            fileMenu.Items.Add(miCancel);
            fileMenu.Items.Add(miOpen);
            fileMenu.Items.Add(new ToolStripSeparator());
            fileMenu.Items.Add(miRemove);
            fileMenu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                var en = files.SelectedEntry;
                if (en == null) { e.Cancel = true; return; }
                // запустить только это — когда очередь не работает и файл не в работе
                miRunOne.Enabled = !queue.Running && en.Status != FileStatus.Working;
                miCancel.Enabled = en.Status == FileStatus.Queued;
                miOpen.Visible = en.Status == FileStatus.Done
                    && !string.IsNullOrEmpty(en.OutputPath);
                miRemove.Enabled = en.Status != FileStatus.Working;
            };
            files.ContextMenuStrip = fileMenu;

            // 2: кнопки списка
            var fRow = Row();
            fRow.Margin = new Padding(0, 5, 0, 0);
            fRow.Controls.Add(MkBtn("Добавить файлы...", delegate { PickFiles(); }));
            fRow.Controls.Add(MkBtn("Удалить выбранное", delegate { files.RemoveSelected(); }));
            fRow.Controls.Add(MkBtn("Очистить всё", delegate { files.ClearAll(); }));
            grid.Controls.Add(fRow, 0, 2);

            // 3: разделитель
            grid.Controls.Add(Sep(), 0, 3);

            // 4: настройки — формат/качество, каналы/нормализация, дорожки
            var sPanel = new FlowLayoutPanel();
            sPanel.AutoSize = true;
            sPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            sPanel.FlowDirection = FlowDirection.TopDown;
            sPanel.WrapContents = false;
            sPanel.Margin = new Padding(0);

            var row1 = Row();
            row1.Controls.Add(MkLbl("Формат:"));
            fmt = MkCombo(new string[] { "opus", "mp3", "aac", "flac", "wav" }, 84);
            fmt.SelectedIndexChanged += delegate { UpdateFormatUi(); ApplyPreset(); };
            row1.Controls.Add(fmt);
            row1.Controls.Add(MkLbl("     Качество:"));
            quality = MkCombo(new string[] {
                "Компактный — голос (48-opus / 64-aac / 96-mp3)",
                "Стандарт — голос (64-opus / 96-aac / 128-mp3)",
                "Высокое — голос (96-opus / 128-aac / 192-mp3)",
                "Максимум — музыка (256-opus / 256-aac / 320-mp3)",
                "Свой (настройки в «Расширенные»)" }, 300);
            quality.DropDownWidth = 470;   // выпадающий список шире закрытого бокса
            quality.SelectedIndexChanged += delegate { OnPresetChanged(); };
            row1.Controls.Add(quality);
            row1.Controls.Add(Tips.Icon(
                "Битрейт по кодекам (opus / aac / mp3) и размер часа (opus):\n" +
                "  Компактный — голос:  48 / 64 / 96k    ≈ 21 МБ/час\n" +
                "  Стандарт — голос:    64 / 96 / 128k   ≈ 28 МБ/час\n" +
                "  Высокое — голос:     96 / 128 / 192k  ≈ 42 МБ/час\n" +
                "  Максимум — музыка:  256 / 256 / 320k ≈ 113 МБ/час\n" +
                "\n" +
                "Пресеты «голос» подобраны под речь; для музыки — «Максимум».\n" +
                "flac/wav — без потерь (пресетов нет, настройки в «Расширенные»)."));
            sPanel.Controls.Add(row1);

            var row2 = Row();
            row2.Margin = new Padding(0, 6, 0, 2);
            row2.Controls.Add(MkLbl("Каналы:"));
            channels = MkCombo(new string[] {
                "Как в исходнике",
                "Моно (сведение)",
                "Стерео: голоса по ушам" }, 210);
            channels.SelectedIndexChanged += delegate { UpdateTracksUi(); };
            row2.Controls.Add(channels);
            row2.Controls.Add(Tips.Icon(
                "Как в исходнике — только смена формата/качества.\n" +
                "    Несколько отмеченных дорожек -> каждая отдельным файлом.\n" +
                "Моно (сведение) — отмеченные дорожки смешиваются в одну.\n" +
                "    Стандарт для переслушивания: минимальный размер.\n" +
                "Стерео: голоса по ушам — для мультитрека (OBS): одна дорожка\n" +
                "    в левое ухо, другая в правое — удобно разбирать, кто что сказал."));
            // нормализация + цель — в общей рамке, чтобы читались как пара
            var normBox = new FlowLayoutPanel();
            normBox.Tag = "group";
            normBox.AutoSize = true;
            normBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            normBox.WrapContents = false;
            normBox.Padding = new Padding(8, 2, 8, 3);
            normBox.Margin = new Padding(20, 0, 0, 0);
            normBox.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Current.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0,
                        normBox.Width - 1, normBox.Height - 1);
            };
            normalize = new CheckBox();
            normalize.Text = "Нормализация громкости";
            normalize.AutoSize = true;
            normalize.Checked = true;
            normalize.Margin = new Padding(0, 5, 0, 0);
            normalize.CheckedChanged += delegate { UpdateNormUi(); };
            normBox.Controls.Add(normalize);
            lufsLabel = MkLbl("   Цель:");
            normBox.Controls.Add(lufsLabel);
            lufs = MkCombo(new string[] {
                "−14 LUFS — громче, стриминг",
                "−16 LUFS — речь, подкасты",
                "−18 LUFS — тише, аудиокниги",
                "−23 LUFS — вещательный стандарт" }, 232);
            lufs.DropDownWidth = 320;   // полный текст в выпадающем списке
            lufs.SelectedIndex = 1;
            normBox.Controls.Add(lufs);
            normBox.Controls.Add(Tips.Icon(
                "Выравнивание громкости по стандарту EBU R128\n" +
                "(как YouTube и подкаст-платформы): тихое подтягивается,\n" +
                "громкое приглушается. В мультитреке каждая дорожка\n" +
                "выравнивается ОТДЕЛЬНО до сведения.\n" +
                "Отключайте, только если нужна точная копия звука."));
            row2.Controls.Add(normBox);
            sPanel.Controls.Add(row2);

            // дорожки (виджет прячется для одной дорожки, ТЗ 3.4);
            // отодвинуты сверху — это отдельная настройка
            tracksPanel = Row();
            tracksPanel.WrapContents = true;
            tracksPanel.Margin = new Padding(0, 10, 0, 2);
            tracksPanel.Visible = false;
            sPanel.Controls.Add(tracksPanel);

            stereoPanel = Row();
            stereoPanel.Margin = new Padding(0, 10, 0, 2);
            stereoPanel.Controls.Add(MkLbl("Левое ухо:"));
            leftEar = MkCombo(new string[0], 220);
            stereoPanel.Controls.Add(leftEar);
            stereoPanel.Controls.Add(MkLbl("     Правое ухо:"));
            rightEar = MkCombo(new string[0], 220);
            stereoPanel.Controls.Add(rightEar);
            stereoPanel.Visible = false;
            sPanel.Controls.Add(stereoPanel);

            grid.Controls.Add(sPanel, 0, 4);

            // 5: расширенно
            adv = new Expander("Расширенные настройки");
            adv.Margin = new Padding(0, 6, 0, 0);
            BuildAdvanced();
            grid.Controls.Add(adv, 0, 5);

            // 6: разделитель
            grid.Controls.Add(Sep(), 0, 6);

            // 7: имя файла + папка вывода (один блок)
            var ioPanel = new FlowLayoutPanel();
            ioPanel.AutoSize = true;
            ioPanel.FlowDirection = FlowDirection.TopDown;
            ioPanel.WrapContents = false;
            ioPanel.Margin = new Padding(0);

            var nRow = Row();
            nRow.Controls.Add(MkLbl("Имя файла:"));
            nameTpl = new TextBox();
            nameTpl.Width = 320;
            nameTpl.Text = "{имя}";
            nRow.Controls.Add(Bordered.Wrap(nameTpl));
            suffix = MkLbl(" + .opus");
            nRow.Controls.Add(suffix);
            nRow.Controls.Add(Tips.Icon(
                "Шаблон имени результата. Теги (русские или английские):\n" +
                "  {имя}/{name}           — имя исходника без расширения\n" +
                "  {дата}/{date}          — дата файла, ГГГГ-ММ-ДД\n" +
                "  {время_чм}/{time_hm}   — время файла, ЧЧ-ММ\n" +
                "  {время_чмс}/{time_hms}  — время файла, ЧЧ-ММ-СС\n" +
                "  {сегодня}/{today}      — сегодняшняя дата\n" +
                "  {#} {##} {###}          — номер в пачке (7 / 07 / 007)\n" +
                "  {дорожка}/{track}      — номер дорожки (мультитрек)\n" +
                "\n" +
                "Если результат совпал бы с исходником — файл пропускается,\n" +
                "исходник не затирается."));
            ioPanel.Controls.Add(nRow);

            var oRow = Row();
            oRow.Margin = new Padding(0, 6, 0, 2);
            oRow.Controls.Add(MkLbl("Вывод:"));
            outNear = new RadioButton();
            outNear.Text = "Рядом с исходником";
            outNear.AutoSize = true;
            outNear.Checked = true;
            outNear.Margin = new Padding(0, 5, 8, 0);
            outNear.CheckedChanged += delegate { UpdateOutputUi(); };
            oRow.Controls.Add(outNear);
            outToDir = new RadioButton();
            outToDir.Text = "В папку:";
            outToDir.AutoSize = true;
            outToDir.Margin = new Padding(8, 5, 4, 0);
            oRow.Controls.Add(outToDir);
            outDir = new TextBox();
            outDir.Width = 300;
            outWrap = Bordered.Wrap(outDir);
            oRow.Controls.Add(outWrap);
            bBrowse = MkBtn("Обзор...", delegate
            {
                using (var d = new FolderBrowserDialog())
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        outToDir.Checked = true;
                        outDir.Text = d.SelectedPath;
                    }
            });
            oRow.Controls.Add(bBrowse);
            overwrite = new CheckBox();
            overwrite.Text = "Перезаписывать готовые";
            overwrite.AutoSize = true;
            overwrite.Margin = new Padding(16, 5, 0, 0);
            Tips.Set(overwrite,
                "Выключено: файлы, для которых результат уже создан,\n" +
                "пропускаются — удобно перезапускать прерванную очередь.\n" +
                "Включено: конвертировать заново и перезаписывать.");
            oRow.Controls.Add(overwrite);
            ioPanel.Controls.Add(oRow);
            grid.Controls.Add(ioPanel, 0, 7);

            // 8: разделитель
            grid.Controls.Add(Sep(), 0, 8);

            // 9: кнопки — две строки (очередь сверху, запуск снизу).
            // runBox — обычная панель во всю ширину, чтобы «Папка логов»
            // прижималась к правому краю.
            var runBox = new Panel();
            runBox.Dock = DockStyle.Top;
            runBox.Height = 74;
            var rRow = Row();     // строка управления очередью
            var rRun = Row();     // строка запуска
            bQueue = MkBtn("В очередь", delegate { QueueSelected(); });
            Tips.Set(bQueue,
                "Назначить выбранному файлу задание со СНИМКОМ текущих\n" +
                "настроек (дальнейшее кручение настроек на него не влияет).\n" +
                "Обработка начнётся по кнопке «Старт».\n" +
                "Для файла в очереди кнопка превращается в «Отменить задание».");
            bQueueAll = MkBtn("В очередь: все", delegate { QueueAll(); });
            Tips.Set(bQueueAll,
                "Поставить задания всем ожидающим файлам списка.\n" +
                "Файлы в очереди/в работе/готовые пропускаются.");
            bStart = MkBtn("СТАРТ", delegate { StartQueue(); });
            bStart.Tag = "accent";
            bStart.Width = 130;
            bStart.Height = 34;
            bStart.AutoSize = false;
            Tips.Set(bStart,
                "Выполнить все задания очереди по порядку списка.\n" +
                "Если заданий нет — предложит поставить все файлы\n" +
                "с текущими настройками.");
            bStop = MkBtn("Стоп", delegate { queue.StopNow(); });
            bStop.Enabled = false;
            stopMenu = new ContextMenuStrip();
            stopMenu.Items.Add("Пропустить текущее задание", null,
                delegate { queue.SkipCurrent(); });
            miPause = new ToolStripMenuItem("Пауза после текущего задания");
            miPause.CheckOnClick = true;
            miPause.CheckedChanged += delegate
            {
                queue.PauseAfter = miPause.Checked;
                UpdateStatusBar();
            };
            stopMenu.Items.Add(miPause);
            bStopMenu = MkBtn("▾", delegate
            {
                stopMenu.Show(bStopMenu, new Point(0, bStopMenu.Height));
            });
            bStopMenu.Width = 28;
            bStopMenu.AutoSize = false;
            bStopMenu.Height = 34;
            bStopMenu.Enabled = false;
            var bClearQueue = MkBtn("Очистить очередь", delegate { ClearQueue(); });
            Tips.Set(bClearQueue,
                "Снять задания со всех файлов в очереди (вернуть в «ожидание»).\n" +
                "Файл, который уже обрабатывается, не трогается — для него «Стоп».");
            var bSummary = MkBtn("Краткий лог", delegate
            {
                if (ShowSummary != null) ShowSummary();
            });
            Tips.Set(bSummary,
                "Открыть краткий итог последнего цикла: что сделано,\n" +
                "размеры, сжатие, ошибки. Полный лог — кнопка «Файл»\n" +
                "в шапке консоли.");
            verbose = new CheckBox();
            verbose.Text = "Подробный вывод ffmpeg";
            verbose.AutoSize = true;
            verbose.Margin = new Padding(14, 9, 0, 0);
            Tips.Set(verbose,
                "Вместо аккуратных процентов в лог идёт полный «живой»\n" +
                "вывод ffmpeg (версии, параметры, скорость). На результат\n" +
                "не влияет — чисто понаблюдать за процессом.");

            // строка 1: постановка/снятие очереди
            rRow.Controls.Add(bQueue);
            rRow.Controls.Add(bQueueAll);
            rRow.Controls.Add(bClearQueue);
            rRow.Dock = DockStyle.Top;
            // строка 2: запуск/остановка + лог; «Папка логов» прижата вправо
            rRun.Controls.Add(bStart);
            rRun.Controls.Add(bStop);
            rRun.Controls.Add(bStopMenu);
            rRun.Controls.Add(bSummary);
            rRun.Controls.Add(verbose);
            rRun.Dock = DockStyle.Left;

            var bLogsFolder = MkBtn("Папка логов", delegate
            {
                if (ShowLogsFolder != null) ShowLogsFolder();
            });
            Tips.Set(bLogsFolder, "Открыть папку logs (полные логи и итоги).");
            bLogsFolder.Dock = DockStyle.Right;
            bLogsFolder.Margin = new Padding(0);

            var runLine = new Panel();   // строка 2 во всю ширину
            runLine.Dock = DockStyle.Top;
            runLine.Height = 38;
            runLine.Controls.Add(rRun);
            runLine.Controls.Add(bLogsFolder);

            // порядок: сначала нижняя строка, потом верхняя (Dock=Top стопкой)
            runBox.Controls.Add(runLine);
            runBox.Controls.Add(rRow);
            grid.Controls.Add(runBox, 0, 9);

            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += delegate { Tick(); };
        }

        // Горизонтальный ряд без переноса — не разъезжается.
        static FlowLayoutPanel Row()
        {
            var f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.WrapContents = false;
            f.Margin = new Padding(0, 2, 0, 2);
            return f;
        }

        // Тонкий разделитель между блоками.
        static Panel Sep()
        {
            var p = new Panel();
            p.Tag = "border";
            p.Height = 1;
            p.Dock = DockStyle.Fill;
            p.Margin = new Padding(0, 13, 0, 13);
            return p;
        }

        void BuildAdvanced()
        {
            var box = new FlowLayoutPanel();
            box.FlowDirection = FlowDirection.TopDown;
            box.WrapContents = false;
            box.AutoSize = true;
            box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            box.Dock = DockStyle.Top;

            // opus — два ряда, чтобы не разъезжалось по ширине
            var opusBox = new FlowLayoutPanel();
            opusBox.FlowDirection = FlowDirection.TopDown;
            opusBox.WrapContents = false;
            opusBox.AutoSize = true;
            opusBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            opusBox.Margin = new Padding(0);

            var ro1 = Row();
            ro1.Controls.Add(MkLbl("Битрейт:"));
            opusBitrate = new TrackBar();
            opusBitrate.Minimum = 16;
            opusBitrate.Maximum = 320;
            opusBitrate.TickFrequency = 8;
            opusBitrate.SmallChange = 8;
            opusBitrate.LargeChange = 16;
            opusBitrate.Width = 240;
            opusBitrate.Value = 64;
            opusBitrate.ValueChanged += delegate
            {
                // шаг 8k: ползунок не должен ходить по 1 (ТЗ-обратная связь)
                int snapped = (int)(Math.Round(opusBitrate.Value / 8.0) * 8);
                if (snapped < opusBitrate.Minimum) snapped = opusBitrate.Minimum;
                if (snapped != opusBitrate.Value)
                {
                    opusBitrate.Value = snapped; // повторно вызовет обработчик
                    return;
                }
                opusBrLabel.Text = opusBitrate.Value + "k";
                OnManualChange();
            };
            ro1.Controls.Add(opusBitrate);
            opusBrLabel = MkLbl("64k");
            opusBrLabel.Font = new Font("Segoe UI Semibold", 10.5f);
            ro1.Controls.Add(opusBrLabel);
            opusVbr = new CheckBox();
            opusVbr.Text = "VBR";
            opusVbr.Checked = true;
            opusVbr.AutoSize = true;
            opusVbr.Margin = new Padding(20, 5, 0, 0);
            opusVbr.CheckedChanged += delegate { OnManualChange(); };
            ro1.Controls.Add(opusVbr);
            opusBox.Controls.Add(ro1);

            var ro2 = Row();
            ro2.Controls.Add(MkLbl("Режим:"));
            opusAuto = MkRadio("Авто — кодек сам распознаёт речь/музыку", true);
            opusVoip = MkRadio("Речь (voip)", false);
            ro2.Controls.Add(opusAuto);
            ro2.Controls.Add(opusVoip);
            ro2.Controls.Add(Tips.Icon(
                "Авто — libopus сам переключается между речью и музыкой\n" +
                "(рекомендуется почти всегда).\n" +
                "Речь (voip) — принудительный уклон в речь, для диктофона\n" +
                "на низком битрейте (16–32k)."));
            opusBox.Controls.Add(ro2);
            pOpus = opusBox;
            box.Controls.Add(opusBox);

            // mp3 — режим CBR/VBR; один combo меняет содержимое (битрейт/качество)
            var rm = Row();
            rm.Controls.Add(MkLbl("Режим:"));
            mp3Cbr = MkRadio("CBR", true);
            mp3Vbr = MkRadio("VBR", false);
            Tips.Set(mp3Cbr, "CBR — постоянный битрейт, предсказуемый размер.");
            Tips.Set(mp3Vbr, "VBR — постоянное качество (V0…V9), размер плавает;\n"
                + "обычно выгоднее CBR по размеру.");
            rm.Controls.Add(mp3Cbr);
            rm.Controls.Add(mp3Vbr);
            mp3ValLbl = MkLbl("     Битрейт:");
            rm.Controls.Add(mp3ValLbl);
            mp3Val = MkCombo(Mp3Br, 150);
            mp3Val.SelectedIndex = mp3BrIdx;
            mp3Val.SelectedIndexChanged += delegate { OnCodecValChanged(false); };
            rm.Controls.Add(mp3Val);
            mp3Cbr.CheckedChanged += delegate { SwitchMode(false); };
            pMp3 = rm;
            box.Controls.Add(rm);

            // aac — режим CBR/VBR; один combo меняет содержимое
            var ra = Row();
            ra.Controls.Add(MkLbl("Режим:"));
            aacCbr = MkRadio("CBR", true);
            aacVbr = MkRadio("VBR", false);
            ra.Controls.Add(aacCbr);
            ra.Controls.Add(aacVbr);
            aacValLbl = MkLbl("     Битрейт:");
            ra.Controls.Add(aacValLbl);
            aacVal = MkCombo(AacBr, 170);
            aacVal.SelectedIndex = aacBrIdx;
            aacVal.SelectedIndexChanged += delegate { OnCodecValChanged(true); };
            ra.Controls.Add(aacVal);
            aacCbr.CheckedChanged += delegate { SwitchMode(true); };
            pAac = ra;
            box.Controls.Add(ra);

            // flac
            var rf = Row();
            rf.Controls.Add(MkLbl("Сжатие:"));
            flacLevel = MkCombo(new string[] {
                "0 — минимальное", "1", "2", "3", "4 — среднее", "5",
                "6", "7", "8 — максимальное" }, 150);
            flacLevel.DropDownWidth = 190;
            flacLevel.SelectedIndex = 5;
            rf.Controls.Add(flacLevel);
            rf.Controls.Add(Tips.Icon(
                "Уровень сжатия НЕ влияет на качество звука (формат без\n" +
                "потерь) — только на размер файла и время кодирования:\n" +
                "8 — жмёт сильнее, но дольше; 0 — быстрее, но крупнее.\n" +
                "Обычно достаточно 5 (значение по умолчанию)."));
            rf.Controls.Add(MkLbl("   Разрядность:"));
            flacBits = MkCombo(new string[] { "как в исходнике", "16 бит", "24 бита" }, 160);
            flacBits.SelectedIndex = 0;
            rf.Controls.Add(flacBits);
            pFlac = rf;
            box.Controls.Add(rf);

            // wav
            var rw = Row();
            rw.Controls.Add(MkLbl("Разрядность:"));
            wavBits = MkCombo(new string[] {
                "как в исходнике", "16 бит (совместимость)",
                "24 бита", "32 бита float (обработка)" }, 220);
            wavBits.SelectedIndex = 0;
            rw.Controls.Add(wavBits);
            pWav = rw;
            box.Controls.Add(rw);

            // общее
            var rc = Row();
            rc.Controls.Add(MkLbl("Частота:"));
            sampleRate = MkCombo(new string[] {
                "авто", "48 кГц", "44.1 кГц", "24 кГц", "16 кГц" }, 110);
            sampleRate.SelectedIndex = 0;
            Tips.Set(sampleRate,
                "48/44.1 кГц — стандарт; 24 кГц — достаточно для речи,\n" +
                "файл меньше; 16 кГц — телефонное качество.");
            rc.Controls.Add(sampleRate);
            rc.Controls.Add(MkLbl("   Доп. параметры ffmpeg:"));
            extraArgs = new TextBox();
            extraArgs.Width = 260;
            Tips.Set(extraArgs,
                "Для продвинутых: добавляется в команду ПОСЛЕ параметров\n" +
                "программы, поэтому перекрывает их (у ffmpeg побеждает\n" +
                "последняя опция). Полная команда печатается в консоль.\n" +
                "\n" +
                "Чистка и обработка звука (-af = аудиофильтр):\n" +
                "  -af \"highpass=f=100\"      убрать гул/рокот снизу\n" +
                "  -af \"lowpass=f=15000\"     убрать шипение сверху\n" +
                "  -af \"afftdn=nf=-25\"       шумоподавление (фон, шум)\n" +
                "  -af \"acompressor\"         сжать динамику (тихо↔громко)\n" +
                "  -af \"silenceremove=...\"   вырезать паузы тишины\n" +
                "  -af \"afade=t=in:d=2\"      плавное появление 2 сек\n" +
                "  -af \"atempo=1.5\"          ускорить ×1.5 (тон сохранён)\n" +
                "  -af \"volume=6dB\"          поднять громкость на 6 дБ\n" +
                "\n" +
                "Обрезка и метаданные:\n" +
                "  -ss 30 -t 120             взять кусок с 0:30 длиной 2:00\n" +
                "  -metadata artist=\"Имя\"    вписать исполнителя/название\n" +
                "\n" +
                "Тонкая настройка кодека:\n" +
                "  -cutoff 15000             срез ВЧ у opus/aac (меньше файл)\n" +
                "  -sample_fmt s16           разрядность выборки\n" +
                "Несколько фильтров подряд: -af \"highpass=f=100,afftdn\"");
            rc.Controls.Add(Bordered.Wrap(extraArgs));
            var bExtraHelp = MkBtn("?", delegate
            {
                HelpBox.Show(FindForm(),
                    "Доп. параметры ffmpeg — справка", ExtraArgsHelp);
            });
            bExtraHelp.Width = 30;
            bExtraHelp.AutoSize = false;
            bExtraHelp.Height = 28;
            rc.Controls.Add(bExtraHelp);
            box.Controls.Add(rc);

            adv.Content.Controls.Add(box);
        }

        // ── помощники UI ─────────────────────────────────────────────

        static FlowLayoutPanel NewFlow()
        {
            var f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.WrapContents = true;
            f.Margin = new Padding(0, 2, 0, 2);
            return f;
        }

        static Label MkLbl(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(0, 7, 4, 0);
            return l;
        }

        static Button MkBtn(string text, EventHandler onClick)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Height = 30;
            b.Margin = new Padding(0, 2, 6, 2);
            b.Click += onClick;
            return b;
        }

        static ComboBox MkCombo(string[] items, int width)
        {
            var c = new DarkCombo();
            c.Items.AddRange(items);
            c.Width = width;
            if (items.Length > 0) c.SelectedIndex = 0;
            c.Margin = new Padding(0, 4, 4, 0);
            return c;
        }

        static RadioButton MkRadio(string text, bool check)
        {
            var r = new RadioButton();
            r.Text = text;
            r.AutoSize = true;
            r.Checked = check;
            r.Margin = new Padding(8, 4, 0, 0);
            return r;
        }

        // ── логика пресетов и формата ────────────────────────────────

        int FmtCol()
        {
            switch ((string)fmt.SelectedItem)
            {
                case "mp3": return 1;
                case "aac": return 2;
                default: return 0;
            }
        }

        void OnPresetChanged()
        {
            if (syncing) return;
            ApplyPreset();
        }

        void ApplyPreset()
        {
            int pi = quality.SelectedIndex;
            if (pi < 0 || pi > 3) return; // «Свой» — не трогаем ползунки
            syncing = true;
            int br = PresetBr[pi, FmtCol()];
            string f = (string)fmt.SelectedItem;
            if (f == "opus")
            {
                opusBitrate.Value = Math.Max(opusBitrate.Minimum,
                    Math.Min(opusBitrate.Maximum, br));
                opusVbr.Checked = true;
            }
            else if (f == "mp3")
            {
                mp3IsVbr = false; mp3Vbr.Checked = false; mp3Cbr.Checked = true;
                mp3ValLbl.Text = "     Битрейт:";
                mp3Val.Items.Clear(); mp3Val.Items.AddRange(Mp3Br);
                int idx = Array.IndexOf(Mp3Br, br.ToString());
                mp3BrIdx = idx < 0 ? 2 : idx;
                mp3Val.SelectedIndex = mp3BrIdx;
            }
            else if (f == "aac")
            {
                aacIsVbr = false; aacVbr.Checked = false; aacCbr.Checked = true;
                aacValLbl.Text = "     Битрейт:";
                aacVal.Items.Clear(); aacVal.Items.AddRange(AacBr);
                int idx = Array.IndexOf(AacBr, br.ToString());
                aacBrIdx = idx < 0 ? 3 : idx;
                aacVal.SelectedIndex = aacBrIdx;
            }
            // «Максимум — музыка» снимает нормализацию (ТЗ 3.4)
            if (pi == 3 && normalize.Checked) normalize.Checked = false;
            if (pi != 3 && !normalize.Checked) normalize.Checked = true;
            syncing = false;
        }

        static void SelectByText(ComboBox c, string text)
        {
            for (int i = 0; i < c.Items.Count; i++)
                if ((string)c.Items[i] == text) { c.SelectedIndex = i; return; }
        }

        void OnManualChange()
        {
            // тронули ползунок — пресет переключается на «Свой» (ТЗ 3.3)
            if (syncing) return;
            syncing = true;
            quality.SelectedIndex = 4;
            syncing = false;
        }

        void UpdateFormatUi()
        {
            string f = (string)fmt.SelectedItem;
            pOpus.Visible = f == "opus";
            pMp3.Visible = f == "mp3";
            pAac.Visible = f == "aac";
            pFlac.Visible = f == "flac";
            pWav.Visible = f == "wav";
            bool lossy = f == "opus" || f == "mp3" || f == "aac";
            quality.Enabled = lossy;
            suffix.Text = " + ." + OutExt(f);
            // блок настроек сменил высоту (opus выше mp3) — пусть окно
            // подрастёт вниз, а не сжимает список файлов (после переразметки)
            if (IsHandleCreated)
                BeginInvoke((MethodInvoker)delegate
                {
                    grid.PerformLayout();
                    if (EnsureHeight != null)
                        EnsureHeight(grid.PreferredSize.Height);
                });
        }

        // Расширение результата: aac пакуем в .m4a (в сыром .aac нет
        // длительности/перемотки — foobar и часть плееров не понимают).
        static string OutExt(string fmt)
        {
            return fmt == "aac" ? "m4a" : fmt;
        }

        const string ExtraArgsHelp =
"ДОП. ПАРАМЕТРЫ FFMPEG\n" +
"\n" +
"Это способ добавить возможности, которых нет в интерфейсе, —\n" +
"почистить звук, обрезать, вписать теги, тонко настроить кодек.\n" +
"Всё, что вы сюда впишете, дописывается в команду и может как\n" +
"дополнять, так и переопределять настройки программы (у ffmpeg\n" +
"побеждает последняя одноимённая опция). Полная команда всегда\n" +
"печатается в консоль — видно, что получилось.\n" +
"\n" +
"ГЛАВНОЕ: битрейт/кодек/каналы/частоту/нормализацию писать НЕ\n" +
"нужно — программа уже подставляет их из настроек выше (Формат,\n" +
"Качество, Каналы, Нормализация). Это поле — ТОЛЬКО для\n" +
"дополнительного: фильтров, обрезки, тегов, редких опций кодека.\n" +
"Если всё же впишете, например, -b:a — ваше значение перебьёт\n" +
"выбранное в программе (последняя опция побеждает), но обычно\n" +
"это не нужно.\n" +
"\n" +
"─────────────────────────────────────────────\n" +
"КУДА ИМЕННО ОНИ ПОПАДАЮТ (важно!)\n" +
"─────────────────────────────────────────────\n" +
"Один файл обрабатывается в НЕСКОЛЬКО заходов ffmpeg подряд:\n" +
"  1) извлечение дорожки во временный WAV\n" +
"  2) измерение громкости (если включена нормализация)\n" +
"  3) нормализация\n" +
"  4) КОДИРОВАНИЕ в итоговый файл\n" +
"\n" +
"Ваши доп. параметры добавляются ТОЛЬКО к последнему шагу —\n" +
"к кодированию, которое создаёт итоговый файл. Первые шаги —\n" +
"внутренняя кухня, их трогать не нужно.\n" +
"\n" +
"Что это значит на практике: аудиофильтр (-af) применяется к\n" +
"звуку, который уже извлечён и нормализован, то есть к финальному\n" +
"результату — как и ожидаешь. Обрезка по времени, теги, настройки\n" +
"кодека — тоже применяются к итоговому файлу.\n" +
"\n" +
"─────────────────────────────────────────────\n" +
"ЧИСТКА И ОБРАБОТКА ЗВУКА  (-af = аудиофильтр)\n" +
"─────────────────────────────────────────────\n" +
"  -af \"highpass=f=100\"     убрать низкий гул/рокот\n" +
"  -af \"lowpass=f=15000\"    убрать высокочастотное шипение\n" +
"  -af \"afftdn=nf=-25\"      шумоподавление (ровный фон, шум)\n" +
"  -af \"anlmdn\"             более мягкое шумоподавление\n" +
"  -af \"acompressor\"        сжать динамику: тихое громче,\n" +
"                            громкое тише (речь ровнее)\n" +
"  -af \"alimiter\"           ограничитель пиков (без перегруза)\n" +
"  -af \"silenceremove=start_periods=1:start_threshold=-40dB\"\n" +
"                            обрезать тишину в начале\n" +
"  -af \"afade=t=in:d=2\"     плавное появление 2 секунды\n" +
"  -af \"afade=t=out:st=58:d=2\"  затухание в конце\n" +
"  -af \"atempo=1.5\"         ускорить ×1.5 (тон сохраняется)\n" +
"  -af \"volume=6dB\"         поднять громкость на 6 дБ\n" +
"\n" +
"Несколько фильтров сразу — через запятую в одной строке:\n" +
"  -af \"highpass=f=100,afftdn,acompressor\"\n" +
"\n" +
"─────────────────────────────────────────────\n" +
"ОБРЕЗКА ПО ВРЕМЕНИ И ТЕГИ\n" +
"─────────────────────────────────────────────\n" +
"  -ss 30 -t 120            взять кусок с 0:30 длиной 2:00\n" +
"  -ss 90                   начать с 1:30 и до конца\n" +
"  -metadata artist=\"Имя\"   исполнитель\n" +
"  -metadata title=\"Эфир\"   название\n" +
"  -metadata:s:a:0 language=rus   язык дорожки\n" +
"\n" +
"─────────────────────────────────────────────\n" +
"ТОНКАЯ НАСТРОЙКА КОДЕКА\n" +
"─────────────────────────────────────────────\n" +
"  -cutoff 15000            срез верхних частот у opus/aac —\n" +
"                            меньше размер без слышимой потери\n" +
"  -sample_fmt s16          разрядность выборки\n" +
"  -frame_duration 60       (opus) длиннее кадр — чуть меньше файл\n" +
"  -compression_level 10    (opus) макс. сжатие, медленнее\n" +
"\n" +
"─────────────────────────────────────────────\n" +
"ЧЕГО НЕ НАДО\n" +
"─────────────────────────────────────────────\n" +
"• Не дублируйте -c:a и -b:a — их программа уже ставит.\n" +
"• Видео-опции бессмысленны: на вкладке «Аудио» видео\n" +
"  отбрасывается.\n" +
"• Фильтр -af может конфликтовать с режимами «Моно (сведение)»\n" +
"  и «Стерео: по ушам» — там уже используется сложный фильтр\n" +
"  сведения. Для обработки фильтрами берите «Как в исходнике».";

        // Переключение CBR<->VBR: сохраняем выбор старого режима, меняем
        // метку и список значений (битрейт<->качество), ставим выбор нового.
        void SwitchMode(bool aac)
        {
            if (mp3Cbr == null || syncing) return;
            syncing = true;
            if (aac)
            {
                if (aacIsVbr) aacQIdx = aacVal.SelectedIndex;
                else aacBrIdx = aacVal.SelectedIndex;
                aacIsVbr = aacVbr.Checked;
                aacValLbl.Text = aacIsVbr ? "     Качество:" : "     Битрейт:";
                aacVal.Items.Clear();
                aacVal.Items.AddRange(aacIsVbr ? AacQ : AacBr);
                aacVal.SelectedIndex = aacIsVbr ? aacQIdx : aacBrIdx;
            }
            else
            {
                if (mp3IsVbr) mp3QIdx = mp3Val.SelectedIndex;
                else mp3BrIdx = mp3Val.SelectedIndex;
                mp3IsVbr = mp3Vbr.Checked;
                mp3ValLbl.Text = mp3IsVbr ? "     Качество:" : "     Битрейт:";
                mp3Val.Items.Clear();
                mp3Val.Items.AddRange(mp3IsVbr ? Mp3Q : Mp3Br);
                mp3Val.SelectedIndex = mp3IsVbr ? mp3QIdx : mp3BrIdx;
            }
            syncing = false;
            OnManualChange();
        }

        void OnCodecValChanged(bool aac)
        {
            if (syncing) return;
            if (aac)
            {
                if (aacIsVbr) aacQIdx = aacVal.SelectedIndex;
                else aacBrIdx = aacVal.SelectedIndex;
            }
            else
            {
                if (mp3IsVbr) mp3QIdx = mp3Val.SelectedIndex;
                else mp3BrIdx = mp3Val.SelectedIndex;
            }
            OnManualChange();
        }

        // «Рядом с исходником» -> поле пути серое
        void UpdateOutputUi()
        {
            bool toDir = outToDir.Checked;
            outDir.Enabled = toDir;
            outWrap.Enabled = toDir;
            bBrowse.Enabled = toDir;
        }

        // нормализация выключена -> «Цель» и её combo явно неактивны
        void UpdateNormUi()
        {
            bool on = normalize.Checked;
            lufs.Enabled = on;
            if (lufsLabel != null)
                lufsLabel.ForeColor = on
                    ? Theme.Current.Text : Theme.Current.TextDim;
        }

        // ── дорожки ──────────────────────────────────────────────────

        void OnListChanged()
        {
            RebuildTracks();
            UpdateQueueButton();
            UpdateStatusBar();
        }

        void RebuildTracks()
        {
            // дорожки показываем по ВЫБРАННОМУ файлу (а не по первому в
            // списке) — иначе виджет «залипал» на структуре первого файла
            ProbeInfo first = null;
            var sel = files.SelectedEntry;
            if (sel != null && sel.Probe != null && sel.Probe.Audio.Count > 0)
                first = sel.Probe;
            else
                foreach (var e in files.Entries)
                    if (e.Probe != null && e.Probe.Audio.Count > 0)
                    { first = e.Probe; break; }

            trackChecks.Clear();
            tracksPanel.Controls.Clear();
            leftEar.Items.Clear();
            rightEar.Items.Clear();

            bool multi = first != null && first.Audio.Count > 1;
            if (multi)
            {
                tracksPanel.Controls.Add(MkLbl("Дорожки:"));
                var trackIcon = Tips.Icon(
                    "Дорожка — отдельный поток в файле (микрофон, системные\n" +
                    "звуки, языки озвучки). Не путать с каналами: моно/стерео —\n" +
                    "это внутри одной дорожки.\n" +
                    "\n" +
                    "В режиме «Как в исходнике» при выборе нескольких дорожек\n" +
                    "каждая будет сконвертирована в ОТДЕЛЬНЫЙ файл — к имени\n" +
                    "добавится приписка «(дорожка N)».\n" +
                    "В режимах «Моно»/«Стерео» отмеченные дорожки сводятся вместе.\n" +
                    "\n" +
                    "Галки задаются по этому файлу; к остальным файлам пачки\n" +
                    "применяются по номерам дорожек.");
                tracksPanel.Controls.Add(trackIcon);
                tracksPanel.SetFlowBreak(trackIcon, true);  // галки — с новой строки
                int ti = 0;
                foreach (var t in first.Audio)
                {
                    var cb = new CheckBox();
                    cb.Text = t.Describe();
                    cb.AutoSize = true;
                    cb.Checked = true;
                    cb.Margin = new Padding(8, 4, 8, 2);
                    trackChecks.Add(cb);
                    tracksPanel.Controls.Add(cb);
                    if (++ti % 3 == 0)             // по 3 дорожки в ряд
                        tracksPanel.SetFlowBreak(cb, true);
                    leftEar.Items.Add(t.Describe());
                    rightEar.Items.Add(t.Describe());
                }
                if (leftEar.Items.Count > 0) leftEar.SelectedIndex = 0;
                if (rightEar.Items.Count > 1) rightEar.SelectedIndex = 1;
                else if (rightEar.Items.Count > 0) rightEar.SelectedIndex = 0;
            }
            Theme.Apply(tracksPanel);   // прокрасить динамически созданные
            UpdateTracksUi();
        }

        void UpdateTracksUi()
        {
            bool multi = trackChecks.Count > 0;
            string ch = ChannelsKey();
            tracksPanel.Visible = multi && ch != "stereo";
            stereoPanel.Visible = multi && ch == "stereo";
        }

        string ChannelsKey()
        {
            switch (channels.SelectedIndex)
            {
                case 1: return "mono";
                case 2: return "stereo";
                default: return "original";
            }
        }

        // ── очередь ──────────────────────────────────────────────────

        AudioSettings Snapshot()
        {
            var s = new AudioSettings();
            s.Format = (string)fmt.SelectedItem;
            s.OpusApp = opusVoip.Checked ? "voip" : "audio";
            s.OpusVbr = opusVbr.Checked;
            if (s.Format == "opus") s.BitrateKbps = opusBitrate.Value;
            else if (s.Format == "mp3")
            {
                s.VbrMode = mp3Vbr.Checked;
                if (s.VbrMode)
                    s.VbrQuality = int.Parse(
                        ((string)mp3Val.SelectedItem).Substring(1, 1));
                else int.TryParse((string)mp3Val.SelectedItem, out s.BitrateKbps);
            }
            else if (s.Format == "aac")
            {
                s.VbrMode = aacVbr.Checked;
                if (s.VbrMode) s.VbrQuality = aacVal.SelectedIndex + 1;
                else int.TryParse((string)aacVal.SelectedItem, out s.BitrateKbps);
            }
            s.FlacLevel = flacLevel.SelectedIndex;
            s.FlacBits = flacBits.SelectedIndex == 1 ? 16
                       : flacBits.SelectedIndex == 2 ? 24 : 0;
            s.WavBits = wavBits.SelectedIndex == 1 ? 16
                      : wavBits.SelectedIndex == 2 ? 24
                      : wavBits.SelectedIndex == 3 ? 32 : 0;
            switch (sampleRate.SelectedIndex)
            {
                case 1: s.SampleRate = 48000; break;
                case 2: s.SampleRate = 44100; break;
                case 3: s.SampleRate = 24000; break;
                case 4: s.SampleRate = 16000; break;
            }
            s.Channels = ChannelsKey();
            if (trackChecks.Count > 0)
            {
                for (int i = 0; i < trackChecks.Count; i++)
                    if (trackChecks[i].Checked) s.Tracks.Add(i);
                if (s.Tracks.Count == 0) s.Tracks.Add(0);
            }
            else s.Tracks.Add(0);
            s.LeftTrack = Math.Max(0, leftEar.SelectedIndex);
            s.RightTrack = Math.Max(0, rightEar.SelectedIndex);
            s.Normalize = normalize.Checked;
            s.Lufs = new int[] { -14, -16, -18, -23 }
                [Math.Max(0, lufs.SelectedIndex)];
            s.NameTemplate = nameTpl.Text;
            s.OutputDir = outToDir.Checked ? outDir.Text.Trim() : "";
            s.Overwrite = overwrite.Checked;
            s.ExtraArgs = extraArgs.Text.Trim();
            s.Verbose = verbose.Checked;
            return s;
        }

        void QueueSelected()
        {
            var e = files.SelectedEntry;
            if (e == null)
            {
                ThemedDialog.Info(FindForm(),
                    "Выберите файл в списке.");
                return;
            }
            if (e.Status == FileStatus.Queued)
            {
                e.Status = FileStatus.Waiting;
                e.Job = null;
                e.Note = "";
                files.UpdateEntry(e);
            }
            else if (e.Status != FileStatus.Working)
            {
                e.Job = Snapshot();
                e.Status = FileStatus.Queued;
                e.BatchIndex = files.Entries.IndexOf(e) + 1;
                files.UpdateEntry(e);
                if (queue.Running) runTotal++;   // добавили на ходу
            }
            UpdateQueueButton();
            UpdateStatusBar();
        }

        // Поставить в очередь файлы, чей статус проходит фильтр.
        int QueueThese(Predicate<FileStatus> match)
        {
            int n = 0;
            foreach (var e in files.Entries)
            {
                if (!match(e.Status)) continue;
                e.Job = Snapshot();
                e.Status = FileStatus.Queued;
                e.BatchIndex = ++n;
                files.UpdateEntry(e);
            }
            if (queue.Running) runTotal += n;   // добавили в очередь на ходу
            UpdateQueueButton();
            UpdateStatusBar();
            return n;
        }

        void QueueAll()
        {
            // с «перезаписывать готовые» — берём и завершённые (готово/
            // пропущено/ошибка), иначе только ожидающие
            bool ow = overwrite.Checked;
            QueueThese(delegate(FileStatus st)
            {
                if (st == FileStatus.Waiting) return true;
                return ow && (st == FileStatus.Done
                    || st == FileStatus.Skipped || st == FileStatus.Error);
            });
        }

        // Запустить только выбранный файл (остальную очередь не трогаем).
        void RunSelectedOnly()
        {
            var e = files.SelectedEntry;
            if (e == null || queue.Running || e.Status == FileStatus.Working)
                return;
            if (e.Status != FileStatus.Queued)   // ещё нет задания — создаём снимок
            {
                e.Job = Snapshot();
                e.Status = FileStatus.Queued;
                e.BatchIndex = 1;
                files.UpdateEntry(e);
            }
            runTotal = 1;
            runDone = 0;
            miPause.Checked = false;
            SaveSettings();
            Settings.Save();
            if (t0 == null)
            {
                t0 = DateTime.Now;
                if (NewLog != null) NewLog();
            }
            timer.Start();
            bStop.Enabled = true;
            bStopMenu.Enabled = true;
            if (ShowLog != null) ShowLog();
            queue.StartSingle(e);
            UpdateStatusBar();
        }

        // Отменить задание выбранного файла (вернуть в «ожидание»).
        void CancelSelectedJob()
        {
            var e = files.SelectedEntry;
            if (e == null || e.Status != FileStatus.Queued) return;
            e.Status = FileStatus.Waiting;
            e.Job = null;
            e.Note = "";
            files.UpdateEntry(e);
            UpdateQueueButton();
            UpdateStatusBar();
        }

        void OpenResultFolder()
        {
            var e = files.SelectedEntry;
            if (e == null || string.IsNullOrEmpty(e.OutputPath)) return;
            if (!System.IO.File.Exists(e.OutputPath)) return;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe",
                    "/select,\"" + e.OutputPath + "\"");
            }
            catch { }
        }

        // Снять задания со всех файлов в очереди (кроме работающего).
        void ClearQueue()
        {
            foreach (var e in files.Entries)
                if (e.Status == FileStatus.Queued)
                {
                    e.Status = FileStatus.Waiting;
                    e.Job = null;
                    e.Note = "";
                    files.UpdateEntry(e);
                }
            UpdateQueueButton();
            UpdateStatusBar();
        }

        void StartQueue()
        {
            bool any = false;
            foreach (var e in files.Entries)
                if (e.Status == FileStatus.Queued) { any = true; break; }

            if (!any)
            {
                int waiting = 0, finished = 0;
                foreach (var e in files.Entries)
                {
                    if (e.Status == FileStatus.Waiting) waiting++;
                    else if (e.Status == FileStatus.Done
                          || e.Status == FileStatus.Skipped
                          || e.Status == FileStatus.Error) finished++;
                }
                // приоритет — ждущим; если их нет, «Старт» перезапускает
                // уже завершённые (готово/пропущено/ошибка)
                bool rerun = waiting == 0;
                int count = rerun ? finished : waiting;
                if (count == 0) return;

                // один файл — не спрашиваем, сразу в очередь и старт
                if (count > 1 && !Settings.GetB("skip_start_confirm", false))
                {
                    bool dontAsk;
                    bool ok = ThemedDialog.Confirm(FindForm(),
                        string.Format(rerun
                            ? "Перезапустить все завершённые файлы ({0} шт.) " +
                              "с текущими настройками?"
                            : "Поставить в очередь все файлы ({0} шт.) " +
                              "с текущими настройками и начать?", count),
                        "Больше не спрашивать", out dontAsk);
                    if (dontAsk) Settings.Set("skip_start_confirm", true);
                    if (!ok) return;
                }
                if (rerun)
                    QueueThese(delegate(FileStatus st)
                    {
                        return st == FileStatus.Done
                            || st == FileStatus.Skipped
                            || st == FileStatus.Error;
                    });
                else QueueAll();
            }

            SaveSettings();
            Settings.Save();
            miPause.Checked = false;
            // счётчик «Файл N из M» — всё, что сейчас в очереди
            runTotal = 0;
            runDone = 0;
            foreach (var e in files.Entries)
                if (e.Status == FileStatus.Queued) runTotal++;
            if (t0 == null)
            {
                t0 = DateTime.Now;
                if (NewLog != null) NewLog();   // новый цикл — консоль с нуля
            }
            timer.Start();
            bStop.Enabled = true;
            bStopMenu.Enabled = true;
            if (ShowLog != null) ShowLog();   // лог выезжает при старте (Rufus)
            queue.Start();
            UpdateStatusBar();
        }

        void WireQueue()
        {
            queue.GetQueued = delegate { return files.Entries; };
            queue.EntryChanged = delegate(FileEntry e)
            {
                files.UpdateEntry(e);
                if (e.Status == FileStatus.Working)
                {
                    tFile = DateTime.Now;
                    curStage = "";
                }
                else
                {
                    tFile = null;
                    if (e.Status == FileStatus.Done
                        || e.Status == FileStatus.Skipped
                        || e.Status == FileStatus.Error) runDone++;
                }
                UpdateQueueButton();
                UpdateStatusBar();
            };
            queue.Log = delegate(FileEntry e, string line) { Log(line); };
            queue.Progress = delegate(int pct)
            {
                if (ProgressSink != null) ProgressSink(pct);
            };
            queue.Stage = delegate(string stage)
            {
                curStage = stage;
                UpdateStatusBar();
            };
            queue.QueueFinished = delegate
            {
                timer.Stop();
                if (t0 != null)
                    lastTotal = FileListView.FmtElapsed(
                        (DateTime.Now - t0.Value).TotalSeconds);
                t0 = null;
                tFile = null;
                curStage = "";
                bStop.Enabled = false;
                bStopMenu.Enabled = false;
                miPause.Checked = false;
                if (ProgressSink != null) ProgressSink(0);
                int done = 0, err = 0, skip = 0;
                foreach (var e in files.Entries)
                {
                    if (e.Status == FileStatus.Done) done++;
                    else if (e.Status == FileStatus.Error) err++;
                    else if (e.Status == FileStatus.Skipped) skip++;
                }
                Log(string.Format(
                    ">>> Очередь завершена: готово {0}, ошибок {1}, пропущено {2}",
                    done, err, skip));
                string t = lastTotal ?? "0:00";
                if (err > 0)
                    Overall("⛔ Готово с ошибками (" + t + ")", 3);
                else
                    Overall("Готово (" + t + ")", 2);
                WriteSummary(done, err, skip, t);
                UpdateStatusBar();
            };
        }

        void UpdateQueueButton()
        {
            var e = files.SelectedEntry;
            if (e != null && e.Status == FileStatus.Queued)
                bQueue.Text = "Отменить задание";
            else
                bQueue.Text = "В очередь";
            bQueue.Enabled = e == null || e.Status != FileStatus.Working;
        }

        public Action<string> SetStatusBar; // подключает MainForm

        void UpdateStatusBar()
        {
            if (SetStatusBar == null) return;
            int queued = 0;
            FileEntry cur = null;
            foreach (var e in files.Entries)
            {
                if (e.Status == FileStatus.Queued) queued++;
                if (e.Status == FileStatus.Working) cur = e;
            }

            string s;
            if (cur != null)
            {
                string stage = string.IsNullOrEmpty(curStage)
                    ? "обработка" : curStage;
                string pos = runTotal > 1
                    ? "Файл " + Math.Min(runDone + 1, runTotal)
                      + " из " + runTotal + " · " : "";
                string el = tFile != null
                    ? " (" + FileListView.FmtElapsed(
                        (DateTime.Now - tFile.Value).TotalSeconds) + ")" : "";
                s = pos + cur.Name + el + ": " + Cap(stage);
                if (miPause != null && miPause.Checked)
                    s += " · ⏸ пауза после текущего";
            }
            else if (queued > 0)
                s = "В очереди: " + queued;
            else
                s = "";   // деталей нет — жёлтая строка пустая
            SetStatusBar(s);
        }

        static string Cap(string s)
        {
            return string.IsNullOrEmpty(s) ? s
                : char.ToUpper(s[0]) + s.Substring(1);
        }

        // Краткий итог цикла — человекочитаемый, в отдельный файл.
        void WriteSummary(int done, int err, int skip, string total)
        {
            if (SummarySink == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Vanish-FFConverter — итог обработки (Аудио)");
            sb.AppendLine("Завершено:      "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Всего времени:  " + LabelTime(total));
            sb.AppendLine(string.Format(
                "Готово: {0} · Ошибок: {1} · Пропущено: {2}", done, err, skip));
            sb.AppendLine(new string('═', 56));
            sb.AppendLine();
            int n = 0;
            foreach (var e in files.Entries)
            {
                if (e.Status != FileStatus.Done && e.Status != FileStatus.Error
                    && e.Status != FileStatus.Skipped) continue;  // не в цикле
                n++;
                string time = LabelTime(FileListView.FmtElapsed(e.DoneSeconds));
                switch (e.Status)
                {
                    case FileStatus.Done:
                        sb.AppendLine("✓ " + n + ". " + e.Name);
                        if (e.Job != null)
                            sb.AppendLine("    параметры: " + e.Job.Summary());
                        if (e.Results != null && e.Results.Count > 0)
                        {
                            if (e.Results.Count == 1)
                            {
                                sb.AppendLine("    файл:      " + e.Results[0][0]);
                                sb.AppendLine("    размер:    " + e.Results[0][1]);
                            }
                            else
                            {
                                sb.AppendLine("    файлов:    " + e.Results.Count);
                                foreach (var r in e.Results)
                                    sb.AppendLine("      • " + r[0]
                                        + "   (" + r[1] + ")");
                            }
                        }
                        sb.AppendLine("    затрачено: " + time
                            + "  (время кодирования)");
                        break;
                    case FileStatus.Error:
                        sb.AppendLine("✗ " + n + ". " + e.Name);
                        sb.AppendLine("    ошибка:    " + e.Note);
                        break;
                    case FileStatus.Skipped:
                        sb.AppendLine("⏭ " + n + ". " + e.Name);
                        sb.AppendLine("    " + e.Note);
                        break;
                }
                sb.AppendLine();   // пустая строка между заданиями
            }
            SummarySink(sb.ToString());
        }

        // «0:02» -> «0м:02с», «1:05:03» -> «1ч:05м:03с»
        static string LabelTime(string hms)
        {
            var p = hms.Split(':');
            if (p.Length == 2) return p[0] + "м:" + p[1] + "с";
            if (p.Length == 3) return p[0] + "ч:" + p[1] + "м:" + p[2] + "с";
            return hms;
        }

        void Tick()
        {
            if (t0 == null) return;
            // общий статус: ▶ (фиолетовый) и общее время прогона
            Overall("▶  " + FileListView.FmtElapsed(
                (DateTime.Now - t0.Value).TotalSeconds), 1);
            UpdateStatusBar();   // обновить время текущего файла в детали
        }

        // ── файлы ────────────────────────────────────────────────────

        void PickFiles()
        {
            using (var d = new OpenFileDialog())
            {
                d.Multiselect = true;
                d.Filter = "Аудио и видео|*.mp3;*.m4a;*.aac;*.opus;*.ogg;*.oga;" +
                    "*.wav;*.flac;*.wma;*.amr;*.mka;*.mp4;*.mkv;*.mov;*.avi;" +
                    "*.webm;*.wmv;*.ts;*.m4v;*.3gp|Все файлы|*.*";
                if (d.ShowDialog(this) == DialogResult.OK)
                    AddFiles(d.FileNames);
            }
        }

        void AddFiles(string[] paths)
        {
            files.AddPaths(paths, delegate(string msg) { Log(msg); });
        }

        // ── настройки ────────────────────────────────────────────────

        void LoadSettings()
        {
            SelectByText(fmt, Settings.GetS("au_fmt", "opus"));
            int qi = Settings.GetI("au_quality", 1);
            quality.SelectedIndex = Math.Min(4, Math.Max(0, qi));
            channels.SelectedIndex = Settings.GetI("au_channels", 0);
            normalize.Checked = Settings.GetB("au_norm", true);
            lufs.SelectedIndex = Settings.GetI("au_lufs", 1);
            nameTpl.Text = Settings.GetS("au_name", "{имя}");
            outDir.Text = Settings.GetS("au_out", "");
            outToDir.Checked = Settings.GetB("au_to_dir", false);
            outNear.Checked = !outToDir.Checked;
            overwrite.Checked = Settings.GetB("au_overwrite", false);
            extraArgs.Text = Settings.GetS("au_extra", "");
            verbose.Checked = Settings.GetB("au_verbose", false);
            adv.Expanded = Settings.GetB("au_adv", false);
            ApplyPreset();
            UpdateOutputUi();
            UpdateNormUi();
        }

        public void SaveSettings()
        {
            Settings.Set("au_fmt", (string)fmt.SelectedItem);
            Settings.Set("au_quality", quality.SelectedIndex);
            Settings.Set("au_channels", channels.SelectedIndex);
            Settings.Set("au_norm", normalize.Checked);
            Settings.Set("au_lufs", lufs.SelectedIndex);
            Settings.Set("au_name", nameTpl.Text);
            Settings.Set("au_out", outDir.Text);
            Settings.Set("au_to_dir", outToDir.Checked);
            Settings.Set("au_overwrite", overwrite.Checked);
            Settings.Set("au_extra", extraArgs.Text);
            Settings.Set("au_verbose", verbose.Checked);
            Settings.Set("au_adv", adv.Expanded);
        }

        public void RefreshTheme()
        {
            files.RefreshTheme();
        }
    }
}
