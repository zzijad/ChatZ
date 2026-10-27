using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace ChatZ;

/// <summary>
/// Kontejner u koji se "uvlači" prozor Viber Desktopa.
/// </summary>
public class ViberHost : HwndHost
{
    private IntPtr _container;
    private IntPtr _viber;
    private long _origStyle;
    private long _origExStyle;

    public bool IsAttached => _viber != IntPtr.Zero && Native.IsWindow(_viber);

    public IntPtr ViberWindow => _viber;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _container = Native.CreateWindowEx(0, "static", "",
            (int)(Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN),
            0, 0, 100, 100, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, _container);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Detach();
        Native.DestroyWindow(hwnd.Handle);
        _container = IntPtr.Zero;
    }

    /// <summary>Ugrađuje dati Viber prozor u kontejner.</summary>
    public bool Attach(IntPtr viberWindow)
    {
        if (_container == IntPtr.Zero || viberWindow == IntPtr.Zero) return false;
        if (_viber == viberWindow && IsAttached) return true;

        Detach();
        _viber = viberWindow;
        _origStyle = Native.GetLong(_viber, Native.GWL_STYLE);
        _origExStyle = Native.GetLong(_viber, Native.GWL_EXSTYLE);

        if (Native.IsIconic(_viber)) Native.ShowWindow(_viber, Native.SW_RESTORE);

        long style = _origStyle & ~(Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME |
                                    Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX);
        style |= Native.WS_CHILD | Native.WS_VISIBLE;
        long exStyle = _origExStyle & ~(Native.WS_EX_APPWINDOW | Native.WS_EX_WINDOWEDGE |
                                        Native.WS_EX_CLIENTEDGE | Native.WS_EX_DLGMODALFRAME);

        Native.SetLong(_viber, Native.GWL_STYLE, style);
        Native.SetLong(_viber, Native.GWL_EXSTYLE, exStyle);
        Native.SetParent(_viber, _container);
        Native.SetWindowPos(_viber, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED);
        Native.ShowWindow(_viber, Native.SW_SHOW);
        ResizeViber();
        return true;
    }

    /// <summary>Vraća Viber u samostalan prozor (skriven, ostaje u svom trayu).</summary>
    public void Detach()
    {
        if (_viber == IntPtr.Zero) return;
        if (Native.IsWindow(_viber))
        {
            Native.ShowWindow(_viber, Native.SW_HIDE);
            Native.SetParent(_viber, IntPtr.Zero);
            Native.SetLong(_viber, Native.GWL_STYLE, _origStyle & ~Native.WS_VISIBLE);
            Native.SetLong(_viber, Native.GWL_EXSTYLE, _origExStyle);
            Native.SetWindowPos(_viber, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
                Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }
        _viber = IntPtr.Zero;
    }

    public void FocusViber()
    {
        if (IsAttached) Native.SetFocus(_viber);
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeViber();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ResizeViber();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        FocusViber();
    }

    private void ResizeViber()
    {
        if (!IsAttached || _container == IntPtr.Zero) return;
        Native.GetClientRect(_container, out var r);
        Native.MoveWindow(_viber, 0, 0, r.Right - r.Left, r.Bottom - r.Top, true);
    }
}

/// <summary>Pronalaženje i pokretanje Viber Desktopa.</summary>
public static class ViberLocator
{
    public static string? FindExe(string? savedPath)
    {
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath)) return savedPath;

        foreach (var p in Process.GetProcessesByName("Viber"))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (path != null && File.Exists(path)) return path;
            }
            catch { /* nema pristupa procesu */ }
        }

        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Viber", "Viber.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Viber", "Viber.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Viber", "Viber.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsRunning() => Process.GetProcessesByName("Viber").Length > 0;

    /// <summary>Glavni prozor Vibera (može biti i skriven u trayu).</summary>
    public static IntPtr FindMainWindow()
    {
        var pids = Process.GetProcessesByName("Viber").Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return IntPtr.Zero;

        IntPtr exact = IntPtr.Zero, largest = IntPtr.Zero;
        long largestArea = 0;

        Native.EnumWindows((hWnd, _) =>
        {
            Native.GetWindowThreadProcessId(hWnd, out uint pid);
            if (!pids.Contains(pid)) return true;
            if (Native.GetWindow(hWnd, Native.GW_OWNER) != IntPtr.Zero) return true;

            string title = Native.GetTitle(hWnd);
            if (title.Length == 0) return true;

            if (title.Equals("Viber", StringComparison.OrdinalIgnoreCase) && exact == IntPtr.Zero)
                exact = hWnd;

            if (title.Contains("Viber", StringComparison.OrdinalIgnoreCase))
            {
                Native.GetWindowRect(hWnd, out var r);
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > largestArea) { largestArea = area; largest = hWnd; }
            }
            return true;
        }, IntPtr.Zero);

        return exact != IntPtr.Zero ? exact : largest;
    }
}
