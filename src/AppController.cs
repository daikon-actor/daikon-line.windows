using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DaikonLine
{
    /// <summary>
    /// 常駐部分のつなぎ役。トレイ・メニュー・設定・IME の監視・ラインを結び付ける。
    /// 依存の向きはここから各部品への一方向（ImeMonitor と LineManager はお互いを知らない）。
    /// </summary>
    internal sealed class AppController : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "DaikonLine";
        private const string AppName = "DaikonLine";

        private readonly ConfigStore _config;
        private readonly Control _invoker;
        private readonly LineManager _lines;
        private readonly TrayIcons _icons;
        private readonly ImeMonitor _monitor;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _visibleItem;
        private readonly ToolStripMenuItem _startupItem;
        private LineSettings _settings;
        private InputMode _mode = InputMode.Unknown;
        private SettingsForm _settingsForm;
        private DiagnosticsForm _diagForm;
        private bool _disposed;

        public AppController()
        {
            _config = new ConfigStore(AppDomain.CurrentDomain.BaseDirectory);
            string loadError;
            _settings = _config.Load(out loadError);

            // 監視スレッドから UI スレッドへ戻すための窓口（ウィンドウハンドルだけ作る）
            _invoker = new Control();
            _invoker.CreateControl();
            IntPtr forceHandle = _invoker.Handle;
            GC.KeepAlive(forceHandle);

            _lines = new LineManager();
            _icons = new TrayIcons();
            _icons.Update(_settings);

            _menu = new ContextMenuStrip();
            ToolStripMenuItem versionItem = new ToolStripMenuItem(AppName + " " + Application.ProductVersion);
            versionItem.Enabled = false;
            _menu.Items.Add(versionItem);
            _menu.Items.Add(new ToolStripSeparator());
            _visibleItem = AddMenuItem("ラインを表示する", delegate { ToggleVisible(); });
            AddMenuItem("設定...", delegate { OpenSettings(); });
            AddMenuItem("診断ウィンドウ", delegate { OpenDiagnostics(); });
            _menu.Items.Add(new ToolStripSeparator());
            _startupItem = AddMenuItem("Windows 起動時に実行", delegate { SetStartup(!IsStartup()); });
            _menu.Items.Add(new ToolStripSeparator());
            AddMenuItem("終了", delegate { ExitApp(); });
            _menu.Opening += delegate
            {
                _startupItem.Checked = IsStartup();
                _visibleItem.Checked = _settings.Visible;
            };

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = _menu;
            _tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ToggleVisible();
            };
            UpdateTray();
            _tray.Visible = true;

            _lines.Visible = _settings.Visible;
            _lines.SetColor(_settings.ColorOf(_mode));
            _lines.Apply(_settings);

            _monitor = new ImeMonitor(new Imm32Probe(), _invoker);
            _monitor.IntervalMs = _settings.PollIntervalMs;
            _monitor.ModeChanged += OnModeChanged;
            _monitor.ForegroundChanged += delegate { _lines.OnForegroundChanged(); };
            _monitor.Start();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            if (loadError != null)
            {
                MessageBox.Show("設定ファイルを読み込めなかったため、既定の設定で起動します。\n\n" + loadError,
                                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------- IME ----------------

        private void OnModeChanged(ImeSnapshot s)
        {
            _mode = s.Mode;
            _lines.SetColor(_settings.ColorOf(_mode));
            UpdateTray();
        }

        private void UpdateTray()
        {
            _tray.Icon = _icons.Get(_mode);
            string text;
            switch (_mode)
            {
                case InputMode.Japanese: text = "あ 日本語"; break;
                case InputMode.Alphanumeric: text = "A 英数"; break;
                default: text = "? 不明"; break;
            }
            if (!_settings.Visible) text += "（ライン非表示）";
            text = AppName + ": " + text;
            _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; // NotifyIcon の上限
        }

        // ---------------- 設定 ----------------

        /// <summary>設定を画面に反映する（保存はしない）</summary>
        private void ApplySettings(LineSettings s)
        {
            _settings = s.Clone();
            _lines.Visible = _settings.Visible;
            _lines.SetColor(_settings.ColorOf(_mode));
            _lines.Apply(_settings);
            _monitor.IntervalMs = _settings.PollIntervalMs;
            _icons.Update(_settings);
            UpdateTray(); // Update の直後にアイコンを差し替える
        }

        private bool SaveSettings()
        {
            try
            {
                _config.Save(_settings);
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                MessageBox.Show("設定を保存できませんでした。\n" + _config.FilePath + "\n\n" + ex.Message +
                                "\n\nDaikonLine のフォルダを、書き込みできる場所（C:\\Program Files 以外）に置いてください。",
                                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void ToggleVisible()
        {
            LineSettings s = _settings.Clone();
            s.Visible = !s.Visible;
            ApplySettings(s);
            SaveSettings();
        }

        private void OpenSettings()
        {
            if (_settingsForm != null)
            {
                _settingsForm.Activate();
                return;
            }

            LineSettings original = _settings.Clone();
            using (SettingsForm form = new SettingsForm(original))
            {
                _settingsForm = form;
                form.Preview += delegate(LineSettings s) { ApplySettings(s); };
                DialogResult result = form.ShowDialog();
                _settingsForm = null;
                if (_disposed) return;

                if (result == DialogResult.OK)
                {
                    ApplySettings(form.Result);
                    SaveSettings();
                }
                else
                {
                    ApplySettings(original);
                }
            }
        }

        private void OpenDiagnostics()
        {
            if (_diagForm != null && !_diagForm.IsDisposed)
            {
                _diagForm.Activate();
                return;
            }
            _diagForm = new DiagnosticsForm(_monitor, _lines);
            _diagForm.FormClosed += delegate { _diagForm = null; }; // モードレスのフォームは閉じると破棄される
            _diagForm.Show();
        }

        private ToolStripMenuItem AddMenuItem(string text, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += onClick;
            _menu.Items.Add(item);
            return item;
        }

        // ---------------- Windows のイベント ----------------
        // SystemEvents は別スレッドから呼ばれることがあるので、UI スレッドに戻してから処理する

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            RunOnUi(delegate { _lines.Rebuild(); });
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                    _monitor.Pause();
                    break;
                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.RemoteConnect:
                    _monitor.Resume();
                    RunOnUi(delegate { _lines.Rebuild(); }); // リモート接続などでモニター構成が変わることがある
                    break;
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _monitor.Pause();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                _monitor.Resume();
                RunOnUi(delegate { _lines.Rebuild(); });
            }
        }

        private void RunOnUi(MethodInvoker action)
        {
            if (_disposed) return;
            try
            {
                if (_invoker.InvokeRequired) _invoker.BeginInvoke(action);
                else action();
            }
            catch (InvalidOperationException) { }
        }

        // ---------------- 自動起動 ----------------

        private static bool IsStartup()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return key != null && key.GetValue(RunValueName) != null;
            }
        }

        private static void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is UnauthorizedAccessException) && !(ex is System.Security.SecurityException) && !(ex is IOException)) throw;
                MessageBox.Show("自動起動の設定を変更できませんでした。\n" + ex.Message, AppName,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------- 終了 ----------------

        private void ExitApp()
        {
            Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                _monitor.Dispose();
                if (_settingsForm != null) _settingsForm.Close();
                if (_diagForm != null) _diagForm.Close();
                _lines.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
                _menu.Dispose();
                _icons.Dispose();
                _invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
