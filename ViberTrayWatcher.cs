using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace ChatZ;

/// <summary>
/// Viber ne objavljuje broj nepročitanih poruka nigde (naslov prozora, tooltip, ikonica prozora).
/// Jedini znak je crvena tačka na Viber ikonici u trayu. Ovde se pronađe ta ikonica preko
/// UI Automation i proveri ima li crvenih piksela u njenom gornjem desnom uglu.
/// Radi samo kad je Viber ikonica vidljiva u trayu (nije sakrivena pod strelicom „^“).
/// </summary>
public static class ViberTrayWatcher
{
    private static AutomationElement? _icon;

    /// <summary>true = ima novih poruka, false = nema, null = ikonica nije nađena.</summary>
    public static bool? Check()
    {
        try
        {
            var rect = GetIconRect();
            if (rect is null) return null;
            return HasRedDot(rect.Value);
        }
        catch
        {
            _icon = null;
            return null;
        }
    }

    private static System.Windows.Rect? GetIconRect()
    {
        if (_icon != null)
        {
            try
            {
                var r = _icon.Current.BoundingRectangle;
                if (IsIconSized(r)) return r;
            }
            catch { }
            _icon = null;
        }

        IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return null;

        var root = AutomationElement.FromHandle(tray);
        var cache = new CacheRequest();
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.BoundingRectangleProperty);
        AutomationElementCollection all;
        using (cache.Activate())
            all = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);

        foreach (AutomationElement el in all)
        {
            string name = el.Cached.Name ?? "";
            // Tray ikonica se zove "Rakuten Viber"; dugmad na taskbaru imaju "... running window".
            if (!name.Contains("Viber", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("running window", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("ChatZ", StringComparison.OrdinalIgnoreCase)) continue;

            var r = el.Cached.BoundingRectangle;
            if (!IsIconSized(r)) continue;
            _icon = el;
            return r;
        }
        return null;
    }

    private static bool IsIconSized(System.Windows.Rect r) =>
        !r.IsEmpty && r.Width >= 12 && r.Height >= 12 && r.Width <= 96 && r.Height <= 96;

    private static bool HasRedDot(System.Windows.Rect r)
    {
        int x = (int)r.X, y = (int)r.Y, w = (int)r.Width, h = (int)r.Height;
        uint[] px = Capture(x, y, w, h);

        int red = 0, purple = 0;
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                uint c = px[j * w + i];
                int R = (int)((c >> 16) & 0xFF), G = (int)((c >> 8) & 0xFF), B = (int)(c & 0xFF);

                // Viberova ljubičasta (#7360F2) — potvrda da gledamo baš Viber ikonicu
                if (R is >= 80 and <= 150 && G is >= 60 and <= 130 && B >= 190) purple++;

                // Crvena tačka je u gornjoj desnoj četvrtini
                if (i >= w / 2 && j < h / 2 && R >= 190 && G <= 90 && B <= 90) red++;
            }
        }
        return purple >= 20 && red >= 4;
    }

    // ---------- snimanje dela ekrana (GDI) ----------

    private static uint[] Capture(int x, int y, int w, int h)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY);
            SelectObject(mem, old);

            var bi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // odozgo nadole
                biPlanes = 1,
                biBitCount = 32,
            };
            var pixels = new uint[w * h];
            GetDIBits(mem, bmp, 0, (uint)h, pixels, ref bi, 0);
            return pixels;
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount;
        public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
        public uint biClrUsed; public uint biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, [Out] uint[] bits,
        ref BITMAPINFOHEADER bi, uint usage);
}
