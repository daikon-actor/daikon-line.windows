using System;
using System.Drawing;
using System.Windows.Forms;

namespace DaikonLine
{
    /// <summary>
    /// 設定画面（DESIGN.md 6.2）。変更はその場で Preview に通知し、OK で確定・キャンセルで元に戻す。
    /// 大きさは固定せず AutoSize で組むので、表示倍率が違っても崩れない。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        /// <summary>設定が変わるたびに呼ばれる（プレビュー用）</summary>
        public event Action<LineSettings> Preview;

        private readonly LineSettings _settings;
        private readonly Button _japaneseButton;
        private readonly Button _alphanumericButton;
        private readonly Button _unknownButton;
        private readonly NumericUpDown _thickness;
        private readonly NumericUpDown _transparency;
        private readonly ComboBox _monitors;
        private readonly NumericUpDown _interval;
        private readonly CheckBox _hideFullscreen;
        private readonly int _em; // 文字の高さ。幅はこれを基準にする（表示倍率に追従）
        private bool _loading;

        public SettingsForm(LineSettings current)
        {
            _settings = current.Clone();

            Text = "DaikonLine の設定";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true; // トレイから開くので、見失わないようにタスクバーに出す
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            using (Icon ic = AppIcon.Load()) Icon = (Icon)ic.Clone();
            _em = Font.Height;

            TableLayoutPanel table = new TableLayoutPanel();
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 2;
            table.Dock = DockStyle.Fill;

            _japaneseButton = AddColorRow(table, "日本語（あ）の色");
            _alphanumericButton = AddColorRow(table, "英数（A）の色");
            _unknownButton = AddColorRow(table, "不明の色");

            _thickness = new NumericUpDown();
            _thickness.Minimum = LineSettings.MinThickness;
            _thickness.Maximum = LineSettings.MaxThickness;
            _thickness.Width = _em * 5;
            _thickness.ValueChanged += delegate { OnChanged(); };
            AddRow(table, "ラインの太さ（px）", _thickness);

            _transparency = new NumericUpDown();
            _transparency.Minimum = LineSettings.MinTransparency;
            _transparency.Maximum = LineSettings.MaxTransparency;
            _transparency.Increment = 10;
            _transparency.Width = _em * 5;
            _transparency.ValueChanged += delegate { OnChanged(); };
            AddRow(table, "透過率（%）", _transparency);

            _monitors = new ComboBox();
            _monitors.DropDownStyle = ComboBoxStyle.DropDownList;
            _monitors.Items.AddRange(new object[] { "すべてのモニター", "メインのモニターだけ", "操作中のモニターだけ" });
            _monitors.Width = _em * 14;
            _monitors.SelectedIndexChanged += delegate { OnChanged(); };
            AddRow(table, "表示するモニター", _monitors);

            _interval = new NumericUpDown();
            _interval.Minimum = LineSettings.MinInterval;
            _interval.Maximum = LineSettings.MaxInterval;
            _interval.Increment = 10;
            _interval.Width = _em * 5;
            _interval.ValueChanged += delegate { OnChanged(); };
            AddRow(table, "更新間隔（ms）", _interval);

            _hideFullscreen = new CheckBox();
            _hideFullscreen.Text = "全画面のアプリ（動画・スライドショーなど）の上では隠す";
            _hideFullscreen.AutoSize = true;
            _hideFullscreen.Margin = new Padding(3, 8, 3, 3);
            _hideFullscreen.CheckedChanged += delegate { OnChanged(); };
            table.Controls.Add(_hideFullscreen, 0, table.RowCount);
            table.SetColumnSpan(_hideFullscreen, 2);
            table.RowCount++;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.Margin = new Padding(0, 12, 0, 0);
            buttons.Anchor = AnchorStyles.Right;

            Button reset = NewButton("既定に戻す");
            reset.Click += delegate { LoadControls(new LineSettings()); OnChanged(); };
            Button ok = NewButton("OK");
            ok.DialogResult = DialogResult.OK;
            Button cancel = NewButton("キャンセル");
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(reset);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            table.Controls.Add(buttons, 0, table.RowCount);
            table.SetColumnSpan(buttons, 2);
            table.RowCount++;

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(table);

            _japaneseButton.Click += delegate { PickColor(_japaneseButton); };
            _alphanumericButton.Click += delegate { PickColor(_alphanumericButton); };
            _unknownButton.Click += delegate { PickColor(_unknownButton); };

            LoadControls(_settings);
        }

        /// <summary>画面で選んだ設定</summary>
        public LineSettings Result
        {
            get { return Collect(); }
        }

        private void LoadControls(LineSettings s)
        {
            _loading = true;
            SetSwatch(_japaneseButton, s.Japanese);
            SetSwatch(_alphanumericButton, s.Alphanumeric);
            SetSwatch(_unknownButton, s.Unknown);
            _thickness.Value = s.Thickness;
            _transparency.Value = s.Transparency;
            _interval.Value = s.PollIntervalMs;
            _monitors.SelectedIndex = (int)s.Monitors;
            _hideFullscreen.Checked = s.HideInFullscreen;
            _loading = false;
        }

        private LineSettings Collect()
        {
            LineSettings s = _settings.Clone();
            s.Japanese = _japaneseButton.BackColor;
            s.Alphanumeric = _alphanumericButton.BackColor;
            s.Unknown = _unknownButton.BackColor;
            s.Thickness = (int)_thickness.Value;
            s.Transparency = (int)_transparency.Value;
            s.PollIntervalMs = (int)_interval.Value;
            s.Monitors = _monitors.SelectedIndex < 0 ? MonitorTarget.All : (MonitorTarget)_monitors.SelectedIndex;
            s.HideInFullscreen = _hideFullscreen.Checked;
            s.Normalize();
            return s;
        }

        private void OnChanged()
        {
            if (_loading) return;
            Action<LineSettings> handler = Preview;
            if (handler != null) handler(Collect());
        }

        private void PickColor(Button button)
        {
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = button.BackColor;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                SetSwatch(button, dlg.Color);
                OnChanged();
            }
        }

        private static void SetSwatch(Button button, Color color)
        {
            button.BackColor = Color.FromArgb(color.R, color.G, color.B);
            button.ForeColor = (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 150 ? Color.Black : Color.White;
            button.Text = ConfigStore.ToHex(color);
        }

        private Button AddColorRow(TableLayoutPanel table, string label)
        {
            Button b = new Button();
            b.FlatStyle = FlatStyle.Flat;
            b.AutoSize = true;
            b.MinimumSize = new Size(_em * 7, 0);
            AddRow(table, label, b);
            return b;
        }

        private static void AddRow(TableLayoutPanel table, string label, Control control)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(3, 6, 12, 6);
            control.Anchor = AnchorStyles.Left;
            table.Controls.Add(l, 0, table.RowCount);
            table.Controls.Add(control, 1, table.RowCount);
            table.RowCount++;
        }

        private Button NewButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.MinimumSize = new Size(_em * 5, 0);
            return b;
        }
    }
}
