using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace ChatZ;

/// <summary>
/// Broj nepročitanih Viber poruka, pročitan iz Viber prozora preko UI Automation
/// (isti interfejs koji koriste čitači ekrana). Viber ne objavljuje taj broj nigde drugde:
/// naslov prozora ga nema, a baza poruka je u novijim verzijama šifrovana.
/// </summary>
public static class ViberUnread
{
    private const int MaxElements = 4000;

    // "3 unread", "3 unread messages", "3 nepročitane poruke", "3 непрочитане"
    private static readonly Regex CountFirst = new(
        @"(\d+)\s*(unread|nepro\w*|непро\w*|new messages?|nov[aeiu]h? poruk\w*|нов\w* порук\w*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Unread: 3", "unread messages 3", "nepročitano: 3"
    private static readonly Regex CountAfter = new(
        @"(unread|nepro\w*|непро\w*)\D{0,24}?(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] KeepWords =
    {
        "unread", "new", "message", "messages", "chat", "chats", "muted", "badge", "count",
        "nepro", "непро", "poruk", "порук", "nov", "нов", "čet", "чет", "razgovor", "разговор",
    };

    /// <summary>Zbir nepročitanih, ili null ako Viber ne izlaže broj.</summary>
    public static int? Count(IntPtr viberWindow)
    {
        try
        {
            var root = AutomationElement.FromHandle(viberWindow);
            var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
            cache.Add(AutomationElement.NameProperty);

            AutomationElementCollection all;
            using (cache.Activate())
                all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

            var seen = new HashSet<string>();
            int total = 0;
            bool found = false;
            int n = 0;

            foreach (AutomationElement el in all)
            {
                if (++n > MaxElements) break;
                string name = el.Cached.Name ?? "";
                if (name.Length == 0 || !seen.Add(name)) continue;

                int? count = Parse(name);
                if (count is int c)
                {
                    total += c;
                    found = true;
                }
            }
            return found ? total : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? Parse(string name)
    {
        var m = CountFirst.Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int a) && a < 10000) return a;
        m = CountAfter.Match(name);
        if (m.Success && int.TryParse(m.Groups[2].Value, out int b) && b < 10000) return b;
        return null;
    }

    /// <summary>
    /// Snima strukturu Viber prozora u tekstualni fajl. Imena kontakata i tekst poruka se
    /// sakrivaju: ostaju samo brojevi i par ključnih reči (unread, nepročitano, poruke…).
    /// </summary>
    public static string WriteDiagnostics(IntPtr viberWindow, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ChatZ — Viber dijagnostika ({DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine("Tekst je sakriven (▪). Vidljivi su samo brojevi, vrste elemenata i ključne reči.");
        sb.AppendLine($"Brojač trenutno vidi: {Count(viberWindow)?.ToString() ?? "ništa"}");
        sb.AppendLine();

        int count = 0;
        try
        {
            var root = AutomationElement.FromHandle(viberWindow);
            Walk(TreeWalker.RawViewWalker, root, 0, sb, ref count);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Greška: {ex.GetType().Name}: {ex.Message}");
        }

        sb.AppendLine();
        sb.AppendLine($"Ukupno elemenata: {count}");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static void Walk(TreeWalker walker, AutomationElement el, int depth, StringBuilder sb, ref int count)
    {
        if (count >= MaxElements || depth > 40) return;
        count++;

        string type, cls, name;
        try
        {
            type = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
            cls = el.Current.ClassName ?? "";
            name = el.Current.Name ?? "";
        }
        catch
        {
            return;
        }

        sb.Append(' ', depth * 2).Append(type);
        if (cls.Length > 0) sb.Append(" [").Append(Mask(cls)).Append(']');
        if (name.Length > 0) sb.Append(" \"").Append(Mask(name)).Append('"');
        sb.AppendLine();

        AutomationElement? child;
        try { child = walker.GetFirstChild(el); } catch { return; }
        while (child != null && count < MaxElements)
        {
            Walk(walker, child, depth + 1, sb, ref count);
            try { child = walker.GetNextSibling(child); } catch { break; }
        }
    }

    private static string Mask(string text) =>
        Regex.Replace(text, @"[\p{L}\p{M}]+", m =>
        {
            string w = m.Value.ToLowerInvariant();
            return KeepWords.Any(k => w.StartsWith(k, StringComparison.Ordinal)) ? m.Value : "▪";
        });
}
