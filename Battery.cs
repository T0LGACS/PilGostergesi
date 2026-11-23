using System.Runtime.InteropServices;
using System.Text;
using HidSharp;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration.Pnp;
using Windows.Gaming.Input;

record Reading(string Name, string Kind, int Pct, bool Charging);

static class Battery
{
    public static List<Reading> ReadAll()
    {
        var hid = DeviceList.Local.GetHidDevices().ToList();
        var list = new List<Reading>();
        foreach (var source in new Func<IEnumerable<Reading>>[] { () => Razer(hid), () => LogitechProX2(hid), () => LogitechHidPP(hid), () => Sony(hid), Xbox, WindowsBluetooth })
            try { list.AddRange(source()); } catch { }
        return list.GroupBy(r => r.Name).Select(g => g.First()).ToList();
    }

    // Runs f per item; one dead/asleep device must not hide the others.
    static IEnumerable<Reading> Each<T>(IEnumerable<T> items, Func<T, Reading?> f)
    {
        foreach (var i in items)
        {
            Reading? r = null;
            try { r = f(i); } catch { }
            if (r != null) yield return r;
        }
    }

    static string Guess(string name)
    {
        var n = name.ToLowerInvariant();
        bool Has(params string[] w) => w.Any(n.Contains);
        return Has("headset", "headphone", "kulaklık", "buds", "airpods", "kraken", "barracuda", "hammerhead") ? "headset"
            : Has("controller", "gamepad", "dualsense", "wolverine", "xbox") ? "gamepad"
            : Has("keyboard", "klavye", "blackwidow", "huntsman", "deathstalker") ? "keyboard"
            : Has("mouse", "fare", "viper", "deathadder", "basilisk", "naga", "cobra", "orochi") ? "mouse"
            : "other";
    }

    // ---- Razer (openrazer protocol): 90-byte feature report, class 0x07, cmd 0x80 = battery, 0x84 = charging.
    static IEnumerable<Reading> Razer(List<HidDevice> hid) =>
        Each(hid.Where(d => d.VendorID == 0x1532 && d.GetMaxFeatureReportLength() == 91).GroupBy(d => d.ProductID).Select(g => g.First()), dev =>
        {
            // Access 0: Windows keeps mouse/keyboard collections locked, but feature reports still work.
            using var h = Native.CreateFile(dev.DevicePath, 0, 3, 0, 3, 0, 0);
            if (h.IsInvalid) return null;
            foreach (byte tid in new byte[] { 0x1F, 0xFF, 0x3F }) // transaction id differs per model generation
            {
                if (RazerAsk(h, tid, 0x80) is not { } raw) continue;
                var name = dev.GetProductName();
                var kind = Guess(name);
                return new(name, kind == "other" ? "mouse" : kind, (int)Math.Round(raw * 100 / 255.0), RazerAsk(h, tid, 0x84) == 1);
            }
            return null;
        });

    static int? RazerAsk(SafeFileHandle h, byte tid, byte cmd)
    {
        var buf = new byte[91];
        buf[2] = tid; buf[6] = 0x02; buf[7] = 0x07; buf[8] = cmd;
        byte crc = 0; for (int i = 3; i < 89; i++) crc ^= buf[i]; buf[89] = crc;
        if (!Native.HidD_SetFeature(h, buf, 91)) return null;
        Thread.Sleep(100);
        var r = new byte[91];
        if (!Native.HidD_GetFeature(h, r, 91) || r[1] != 0x02) return null; // 0x02 = success; asleep/off otherwise
        return r[10];
    }

    // ---- Logitech PRO X 2 LIGHTSPEED headset: vendor protocol on the 0xFFA0 collection (HeadsetControl / Linux hid-logitech-headset patch).
    static IEnumerable<Reading> LogitechProX2(List<HidDevice> hid) =>
        Each(hid.Where(d => d.VendorID == 0x046D && d.ProductID == 0x0AF7 && d.GetMaxOutputReportLength() == 64), dev =>
        {
            using var s = dev.Open();
            s.ReadTimeout = 1500;
            var req = new byte[64];
            new byte[] { 0x51, 0x08, 0x00, 0x03, 0x1a, 0x00, 0x03, 0x00, 0x04, 0x0a }.CopyTo(req, 0);
            s.Write(req);
            var b = new byte[64];
            for (int i = 0; i < 5; i++)
            {
                s.Read(b);
                if (b[0] == 0x51 && b[1] == 0x05 && b[6] == 0x00) return null; // headset switched off
                if (b[0] == 0x51 && b[1] == 0x0B && b[8] == 0x04 && b[10] <= 100) return new("Logitech PRO X 2", "headset", b[10], b[12] == 0x02);
            }
            return null;
        });

