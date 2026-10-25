using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace DaikonLine
{
    /// <summary>
    /// 診断ウィンドウ（DESIGN.md 6.3）。判定の中身を表示する。
    /// 自分が前面のあいだは判定が止まるので、最後に判定した他のアプリの情報がそのまま残り、コピーできる。
    /// ウィンドウタイトルは個人的な情報を含みやすいので表示しない。
    /// </summary>
    internal sealed class DiagnosticsForm : Form
    {
        private readonly ImeMonitor _monitor;
        private readonly LineManager _lines;
        private readonly TextBox _text;
        private readonly Timer _timer = new Timer();

        public DiagnosticsForm(ImeMonitor monitor, LineManager lines)
        {
            _monitor = monitor;
            _lines = lines;

            Text = "DaikonLine 診断";
            TopMost = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            Font = SystemFonts.MessageBoxFont;
            using (Icon ic = AppIcon.Load()) Icon = (Icon)ic.Clone();

            int em = Font.Height;
            ClientSize = new Size(em * 30, em * 20);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - em, wa.Top + em * 2);

            _text = new TextBox();
            _text.Multiline = true;
            _text.ReadOnly = true;
            _text.ScrollBars = ScrollBars.Vertical;
            _text.Dock = DockStyle.Fill;
            _text.Font = new Font("Consolas", Font.SizeInPoints);
            _text.BackColor = SystemColors.Window;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;
            bar.FlowDirection = FlowDirection.RightToLeft;

            Button close = new Button();
            close.Text = "閉じる";
            close.AutoSize = true;
            close.Click += delegate { Close(); };
            Button copy = new Button();
            copy.Text = "コピー";
            copy.AutoSize = true;
            copy.Click += delegate { CopyToClipboard(); };
            bar.Controls.Add(close);
            bar.Controls.Add(copy);

            Label note = new Label();
            note.Text = "確かめたいアプリの入力欄をクリックしてから、ここに戻って「コピー」を押してください。";
            note.AutoSize = true;
            note.Dock = DockStyle.Top;
            note.Padding = new Padding(4);

            Controls.Add(_text);
            Controls.Add(bar);
            Controls.Add(note);

            _timer.Interval = 200;
            _timer.Tick += delegate { UpdateText(false); };
            _timer.Start();
            UpdateText(true);
        }

        private void UpdateText(bool force)
        {
            ImeSnapshot s = _monitor.Latest;
            string text = Format(s, _monitor.CurrentMode, _lines.Describe());
            if (!force && text == _text.Text) return; // 変わらなければ書き換えない（選択が消えないように）
            _text.Text = text;
        }

        private void CopyToClipboard()
        {
            try
            {
                Clipboard.SetText(_text.Text);
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                MessageBox.Show(this, "クリップボードにコピーできませんでした。\n" + ex.Message, "DaikonLine");
            }
        }

        private string Format(ImeSnapshot s, InputMode shownMode, string lines)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DaikonLine " + Application.ProductVersion);
            sb.AppendLine("OS            : " + Environment.OSVersion.VersionString);
            sb.AppendLine("ラインの色    : " + ModeText(shownMode));
            sb.AppendLine("DPI 倍率      : " + DpiText());
            sb.Append(lines);
            sb.AppendLine();
            if (s == null)
            {
                sb.AppendLine("（まだ判定していません）");
                return sb.ToString();
            }
            sb.AppendLine("判定時刻      : " + s.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.AppendLine("判定結果      : " + ModeText(s.Mode));
            sb.AppendLine("不明の理由    : " + (s.Failure ?? "-"));
            sb.AppendLine("プロセス      : " + (s.ProcessName.Length > 0 ? s.ProcessName : "?") + " (PID " + s.ProcessId + ")");
            sb.AppendLine("前面のクラス  : " + s.ForegroundClass);
            sb.AppendLine("入力先クラス  : " + s.TargetClass);
            sb.AppendLine("入力先 HWND   : 0x" + s.Target.ToInt64().ToString("X", CultureInfo.InvariantCulture));
            sb.AppendLine("キーボード    : 0x" + s.Hkl.ToInt64().ToString("X8", CultureInfo.InvariantCulture));
            sb.AppendLine("IME オン/オフ : " + (s.OpenStatus < 0 ? "-" : (s.OpenStatus != 0 ? "オン (1)" : "オフ (0)")));
            sb.AppendLine("変換モード    : " + ConversionText(s.ConversionMode));
            sb.AppendLine("判定時間      : " + s.ElapsedMs.ToString("0.000", CultureInfo.InvariantCulture) + " ms");
            return sb.ToString();
        }

        private string DpiText()
        {
            using (Graphics g = CreateGraphics())
            {
                return (g.DpiX / 96f * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
            }
        }

        private static string ModeText(InputMode mode)
        {
            switch (mode)
            {
                case InputMode.Japanese: return "日本語（緑）";
                case InputMode.Alphanumeric: return "英数（赤）";
                default: return "不明（灰）";
            }
        }

        private static string ConversionText(int mode)
        {
            if (mode < 0) return "-";
            StringBuilder sb = new StringBuilder("0x" + mode.ToString("X4", CultureInfo.InvariantCulture));
            if ((mode & NativeMethods.IME_CMODE_NATIVE) != 0) sb.Append(" NATIVE");
            if ((mode & NativeMethods.IME_CMODE_KATAKANA) != 0) sb.Append(" KATAKANA");
            if ((mode & NativeMethods.IME_CMODE_FULLSHAPE) != 0) sb.Append(" FULLSHAPE");
            if ((mode & NativeMethods.IME_CMODE_ROMAN) != 0) sb.Append(" ROMAN");
            return sb.ToString();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
                _text.Font.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
