using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

// What every window in the app shares: palette, easing, animation helpers and the acrylic backdrop.
abstract class GlassWindow : Window
{
    [DllImport("user32.dll")] protected static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] protected static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);
    struct Margins { public int L, R, T, B; }
    struct AccentPolicy { public int State, Flags, Color, AnimationId; }
    struct WinCompAttrData { public int Attribute; public IntPtr Data; public int Size; }

    protected static readonly Color Text = Color.FromArgb(235, 255, 255, 255);
    protected static readonly Color Blue = Color.FromRgb(10, 132, 255);
    protected static readonly Color Green = Color.FromRgb(48, 209, 88);
    protected static readonly Color Ring = Color.FromArgb(140, 255, 255, 255);
    protected static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
    protected static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    protected static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

    // Blurred, rounded, dark-framed glass behind a transparent WPF window. Tint is ABGR.
    protected static void MakeGlass(IntPtr hwnd, int tint)
    {
        HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
        var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref m);
        int dark = 1, round = 2;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);    // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(hwnd, 33, ref round, 4);   // DWMWA_WINDOW_CORNER_PREFERENCE = round
        // Acrylic blur via accent policy: unlike the system backdrop it stays blurred when the
        // window isn't focused, and the tint is ours.
        var accent = new AccentPolicy { State = 4, Color = tint }; // ACCENT_ENABLE_ACRYLICBLURBEHIND
        var size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(accent, ptr, false);
        var data = new WinCompAttrData { Attribute = 19, Data = ptr, Size = size }; // WCA_ACCENT_POLICY
        SetWindowCompositionAttribute(hwnd, ref data);
        Marshal.FreeHGlobal(ptr);
    }

    protected static void Anim(IAnimatable target, DependencyProperty p, double to, int ms, IEasingFunction ease, double? from = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        if (from.HasValue) a.From = from.Value;
        target.BeginAnimation(p, a);
    }

    protected static void Anim(SolidColorBrush brush, Color to)
    {
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(180)) { EasingFunction = Out });
    }

    protected static void Go(IAnimatable target, DependencyProperty p, double to, int ms, IEasingFunction ease, int delay, EventHandler done, double? from)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delay) };
        if (from.HasValue) a.From = from.Value;
        if (done != null) a.Completed += done;
        target.BeginAnimation(p, a);
    }

    protected static void After(int ms, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (s, e) => { t.Stop(); action(); };
        t.Start();
    }
}