    // ---- Logitech HID++ 2.0 (Lightspeed/Unifying receivers, Bolt, direct USB/BT): long reports 0x11, 20 bytes.
    static IEnumerable<Reading> LogitechHidPP(List<HidDevice> hid)
    {
        foreach (var dev in hid.Where(d => d.VendorID == 0x046D && d.GetMaxOutputReportLength() == 20 && d.GetMaxInputReportLength() == 20))
        {
            HidStream s;
            try { s = dev.Open(); } catch { continue; }
            using (s)
            {
                s.ReadTimeout = 300;
                foreach (var r in Each(new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 }, idx => HidPP(s, idx))) yield return r;
            }
        }
    }

    static Reading? HidPP(HidStream s, byte dev)
    {
        byte[]? Req(byte feat, byte fn, params byte[] p)
        {
            var b = new byte[20];
            b[0] = 0x11; b[1] = dev; b[2] = feat; b[3] = (byte)(fn << 4 | 0x0A);
            p.CopyTo(b, 4);
            s.Write(b);
            var r = new byte[20];
            var until = Environment.TickCount64 + 600;
            while (Environment.TickCount64 < until)
            {
                try { s.Read(r); } catch (TimeoutException) { return null; }
                if (r[0] != 0x11 || r[1] != dev) continue;
                if (r[2] == 0xFF) return null; // HID++ 2.0 error
                if (r[2] == feat && r[3] == b[3]) return r;
            }
            return null;
        }
        byte Feature(ushort id) => Req(0, 0, (byte)(id >> 8), (byte)id) is { } r ? r[4] : (byte)0;

        if (Req(0, 1) == null) return null; // ping: empty slot or device off

        int pct; bool charging;
        byte f;
        if ((f = Feature(0x1004)) > 0 && Req(f, 1) is { } u) { pct = u[4]; charging = u[6] is 1 or 2; }
        else if ((f = Feature(0x1000)) > 0 && Req(f, 0) is { } b) { pct = b[4]; charging = b[6] is 1 or 2; }
        else if ((f = Feature(0x1001)) > 0 && Req(f, 0) is { } v)
        {
            // shortcut: linear 3.5V-4.1V curve, real Li-ion curve is non-linear; fine for a rough gauge.
            pct = Math.Clamp(((v[4] << 8 | v[5]) - 3500) * 100 / 600, 0, 100);
            charging = (v[6] & 0x80) != 0;
        }
        else return null;

        string name = "Logitech cihaz", kind = "other";
        if ((f = Feature(0x0005)) > 0 && Req(f, 0) is { } c)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < c[4]; i += 16)
                if (Req(f, 1, (byte)i) is { } part) sb.Append(Encoding.ASCII.GetString(part, 4, 16));
            name = sb.ToString().TrimEnd('\0', ' ');
            if (Req(f, 2) is { } t) kind = t[4] switch { 0 or 2 => "keyboard", 3 or 4 or 5 => "mouse", 8 => "headset", 11 or 12 => "gamepad", _ => "other" };
        }
        return new(name, kind, pct, charging);
    }

    // ---- Sony DualSense / DualSense Edge / DualShock 4, USB and Bluetooth (layout from Linux hid-playstation / hid-sony).
    static IEnumerable<Reading> Sony(List<HidDevice> hid) =>
        Each(hid.Where(d => d.VendorID == 0x054C && d.ProductID is 0x0CE6 or 0x0DF2 or 0x05C4 or 0x09CC && d.GetMaxInputReportLength() >= 64), dev =>
        {
            bool ds5 = dev.ProductID is 0x0CE6 or 0x0DF2, bt = dev.GetMaxInputReportLength() > 64;
            using var s = dev.Open();
            s.ReadTimeout = 1000;
            if (bt)
            {
                // Reading a feature report switches a BT controller from the short report to the full one that carries battery.
                var f = new byte[dev.GetMaxFeatureReportLength()];
                f[0] = ds5 ? (byte)0x05 : (byte)0x02;
                s.GetFeature(f);
            }
            byte want = bt ? (ds5 ? (byte)0x31 : (byte)0x11) : (byte)0x01;
            int at = ds5 ? (bt ? 54 : 53) : (bt ? 32 : 30);
            string name = dev.ProductID switch { 0x0CE6 => "DualSense", 0x0DF2 => "DualSense Edge", _ => "DualShock 4" };
            var b = new byte[dev.GetMaxInputReportLength()];
            for (int i = 0; i < 30; i++)
            {
                s.Read(b);
                if (b[0] != want) continue;
                int level = b[at] & 0x0F, state = b[at] >> 4;
                int pct = Math.Min(level * 10 + 5, 100);
                if (ds5) return state switch { 0 => new(name, "gamepad", pct, false), 1 => new(name, "gamepad", pct, true), 2 => new(name, "gamepad", 100, false), _ => null };
                bool cable = (state & 1) == 1;
                return new(name, "gamepad", cable && level >= 10 ? 100 : pct, cable && level < 10);
            }
            return null;
        });

    // ---- Xbox and other controllers Windows reports battery for.
    static IEnumerable<Reading> Xbox() =>
        Each(RawGameController.RawGameControllers, c =>
        {
            var r = c.TryGetBatteryReport();
            if (r?.FullChargeCapacityInMilliwattHours is not int full || r.RemainingCapacityInMilliwattHours is not int rem || full <= 0) return null;
            return new(c.DisplayName, "gamepad", Math.Clamp(rem * 100 / full, 0, 100), r.Status == Windows.System.Power.BatteryStatus.Charging);
        });

    // ---- Anything Windows itself knows the battery of (Bluetooth LE mice/keyboards, BT headphones): DEVPKEY_Bluetooth_Battery.
    const string BtBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    static IEnumerable<Reading> WindowsBluetooth()
    {
        var devs = PnpObject.FindAllAsync(PnpObjectType.Device, [BtBatteryKey, "System.ItemNameDisplay", "System.Devices.DevObjectType"], "System.Devices.Present:=System.StructuredQueryType.Boolean#True").AsTask().GetAwaiter().GetResult();
        return Each(devs, d =>
        {
            if (!d.Properties.TryGetValue(BtBatteryKey, out var v) || v is not byte pct) return null;
            var name = d.Properties.TryGetValue("System.ItemNameDisplay", out var n) && n is string sn ? sn : "Bluetooth cihaz";
            return new(name, Guess(name), Math.Min((int)pct, 100), false);
        });
    }
}

static class Native
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint sa, uint disp, uint flags, nint tmpl);
    [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int n);
    [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int n);
}
