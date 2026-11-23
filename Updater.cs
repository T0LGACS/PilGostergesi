using System.Reflection;
using System.Text.Json;

// Pulls the newest PilGostergesi.exe from the GitHub release of this repo and swaps it in place.
static class Updater
{
    const string Repo = "T0LGACS/PilGostergesi";
    const string AssetName = "PilGostergesi.exe";

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version!;
    public static string CurrentText => "v" + Current.ToString(3);

    /// <returns>The new tag if an update was installed (caller restarts), otherwise null.</returns>
    public static async Task<string?> CheckAndInstall()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PilGostergesi-updater");
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var latest) || latest <= Current) return null;

        var url = doc.RootElement.GetProperty("assets").EnumerateArray()
            .First(a => a.GetProperty("name").GetString() == AssetName)
            .GetProperty("browser_download_url").GetString()!;
        var bytes = await http.GetByteArrayAsync(url);
        if (bytes.Length < 100_000 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("İndirilen dosya geçerli bir exe değil.");

        var exe = Environment.ProcessPath!;
        var tmp = exe + ".new";
        await File.WriteAllBytesAsync(tmp, bytes);
        File.Move(exe, exe + ".old", true); // Windows lets a running exe be renamed, not overwritten
        try { File.Move(tmp, exe); }
        catch { File.Move(exe + ".old", exe); throw; } // put the working version back
        return tag;
    }

    public static void CleanupOld()
    {
        try { File.Delete(Environment.ProcessPath + ".old"); } catch { } // still locked right after restart: next start gets it
    }
}
