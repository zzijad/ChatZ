using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace ChatZ;

/// <summary>Svetla / tamna tema za zaglavlje, statusnu traku i ostale delove ChatZ-a.</summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    public static bool WindowsUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(string mode)
    {
        IsDark = mode switch
        {
            "dark" => true,
            "light" => false,
            _ => WindowsUsesDark(),
        };

        var r = Application.Current.Resources;
        if (IsDark)
        {
            Set(r, "PageBrush", "#17191B");
            Set(r, "SurfaceBrush", "#1D2023");
            Set(r, "LineBrush", "#2C3034");
            Set(r, "TextBrush", "#E9EDEF");
            Set(r, "MutedBrush", "#A3ACB5");
            Set(r, "ChipBrush", "#2A2F34");
            Set(r, "ErrorBrush", "#FF8A80");
        }
        else
        {
            Set(r, "PageBrush", "#F6F7F9");
            Set(r, "SurfaceBrush", "#FFFFFF");
            Set(r, "LineBrush", "#DFE2E6");
            Set(r, "TextBrush", "#1B1E23");
            Set(r, "MutedBrush", "#4D5661");
            Set(r, "ChipBrush", "#E6E9ED");
            Set(r, "ErrorBrush", "#B3261E");
        }

        foreach (Window w in Application.Current.Windows)
            ApplyTitleBar(w);
    }

    /// <summary>Tamna ili svetla Windows naslovna traka prozora.</summary>
    public static void ApplyTitleBar(Window w)
    {
        IntPtr hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = IsDark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+ i Windows 11), 19 = starije verzije
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    private static void Set(ResourceDictionary r, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        r[key] = brush;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
