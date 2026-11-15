using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace DynamicIsland;

public static class Util
{
    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            hex = hex.Trim();
            return (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex);
        }
        catch { return fallback; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static SolidColorBrush Brush(string hex) => Brush(ParseColor(hex, Colors.White));

    public static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    public static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (double h, double s, double v) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }
}

/// <summary>윈도우 시작 시 자동 실행 (현재 사용자 Run 키).</summary>
public static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "DynamicIsland";

    public static bool Get()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) != null;
        }
        catch { return false; }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            // --startup: 로그인 직후 켜졌다는 표시 (환영 연출을 보여 준다)
            if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --startup");
            else k.DeleteValue(Name, false);
        }
        catch { }
    }

    /// <summary>자동 실행이 켜져 있을 때, 실행할 파일만 다른 위치(설치본)로 바꾼다.</summary>
    public static void PointTo(string exe)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            k.SetValue(Name, $"\"{exe}\" --startup");
        }
        catch { }
    }

    /// <summary>자동 실행이 이 폴더 안의 실행 파일을 가리키는지.</summary>
    public static bool PointsInto(string folder)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is string s && s.Contains(folder, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

public static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    /// <summary>주 모니터를 정확히 덮는 창(게임·동영상 전체 화면)이 앞에 있는지.</summary>
    public static bool IsFullscreenAppActive(IntPtr self)
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || h == self) return false;
        var sb = new System.Text.StringBuilder(64);
        GetClassName(h, sb, 64);
        var cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;
        if (!GetWindowRect(h, out var r)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref info)) return false;
        var m = info.rcMonitor;
        bool primary = (info.dwFlags & 1) != 0;
        return primary && r.Left == m.Left && r.Top == m.Top && r.Right == m.Right && r.Bottom == m.Bottom;
    }
}
