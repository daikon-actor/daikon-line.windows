using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DaikonLine
{
    /// <summary>
    /// 1 モニター分のライン（DESIGN.md 4.1）。
    /// クリック透過・フォーカスを奪わない・Alt+Tab に出ない・最前面。
    /// 座標は物理ピクセル（Program で Per-Monitor DPI を宣言しているため）。
    /// </summary>
    internal sealed class LineForm : Form
    {
        private Rectangle _lineBounds;
        private byte _alpha;

        public LineForm(Rectangle screenBounds, int thickness, Color color, byte alpha)
        {
            _alpha = alpha;
            Text = "DaikonLine";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = color;
            AutoScaleMode = AutoScaleMode.None; // 表示倍率で大きさを変えさせない
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            ScreenBounds = screenBounds;
            _lineBounds = new Rectangle(screenBounds.Left, screenBounds.Top, screenBounds.Width, thickness);
            Bounds = _lineBounds;
        }

        /// <summary>不透明度（0〜255）。255 = 不透明</summary>
        public byte Alpha
        {
            get { return _alpha; }
            set
            {
                if (_alpha == value) return;
                _alpha = value;
                if (IsHandleCreated) NativeMethods.SetLayeredWindowAttributes(Handle, 0, _alpha, NativeMethods.LWA_ALPHA);
            }
        }

        /// <summary>指定したラインの位置と大きさ（物理ピクセル）</summary>
        public Rectangle LineBounds
        {
            get { return _lineBounds; }
        }

        /// <summary>実際のウィンドウの位置と大きさ（診断用）</summary>
        public Rectangle ActualBounds
        {
            get
            {
                NativeMethods.RECT r;
                if (!IsHandleCreated || !NativeMethods.GetWindowRect(Handle, out r)) return Rectangle.Empty;
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
        }

        /// <summary>このラインを出すモニターの画面全体</summary>
        public Rectangle ScreenBounds { get; private set; }

        public Color LineColor
        {
            get { return BackColor; }
            set
            {
                if (BackColor == value) return;
                BackColor = value;
                Invalidate();
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                              NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW |
                              NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WS_EX_LAYERED は透明度を設定しないと何も表示されない
            NativeMethods.SetLayeredWindowAttributes(Handle, 0, _alpha, NativeMethods.LWA_ALPHA);
            ApplyBounds();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // OnPaint でまとめて塗る（ちらつき防止）
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        /// <summary>最前面に戻す（他の TopMost ウィンドウの下に回ったとき用）</summary>
        public void BringToTopMost()
        {
            if (!IsHandleCreated) return;
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                                       NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        // WinForms が大きさを変えようとしても（自動スケーリングなど）、常に指定の位置・大きさにする
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (_lineBounds.IsEmpty)
            {
                base.SetBoundsCore(x, y, width, height, specified);
                return;
            }
            base.SetBoundsCore(_lineBounds.X, _lineBounds.Y, _lineBounds.Width, _lineBounds.Height, BoundsSpecified.All);
        }

        /// <summary>指定の位置・大きさに置き直して最前面にする</summary>
        public void ApplyBounds()
        {
            if (!IsHandleCreated) return;
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, _lineBounds.X, _lineBounds.Y,
                                       _lineBounds.Width, _lineBounds.Height, NativeMethods.SWP_NOACTIVATE);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case NativeMethods.WM_MOUSEACTIVATE:
                    m.Result = new IntPtr(NativeMethods.MA_NOACTIVATE);
                    return;
                case NativeMethods.WM_NCHITTEST:
                    m.Result = new IntPtr(NativeMethods.HTTRANSPARENT);
                    return;
                case NativeMethods.WM_WINDOWPOSCHANGING:
                    // Windows 側から大きさを変えられそうになっても、指定の位置・大きさに戻す
                    if (!_lineBounds.IsEmpty && m.LParam != IntPtr.Zero)
                    {
                        NativeMethods.WINDOWPOS wp = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.WINDOWPOS));
                        if ((wp.flags & NativeMethods.SWP_NOMOVE) == 0)
                        {
                            wp.x = _lineBounds.X;
                            wp.y = _lineBounds.Y;
                        }
                        if ((wp.flags & NativeMethods.SWP_NOSIZE) == 0)
                        {
                            wp.cx = _lineBounds.Width;
                            wp.cy = _lineBounds.Height;
                        }
                        Marshal.StructureToPtr(wp, m.LParam, false);
                    }
                    break;
                case NativeMethods.WM_DPICHANGED:
                    // 物理ピクセルで置いているので、Windows の提案する大きさには従わない
                    ApplyBounds();
                    m.Result = IntPtr.Zero;
                    return;
            }
            base.WndProc(ref m);
        }
    }
}
