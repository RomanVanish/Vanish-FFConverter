using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VanishFF
{
    // Палитры по мотивам Claude Desktop (ТЗ 2.6).
    class Palette
    {
        public bool IsDark;
        public Color WindowBg;   // фон окна
        public Color PanelBg;    // панели, поля, списки
        public Color ConsoleBg;  // консоль
        public Color Border;     // границы 1px
        public Color Text;       // основной текст
        public Color TextDim;    // вторичный текст
        public Color Accent;     // терракота: Старт, активная вкладка
        public Color AccentHover;
        public Color AccentText; // текст на акцентной заливке
        public Color StQueued;   // статусы файлов (ТЗ 2.6)
        public Color StDone;
        public Color StError;
        public Color StWorking;  // «в работе» — яркий фиолетовый
        public Color StatusText; // жёлтый текст статуса внизу окна
    }

    static class Theme
    {
        public static readonly Palette Dark = new Palette
        {
            IsDark = true,
            WindowBg = FromHex("262624"),
            PanelBg = FromHex("30302E"),
            ConsoleBg = FromHex("1F1E1B"),
            Border = FromHex("3E3E3A"),
            Text = FromHex("E8E6DC"),
            TextDim = FromHex("A6A39A"),
            Accent = FromHex("D97757"),
            AccentHover = FromHex("C4633F"),
            AccentText = FromHex("FAF9F5"),
            StQueued = FromHex("D9C27A"),
            StDone = FromHex("7FBF7F"),
            StError = FromHex("E08080"),
            StWorking = FromHex("B794F6"),
            StatusText = FromHex("F2C94C"),
        };

        public static readonly Palette Light = new Palette
        {
            IsDark = false,
            WindowBg = FromHex("FAF9F5"),
            PanelBg = FromHex("F0EEE6"),
            ConsoleBg = FromHex("FFFFFF"),
            Border = FromHex("D8D5CC"),
            Text = FromHex("141413"),
            TextDim = FromHex("73726C"),
            Accent = FromHex("D97757"),
            AccentHover = FromHex("C4633F"),
            AccentText = FromHex("FFFFFF"),
            StQueued = FromHex("A8862B"),
            StDone = FromHex("3E8E3E"),
            StError = FromHex("C05050"),
            StWorking = FromHex("7C3AED"),
            StatusText = FromHex("B8860B"),
        };

        public static Palette Current = Dark;

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color FromHex(string hex)
        {
            return Color.FromArgb(
                Convert.ToInt32(hex.Substring(0, 2), 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16));
        }

        // Рекурсивная прокраска. Роль элемента задаётся через Tag:
        //   "panel"  — фон панели (список, поле)
        //   "accent" — главная кнопка (Старт)
        //   "dim"    — вторичный текст
        // Без тега — фон окна/обычный текст.
        public static void Apply(Control root)
        {
            Palette p = Current;
            ApplyTo(root, p);
            foreach (Control c in root.Controls) Apply(c);
        }

        static void ApplyTo(Control c, Palette p)
        {
            string tag = c.Tag as string;

            var btn = c as Button;
            if (btn != null)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.UseVisualStyleBackColor = false;
                if (tag == "accent")
                {
                    btn.BackColor = p.Accent;
                    btn.ForeColor = p.AccentText;
                    btn.FlatAppearance.BorderSize = 0;
                    btn.FlatAppearance.MouseOverBackColor = p.AccentHover;
                    btn.FlatAppearance.MouseDownBackColor = p.AccentHover;
                }
                else
                {
                    btn.BackColor = p.PanelBg;
                    btn.ForeColor = p.Text;
                    btn.FlatAppearance.BorderSize = 1;
                    btn.FlatAppearance.BorderColor = p.Border;
                    btn.FlatAppearance.MouseOverBackColor = p.Border;
                    btn.FlatAppearance.MouseDownBackColor = p.Border;
                }
                return;
            }

            // рамка вокруг поля/списка (WinForms рисует системную белую —
            // мы её прячем и даём свою через Panel-обёртку, ТЗ 2.6)
            if (tag == "border")
            {
                c.BackColor = p.Border;
                return;
            }

            // значок ⓘ — песочный, фон прозрачный (берёт цвет родителя)
            if (tag == "info")
            {
                c.BackColor = Color.Transparent;
                c.ForeColor = p.StQueued;
                return;
            }

            // рамка-группа (нормализация + цель): чуть приподнятый фон
            if (tag == "group")
            {
                c.BackColor = p.PanelBg;
                c.ForeColor = p.Text;
                return;
            }

            // жёлтый текст статуса внизу окна
            if (tag == "status")
            {
                c.BackColor = Color.Transparent;
                c.ForeColor = p.StatusText;
                return;
            }

            var combo = c as ComboBox;
            if (combo != null)
            {
                // FlatStyle.Flat заставляет DropDownList честно красить фон
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = p.PanelBg;
                combo.ForeColor = p.Text;
                return;
            }

            if (c is TextBox || c is ListBox || c is ListView)
            {
                c.BackColor = p.PanelBg;
                c.ForeColor = p.Text;
                return;
            }

            var track = c as TrackBar;
            if (track != null)
            {
                c.BackColor = p.PanelBg;
                return;
            }

            if (tag == "console")
            {
                c.BackColor = p.ConsoleBg;
                c.ForeColor = p.Text;
                return;
            }
            if (tag == "panel")
            {
                c.BackColor = p.PanelBg;
                c.ForeColor = p.Text;
                return;
            }

            // подписи/флажки — прозрачный фон, чтобы совпадать с любым
            // родителем (окно, панель, рамка-группа)
            if (c is Label || c is CheckBox || c is RadioButton)
            {
                c.BackColor = Color.Transparent;
                c.ForeColor = (tag == "dim") ? p.TextDim : p.Text;
                return;
            }

            c.BackColor = p.WindowBg;
            c.ForeColor = (tag == "dim") ? p.TextDim : p.Text;
        }

        // Тёмный заголовок окна (ТЗ 1.4): флаг DWM в Windows 10/11.
        // 20 — DWMWA_USE_IMMERSIVE_DARK_MODE (19 — на старых сборках Win10).
        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr,
                                                ref int value, int size);

        public static void ApplyTitleBar(Form f)
        {
            int dark = Current.IsDark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref dark, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref dark, 4);
            }
            catch { }
        }
    }
}
