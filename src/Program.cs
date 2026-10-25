// DaikonLine - 画面の最上端に IME の入力モードを色のラインで表示する常駐アプリ
// Windows 付属の csc.exe (C# 5) でビルドできるよう、C# 5 の範囲で書くこと。

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace DaikonLine
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "DaikonLine_SingleInstance_5C1E", out created))
            {
                if (!created) return; // 二重起動しない

                DeclareDpiAwareness();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // 想定外の例外でも常駐を落とさない（DESIGN.md 9 章）
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Debug.WriteLine(e.Exception);
                };

                Application.Run(new AppController());
                GC.KeepAlive(mutex);
            }
        }

        // 座標を物理ピクセルで扱うため Per-Monitor DPI を宣言する（DESIGN.md 4.4）。
        // Windows 10 1703 以降 → 8.1 以降 → それより前、の順に試す。
        private static void DeclareDpiAwareness()
        {
            try
            {
                if (NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return;
            }
            catch (EntryPointNotFoundException) { }

            try
            {
                if (NativeMethods.SetProcessDpiAwareness(NativeMethods.PROCESS_PER_MONITOR_DPI_AWARE) == 0) return;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            try { NativeMethods.SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
        }
    }
}
