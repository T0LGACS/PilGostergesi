using System.Text.Json;
using Microsoft.Win32;

class Settings
{
    public int HighColor { get; set; } = Color.FromArgb(16, 137, 62).ToArgb();
    public int MidColor { get; set; } = Color.FromArgb(202, 80, 16).ToArgb();
    public int LowColor { get; set; } = Color.FromArgb(196, 43, 28).ToArgb();
    public int ChargingColor { get; set; } = Color.FromArgb(0, 120, 215).ToArgb();
    public int TextColor { get; set; } = (LightTaskbar ? Color.Black : Color.White).ToArgb();
    public int HighAt { get; set; } = 50;
    public int LowAt { get; set; } = 20;
    public int IntervalSec { get; set; } = 60;
    public int OffsetX { get; set; } = 4;

    public static bool LightTaskbar =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PilGostergesi", "ayarlar.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); } // missing or broken file: fall back to defaults
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }

    public void Reset()
    {
        var d = new Settings();
        foreach (var p in typeof(Settings).GetProperties()) p.SetValue(this, p.GetValue(d));
    }

    public Color LevelColor(int pct, bool charging) =>
        Color.FromArgb(charging ? ChargingColor : pct >= HighAt ? HighColor : pct >= LowAt ? MidColor : LowColor);
}
