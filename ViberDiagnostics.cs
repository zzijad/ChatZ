using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ChatZ;

/// <summary>
/// Snima tragove po kojima bi se mogao prepoznati broj nepročitanih Viber poruka:
/// ikonice svih Viber prozora (Viber možda na njih crta broj) i tooltip Viber ikonice u trayu.
/// Ne snima sadržaj poruka; naslovi prozora se maskiraju.
/// </summary>
public static class ViberDiagnostics
{
    private static readonly string[] KeepWords =
    {
        "viber", "rakuten", "unread", "new", "message", "nepro", "непро", "poruk", "порук", "nov", "нов",
    };

    /// <summary>Pravi folder sa info.txt i PNG ikonicama. embedded = Viber prozor ugrađen u ChatZ.</summary>
    public static string Write(string baseFolder, IntPtr embedded)
    {
        string folder = Path.Combine(baseFolder, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
        Directory.CreateDirectory(folder);

        var sb = new StringBuilder();
        sb.AppendLine($"ChatZ — Viber dijagnostika 2 ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
        sb.AppendLine($"Windows: {Environment.OSVersion.Version}");
        sb.AppendLine($"ChatZ: {typeof(ViberDiagnostics).Assembly.GetName().Version}");

        var procs = Process.GetProcessesByName("Viber");
        foreach (var p in procs)
        {
            try
            {
                string? exe = p.MainModule?.FileName;
                if (exe != null) sb.AppendLine($"Viber: {FileVersionInfo.GetVersionInfo(exe).FileVersion}");
            }
            catch { }
            break;
        }
        sb.AppendLine();

        // --- Prozori i ikonice ---
        var pids = procs.Select(p => (uint)p.Id).ToHashSet();
        var windows = new List<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pids.Contains(pid)) windows.Add(h);
            return true;
        }, IntPtr.Zero);

        // Ugrađeni prozor više nije top-level, pa ga EnumWindows ne vidi.
        if (embedded != IntPtr.Zero && !windows.Contains(embedded)) windows.Insert(0, embedded);

        sb.AppendLine($"Viber prozori: {windows.Count}");
        int idx = 0;
        foreach (var h in windows)
        {
            idx++;
            string cls = Native.GetClass(h);
            string title = Mask(Native.GetTitle(h));
            bool visible = Native.IsWindowVisible(h);
            sb.AppendLine($"#{idx} class={cls} title=\"{title}\" visible={visible}");

            SaveIcon(sb, folder, idx, "small", Native.SendMessage(h, Native.WM_GETICON, (IntPtr)Native.ICON_SMALL, IntPtr.Zero));
            SaveIcon(sb, folder, idx, "small2", Native.SendMessage(h, Native.WM_GETICON, (IntPtr)Native.ICON_SMALL2, IntPtr.Zero));
            SaveIcon(sb, folder, idx, "big", Native.SendMessage(h, Native.WM_GETICON, (IntPtr)Native.ICON_BIG, IntPtr.Zero));
            SaveIcon(sb, folder, idx, "class", Native.GetClassLongPtrSafe(h, Native.GCLP_HICON));
            SaveIcon(sb, folder, idx, "classsm", Native.GetClassLongPtrSafe(h, Native.GCLP_HICONSM));
        }
        sb.AppendLine();

        // --- Tray ---
        sb.AppendLine("Tray ikonice koje pominju Viber:");
        var trayTask = Task.Run(ReadTrayNames);
        var tray = trayTask.Wait(TimeSpan.FromSeconds(15)) ? trayTask.Result : new List<string> { "(isteklo vreme)" };
        if (tray.Count == 0) sb.AppendLine("  (nijedna)");
        foreach (var n in tray) sb.AppendLine($"  \"{n}\"");

        File.WriteAllText(Path.Combine(folder, "info.txt"), sb.ToString(), Encoding.UTF8);
        return folder;
    }

    private static void SaveIcon(StringBuilder sb, string folder, int idx, string kind, IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            byte[] png = ms.ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(png))[..12];
            string file = $"prozor{idx}-{kind}.png";
            File.WriteAllBytes(Path.Combine(folder, file), png);
            sb.AppendLine($"   ikonica {kind}: {src.PixelWidth}x{src.PixelHeight} hash={hash} → {file}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"   ikonica {kind}: greška {ex.GetType().Name}");
        }
    }

    private static List<string> ReadTrayNames()
    {
        var result = new List<string>();
        foreach (string cls in new[] { "Shell_TrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland" })
        {
            IntPtr h = Native.FindWindow(cls, null);
            if (h == IntPtr.Zero) continue;
            try
            {
                var root = AutomationElement.FromHandle(h);
                var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None };
                cache.Add(AutomationElement.NameProperty);
                AutomationElementCollection all;
                using (cache.Activate())
                    all = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                foreach (AutomationElement el in all)
                {
                    string name = el.Cached.Name ?? "";
                    if (name.Contains("viber", StringComparison.OrdinalIgnoreCase) && !result.Contains(name))
                        result.Add(name.Replace("\r", " ").Replace("\n", " | "));
                }
            }
            catch { }
        }
        return result;
    }

    private static string Mask(string text) =>
        Regex.Replace(text, @"[\p{L}\p{M}]+", m =>
        {
            string w = m.Value.ToLowerInvariant();
            return KeepWords.Any(k => w.StartsWith(k, StringComparison.Ordinal)) ? m.Value : "▪";
        });
}
