using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DaikonLine
{
    /// <summary>
    /// モニターごとのライン（LineForm）の管理（DESIGN.md 4.2〜4.4）。
    /// IME のことは知らず、色を受け取って塗るだけ。
    /// </summary>
    internal sealed class LineManager : IDisposable
    {
        private const int CheckIntervalMs = 250; // 全画面・操作中のモニターの確認間隔

        private readonly List<LineForm> _lines = new List<LineForm>();
        private readonly Timer _checkTimer = new Timer();
        private readonly uint _ownPid;
        private LineSettings _settings = new LineSettings();
        private Color _color;
        private bool _visible = true;
        private Rectangle _fullscreenScreen = Rectangle.Empty; // 全画面アプリがあるモニター
        private Rectangle _activeScreen = Rectangle.Empty;     // 前面ウィンドウがあるモニター

        public LineManager()
        {
            using (Process p = Process.GetCurrentProcess()) _ownPid = (uint)p.Id;
            _color = _settings.Unknown;
            _checkTimer.Interval = CheckIntervalMs;
            _checkTimer.Tick += delegate { CheckForeground(); };
        }

        public bool Visible
        {
            get { return _visible; }
            set
            {
                if (_visible == value) return;
                _visible = value;
                UpdateVisibility();
            }
        }

        /// <summary>設定を反映する（太さ・モニターが変わったらラインを作り直す）。</summary>
        public void Apply(LineSettings settings)
        {
            bool rebuild = _lines.Count == 0 || settings.Thickness != _settings.Thickness || settings.Monitors != _settings.Monitors;
            _settings = settings.Clone();
            if (rebuild)
            {
                Rebuild();
            }
            else
            {
                foreach (LineForm f in _lines) f.Alpha = _settings.Alpha; // 透過率は作り直さずに変える
                UpdateVisibility();
            }
        }

        public void SetColor(Color color)
        {
            _color = color;
            foreach (LineForm f in _lines) f.LineColor = color;
        }

        /// <summary>モニターの構成を取り直してラインを作り直す（接続・解像度・倍率の変更時）。</summary>
        public void Rebuild()
        {
            DisposeLines();
            Screen[] screens = _settings.Monitors == MonitorTarget.Primary
                ? new Screen[] { Screen.PrimaryScreen }
                : Screen.AllScreens;
            foreach (Screen sc in screens)
            {
                if (sc == null) continue;
                _lines.Add(new LineForm(sc.Bounds, _settings.Thickness, _color, _settings.Alpha));
            }
            _fullscreenScreen = Rectangle.Empty;
            _activeScreen = Rectangle.Empty;
            CheckForeground(true);
        }

        /// <summary>前面ウィンドウが変わったとき。最前面に戻し、全画面・操作中のモニターを確認する。</summary>
        public void OnForegroundChanged()
        {
            CheckForeground();
            foreach (LineForm f in _lines)
            {
                if (f.Visible) f.BringToTopMost();
            }
        }

        private void CheckForeground()
        {
            CheckForeground(false);
        }

        private void CheckForeground(bool force)
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            uint pid = 0;
            if (fg != IntPtr.Zero) NativeMethods.GetWindowThreadProcessId(fg, out pid);

            Rectangle full = _fullscreenScreen;
            Rectangle active = _activeScreen;
            if (fg == IntPtr.Zero)
            {
                full = Rectangle.Empty;
            }
            else if (pid != _ownPid) // 自分のメニューや設定画面のときは前の状態を保つ
            {
                NativeMethods.RECT mon;
                if (NativeMethods.TryGetMonitorRect(fg, out mon))
                {
                    Rectangle monRect = Rectangle.FromLTRB(mon.Left, mon.Top, mon.Right, mon.Bottom);
                    active = monRect;
                    full = IsFullscreen(fg, mon) ? monRect : Rectangle.Empty;
                }
            }

            if (force || full != _fullscreenScreen || active != _activeScreen)
            {
                _fullscreenScreen = full;
                _activeScreen = active;
                UpdateVisibility();
            }
        }

        // 画面全体（作業領域ではない）を覆い、最大化ではないウィンドウを全画面とみなす（DESIGN.md 4.3）
        private static bool IsFullscreen(IntPtr hwnd, NativeMethods.RECT mon)
        {
            string cls = NativeMethods.GetClassNameOf(hwnd);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            if (NativeMethods.IsZoomed(hwnd) || NativeMethods.IsIconic(hwnd)) return false;
            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(hwnd, out r)) return false;
            return r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom;
        }

        private void UpdateVisibility()
        {
            foreach (LineForm f in _lines)
            {
                bool show = _visible;
                if (show && _settings.Monitors == MonitorTarget.Active && !_activeScreen.IsEmpty)
                    show = f.ScreenBounds == _activeScreen;
                if (show && _settings.HideInFullscreen && f.ScreenBounds == _fullscreenScreen)
                    show = false;

                if (show && !f.Visible)
                {
                    f.Show(); // ShowWithoutActivation なのでフォーカスは移らない
                    f.ApplyBounds(); // 表示時に大きさを変えられていても指定どおりに戻す
                }
                else if (!show && f.Visible)
                {
                    f.Hide();
                }
            }
            // 全画面・操作中のモニターの確認は、表示しているときだけ
            _checkTimer.Enabled = _visible && _lines.Count > 0 &&
                                  (_settings.HideInFullscreen || _settings.Monitors == MonitorTarget.Active);
        }

        /// <summary>各ラインの指定サイズと実際のサイズ（診断用）</summary>
        public string Describe()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                LineForm f = _lines[i];
                Rectangle want = f.LineBounds;
                Rectangle actual = f.ActualBounds;
                sb.Append("ライン ").Append(i + 1).Append("       : ");
                sb.Append(want.Width).Append("x").Append(want.Height).Append(" @ (").Append(want.X).Append(",").Append(want.Y).Append(")");
                if (!f.Visible) sb.Append(" 非表示");
                else sb.Append(" / 実際 ").Append(actual.Width).Append("x").Append(actual.Height)
                       .Append(" @ (").Append(actual.X).Append(",").Append(actual.Y).Append(")");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void DisposeLines()
        {
            foreach (LineForm f in _lines) f.Dispose();
            _lines.Clear();
        }

        public void Dispose()
        {
            _checkTimer.Stop();
            _checkTimer.Dispose();
            DisposeLines();
        }
    }
}
