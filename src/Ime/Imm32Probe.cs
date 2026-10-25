using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DaikonLine
{
    /// <summary>
    /// IMM32 の「既定の IME ウィンドウ」に WM_IME_CONTROL で問い合わせる方式（DESIGN.md 5.2）。
    /// 他プロセスの入力コンテキストは ImmGetContext では読めないため、この方法を使う。
    /// </summary>
    internal sealed class Imm32Probe : IImeProbe
    {
        private const int LangJapanese = 0x0411;
        private const int PrimaryLangChinese = 0x04;
        private const int PrimaryLangJapanese = 0x11;
        private const int PrimaryLangKorean = 0x12;
        private const uint TimeoutMs = 50;

        private readonly uint _ownPid;
        private readonly Dictionary<uint, string> _processNames = new Dictionary<uint, string>();

        public Imm32Probe()
        {
            using (Process p = Process.GetCurrentProcess()) _ownPid = (uint)p.Id;
        }

        public ImeSnapshot Probe()
        {
            Stopwatch sw = Stopwatch.StartNew();
            ImeSnapshot s = ProbeCore();
            if (s != null)
            {
                s.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                s.Time = DateTime.Now;
            }
            return s;
        }

        private ImeSnapshot ProbeCore()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return ImeSnapshot.Fail("前面のウィンドウがありません");

            uint fgPid;
            uint fgTid = NativeMethods.GetWindowThreadProcessId(fg, out fgPid);
            if (fgPid == _ownPid) return null; // トレイメニューや設定画面。前の状態を保つ

            ImeSnapshot s = new ImeSnapshot();
            s.Foreground = fg;
            s.ForegroundClass = NativeMethods.GetClassNameOf(fg);

            // UWP アプリは枠（ApplicationFrameHost）と中身（CoreWindow）が別スレッド・別プロセス
            IntPtr fallback = fg;
            uint guiTid = fgTid;
            if (s.ForegroundClass == "ApplicationFrameWindow")
            {
                IntPtr core = NativeMethods.FindWindowEx(fg, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
                if (core != IntPtr.Zero)
                {
                    uint corePid;
                    guiTid = NativeMethods.GetWindowThreadProcessId(core, out corePid);
                    fallback = core;
                }
            }

            // フォーカスのあるウィンドウ → 無ければアクティブウィンドウ → 前面ウィンドウ
            IntPtr target = IntPtr.Zero;
            NativeMethods.GUITHREADINFO gti = new NativeMethods.GUITHREADINFO();
            gti.cbSize = Marshal.SizeOf(typeof(NativeMethods.GUITHREADINFO));
            if (guiTid != 0 && NativeMethods.GetGUIThreadInfo(guiTid, ref gti))
            {
                target = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : gti.hwndActive;
            }
            if (target == IntPtr.Zero) target = fallback;

            uint pid;
            uint tid = NativeMethods.GetWindowThreadProcessId(target, out pid);
            s.Target = target;
            s.TargetClass = NativeMethods.GetClassNameOf(target);
            s.ProcessId = pid;
            s.ProcessName = GetProcessName(pid);
            if (tid == 0)
            {
                s.Failure = "入力先のスレッドが取れません";
                return s;
            }

            // キーボード配列が日本語でなければ、英数か対象外の IME
            s.Hkl = NativeMethods.GetKeyboardLayout(tid);
            int lang = (int)((long)s.Hkl & 0xFFFF);
            if (lang == 0)
            {
                s.Failure = "キーボード配列が取れません";
                return s;
            }
            if (lang != LangJapanese)
            {
                int primary = lang & 0x3FF;
                if (primary == PrimaryLangChinese || primary == PrimaryLangJapanese || primary == PrimaryLangKorean)
                {
                    s.Failure = "日本語以外の IME（対象外）";
                    return s;
                }
                s.Mode = InputMode.Alphanumeric; // 英語キーボードなど
                return s;
            }

            IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(target);
            if (imeWnd == IntPtr.Zero)
            {
                s.Failure = "IME ウィンドウがありません";
                return s;
            }

            int open;
            if (!Query(imeWnd, NativeMethods.IMC_GETOPENSTATUS, out open))
            {
                s.Failure = FailureText("オン/オフの取得に失敗");
                return s;
            }
            s.OpenStatus = open;
            if (open == 0)
            {
                s.Mode = InputMode.Alphanumeric;
                return s;
            }

            int conv;
            if (!Query(imeWnd, NativeMethods.IMC_GETCONVERSIONMODE, out conv))
            {
                s.Failure = FailureText("入力モードの取得に失敗");
                return s;
            }
            s.ConversionMode = conv;
            s.Mode = (conv & NativeMethods.IME_CMODE_NATIVE) != 0 ? InputMode.Japanese : InputMode.Alphanumeric;
            return s;
        }

        private static bool Query(IntPtr imeWnd, int command, out int value)
        {
            IntPtr result;
            IntPtr ok = NativeMethods.SendMessageTimeout(imeWnd, NativeMethods.WM_IME_CONTROL, new IntPtr(command), IntPtr.Zero,
                                                         NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs, out result);
            value = (int)(long)result;
            return ok != IntPtr.Zero;
        }

        private static string FailureText(string what)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 5) return what + "（アクセス拒否。管理者として実行中のアプリの可能性）";
            if (err == 1460) return what + "（タイムアウト）";
            return err != 0 ? what + "（エラー " + err + "）" : what;
        }

        // プロセス名はプロセス ID ごとに覚えておく（診断・ツールチップ用）
        private string GetProcessName(uint pid)
        {
            string name;
            if (_processNames.TryGetValue(pid, out name)) return name;
            if (_processNames.Count > 64) _processNames.Clear();

            name = "";
            IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h != IntPtr.Zero)
            {
                try
                {
                    StringBuilder sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (NativeMethods.QueryFullProcessImageName(h, 0, sb, ref size))
                        name = Path.GetFileName(sb.ToString(0, size));
                }
                finally
                {
                    NativeMethods.CloseHandle(h);
                }
            }
            _processNames[pid] = name;
            return name;
        }
    }
}
