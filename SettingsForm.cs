using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class SettingsForm : Form
{
    static readonly Color Bg = Color.FromArgb(18, 18, 20);
    static readonly Color CardBg = Color.FromArgb(32, 32, 36);
    static readonly Color Field = Color.FromArgb(48, 48, 54);
    static readonly Color Line = Color.FromArgb(62, 62, 70);
    static readonly Color Ink = Color.FromArgb(236, 236, 240);
    static readonly Color Muted = Color.FromArgb(148, 148, 158);
    const int W = 320; // inner width of every card, keeps all rows on one grid
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly Settings s;
    readonly Action changed;
    PictureBox preview = null!;

    public SettingsForm(Settings s, Action changed)
    {
        this.s = s; this.changed = changed;
        Text = "Pil Göstergesi";
        Font = new Font("Segoe UI", 10f);
        BackColor = Bg; ForeColor = Ink;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new(20, 14, 20, 18);
        Build();
    }

    [DllImport("dwmapi")] static extern int DwmSetWindowAttribute(nint h, int attr, ref int v, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1;
        DwmSetWindowAttribute(Handle, 20, ref on, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE: black title bar
    }

    void Build()
    {
        SuspendLayout();
        Controls.Clear();
        var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new(0) };
        col.Controls.Add(new Label { Text = "Pil Göstergesi", Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Margin = new(0) });
        col.Controls.Add(new Label { Text = $"Görev çubuğundaki göstergenin ayarları · {Updater.CurrentText}", ForeColor = Muted, AutoSize = true, Margin = new(1, 0, 0, 14) });

        preview = new PictureBox { Size = new(W, 56), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Settings.LightTaskbar ? Color.FromArgb(238, 238, 238) : Color.FromArgb(28, 28, 28), Margin = new(0) };
        col.Controls.Add(Card("Önizleme", preview));

        var colors = new TableLayoutPanel { ColumnCount = 5, RowCount = 2, Size = new(W, 62), Margin = new(0) };
        for (int i = 0; i < 5; i++) colors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        (string Name, Func<int> Get, Action<int> Set)[] swatches =
        [
            ("Dolu", () => s.HighColor, v => s.HighColor = v),
            ("Orta", () => s.MidColor, v => s.MidColor = v),
            ("Düşük", () => s.LowColor, v => s.LowColor = v),
            ("Şarjda", () => s.ChargingColor, v => s.ChargingColor = v),
            ("Yazı", () => s.TextColor, v => s.TextColor = v),
        ];
        for (int i = 0; i < swatches.Length; i++)
        {
            colors.Controls.Add(Swatch(swatches[i].Get, swatches[i].Set), i, 0);
            colors.Controls.Add(new Label { Text = swatches[i].Name, ForeColor = Muted, Font = new Font("Segoe UI", 8.5f), AutoSize = true, Anchor = AnchorStyles.None, Margin = new(0, 2, 0, 0) }, i, 1);
        }
        col.Controls.Add(Card("Renkler", colors));

        col.Controls.Add(Card("Eşikler",
            Row("Dolu sayılır (%)", Num(1, 100, () => s.HighAt, v => s.HighAt = v)),
            Row("Düşük sayılır (%)", Num(0, 99, () => s.LowAt, v => s.LowAt = v))));

        var autostart = new CheckBox { AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new(0, 0, 4, 0) };
        autostart.FlatAppearance.BorderColor = Line;
        autostart.FlatAppearance.CheckedBackColor = Field;
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) autostart.Checked = k?.GetValue("PilGostergesi") != null;
        autostart.CheckedChanged += (_, _) =>
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (autostart.Checked) k.SetValue("PilGostergesi", $"\"{Application.ExecutablePath}\"");
            else k.DeleteValue("PilGostergesi", false);
        };
        col.Controls.Add(Card("Genel",
            Row("Yenileme (saniye)", Num(10, 3600, () => s.IntervalSec, v => s.IntervalSec = v)),
            Row("Sağdan boşluk (px)", Num(-2000, 2000, () => s.OffsetX, v => s.OffsetX = v)),
            Row("Windows açılınca başlat", autostart)));

        var reset = new Button { Text = "Varsayılana dön", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = CardBg, ForeColor = Ink, Cursor = Cursors.Hand, Margin = new(0, 2, 0, 0), Padding = new(10, 3, 10, 3) };
        reset.FlatAppearance.BorderColor = Line;
        reset.FlatAppearance.MouseOverBackColor = Field;
        reset.Click += (_, _) => { s.Reset(); Apply(); Build(); };
        col.Controls.Add(reset);

        Controls.Add(col);
        RefreshPreview();
        ResumeLayout();
    }

    void Apply() { s.Save(); changed(); RefreshPreview(); }

    void RefreshPreview()
    {
        Reading[] sample = [new("Fare", "mouse", 85, false), new("Klavye", "keyboard", 35, false), new("Kulaklık", "headset", 5, false), new("Kol", "gamepad", 60, true)];
        var old = preview.Image;
        preview.Image = Widget.Render(s, sample, 48);
        old?.Dispose();
    }

    // Rounded dark card with a small caption; children stack vertically on the W-wide grid.
    Control Card(string title, params Control[] children)
    {
        var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = CardBg, Padding = new(16, 12, 16, 14), Margin = new(0, 0, 0, 12) };
        body.Controls.Add(new Label { Text = title.ToUpperInvariant(), Font = new Font("Segoe UI Semibold", 8.5f), ForeColor = Muted, AutoSize = true, Margin = new(0, 0, 0, 8) });
        body.Controls.AddRange(children);
        body.Resize += (_, _) =>
        {
            using var path = new GraphicsPath();
            int r = 16, w = body.Width, h = body.Height;
            path.AddArc(0, 0, r, r, 180, 90); path.AddArc(w - r, 0, r, r, 270, 90);
            path.AddArc(w - r, h - r, r, r, 0, 90); path.AddArc(0, h - r, r, r, 90, 90);
            body.Region = new Region(path);
        };
        return body;
    }

    // Label on the left, control pinned to the right edge.
    static Control Row(string label, Control input)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, Size = new(W, 36), Margin = new(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new(0) });
        input.Anchor = AnchorStyles.Right;
        row.Controls.Add(input);
        return row;
    }

    Control Swatch(Func<int> get, Action<int> set)
    {
        var p = new Panel { Size = new(34, 34), Cursor = Cursors.Hand, Anchor = AnchorStyles.None, Margin = new(0) };
        p.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Color.FromArgb(get()));
            e.Graphics.FillEllipse(b, 3, 3, 27, 27);
            using var pen = new Pen(Line, 2);
            e.Graphics.DrawEllipse(pen, 3, 3, 27, 27);
        };
        p.Click += (_, _) =>
        {
            using var d = new ColorDialog { Color = Color.FromArgb(get()), FullOpen = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            set(d.Color.ToArgb()); p.Invalidate(); Apply();
        };
        return p;
    }

    Control Num(int min, int max, Func<int> get, Action<int> set)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(get(), min, max), Width = 76, BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, BackColor = Field, ForeColor = Ink, Margin = new(0) };
        n.ValueChanged += (_, _) => { set((int)n.Value); Apply(); };
        return n;
    }
}
