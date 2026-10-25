using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DaikonLine
{
    /// <summary>
    /// 状態別のトレイアイコン（DESIGN.md 4.5）。角丸四角を状態の色で塗り、白で「あ」「A」「?」。
    /// 色の設定が変わったときだけ作り直す。
    /// </summary>
    internal sealed class TrayIcons : IDisposable
    {
        private Icon _japanese;
        private Icon _alphanumeric;
        private Icon _unknown;
        private IntPtr[] _handles = new IntPtr[0];
        private Color _cJapanese;
        private Color _cAlphanumeric;
        private Color _cUnknown;

        public void Update(LineSettings s)
        {
            if (_japanese != null && s.Japanese == _cJapanese && s.Alphanumeric == _cAlphanumeric && s.Unknown == _cUnknown)
                return;

            IntPtr[] oldHandles = _handles;
            Icon[] oldIcons = new Icon[] { _japanese, _alphanumeric, _unknown };

            _cJapanese = s.Japanese;
            _cAlphanumeric = s.Alphanumeric;
            _cUnknown = s.Unknown;
            IntPtr h1, h2, h3;
            _japanese = Create(s.Japanese, "あ", out h1);
            _alphanumeric = Create(s.Alphanumeric, "A", out h2);
            _unknown = Create(s.Unknown, "?", out h3);
            _handles = new IntPtr[] { h1, h2, h3 };

            // 古いアイコンは破棄する。呼び出し側は Update の直後に NotifyIcon.Icon を Get の結果に差し替えること
            foreach (Icon ic in oldIcons)
            {
                if (ic != null) ic.Dispose();
            }
            foreach (IntPtr h in oldHandles) NativeMethods.DestroyIcon(h);
        }

        public Icon Get(InputMode mode)
        {
            switch (mode)
            {
                case InputMode.Japanese: return _japanese;
                case InputMode.Alphanumeric: return _alphanumeric;
                default: return _unknown;
            }
        }

        private static Icon Create(Color color, string text, out IntPtr handle)
        {
            Size size = SystemInformation.SmallIconSize;
            int w = Math.Max(16, size.Width);
            int h = Math.Max(16, size.Height);
            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    float r = w * 0.22f;
                    using (GraphicsPath path = RoundRect(new RectangleF(0, 0, w - 1, h - 1), r))
                    using (SolidBrush b = new SolidBrush(color))
                    {
                        g.FillPath(b, path);
                    }

                    string fontName = text == "あ" ? "Yu Gothic UI" : "Segoe UI";
                    using (Font font = new Font(fontName, h * 0.68f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, w, h + 1), sf);
                    }
                }
                handle = bmp.GetHicon();
                return Icon.FromHandle(handle);
            }
        }

        private static GraphicsPath RoundRect(RectangleF rc, float r)
        {
            float d = r * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(rc.Left, rc.Top, d, d, 180, 90);
            p.AddArc(rc.Right - d, rc.Top, d, d, 270, 90);
            p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            p.AddArc(rc.Left, rc.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Dispose()
        {
            foreach (Icon ic in new Icon[] { _japanese, _alphanumeric, _unknown })
            {
                if (ic != null) ic.Dispose();
            }
            foreach (IntPtr h in _handles) NativeMethods.DestroyIcon(h);
            _handles = new IntPtr[0];
            _japanese = _alphanumeric = _unknown = null;
        }
    }
}
