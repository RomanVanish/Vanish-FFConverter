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
        TextBox nameTpl, outDir, extraArgs;
        Button bQueue, bQueueAll, bStart, bStop, bStopMenu, bClearCon;
        Label elapsed, suffix;
        TextBox console;
        Expander adv;
        Timer timer;
        DateTime? t0, tFile;
        ContextMenuStrip stopMenu;
        ToolStripMenuItem miPause;

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
        RadioButton mp3Cbr, mp3Vbr;
        ComboBox mp3Bitrate, mp3Q;
        RadioButton aacCbr, aacVbr;
        ComboBox aacBitrate, aacQ;
        ComboBox flacLevel, flacBits, wavBits;

        bool syncing; // защита от рекурсии пресет<->ползунки

        static readonly string[] PresetKeys = { "compact", "standard", "high", "music", "custom" };
        // opus / mp3 / aac
        static readonly int[,] PresetBr = {
            { 32,  64,  48 },
            { 64, 128,  96 },
            { 96, 192, 128 },
            { 256, 320, 256 } };

        public TabAudio(QueueManager q)
        {
            queue = q;
            Dock = DockStyle.Fill;
            BuildUi();
            LoadSettings();
            WireQueue();
            UpdateFormatUi();
            UpdateQueueButton();
        }

        // ── интерфейс ────────────────────────────────────────────────

        void BuildUi()
        {
            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.Padding = new Padding(10, 6, 10, 4);
            for (int i = 0; i < 9; i++)
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles[1] = new RowStyle(SizeType.Percent, 42);
            grid.RowStyles[8] = new RowStyle(SizeType.Percent, 58);
            Controls.Add(grid);

            // 0: заголовок списка
            var head = new Label();
            head.Text = "Файлы (любой аудио/видео формат; из видео берётся звук):";
            head.AutoSize = true;
            head.Margin = new Padding(0, 0, 0, 4);
            Tips.Set(head, "Статусы:  — ожидает   ⏳ в очереди   ► в работе\n" +
                           "✓ готово   ✗ ошибка   ⏭ пропущено\n" +
                           "Файлы обрабатываются в порядке списка;\n" +
                           "строки можно перетаскивать мышью.");
            grid.Controls.Add(head, 0, 0);

            // 1: список
            files = new FileListView();
            files.Dock = DockStyle.Fill;
            files.Margin = new Padding(0);
            files.FilesDropped = delegate(string[] paths) { AddFiles(paths); };
            files.SelectionChangedCb = delegate { UpdateQueueButton(); };
            files.ListChanged = delegate { OnListChanged(); };
            grid.Controls.Add(files, 0, 1);

            // 2: кнопки списка
            var fRow = NewFlow();
            var bAdd = MkBtn("Добавить файлы...", delegate { PickFiles(); });
            var bDel = MkBtn("Удалить выбранное", delegate { files.RemoveSelected(); });
            var bClr = MkBtn("Очистить всё", delegate { files.ClearAll(); });
            fRow.Controls.Add(bAdd); fRow.Controls.Add(bDel); fRow.Controls.Add(bClr);
            grid.Controls.Add(fRow, 0, 2);

            // 3: настройки — формат/качество/каналы/нормализация
            var sPanel = new FlowLayoutPanel();
            sPanel.AutoSize = true;
            sPanel.FlowDirection = FlowDirection.TopDown;
            sPanel.WrapContents = false;
            sPanel.Margin = new Padding(0, 6, 0, 0);

            var row1 = NewFlow();
            row1.Controls.Add(MkLbl("Формат:"));
            fmt = MkCombo(new string[] { "opus", "mp3", "aac", "flac", "wav" }, 90);
            fmt.SelectedIndexChanged += delegate { UpdateFormatUi(); ApplyPreset(); };
            row1.Controls.Add(fmt);
            row1.Controls.Add(MkLbl("   Качество:"));
            quality = MkCombo(new string[] {
                "Компактный — голос (opus 32 / mp3 64 / aac 48)",
                "Стандарт — голос (opus 64 / mp3 128 / aac 96)",
                "Высокое — голос (opus 96 / mp3 192 / aac 128)",
                "Максимум — музыка (opus 256 / mp3 320 / aac 256)",
                "Свой (настройки в «Расширенно»)" }, 330);
            quality.SelectedIndexChanged += delegate { OnPresetChanged(); };
            Tips.Set(quality,
                "Примерный размер часа записи (opus):\n" +
                "  Компактный 32k  ≈ 14 МБ/час\n" +
                "  Стандарт  64k  ≈ 28 МБ/час\n" +
                "  Высокое   96k  ≈ 42 МБ/час\n" +
                "  Максимум 256k ≈ 113 МБ/час\n" +
                "Пресеты «голос» подобраны под речь; для музыки — «Максимум».");
            row1.Controls.Add(quality);
            sPanel.Controls.Add(row1);

            var row2 = NewFlow();
            row2.Controls.Add(MkLbl("Каналы:"));
            channels = MkCombo(new string[] {
                "Как в исходнике (только конвертация)",
                "Моно (сведение)",
                "Стерео: голоса по ушам" }, 260);
            channels.SelectedIndexChanged += delegate { UpdateTracksUi(); };
            Tips.Set(channels,
                "Как в исходнике — только смена формата/качества.\n" +
                "    Несколько отмеченных дорожек -> каждая отдельным файлом.\n" +
                "Моно (сведение) — отмеченные дорожки смешиваются в одну.\n" +
                "    Стандарт для переслушивания: минимальный размер.\n" +
                "Стерео: голоса по ушам — для мультитрека (OBS): одна дорожка\n" +
                "    в левое ухо, другая в правое — удобно разбирать, кто что сказал.");
            row2.Controls.Add(channels);
            normalize = new CheckBox();
            normalize.Text = "   Нормализация громкости";
            normalize.AutoSize = true;
            normalize.Checked = true;
            normalize.Margin = new Padding(12, 4, 0, 0);
            normalize.CheckedChanged += delegate { lufs.Enabled = normalize.Checked; };
            Tips.Set(normalize,
                "Выравнивание воспринимаемой громкости по стандарту EBU R128\n" +
                "(как YouTube и подкаст-платформы): тихое подтягивается,\n" +
                "громкое приглушается. В мультитреке каждая дорожка\n" +
                "выравнивается ОТДЕЛЬНО до сведения.\n" +
                "Отключайте, только если нужна точная копия исходного звука.");
            row2.Controls.Add(normalize);
            row2.Controls.Add(MkLbl(" Цель:"));
            lufs = MkCombo(new string[] {
                "−14 LUFS — громче, стриминг",
                "−16 LUFS — речь, подкасты",
                "−18 LUFS — тише, аудиокниги",
                "−23 LUFS — вещательный стандарт" }, 240);
            lufs.SelectedIndex = 1;
            row2.Controls.Add(lufs);
            sPanel.Controls.Add(row2);

            // дорожки (виджет прячется для одной дорожки, ТЗ 3.4)
            tracksPanel = NewFlow();
            tracksPanel.Visible = false;
            sPanel.Controls.Add(tracksPanel);

            stereoPanel = NewFlow();
            var sp = (FlowLayoutPanel)stereoPanel;
            sp.Controls.Add(MkLbl("Левое ухо:"));
            leftEar = MkCombo(new string[0], 220);
            sp.Controls.Add(leftEar);
            sp.Controls.Add(MkLbl("   Правое ухо:"));
            rightEar = MkCombo(new string[0], 220);
            sp.Controls.Add(rightEar);
            stereoPanel.Visible = false;
            sPanel.Controls.Add(stereoPanel);

            grid.Controls.Add(sPanel, 0, 3);

            // 4: расширенно
            adv = new Expander("Расширенно");
            BuildAdvanced();
            grid.Controls.Add(adv, 0, 4);

            // 5: имя файла
            var nRow = NewFlow();
            nRow.Controls.Add(MkLbl("Имя файла:"));
            nameTpl = new TextBox();
            nameTpl.Width = 340;
            nameTpl.Text = "{имя}";
            Tips.Set(nameTpl,
                "Шаблон имени результата. Теги (русские или английские):\n" +
                "  {имя}/{name}          — имя исходника без расширения\n" +
                "  {дата}/{date}          — дата файла, ГГГГ-ММ-ДД\n" +
                "  {время_чм}/{time_hm}   — время файла, ЧЧ-ММ\n" +
                "  {время_чмс}/{time_hms} — время файла, ЧЧ-ММ-СС\n" +
                "  {сегодня}/{today}      — сегодняшняя дата\n" +
                "  {#} {##} {###}         — номер в пачке (7 / 07 / 007)\n" +
                "  {дорожка}/{track}      — номер дорожки (мультитрек)\n" +
                "Если результат совпал бы с исходником — файл пропускается,\n" +
                "исходник не затирается.");
            nRow.Controls.Add(nameTpl);
            suffix = MkLbl(" + .opus");
            nRow.Controls.Add(suffix);
            grid.Controls.Add(nRow, 0, 5);

            // 6: папка вывода
            var oRow = NewFlow();
            oRow.Controls.Add(MkLbl("Папка вывода:"));
            outDir = new TextBox();
            outDir.Width = 340;
            Tips.Set(outDir, "Пусто — результат кладётся рядом с исходником.");
            oRow.Controls.Add(outDir);
            var bBrowse = MkBtn("Обзор...", delegate
            {
                using (var d = new FolderBrowserDialog())
                    if (d.ShowDialog(this) == DialogResult.OK)
                        outDir.Text = d.SelectedPath;
            });
            oRow.Controls.Add(bBrowse);
            overwrite = new CheckBox();
            overwrite.Text = "Перезаписывать готовые";
            overwrite.AutoSize = true;
            overwrite.Margin = new Padding(12, 4, 0, 0);
            Tips.Set(overwrite,
                "Выключено: файлы, для которых результат уже создан,\n" +
                "пропускаются — удобно перезапускать прерванную очередь.\n" +
                "Включено: конвертировать заново и перезаписывать.");
            oRow.Controls.Add(overwrite);
            grid.Controls.Add(oRow, 0, 6);

            // 7: управление
            var rRow = NewFlow();
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
            bClearCon = MkBtn("Очистить консоль", delegate { console.Clear(); });
            elapsed = MkLbl("");
            elapsed.Margin = new Padding(12, 8, 0, 0);
            rRow.Controls.Add(bQueue);
            rRow.Controls.Add(bQueueAll);
            rRow.Controls.Add(bStart);
            rRow.Controls.Add(bStop);
            rRow.Controls.Add(bStopMenu);
            rRow.Controls.Add(bClearCon);
            rRow.Controls.Add(elapsed);
            grid.Controls.Add(rRow, 0, 7);

            // 8: консоль
            console = new TextBox();
            console.Multiline = true;
            console.ReadOnly = true;
            console.ScrollBars = ScrollBars.Vertical;
            console.Dock = DockStyle.Fill;
            console.Tag = "console";
            var mono = new Font("Cascadia Mono", 9.5f);
            if (mono.Name != "Cascadia Mono") mono = new Font("Consolas", 9.5f);
            console.Font = mono;
            grid.Controls.Add(console, 0, 8);

            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += delegate { Tick(); };
        }

        void BuildAdvanced()
        {
            var box = new FlowLayoutPanel();
            box.FlowDirection = FlowDirection.TopDown;
            box.WrapContents = false;
            box.AutoSize = true;
            box.Dock = DockStyle.Top;

            // opus
            var ro = NewFlow();
            ro.Controls.Add(MkLbl("Битрейт:"));
            opusBitrate = new TrackBar();
            opusBitrate.Minimum = 16;
            opusBitrate.Maximum = 320;
            opusBitrate.TickFrequency = 32;
            opusBitrate.Width = 260;
            opusBitrate.Value = 64;
            opusBitrate.ValueChanged += delegate
            {
                opusBrLabel.Text = opusBitrate.Value + "k";
                OnManualChange();
            };
            ro.Controls.Add(opusBitrate);
            opusBrLabel = MkLbl("64k");
            ro.Controls.Add(opusBrLabel);
            ro.Controls.Add(MkLbl("   Режим:"));
            opusAuto = MkRadio("Авто — кодек сам распознаёт речь/музыку", true);
            opusVoip = MkRadio("Речь (voip)", false);
            Tips.Set(opusVoip, "Принудительный уклон в речь —\n" +
                "для диктофона на низком битрейте (16–32k).");
            ro.Controls.Add(opusAuto);
            ro.Controls.Add(opusVoip);
            opusVbr = new CheckBox();
            opusVbr.Text = "VBR";
            opusVbr.Checked = true;
            opusVbr.AutoSize = true;
            opusVbr.Margin = new Padding(12, 4, 0, 0);
            opusVbr.CheckedChanged += delegate { OnManualChange(); };
            ro.Controls.Add(opusVbr);
            pOpus = ro;
            box.Controls.Add(ro);

            // mp3
            var rm = NewFlow();
            mp3Cbr = MkRadio("CBR", true);
            mp3Vbr = MkRadio("VBR", false);
            Tips.Set(mp3Cbr, "CBR — постоянный битрейт, предсказуемый размер.");
            Tips.Set(mp3Vbr, "VBR — постоянное качество, размер плавает;\nобычно выгоднее CBR.");
            rm.Controls.Add(mp3Cbr);
            rm.Controls.Add(mp3Vbr);
            rm.Controls.Add(MkLbl("   Битрейт:"));
            mp3Bitrate = MkCombo(new string[] { "64", "96", "128", "160", "192", "256", "320" }, 80);
            mp3Bitrate.SelectedIndex = 2;
            mp3Bitrate.SelectedIndexChanged += delegate { OnManualChange(); };
            rm.Controls.Add(mp3Bitrate);
            rm.Controls.Add(MkLbl("   VBR-качество:"));
            mp3Q = MkCombo(new string[] { "V0 — лучшее", "V2", "V4 — среднее", "V6", "V9 — худшее" }, 140);
            mp3Q.SelectedIndex = 1;
            mp3Q.SelectedIndexChanged += delegate { OnManualChange(); };
            rm.Controls.Add(mp3Q);
            mp3Cbr.CheckedChanged += delegate { OnManualChange(); };
            pMp3 = rm;
            box.Controls.Add(rm);

            // aac
            var ra = NewFlow();
            aacCbr = MkRadio("CBR", true);
            aacVbr = MkRadio("VBR", false);
            ra.Controls.Add(aacCbr);
            ra.Controls.Add(aacVbr);
            ra.Controls.Add(MkLbl("   Битрейт:"));
            aacBitrate = MkCombo(new string[] { "48", "64", "96", "128", "192", "256", "320" }, 80);
            aacBitrate.SelectedIndex = 3;
            aacBitrate.SelectedIndexChanged += delegate { OnManualChange(); };
            ra.Controls.Add(aacBitrate);
            ra.Controls.Add(MkLbl("   VBR-качество:"));
            aacQ = MkCombo(new string[] { "1 — хуже/меньше", "2", "3 — среднее", "4", "5 — лучше/больше" }, 160);
            aacQ.SelectedIndex = 2;
            aacQ.SelectedIndexChanged += delegate { OnManualChange(); };
            ra.Controls.Add(aacQ);
            aacCbr.CheckedChanged += delegate { OnManualChange(); };
            pAac = ra;
            box.Controls.Add(ra);

            // flac
            var rf = NewFlow();
            rf.Controls.Add(MkLbl("Сжатие:"));
            flacLevel = MkCombo(new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8" }, 60);
            flacLevel.SelectedIndex = 5;
            Tips.Set(flacLevel,
                "Уровень сжатия НЕ влияет на качество звука (формат без\n" +
                "потерь) — только на размер файла и время кодирования:\n" +
                "8 — жмёт сильнее, но дольше. Обычно достаточно 5.");
            rf.Controls.Add(flacLevel);
            rf.Controls.Add(MkLbl("   Разрядность:"));
            flacBits = MkCombo(new string[] { "как в исходнике", "16 бит", "24 бита" }, 160);
            flacBits.SelectedIndex = 0;
            rf.Controls.Add(flacBits);
            pFlac = rf;
            box.Controls.Add(rf);

            // wav
            var rw = NewFlow();
            rw.Controls.Add(MkLbl("Разрядность:"));
            wavBits = MkCombo(new string[] {
                "как в исходнике", "16 бит (совместимость)",
                "24 бита", "32 бита float (обработка)" }, 220);
            wavBits.SelectedIndex = 0;
            rw.Controls.Add(wavBits);
            pWav = rw;
            box.Controls.Add(rw);

            // общее
            var rc = NewFlow();
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
                "последняя опция). Полная команда печатается в консоль.");
            rc.Controls.Add(extraArgs);
            verbose = new CheckBox();
            verbose.Text = "Подробный вывод ffmpeg";
            verbose.AutoSize = true;
            verbose.Margin = new Padding(12, 4, 0, 0);
            Tips.Set(verbose,
                "Вместо аккуратных процентов в консоль идёт полный «живой»\n" +
                "вывод ffmpeg. На результат не влияет.");
            rc.Controls.Add(verbose);
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
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
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
                mp3Cbr.Checked = true;
                SelectByText(mp3Bitrate, br.ToString());
            }
            else if (f == "aac")
            {
                aacCbr.Checked = true;
                SelectByText(aacBitrate, br.ToString());
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
            suffix.Text = " + ." + f;
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
            var entries = files.Entries;
            ProbeInfo first = null;
            foreach (var e in entries)
                if (e.Probe != null && e.Probe.Audio.Count > 0)
                { first = e.Probe; break; }

            trackChecks.Clear();
            tracksPanel.Controls.Clear();
            leftEar.Items.Clear();
            rightEar.Items.Clear();

            bool multi = first != null && first.Audio.Count > 1;
            if (multi)
            {
                var lbl = MkLbl("Дорожки:");
                Tips.Set(lbl,
                    "Дорожка — отдельный поток в файле (микрофон, системные\n" +
                    "звуки, языки озвучки). Не путать с каналами: моно/стерео —\n" +
                    "это внутри одной дорожки.\n" +
                    "Галки применяются по этому файлу; к остальным файлам\n" +
                    "пачки — по номерам дорожек.");
                tracksPanel.Controls.Add(lbl);
                foreach (var t in first.Audio)
                {
                    var cb = new CheckBox();
                    cb.Text = t.Describe();
                    cb.AutoSize = true;
                    cb.Checked = true;
                    cb.Margin = new Padding(8, 4, 0, 0);
                    trackChecks.Add(cb);
                    tracksPanel.Controls.Add(cb);
                    leftEar.Items.Add(t.Describe());
                    rightEar.Items.Add(t.Describe());
                }
                if (leftEar.Items.Count > 0) leftEar.SelectedIndex = 0;
                if (rightEar.Items.Count > 1) rightEar.SelectedIndex = 1;
                else if (rightEar.Items.Count > 0) rightEar.SelectedIndex = 0;
            }
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
                int.TryParse((string)mp3Bitrate.SelectedItem, out s.BitrateKbps);
                string q = (string)mp3Q.SelectedItem;
                s.VbrQuality = int.Parse(q.Substring(1, 1));
            }
            else if (s.Format == "aac")
            {
                s.VbrMode = aacVbr.Checked;
                int.TryParse((string)aacBitrate.SelectedItem, out s.BitrateKbps);
                s.VbrQuality = aacQ.SelectedIndex + 1;
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
            s.OutputDir = outDir.Text.Trim();
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
            }
            UpdateQueueButton();
            UpdateStatusBar();
        }

        void QueueAll()
        {
            int n = 0;
            var entries = files.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Status != FileStatus.Waiting) continue;
                e.Job = Snapshot();
                e.Status = FileStatus.Queued;
                e.BatchIndex = ++n;
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
                int waiting = 0;
                foreach (var e in files.Entries)
                    if (e.Status == FileStatus.Waiting) waiting++;
                if (waiting == 0) return;

                if (!Settings.GetB("skip_start_confirm", false))
                {
                    bool dontAsk;
                    bool ok = ThemedDialog.Confirm(FindForm(),
                        string.Format("Поставить в очередь все файлы " +
                        "({0} шт.) с текущими настройками и начать?", waiting),
                        "Больше не спрашивать", out dontAsk);
                    if (dontAsk) Settings.Set("skip_start_confirm", true);
                    if (!ok) return;
                }
                QueueAll();
            }

            SaveSettings();
            Settings.Save();
            miPause.Checked = false;
            if (t0 == null) t0 = DateTime.Now;
            timer.Start();
            bStop.Enabled = true;
            bStopMenu.Enabled = true;
            queue.Start();
            UpdateStatusBar();
        }

        void WireQueue()
        {
            queue.GetQueued = delegate { return files.Entries; };
            queue.EntryChanged = delegate(FileEntry e)
            {
                files.UpdateEntry(e);
                if (e.Status == FileStatus.Working) tFile = DateTime.Now;
                else tFile = null;
                UpdateQueueButton();
                UpdateStatusBar();
            };
            queue.Log = delegate(FileEntry e, string line)
            {
                console.AppendText(line + "\r\n");
            };
            queue.QueueFinished = delegate
            {
                timer.Stop();
                Tick();
                t0 = null;
                tFile = null;
                bStop.Enabled = false;
                bStopMenu.Enabled = false;
                miPause.Checked = false;
                int done = 0, err = 0, skip = 0;
                foreach (var e in files.Entries)
                {
                    if (e.Status == FileStatus.Done) done++;
                    else if (e.Status == FileStatus.Error) err++;
                    else if (e.Status == FileStatus.Skipped) skip++;
                }
                console.AppendText(string.Format(
                    ">>> Очередь завершена: готово {0}, ошибок {1}, пропущено {2}\r\n",
                    done, err, skip));
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
            string s = cur != null
                ? "В работе: " + cur.Name + " (Аудио) · в очереди: " + queued
                : queued > 0
                    ? "В очереди: " + queued + " (Аудио)"
                    : L.StatusIdle;
            if (miPause != null && miPause.Checked && cur != null)
                s += " · ⏸ пауза после текущего";
            SetStatusBar(s);
        }

        void Tick()
        {
            if (t0 == null) { elapsed.Text = ""; return; }
            string s = "Всего: " + FileListView.FmtElapsed(
                (DateTime.Now - t0.Value).TotalSeconds);
            if (tFile != null)
                s += " | текущий: " + FileListView.FmtElapsed(
                    (DateTime.Now - tFile.Value).TotalSeconds);
            elapsed.Text = s;
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
            files.AddPaths(paths, delegate(string msg)
            {
                console.AppendText(msg + "\r\n");
            });
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
            overwrite.Checked = Settings.GetB("au_overwrite", false);
            extraArgs.Text = Settings.GetS("au_extra", "");
            verbose.Checked = Settings.GetB("au_verbose", false);
            adv.Expanded = Settings.GetB("au_adv", false);
            ApplyPreset();
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
