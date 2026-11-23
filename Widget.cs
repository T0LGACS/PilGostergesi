using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;

// Borderless per-pixel-alpha window sitting on the taskbar, just left of the notification area.
// shortcut: assumes a bottom, non-auto-hide taskbar on the primary monitor; handle other layouts if someone uses them.
class Widget : Form
{
    readonly Settings s;
    readonly ToolTip tip = new();
    List<Reading> items = [];
    bool blink;

    static readonly string IconFont = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public Widget(Settings s, ContextMenuStrip menu, Action openSettings)
    {
        this.s = s;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position); else openSettings(); };
        // The taskbar raises itself over topmost windows when clicked, so re-assert position/z-order; also drives the low-battery blink.
        var keep = new System.Windows.Forms.Timer { Interval = 500 };
        keep.Tick += (_, _) => Place();
        keep.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 /*LAYERED*/ | 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/ | 0x08000000 /*NOACTIVATE*/;
            return cp;
        }
    }
    protected override bool ShowWithoutActivation => true;

    public void SetItems(List<Reading> list)
    {
        items = list;
        tip.SetToolTip(this, string.Join("\n", list.Select(r => $"{r.Name}: %{r.Pct}{(r.Charging ? " (şarjda)" : "")}")));
        Place();
    }

    void Place()
    {
        var tray = N.FindWindow("Shell_TrayWnd", null);
        if (items.Count == 0 || tray == 0 || FullscreenApp()) { Hide(); return; }
        N.GetWindowRect(tray, out var bar);
        N.GetWindowRect(N.FindWindowEx(tray, 0, "TrayNotifyWnd", null), out var notify);
        int right = notify.L > bar.L ? notify.L : bar.R - 300;

        blink = !blink;
        using var bmp = Render(s, items, bar.B - bar.T, blink);
        var dst = new N.POINT(right - bmp.Width - s.OffsetX, bar.T);
        var size = new N.SIZE(bmp.Width, bmp.Height);
        var src = new N.POINT(0, 0);
        var blend = new N.BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
        nint screen = N.GetDC(0), mem = N.CreateCompatibleDC(screen), hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = N.SelectObject(mem, hbmp);
        N.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
        N.SelectObject(mem, old); N.DeleteObject(hbmp); N.DeleteDC(mem); N.ReleaseDC(0, screen);

        if (!Visible) Show();
        N.SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
    }

    static bool FullscreenApp()
    {
        var fg = N.GetForegroundWindow();
        if (fg == 0) return false;
        var cls = new StringBuilder(64);
        N.GetClassName(fg, cls, 64);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        N.GetWindowRect(fg, out var r);
        var sc = Screen.PrimaryScreen!.Bounds;
        return r.L <= sc.Left && r.T <= sc.Top && r.R >= sc.Right && r.B >= sc.Bottom;
    }

    public static Bitmap Render(Settings s, IReadOnlyList<Reading> items, int h, bool blinkOn = true)
    {
        float k = h / 48f;
        int cw = (int)(52 * k);
        var bmp = new Bitmap(Math.Max(1, cw * items.Count), h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(1, 0, 0, 0)); // alpha 1 instead of 0 keeps the window clickable
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var fg = Color.FromArgb(s.TextColor);
        using var fgBrush = new SolidBrush(fg);
        using var pen = new Pen(fg, 1.4f * k);
        using var iconFont = new Font(IconFont, 12 * k, GraphicsUnit.Pixel);
        using var textFont = new Font("Segoe UI", 11.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (int i = 0; i < items.Count; i++)
        {
            var r = items[i];
            float x0 = i * cw, glyphW = 14 * k, bw = 21 * k, bh = 11 * k, gap = 3 * k;
            float left = x0 + (cw - (glyphW + gap + bw + 2 * k)) / 2, cy = 16 * k;

            string glyph = r.Kind switch { "mouse" => "", "keyboard" => "", "headset" => "", "gamepad" => "", _ => "" };
            g.DrawString(glyph, iconFont, fgBrush, new RectangleF(left, cy - 8 * k, glyphW, 16 * k), center);

            float bx = left + glyphW + gap, by = cy - bh / 2;
            using (var path = Rounded(bx, by, bw, bh, 2.5f * k)) g.DrawPath(pen, path);
            g.FillRectangle(fgBrush, bx + bw + 0.6f * k, cy - 2.5f * k, 1.8f * k, 5 * k); // battery tip
            float inner = (bw - 4 * k) * Math.Clamp(r.Pct, 0, 100) / 100f;
            bool critical = r.Pct < 10 && !r.Charging;
            if (critical) inner = blinkOn ? bw - 4 * k : 0; // whole interior flashes red
            if (inner > 0)
            using (var fill = new SolidBrush(critical ? Color.FromArgb(s.LowColor) : s.LevelColor(r.Pct, r.Charging)))
            using (var path = Rounded(bx + 2 * k, by + 2 * k, Math.Max(inner, 1.5f * k), bh - 4 * k, 1 * k)) g.FillPath(fill, path);

            if (r.Charging)
            {
                // Lightning bolt inside the battery, outlined so it reads on any fill colour.
                float lw = 7 * k, lh = bh - 1 * k, lx = bx + (bw - lw) / 2, ly = cy - lh / 2;
                PointF P(float px, float py) => new(lx + px * lw, ly + py * lh);
                PointF[] bolt = [P(0.6f, 0), P(0.1f, 0.58f), P(0.45f, 0.58f), P(0.35f, 1), P(0.9f, 0.4f), P(0.55f, 0.4f), P(0.7f, 0)];
                using var outline = new Pen(fg.GetBrightness() > 0.5f ? Color.Black : Color.White, 1.2f * k) { LineJoin = LineJoin.Round };
                g.DrawPolygon(outline, bolt);
                g.FillPolygon(fgBrush, bolt);
            }

            g.DrawString($"{r.Pct}%", textFont, fgBrush, new RectangleF(x0, 26 * k, cw, 16 * k), center);
        }
        return bmp;
    }

    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static class N
    {
        public record struct POINT(int X, int Y);
        public record struct SIZE(int W, int H);
        public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string c, string? n);
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint p, nint a, string c, string? n);
        [DllImport("user32")] public static extern bool GetWindowRect(nint h, out RECT r);
        [DllImport("user32")] public static extern nint GetForegroundWindow();
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint h, StringBuilder s, int n);
        [DllImport("user32")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int hh, uint f);
        [DllImport("user32")] public static extern bool UpdateLayeredWindow(nint h, nint dst, ref POINT p, ref SIZE s, nint src, ref POINT sp, int key, ref BLEND b, int f);
        [DllImport("user32")] public static extern nint GetDC(nint h);
        [DllImport("user32")] public static extern int ReleaseDC(nint h, nint dc);
        [DllImport("gdi32")] public static extern nint CreateCompatibleDC(nint dc);
        [DllImport("gdi32")] public static extern bool DeleteDC(nint dc);
        [DllImport("gdi32")] public static extern nint SelectObject(nint dc, nint o);
        [DllImport("gdi32")] public static extern bool DeleteObject(nint o);
    }
}
