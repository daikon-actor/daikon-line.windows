using System;

namespace DaikonLine
{
    /// <summary>判定結果。ラインの色に対応する。</summary>
    internal enum InputMode
    {
        Unknown,
        Japanese,
        Alphanumeric
    }

    /// <summary>1 回の判定の結果と、診断用の中身。</summary>
    internal sealed class ImeSnapshot
    {
        public InputMode Mode;
        public IntPtr Foreground;     // 前面のウィンドウ
        public string ForegroundClass = "";
        public IntPtr Target;         // 判定したウィンドウ（フォーカスのあるウィンドウ）
        public string TargetClass = "";
        public uint ProcessId;
        public string ProcessName = "";
        public IntPtr Hkl;
        public int OpenStatus = -1;     // -1 = 取得していない / 失敗
        public int ConversionMode = -1; // -1 = 取得していない / 失敗
        public string Failure;          // 不明になった理由（判定できたときは null）
        public double ElapsedMs;        // 判定にかかった時間
        public DateTime Time;

        public static ImeSnapshot Fail(string reason)
        {
            ImeSnapshot s = new ImeSnapshot();
            s.Mode = InputMode.Unknown;
            s.Failure = reason;
            return s;
        }
    }
}
