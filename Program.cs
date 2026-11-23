ApplicationConfiguration.Initialize();

var settings = Settings.Load();
var last = new List<Reading>();
var timer = new System.Windows.Forms.Timer { Interval = settings.IntervalSec * 1000 };
var menu = new ContextMenuStrip();
SettingsForm? form = null;
Widget widget = null!;
widget = new Widget(settings, menu, OpenSettings);
// One plain tray icon as a fallback way to reach the menu when no device is connected.
var tray = new NotifyIcon { ContextMenuStrip = menu, Text = "Pil Göstergesi", Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath), Visible = true };
tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };

void OpenSettings()
{
    if (form is { IsDisposed: false }) { form.Activate(); return; }
    form = new SettingsForm(settings, () => { timer.Interval = settings.IntervalSec * 1000; widget.SetItems(last); });
    form.Show();
}

bool busy = false;
var misses = new Dictionary<string, int>();
async Task Refresh()
{
    if (busy) return;
    busy = true;
    try
    {
        var fresh = await Task.Run(Battery.ReadAll);
        var seen = fresh.Select(r => r.Name).ToHashSet();
        foreach (var n in seen) misses.Remove(n);
        // A device can skip a reply while its power state changes (cable pulled, waking up):
        // keep its last value through 2 missed reads and re-check fast instead of dropping it.
        foreach (var old in last.Where(o => !seen.Contains(o.Name)))
        {
            int m = misses.GetValueOrDefault(old.Name) + 1;
            if (m <= 2) { misses[old.Name] = m; fresh.Add(old); }
            else misses.Remove(old.Name);
        }
        var prev = last;
        int Order(Reading r) { int i = prev.FindIndex(o => o.Name == r.Name); return i < 0 ? int.MaxValue : i; }
        last = fresh.OrderBy(Order).ToList(); // keep icons in a stable order
        widget.SetItems(last);
        timer.Interval = misses.Count > 0 ? 5000 : settings.IntervalSec * 1000;
    }
    finally { busy = false; }
}

async Task CheckUpdate(bool manual)
{
    try
    {
        if (await Updater.CheckAndInstall() is { } tag)
        {
            tray.Visible = false;
            System.Diagnostics.Process.Start(Environment.ProcessPath!, "--guncellendi");
            Application.Exit();
        }
        else if (manual) tray.ShowBalloonTip(3000, "Pil Göstergesi", $"En güncel sürümdesin ({Updater.CurrentText}).", ToolTipIcon.Info);
    }
    catch (Exception e)
    {
        if (manual) tray.ShowBalloonTip(3000, "Pil Göstergesi", "Güncelleme denetlenemedi: " + e.Message, ToolTipIcon.Warning);
    }
}

menu.Items.Add("Ayarlar", null, (_, _) => OpenSettings());
menu.Items.Add("Yenile", null, async (_, _) => await Refresh());
menu.Items.Add("Güncellemeleri denetle", null, async (_, _) => await CheckUpdate(true));
menu.Items.Add("Çıkış", null, (_, _) => Application.Exit());

// If the user turned on autostart, keep it pointing at wherever this exe lives now.
using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
    if (run?.GetValue("PilGostergesi") is string p && p != $"\"{Application.ExecutablePath}\"")
        run.SetValue("PilGostergesi", $"\"{Application.ExecutablePath}\"");

Updater.CleanupOld();
timer.Tick += async (_, _) => await Refresh();
timer.Start();
_ = Refresh();
var updateTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
updateTimer.Tick += async (_, _) => await CheckUpdate(false);
updateTimer.Start();
_ = CheckUpdate(false);
if (args.Contains("--ayarlar")) OpenSettings();
if (args.Contains("--guncellendi")) tray.ShowBalloonTip(3000, "Pil Göstergesi", $"{Updater.CurrentText} sürümüne güncellendi.", ToolTipIcon.Info);
Application.ApplicationExit += (_, _) => tray.Visible = false;
Application.Run();
