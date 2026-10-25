using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace DaikonLine
{
    /// <summary>
    /// IME 状態の監視（DESIGN.md 5.3 / 5.4）。
    /// WinEvent（UI スレッド）で前面・フォーカスの変化を受け、監視スレッドを起こす。
    /// 監視スレッドは一定間隔でも判定し、色が変わったときだけ UI スレッドへ通知する。
    /// </summary>
    internal sealed class ImeMonitor : IDisposable
    {
        private const int FastRetryMs = 30;       // 入力先が変わった直後の再試行の間隔
        private const int NewTargetGraceMs = 200; // 入力先が変わってから灰色にするまでの猶予
        private const int FailuresBeforeGray = 2; // 同じ入力先で何回続けて失敗したら灰色にするか

        /// <summary>色（InputMode）が変わった。UI スレッドで呼ばれる。</summary>
        public event Action<ImeSnapshot> ModeChanged;

        /// <summary>前面のウィンドウが変わった。UI スレッドで呼ばれる。</summary>
        public event Action ForegroundChanged;

        private readonly IImeProbe _probe;
        private readonly Control _invoker;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly NativeMethods.WinEventProc _winEventProc; // GC に回収されないように保持
        private IntPtr _hookForeground;
        private IntPtr _hookFocus;
        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _paused;
        private volatile int _intervalMs = 100;
        private volatile ImeSnapshot _latest;

        // 以下は監視スレッドだけが触る
        private ImeSnapshot _published;
        private IntPtr _pendingTarget;
        private readonly Stopwatch _pendingSince = new Stopwatch();
        private int _failures;

        public ImeMonitor(IImeProbe probe, Control invoker)
        {
            _probe = probe;
            _invoker = invoker;
            _winEventProc = OnWinEvent;
        }

        /// <summary>定期チェックの間隔（ms）</summary>
        public int IntervalMs
        {
            get { return _intervalMs; }
            set { _intervalMs = Math.Max(10, value); _wake.Set(); }
        }

        /// <summary>最後に判定した結果（診断ウィンドウ用。自分自身が前面のときは更新しない）</summary>
        public ImeSnapshot Latest
        {
            get { return _latest; }
        }

        /// <summary>現在ラインに出している結果</summary>
        public InputMode CurrentMode { get; private set; }

        /// <summary>UI スレッドから呼ぶこと（WinEvent は呼んだスレッドのメッセージループで届く）。</summary>
        public void Start()
        {
            if (_thread != null) return;
            uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
            _hookForeground = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
                                                            IntPtr.Zero, _winEventProc, 0, 0, flags);
            _hookFocus = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_FOCUS, NativeMethods.EVENT_OBJECT_FOCUS,
                                                       IntPtr.Zero, _winEventProc, 0, 0, flags);

            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "DaikonLine.ImeMonitor";
            _thread.Start();
        }

        /// <summary>監視を止める（画面ロック・スリープ中）。</summary>
        public void Pause()
        {
            _paused = true;
        }

        /// <summary>監視を再開し、すぐに 1 回判定する。</summary>
        public void Resume()
        {
            _paused = false;
            _wake.Set();
        }

        /// <summary>すぐに判定し直す。</summary>
        public void Poke()
        {
            _wake.Set();
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            _wake.Set();
            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                Action handler = ForegroundChanged;
                if (handler != null)
                {
                    try { handler(); }
                    catch (Exception ex) { Debug.WriteLine(ex); }
                }
            }
        }

        private void Run()
        {
            bool fast = false;
            while (!_stop)
            {
                int wait = _paused ? Timeout.Infinite : (fast ? FastRetryMs : _intervalMs);
                _wake.WaitOne(wait);
                if (_stop) break;
                if (_paused) continue;

                ImeSnapshot s;
                try
                {
                    s = _probe.Probe();
                }
                catch (Exception ex)
                {
                    s = ImeSnapshot.Fail("判定中の例外: " + ex.Message);
                }
                if (s == null)
                {
                    fast = false; // 自分自身が前面。前の状態を保つ
                    continue;
                }
                _latest = s;
                fast = Evaluate(s);
            }
        }

        /// <summary>灰色のルール（DESIGN.md 5.4）を適用して必要なら通知する。</summary>
        /// <returns>すぐに再試行したいとき true</returns>
        private bool Evaluate(ImeSnapshot s)
        {
            if (s.Target != _pendingTarget)
            {
                _pendingTarget = s.Target;
                _pendingSince.Restart();
                _failures = 0;
            }

            if (s.Mode != InputMode.Unknown)
            {
                _failures = 0;
                Publish(s);
                return false;
            }

            _failures++;
            bool sameTarget = _published != null && _published.Target == s.Target;
            if (sameTarget)
            {
                // 今の色を出している入力先での一時的な失敗は、続いたときだけ灰色にする
                if (_failures >= FailuresBeforeGray) Publish(s);
                return false;
            }

            // 入力先が変わった直後：前の色を持ち越さないよう、猶予を過ぎたら灰色
            if (_pendingSince.ElapsedMilliseconds >= NewTargetGraceMs || _published == null)
            {
                Publish(s);
                return false;
            }
            return true;
        }

        private void Publish(ImeSnapshot s)
        {
            bool changed = _published == null || _published.Mode != s.Mode;
            _published = s;
            if (!changed) return;

            try
            {
                if (_invoker.IsHandleCreated && !_invoker.IsDisposed)
                    _invoker.BeginInvoke(new Action<ImeSnapshot>(RaiseModeChanged), s);
            }
            catch (InvalidOperationException) { } // 終了処理中（ObjectDisposedException も含む）
        }

        private void RaiseModeChanged(ImeSnapshot s)
        {
            if (_stop) return;
            CurrentMode = s.Mode;
            Action<ImeSnapshot> handler = ModeChanged;
            if (handler != null) handler(s);
        }

        /// <summary>UI スレッドから呼ぶこと。</summary>
        public void Dispose()
        {
            _stop = true;
            if (_hookForeground != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hookForeground);
            if (_hookFocus != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hookFocus);
            _hookForeground = IntPtr.Zero;
            _hookFocus = IntPtr.Zero;
            _wake.Set();
            bool ended = _thread == null || _thread.Join(1000);
            _thread = null;
            if (ended) _wake.Close(); // 終わっていないスレッドがあれば閉じない（プロセス終了で消える）
        }
    }
}
