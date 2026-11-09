// Always-on-top glass todo panel. Ctrl+Alt+T toggles, Esc / Ctrl+Q hide, Ctrl+Shift+Q quits.
// Build: build.cmd (uses the .NET Framework csc.exe that ships with Windows, C# 5).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

class Item { public string Text; public int State; } // 0 todo, 1 ongoing, 2 done

class Prayer
{
    public string Name;
    public DateTime At;
    public string Key { get { return At.ToString("yyyy-MM-dd ", CultureInfo.InvariantCulture) + Name; } }
}

// Prayer times computed offline from the sun's position.
static class Prayers
{
    // Changed from the dashboard and kept in prayer-settings.txt. Default: Eastern Province, Sri Lanka.
    public static string Place = "Batticaloa";
    public static double Lat = 7.717, Lon = 81.70;
    public static TimeZoneInfo Zone = TimeZoneInfo.Local;
    static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prayer-settings.txt");
    const double FajrAngle = 18, IshaAngle = 18; // Asr uses the Shafi'i shadow length of 1
    // Calibration: minutes added to match your local mosque timetable, then run build.cmd.
    //                                  Fajr Sunrise Dhuhr Asr Maghrib Isha
    static readonly double[] Offset = {   0,    0,     0,   0,    0,    0 };
    static readonly string[] Names = { "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha" };
    public static readonly string[] SlotNames = { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };
    public static readonly int[] SlotTimes = { 0, 2, 3, 4, 5 }; // index of each prayer in For()

    public static DateTime Now { get { return TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zone); } }
    public static double TzAt(DateTime day) { return Zone.GetUtcOffset(day).TotalHours; }
    public static bool ValidLocation(double lat, double lon) { return lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180; }

    public static void LoadSettings()
    {
        try
        {
            var p = File.ReadAllText(SettingsFile).Trim().Split('|');
            double lat = double.Parse(p[1], CultureInfo.InvariantCulture), lon = double.Parse(p[2], CultureInfo.InvariantCulture);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(p[3]);
            if (ValidLocation(lat, lon)) { Place = p[0]; Lat = lat; Lon = lon; Zone = zone; }
        }
        catch (Exception) { } // no file yet, or a damaged one: keep the defaults
    }

    public static bool SaveSettings()
    {
        try
        {
            File.WriteAllText(SettingsFile, string.Join("|", Place, Lat.ToString(CultureInfo.InvariantCulture), Lon.ToString(CultureInfo.InvariantCulture), Zone.Id));
            return true;
        }
        catch (Exception) { return false; }
    }

    static double Rad(double d) { return d * Math.PI / 180; }
    static double Deg(double r) { return r * 180 / Math.PI; }

    static void Sun(double jd, out double decl, out double eqt)
    {
        double d = jd - 2451545.0;
        double g = Rad(357.529 + 0.98560028 * d), q = 280.459 + 0.98564736 * d;
        double l = Rad(q + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g));
        double e = Rad(23.439 - 0.00000036 * d);
        double ra = Deg(Math.Atan2(Math.Cos(e) * Math.Sin(l), Math.Cos(l))) / 15;
        decl = Math.Asin(Math.Sin(e) * Math.Sin(l));
        eqt = q / 15 - ra;
        eqt -= Math.Round(eqt / 24) * 24;
    }

    // Local times for Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha. tz is hours east of UTC.
    public static DateTime[] For(DateTime day, double tz)
    {
        double jd = day.Date.ToOADate() + 2415018.5, lat = Rad(Lat);
        double[] solar = { 5, 6, 12, 13, 18, 18 };
        for (int pass = 0; pass < 3; pass++) // the sun moves during the day, so refine the guess
            for (int i = 0; i < 6; i++)
            {
                double decl, eqt;
                Sun(jd + (solar[i] - Lon / 15) / 24, out decl, out eqt);
                double noon = 12 - eqt;
                if (i == 2) { solar[i] = noon; continue; }
                double alt = i == 0 ? -FajrAngle : i == 5 ? -IshaAngle
                    : i == 3 ? Deg(Math.Atan(1 / (1 + Math.Tan(Math.Abs(lat - decl))))) : -0.833;
                // ponytail: clamped so far-north locations can't crash; add a high-latitude rule if one is ever used past about 48 degrees.
                double cosH = (Math.Sin(Rad(alt)) - Math.Sin(decl) * Math.Sin(lat)) / (Math.Cos(decl) * Math.Cos(lat));
                double hours = Deg(Math.Acos(Math.Max(-1, Math.Min(1, cosH)))) / 15;
                solar[i] = noon + (i < 2 ? -hours : hours);
            }
        // Rounded up so a reminder never fires before the time has actually entered.
        return solar.Select((h, i) => day.Date.AddMinutes(Math.Ceiling((h + tz - Lon / 15) * 60 + Offset[i]))).ToArray();
    }

    // The prayer whose time is running now. Null between sunrise and Dhuhr, when none is due.
    public static Prayer Current(DateTime now, double tz)
    {
        var today = For(now, tz);
        int i = Array.FindLastIndex(today, t => t <= now);
        if (i == 1) return null;
        return i < 0 ? new Prayer { Name = Names[5], At = For(now.AddDays(-1), tz)[5] } // Isha runs past midnight
                     : new Prayer { Name = Names[i], At = today[i] };
    }

    // TodoPanel.exe --check: exit code is the number of failures. Reference: aladhan.com, same location and angles.
    public static int Check()
    {
        var day = new DateTime(2026, 11, 9);
        var t = For(day, 5.5);
        int[] expected = { 4 * 60 + 41, 5 * 60 + 53, 11 * 60 + 47, 15 * 60 + 9, 17 * 60 + 41, 18 * 60 + 53 };
        int fails = t.Where((x, i) => Math.Abs(x.TimeOfDay.TotalMinutes - expected[i]) > 1 + Math.Abs(Offset[i])).Count();
        Func<int, string> at = h => { var p = Current(day.AddHours(h), 5.5); return p == null ? null : p.Key; };
        if (at(3) != "2026-11-08 Isha") fails++;
        if (at(8) != null) fails++;
        if (at(16) != "2026-11-09 Asr") fails++;
        if (at(23) != "2026-11-09 Isha") fails++;

        // A prayer is Missed exactly when the next prayer starts, unless Prayed was clicked.
        DateTime start = day.AddHours(12), end = day.AddHours(15), since = day;
        if (Tracker.Classify(false, start, end, since, day.AddHours(11)) != Tracker.Upcoming) fails++;
        if (Tracker.Classify(false, start, end, since, day.AddHours(14)) != Tracker.Pending) fails++;
        if (Tracker.Classify(false, start, end, since, end) != Tracker.Missed) fails++;
        if (Tracker.Classify(true, start, end, since, end) != Tracker.Prayed) fails++;
        if (Tracker.Classify(false, start, end, day.AddHours(16), day.AddHours(17)) != Tracker.Untracked) fails++;
        int current, best;
        Tracker.Streaks(new[] { 3, 3, 3, 4, 3, 0, 3, 2 }, out current, out best);
        if (current != 2 || best != 3) fails++;
        if (TodoPanel.PrayerIsland.WaterWait(true, false) != TimeSpan.FromMinutes(90)) fails++;
        if (TodoPanel.PrayerIsland.WaterWait(false, false) != TimeSpan.FromMinutes(10)) fails++;
        if (Prayers.ValidLocation(91, 0) || Prayers.ValidLocation(0, 181) || !Prayers.ValidLocation(7.7, 81.7)) fails++;
        return fails;
    }
}

class Slot { public DateTime Day, Start; public int Index, Status; public double? Delay; public string Key; }

// prayed.txt: a "since" line, then one line per prayer marked prayed. Missed is never stored:
// it is worked out from the times, so there is no job that can fail to run while the PC is off.
static class Tracker
{
    public const int Untracked = 0, Upcoming = 1, Pending = 2, Prayed = 3, Missed = 4;
    const string StampFormat = "yyyy-MM-dd HH:mm";

    static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prayed.txt");
    static readonly Dictionary<string, DateTime?> done = new Dictionary<string, DateTime?>();
    static bool unreadable; // never overwrite a log that exists but could not be read
    public static DateTime Since;

    public static void Load()
    {
        bool found = false;
        try
        {
            if (File.Exists(LogFile))
                foreach (var line in File.ReadAllLines(LogFile).Where(l => l.Trim().Length > 0))
                {
                    DateTime t;
                    var parts = line.Trim().Split('\t');
                    bool stamped = parts.Length > 1 && DateTime.TryParseExact(parts[1], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
                    if (line.StartsWith("since "))
                        found = DateTime.TryParseExact(line.Substring(6).Trim(), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out Since);
                    else if (stamped) { DateTime.TryParseExact(parts[1], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out t); done[parts[0]] = t; }
                    else done[parts[0]] = null;
                }
        }
        catch (Exception) { unreadable = true; }
        if (!found) { Since = Prayers.Now; Save(); }
    }

    static void Save()
    {
        if (unreadable) return;
        var lines = new[] { "since " + Since.ToString(StampFormat, CultureInfo.InvariantCulture) }
            .Concat(done.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Key + (d.Value.HasValue ? "\t" + d.Value.Value.ToString(StampFormat, CultureInfo.InvariantCulture) : "")));
        try
        {
            // Write to a temp file then swap, so a crash mid-write never truncates the history.
            var tmp = LogFile + ".tmp";
            File.WriteAllLines(tmp, lines);
            if (File.Exists(LogFile)) File.Replace(tmp, LogFile, null);
            else File.Move(tmp, LogFile);
        }
        catch (Exception) { }
    }

    public static bool Has(string key) { return done.ContainsKey(key); }
    public static void Mark(string key, DateTime? when) { done[key] = when; Save(); }
    public static void Unmark(string key) { done.Remove(key); Save(); }

    public static int Classify(bool isDone, DateTime start, DateTime end, DateTime since, DateTime now)
    {
        if (isDone) return Prayed;
        if (end <= since) return Untracked; // finished before tracking began
        return now < start ? Upcoming : now < end ? Pending : Missed;
    }

    // Five slots per day, oldest first.
    public static Slot[] Slots(DateTime from, int days, DateTime now)
    {
        var result = new Slot[days * 5];
        var times = Prayers.For(from, Prayers.TzAt(from));
        for (int d = 0; d < days; d++)
        {
            var day = from.Date.AddDays(d);
            var tomorrow = Prayers.For(day.AddDays(1), Prayers.TzAt(day.AddDays(1)));
            for (int i = 0; i < 5; i++)
            {
                var start = times[Prayers.SlotTimes[i]];
                var end = i < 4 ? times[Prayers.SlotTimes[i + 1]] : tomorrow[0];
                var key = new Prayer { Name = Prayers.SlotNames[i], At = start }.Key;
                DateTime? stamp;
                done.TryGetValue(key, out stamp);
                double? delay = stamp.HasValue && stamp.Value >= start ? (stamp.Value - start).TotalMinutes : (double?)null;
                result[d * 5 + i] = new Slot { Day = day, Start = start, Index = i, Key = key, Delay = delay, Status = Classify(done.ContainsKey(key), start, end, Since, now) };
            }
            times = tomorrow;
        }
        return result;
    }

    // Prayers in a row. Upcoming, current and untracked ones neither extend nor break a run.
    public static void Streaks(int[] statuses, out int current, out int best)
    {
        current = best = 0;
        foreach (var s in statuses)
        {
            if (s == Missed) current = 0;
            else if (s == Prayed) best = Math.Max(best, ++current);
        }
    }
}

class TodoPanel : Window
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);
    struct Margins { public int L, R, T, B; }
    struct AccentPolicy { public int State, Flags, Color, AnimationId; }
    struct WinCompAttrData { public int Attribute; public IntPtr Data; public int Size; }

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, VK_T = 0x54, VK_P = 0x50;

    static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string DataFile = Path.Combine(Dir, "todos.txt");
    static readonly string PosFile = Path.Combine(Dir, "position.txt");

    static readonly Color Text = Color.FromArgb(235, 255, 255, 255);
    static readonly Color Blue = Color.FromRgb(10, 132, 255);
    static readonly Color Green = Color.FromRgb(48, 209, 88);
    static readonly Color Ring = Color.FromArgb(140, 255, 255, 255);
    static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

    readonly Grid root = new Grid();
    Border panel; // glass layer; what show/hide animates
    readonly StackPanel list = new StackPanel();
    readonly TextBox input = new TextBox();
    readonly TextBlock count = new TextBlock();
    readonly TextBlock empty = new TextBlock();
    readonly ScaleTransform scale = new ScaleTransform(1, 1);
    readonly TranslateTransform slide = new TranslateTransform();
    bool hiding;
    TextBox editing; // inline editor of the row being renamed, if any
    PrayerDashboard dashboard;

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--check")) Environment.Exit(Prayers.Check());
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        // --demo and --demo-water preview one island alone, with "Not yet" coming back after 10 seconds.
        bool demoWater = args.Contains("--demo-water");
        app.Run(demoWater || args.Contains("--demo") ? (Window)new PrayerIsland(true, demoWater) : new TodoPanel());
    }

    TodoPanel()
    {
        Title = "Todo";
        Width = 340; Height = 440;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = new SolidColorBrush(Text);
        LoadPosition();

        // Header
        var header = new Grid { Margin = new Thickness(4, 0, 4, 12) };
        header.Children.Add(new TextBlock { Text = "Todo", FontSize = 20, FontWeight = FontWeights.SemiBold });
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Opacity = 0.55;
        count.Margin = new Thickness(0, 0, 32, 0);
        header.Children.Add(count);
        var prayersFill = new SolidColorBrush(Colors.Transparent);
        var prayersButton = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = prayersFill, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Prayer dashboard (Ctrl+Alt+P)", Child = PrayerIsland.Moon(15) };
        prayersButton.MouseEnter += (s, e) => Anim(prayersFill, Color.FromArgb(40, 255, 255, 255));
        prayersButton.MouseLeave += (s, e) => Anim(prayersFill, Colors.Transparent);
        prayersButton.MouseLeftButtonDown += (s, e) => e.Handled = true; // don't start window drag
        prayersButton.MouseLeftButtonUp += (s, e) => OpenDashboard();
        header.Children.Add(prayersButton);

        // Input with placeholder
        var placeholder = new TextBlock { Text = "Add a task…", Opacity = 0.45, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        input.Background = Brushes.Transparent;
        input.BorderThickness = new Thickness(0);
        input.Foreground = Foreground;
        input.CaretBrush = Foreground;
        input.FontSize = 14;
        input.TextChanged += (s, e) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.KeyDown += OnInputKey;
        var inputGrid = new Grid();
        inputGrid.Children.Add(placeholder);
        inputGrid.Children.Add(input);
        placeholder.FontSize = 14;
        var inputBox = new Border
        {
            Child = inputGrid,
            Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
        };

        // List
        empty.Text = "All clear";
        empty.Opacity = 0.4;
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.Margin = new Thickness(0, 40, 0, 0);
        var listHost = new Grid();
        listHost.Children.Add(empty);
        listHost.Children.Add(list);
        var scroll = new ScrollViewer { Content = listHost, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };

        root.Margin = new Thickness(14);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(inputBox, 1);
        Grid.SetRow(scroll, 2);
        root.Children.Add(header);
        root.Children.Add(inputBox);
        root.Children.Add(scroll);
        // Glass layer over the blurred desktop: light top-down sheen + hairline highlight border.
        var glass = new Border
        {
            Child = root,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(Color.FromArgb(46, 255, 255, 255), Color.FromArgb(12, 255, 255, 255), 90),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90),
        };
        glass.RenderTransformOrigin = new Point(0.5, 0);
        var tg = new TransformGroup();
        tg.Children.Add(scale);
        tg.Children.Add(slide);
        glass.RenderTransform = tg;
        glass.Opacity = 0;
        panel = glass;
        Content = glass;

        foreach (var item in Load()) AddRow(item, false);
        UpdateCount();

        // Drag from anywhere that isn't a row or the input.
        MouseLeftButtonDown += (s, e) => { DragMove(); SavePosition(); };
        PreviewKeyDown += (s, e) =>
        {
            if (editing != null) return; // let the inline editor handle Esc / Enter
            // Ctrl+Q only hides, so the hotkey keeps working. Ctrl+Shift+Q really exits.
            if (e.Key == Key.Q && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) Close();
            else if (e.Key == Key.Escape || (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)) { HidePanel(); e.Handled = true; }
        };
        SourceInitialized += (s, e) => InitNative();
        Loaded += (s, e) => ShowPanel();
        Closing += (s, e) => SavePosition();
    }

    void InitNative()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Tool window = hidden from Alt+Tab (GWL_EXSTYLE: +WS_EX_TOOLWINDOW, -WS_EX_APPWINDOW).
        SetWindowLong(hwnd, -20, (GetWindowLong(hwnd, -20) | 0x80) & ~0x40000);
        var src = HwndSource.FromHwnd(hwnd);
        MakeGlass(hwnd, 0x50181818); // alpha 0x50 over near-black

        if (!RegisterHotKey(hwnd, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_T))
        {
            MessageBox.Show("Ctrl+Alt+T is already taken (is the panel already running?).", "Todo");
            Application.Current.Shutdown();
            return;
        }
        src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_HOTKEY)
            {
                if (w.ToInt32() == 2) OpenDashboard(); else Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        });
        RegisterHotKey(hwnd, 2, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_P); // optional: the moon button also opens it
        // After the hotkey check, so a second instance never reminds twice or touches the log.
        Prayers.LoadSettings();
        Tracker.Load();
        new PrayerIsland(false).Show();
    }

    void OpenDashboard()
    {
        if (IsVisible && !hiding) HidePanel(); // the panel is topmost and would sit on top of the dashboard
        if (dashboard == null)
        {
            dashboard = new PrayerDashboard();
            dashboard.Closed += (s, e) => dashboard = null;
            dashboard.Show();
        }
        dashboard.Activate();
    }

    // Blurred, rounded, dark-framed glass behind a transparent WPF window. Tint is ABGR.
    static void MakeGlass(IntPtr hwnd, int tint)
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

    // ---- show / hide ----

    void Toggle()
    {
        if (IsVisible && !hiding && IsActive) HidePanel();
        else ShowPanel();
    }

    void ShowPanel()
    {
        hiding = false;
        Show();
        Activate();
        input.Focus();
        Anim(panel, OpacityProperty, 1, 220, Out);
        Anim(scale, ScaleTransform.ScaleXProperty, 1, 380, Spring, 0.94);
        Anim(scale, ScaleTransform.ScaleYProperty, 1, 380, Spring, 0.94);
        Anim(slide, TranslateTransform.YProperty, 0, 380, Spring, -12);
    }

    void HidePanel()
    {
        hiding = true;
        SavePosition();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = Out };
        fade.Completed += (s, e) => { if (hiding) Hide(); };
        panel.BeginAnimation(OpacityProperty, fade);
        Anim(scale, ScaleTransform.ScaleXProperty, 0.96, 150, Out);
        Anim(scale, ScaleTransform.ScaleYProperty, 0.96, 150, Out);
        Anim(slide, TranslateTransform.YProperty, -8, 150, Out);
    }

    // ---- rows ----

    void OnInputKey(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var text = input.Text.Trim();
        if (text.Length == 0) return;
        AddRow(new Item { Text = text }, true);
        input.Clear();
        Save();
    }

    void AddRow(Item item, bool animate)
    {
        var tick = new TextBlock { Text = "", FontFamily = Icons, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        var checkFill = new SolidColorBrush(Colors.Transparent);
        var checkRing = new SolidColorBrush(Ring);
        var dotScale = new ScaleTransform(0, 0);
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Blue), RenderTransform = dotScale, RenderTransformOrigin = new Point(0.5, 0.5) };
        var mark = new Grid();
        mark.Children.Add(dot);
        mark.Children.Add(tick);
        var check = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), BorderBrush = checkRing, Background = checkFill, Child = mark, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0), Cursor = Cursors.Hand };
        var label = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var del = new TextBlock { Text = "", FontFamily = Icons, FontSize = 10, Opacity = 0, Padding = new Thickness(6), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(label, 1);
        Grid.SetColumn(del, 2);
        grid.Children.Add(check);
        grid.Children.Add(label);
        grid.Children.Add(del);

        var hover = new SolidColorBrush(Colors.Transparent);
        var row = new Border { Child = grid, Background = hover, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 7, 4, 7), Margin = new Thickness(0, 1, 0, 1), Tag = item, Cursor = Cursors.Hand };
        var move = new TranslateTransform();
        row.RenderTransform = move;

        // todo: plain ring. ongoing: blue ring + blue dot. done: filled green + tick.
        Action applyState = () =>
        {
            bool ongoing = item.State == 1, done = item.State == 2;
            Anim(checkFill, done ? Green : Color.FromArgb(0, Green.R, Green.G, Green.B));
            Anim(checkRing, done ? Green : ongoing ? Blue : Ring);
            Anim(dotScale, ScaleTransform.ScaleXProperty, ongoing ? 1 : 0, 260, ongoing ? Spring : Out);
            Anim(dotScale, ScaleTransform.ScaleYProperty, ongoing ? 1 : 0, 260, ongoing ? Spring : Out);
            Anim(tick, OpacityProperty, done ? 1 : 0, 180, Out);
            Anim(label, OpacityProperty, done ? 0.45 : 1, 180, Out);
            label.TextDecorations = done ? TextDecorations.Strikethrough : null;
        };
        tick.Opacity = item.State == 2 ? 1 : 0;
        applyState();

        row.MouseEnter += (s, e) => { Anim(hover, Color.FromArgb(22, 255, 255, 255)); Anim(del, OpacityProperty, 0.7, 150, Out); };
        row.MouseLeave += (s, e) => { Anim(hover, Colors.Transparent); Anim(del, OpacityProperty, 0, 150, Out); };
        Action<int> advance = step =>
        {
            item.State = (item.State + step) % 3; // todo -> ongoing -> done -> todo
            applyState();
            Anim(check, Border.WidthProperty, 18, 260, Spring, 14);
            Save();
        };
        // Click anywhere on the row advances the state. Double-click on the text undoes that
        // first click and opens an inline editor instead. Enter / click away saves, Esc cancels.
        row.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true; // don't start window drag
            if (editing != null || del.IsMouseOver) return;
            if (e.ClickCount == 1 || check.IsMouseOver) { advance(1); return; }
            if (e.ClickCount != 2) return;
            advance(2); // = one step back, undoing the first click
            // ponytail: first click of a double-click flashes the next state briefly; a click-delay timer avoids it but makes every tick laggy.
            var editor = new TextBox
            {
                Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Margin = new Thickness(-2, 0, 0, 0), Foreground = Foreground, CaretBrush = Foreground,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(editor, 1);
            bool ended = false;
            Action<bool> end = commit =>
            {
                if (ended) return;
                ended = true;
                editing = null;
                var text = editor.Text.Trim();
                if (commit && text.Length > 0 && text != item.Text) { item.Text = text; label.Text = text; Save(); }
                grid.Children.Remove(editor);
                label.Visibility = Visibility.Visible;
            };
            editor.PreviewKeyDown += (s2, e2) =>
            {
                if (e2.Key == Key.Enter) { end(true); input.Focus(); e2.Handled = true; }
                else if (e2.Key == Key.Escape) { end(false); input.Focus(); e2.Handled = true; }
            };
            editor.LostKeyboardFocus += (s2, e2) => end(true);
            label.Visibility = Visibility.Hidden;
            grid.Children.Add(editor);
            editing = editor;
            // Focus after layout, otherwise the just-added editor can't take focus yet.
            Dispatcher.BeginInvoke(new Action(() => { editor.Focus(); editor.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        };
        del.MouseLeftButtonUp += (s, e) => { e.Handled = true; RemoveRow(row); };

        list.Children.Add(row);
        if (animate)
        {
            Anim(row, OpacityProperty, 1, 260, Out, 0);
            Anim(move, TranslateTransform.YProperty, 0, 340, Spring, -10);
        }
    }

    void RemoveRow(Border row)
    {
        row.Tag = null;
        row.IsHitTestVisible = false;
        Save();
        Anim(row, OpacityProperty, 0, 160, Out);
        Anim((TranslateTransform)row.RenderTransform, TranslateTransform.XProperty, 30, 200, Out);
        var collapse = new DoubleAnimation(row.ActualHeight, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out, BeginTime = TimeSpan.FromMilliseconds(120) };
        collapse.Completed += (s, e) => list.Children.Remove(row);
        row.BeginAnimation(HeightProperty, collapse);
    }

    void UpdateCount()
    {
        var items = list.Children.OfType<Border>().Select(b => b.Tag).OfType<Item>().ToList();
        count.Text = items.Count(i => i.State != 2) + " left";
        Anim(empty, OpacityProperty, items.Count == 0 ? 0.4 : 0, 200, Out);
    }

    // ---- animation helpers ----

    static void Anim(IAnimatable target, DependencyProperty p, double to, int ms, IEasingFunction ease, double? from = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        if (from.HasValue) a.From = from.Value;
        target.BeginAnimation(p, a);
    }

    static void Anim(SolidColorBrush brush, Color to)
    {
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(180)) { EasingFunction = Out });
    }

    // ---- persistence: todos.txt, one "[ ] todo" / "[-] ongoing" / "[x] done" per line ----

    static readonly string[] Marks = { "[ ] ", "[-] ", "[x] " };

    static Item[] Load()
    {
        if (!File.Exists(DataFile)) return new Item[0];
        return File.ReadAllLines(DataFile).Where(l => l.Trim().Length > 0).Select(l =>
        {
            int state = Array.FindIndex(Marks, m => l.StartsWith(m));
            return new Item { Text = state >= 0 ? l.Substring(4) : l.Trim(), State = Math.Max(state, 0) };
        }).ToArray();
    }

    void Save()
    {
        var lines = list.Children.OfType<Border>().Select(b => b.Tag).OfType<Item>()
            .Select(i => Marks[i.State] + i.Text).ToArray();
        // Write to a temp file then swap, so a crash mid-write never truncates the list.
        var tmp = DataFile + ".tmp";
        File.WriteAllLines(tmp, lines);
        if (File.Exists(DataFile)) File.Replace(tmp, DataFile, null);
        else File.Move(tmp, DataFile);
        UpdateCount();
    }

    void LoadPosition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 40;
        try
        {
            var p = File.ReadAllText(PosFile).Split(',');
            double x = double.Parse(p[0]), y = double.Parse(p[1]);
            // Ignore saved spots that are now off-screen (monitor unplugged etc).
            if (x >= SystemParameters.VirtualScreenLeft && y >= SystemParameters.VirtualScreenTop &&
                x + 40 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                y + 40 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            { Left = x; Top = y; }
        }
        catch (Exception) { }
    }

    void SavePosition()
    {
        try { File.WriteAllText(PosFile, (int)Left + "," + (int)Top); } catch (Exception) { }
    }

    // ---- prayer reminder: Dynamic Island style pill at the top centre of the screen ----

    // A fixed per-pixel transparent window: curves are fully anti-aliased, and clicks on its empty
    // pixels fall through to whatever is underneath. DWM acrylic can't be used here because it
    // fills the whole window rectangle and ignores window regions.
    internal class PrayerIsland : Window
    {
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        class SpringValue { public double X, V, To; }

        const double Stiffness = 260, Damping = 24; // slightly underdamped: a small settle, no wobble
        const double WinW = 500, WinH = 170, Inset = 2, BlurMargin = 40;
        const double SeedW = 76, SeedH = 26;
        const double CompactW = 244, CompactH = 44;
        const double OpenW = 430, OpenH = 126, OpenR = 34; // OpenR = button radius 20 + inset 14, so the curves are concentric

        static readonly Color Gold = Color.FromRgb(255, 214, 140), WaterBlue = Color.FromRgb(120, 200, 255);
        static readonly Geometry MoonShape = BuildMoon();
        static readonly Geometry DropShape = Geometry.Parse("M12,2 C12,2 5,10 5,15 A7,7 0 0 0 19,15 C19,10 12,2 12,2 Z");

        readonly bool demo, demoWater;
        readonly Prayer waterNotice = new Prayer { Name = "Drink water" };
        readonly Prayer demoPrayer = new Prayer { Name = "Asr", At = DateTime.Now };
        readonly SpringValue w = new SpringValue(), h = new SpringValue(), r = new SpringValue();
        readonly Border pill = new Border();
        readonly Image backdrop = new Image { Stretch = Stretch.Fill, Margin = new Thickness(-BlurMargin), Effect = new BlurEffect { Radius = 36, RenderingBias = RenderingBias.Performance } };
        readonly Grid stage = new Grid { Opacity = 0 };
        readonly RectangleGeometry clip = new RectangleGeometry();
        readonly TextBlock compactLeft = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock compactRight = new TextBlock { FontSize = 13, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 18, 0) };
        readonly TextBlock nameText = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold };
        readonly TextBlock detailText = new TextBlock { FontSize = 12.5, Opacity = 0.8, Margin = new Thickness(0, -2, 0, 0) };
        readonly TextBlock timeText = new TextBlock { FontSize = 20, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        readonly System.Windows.Shapes.Ellipse glow = new System.Windows.Shapes.Ellipse { Width = 44, Height = 44, Opacity = 0 };
        readonly System.Windows.Shapes.Path smallIcon = Moon(18), bigIcon = Moon(24);
        readonly System.Windows.Shapes.Path check = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,10.5 L8,15.5 L17,5"), Width = 20, Height = 20, Opacity = 0,
            Stroke = new SolidColorBrush(Green), StrokeThickness = 2.4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            StrokeDashArray = new DoubleCollection { 9, 9 }, StrokeDashOffset = 9,
        };
        Grid compact, header, actions;
        TextBlock yesLabel;

        IntPtr hwnd;
        double dpi = 1;
        bool looping, open;
        TimeSpan lastFrame;
        Prayer shown; // what the island is displaying; null while hidden
        string prayedKey, snoozedKey;
        DateTime snoozedUntil, waterDue; // waterDue is UTC, so changing the prayer time zone can't shift it

        bool ShowingWater { get { return shown == waterNotice; } }

        internal static TimeSpan WaterWait(bool drank, bool demo)
        {
            if (demo) return TimeSpan.FromSeconds(drank ? 20 : 10);
            return TimeSpan.FromMinutes(drank ? 90 : 10);
        }

        internal PrayerIsland(bool demo, bool demoWater = false)
        {
            this.demo = demo;
            this.demoWater = demoWater;
            waterDue = demoWater ? DateTime.UtcNow : demo ? DateTime.MaxValue : DateTime.UtcNow + WaterWait(true, false);
            Title = "Prayer reminder";
            Width = WinW; Height = WinH;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            Foreground = new SolidColorBrush(Text);
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - WinW) / 2;
            Top = area.Top + 6;

            pill.HorizontalAlignment = HorizontalAlignment.Center;
            pill.VerticalAlignment = VerticalAlignment.Top;
            pill.Margin = new Thickness(0, Inset, 0, 0);
            pill.BorderThickness = new Thickness(1);
            pill.Background = new LinearGradientBrush(Color.FromArgb(165, 38, 38, 42), Color.FromArgb(150, 12, 12, 14), 90);
            pill.BorderBrush = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90);

            // Compact: icon and label on the left, detail on the right.
            var iconSlot = new Grid { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) };
            iconSlot.Children.Add(smallIcon);
            iconSlot.Children.Add(check);
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            left.Children.Add(iconSlot);
            left.Children.Add(compactLeft);
            compact = Layer(CompactW, CompactH, Inset);
            compact.Children.Add(left);
            compact.Children.Add(compactRight);

            // Open: tile, name and time on top, the two actions underneath.
            var tile = new Grid { Width = 44, Height = 44, Margin = new Thickness(0, 0, 12, 0) };
            tile.Children.Add(glow);
            tile.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)) });
            bigIcon.HorizontalAlignment = HorizontalAlignment.Center;
            bigIcon.VerticalAlignment = VerticalAlignment.Center;
            tile.Children.Add(bigIcon);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(nameText);
            titles.Children.Add(detailText);
            var who = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            who.Children.Add(tile);
            who.Children.Add(titles);
            header = Layer(OpenW - 36, 44, Inset + 14);
            header.Children.Add(who);
            header.Children.Add(timeText);

            actions = Layer(OpenW - 28, 40, Inset + 72);
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            var notYet = MakeButton("Not yet", Color.FromArgb(38, 255, 255, 255), Color.FromArgb(64, 255, 255, 255), Colors.White, () => Answer(false));
            var prayed = MakeButton("Prayed", Green, Color.FromRgb(76, 224, 112), Color.FromRgb(4, 40, 16), () => Answer(true));
            yesLabel = (TextBlock)prayed.Child;
            Grid.SetColumn(prayed, 2);
            actions.Children.Add(notYet);
            actions.Children.Add(prayed);

            var content = new Grid { Width = WinW, Height = WinH, Clip = clip };
            content.Children.Add(compact);
            content.Children.Add(header);
            content.Children.Add(actions);
            var glass = new Grid { Width = WinW, Height = WinH, Clip = clip }; // clip outside the blur so the edge stays crisp
            glass.Children.Add(backdrop);
            stage.Children.Add(glass);
            stage.Children.Add(pill);
            stage.Children.Add(content);
            Content = stage;

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x80 | 0x08000000); // TOOLWINDOW | NOACTIVATE: never takes focus from what you're typing in
                dpi = HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice.M11;
            };
            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (s, e) => Tick();
            clock.Start();
        }

        static Grid Layer(double width, double height, double top)
        {
            return new Grid
            {
                Width = width, Height = height, Opacity = 0, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, top, 0, 0),
                RenderTransform = new ScaleTransform(1, 1), RenderTransformOrigin = new Point(0.5, 0.5),
            };
        }

        static Geometry BuildMoon()
        {
            var crescent = Geometry.Combine(new EllipseGeometry(new Point(12, 12), 10, 10), new EllipseGeometry(new Point(16.2, 9.6), 8.3, 8.3), GeometryCombineMode.Exclude, null);
            var shape = new GeometryGroup();
            shape.Children.Add(crescent);
            shape.Children.Add(Geometry.Parse("M18.6,6.2 L19.5,8.5 L21.8,9.4 L19.5,10.3 L18.6,12.6 L17.7,10.3 L15.4,9.4 L17.7,8.5 Z"));
            return shape;
        }

        internal static System.Windows.Shapes.Path Moon(double size)
        {
            return new System.Windows.Shapes.Path { Data = MoonShape, Width = size, Height = size, Stretch = Stretch.Uniform, Fill = new SolidColorBrush(Gold) };
        }

        static Border MakeButton(string label, Color rest, Color hover, Color ink, Action click)
        {
            var fill = new SolidColorBrush(rest);
            var scale = new ScaleTransform(1, 1);
            var b = new Border
            {
                Background = fill, CornerRadius = new CornerRadius(20), Cursor = Cursors.Hand,
                RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5),
                Child = new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Action<double, int> press = (to, ms) =>
            {
                Anim(scale, ScaleTransform.ScaleXProperty, to, ms, Spring);
                Anim(scale, ScaleTransform.ScaleYProperty, to, ms, Spring);
            };
            b.MouseEnter += (s, e) => Anim(fill, hover);
            b.MouseLeave += (s, e) => { Anim(fill, rest); press(1, 240); };
            b.MouseLeftButtonDown += (s, e) => press(0.94, 140);
            b.MouseLeftButtonUp += (s, e) => { press(1, 280); click(); };
            return b;
        }

        // ---- schedule ----

        void Tick()
        {
            var now = Prayers.Now;
            var p = demoWater ? null : demo ? demoPrayer : Prayers.Current(now, Prayers.TzAt(now));
            bool done = p != null && (demo ? p.Key == prayedKey : Tracker.Has(p.Key));
            bool prayerDue = p != null && !done && !(p.Key == snoozedKey && now < snoozedUntil);
            if (shown != null)
            {
                if (!open) return;
                // A due prayer replaces water, which stays due and returns afterwards.
                if (ShowingWater) { if (prayerDue) Dismiss(); }
                // The prayer's time ended with the island still up. The next tick shows the new one.
                else if (p == null || p.Key != shown.Key || done) Dismiss();
                return;
            }
            if (prayerDue) Present(p);
            else if (DateTime.UtcNow >= waterDue)
            {
                waterNotice.At = now;
                Present(waterNotice);
            }
        }

        internal void Answer(bool yes)
        {
            if (!open) return;
            open = false;
            glow.BeginAnimation(OpacityProperty, null);
            var name = shown.Name;
            string backIn = demo ? "Back in 10 sec" : "Back in 10 min";
            if (ShowingWater)
            {
                waterDue = DateTime.UtcNow + WaterWait(yes, demo);
                if (yes && demo) prayedKey = "water"; // ends the demo once the island has gone
                compactLeft.Text = yes ? "Drank" : "Not yet";
                compactRight.Text = yes ? (demo ? "Next in 20 sec" : "Next in 90 min") : backIn;
            }
            else
            {
                if (!yes)
                {
                    snoozedKey = shown.Key;
                    snoozedUntil = Prayers.Now + (demo ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(10));
                }
                else if (demo) prayedKey = shown.Key;
                else Tracker.Mark(shown.Key, Prayers.Now);
                compactLeft.Text = yes ? "Prayed" : "Not yet";
                compactRight.Text = yes ? name : backIn;
            }
            Conceal(header);
            Conceal(actions);
            Morph(CompactW, CompactH, CompactH / 2);
            Reveal(compact, 110);
            if (yes)
            {
                smallIcon.Opacity = 0;
                check.Opacity = 1;
                Go(check, System.Windows.Shapes.Shape.StrokeDashOffsetProperty, 0, 360, Out, 180, null, 9);
            }
            After(1700, Dismiss);
        }

        // ---- island choreography ----

        internal void Present(Prayer p)
        {
            shown = p;
            bool water = ShowingWater;
            var accent = water ? WaterBlue : Gold;
            detailText.Text = water ? "Have a glass of water" : "It's time to pray";
            yesLabel.Text = water ? "Drank" : "Prayed";
            smallIcon.Data = bigIcon.Data = water ? DropShape : MoonShape;
            smallIcon.Fill = bigIcon.Fill = new SolidColorBrush(accent);
            glow.Fill = new RadialGradientBrush(Color.FromArgb(120, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
            nameText.Text = compactLeft.Text = p.Name;
            timeText.Text = compactRight.Text = p.At.ToString("h:mm tt", CultureInfo.InvariantCulture);
            smallIcon.Opacity = 1;
            check.Opacity = 0;
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13); // back on top of other topmost windows: NOSIZE | NOMOVE | NOACTIVATE
            CaptureBackdrop();
            Jump(w, SeedW); Jump(h, SeedH); Jump(r, SeedH / 2);
            Morph(CompactW, CompactH, CompactH / 2);
            Go(stage, OpacityProperty, 1, 140, Out, 0, null, null);
            Reveal(compact, 90);
            After(620, () =>
            {
                open = true;
                Morph(OpenW, OpenH, OpenR);
                Conceal(compact);
                Reveal(header, 70);
                Reveal(actions, 140);
                glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(1600))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            });
        }

        void Dismiss()
        {
            open = false;
            glow.BeginAnimation(OpacityProperty, null);
            Conceal(compact);
            Conceal(header);
            Conceal(actions);
            Morph(SeedW, SeedH, SeedH / 2);
            Go(stage, OpacityProperty, 0, 200, Out, 80, null, null);
            After(320, () =>
            {
                StopLoop();
                backdrop.Source = null;
                shown = null;
                if (demo && prayedKey != null) Application.Current.Shutdown();
            });
        }

        // ponytail: glass is a blurred snapshot taken as the island appears, so it won't follow
        // windows moving behind it. Live refresh = SetWindowDisplayAffinity(exclude) + a recapture timer.
        void CaptureBackdrop()
        {
            int wide = (int)((WinW + 2 * BlurMargin) * dpi), tall = (int)((WinH + 2 * BlurMargin) * dpi);
            using (var bmp = new System.Drawing.Bitmap(wide, tall, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen((int)((Left - BlurMargin) * dpi), (int)((Top - BlurMargin) * dpi), 0, 0, bmp.Size);
                var handle = bmp.GetHbitmap();
                backdrop.Source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(handle);
            }
        }

        void Reveal(FrameworkElement el, int delay)
        {
            var scale = (ScaleTransform)el.RenderTransform;
            var blur = new BlurEffect { Radius = 10 };
            el.Effect = blur;
            el.IsHitTestVisible = true;
            Go(el, OpacityProperty, 1, 240, Out, delay, null, null);
            Go(scale, ScaleTransform.ScaleXProperty, 1, 420, Spring, delay, null, 0.92);
            Go(scale, ScaleTransform.ScaleYProperty, 1, 420, Spring, delay, null, 0.92);
            // Drop the effect once sharp: text under a BlurEffect renders softer even at radius 0.
            Go(blur, BlurEffect.RadiusProperty, 0, 260, Out, delay, (s, e) => { if (el.Effect == blur) el.Effect = null; }, null);
        }

        void Conceal(FrameworkElement el)
        {
            var scale = (ScaleTransform)el.RenderTransform;
            var blur = new BlurEffect { Radius = 0 };
            el.Effect = blur;
            el.IsHitTestVisible = false;
            Go(el, OpacityProperty, 0, 150, Out, 0, null, null);
            Go(scale, ScaleTransform.ScaleXProperty, 0.96, 150, Out, 0, null, null);
            Go(scale, ScaleTransform.ScaleYProperty, 0.96, 150, Out, 0, null, null);
            Go(blur, BlurEffect.RadiusProperty, 8, 150, Out, 0, null, null);
        }

        internal static void Go(IAnimatable target, DependencyProperty p, double to, int ms, IEasingFunction ease, int delay, EventHandler done, double? from)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delay) };
            if (from.HasValue) a.From = from.Value;
            if (done != null) a.Completed += done;
            target.BeginAnimation(p, a);
        }

        static void After(int ms, Action action)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); action(); };
            t.Start();
        }

        // ---- spring-driven pill shape ----

        static void Jump(SpringValue s, double x) { s.X = s.To = x; s.V = 0; }

        void Morph(double width, double height, double radius)
        {
            w.To = width; h.To = height; r.To = radius;
            if (looping) return;
            looping = true;
            lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }

        void StopLoop()
        {
            if (looping) CompositionTarget.Rendering -= OnFrame;
            looping = false;
        }

        void OnFrame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == lastFrame) return; // WPF can raise Rendering more than once per frame
            double dt = lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - lastFrame).TotalSeconds, 1 / 30.0);
            lastFrame = now;
            foreach (var s in new[] { w, h, r })
                for (int i = 0; i < 4; i++) // substeps keep the spring stable on a slow frame
                {
                    s.V += (-Stiffness * (s.X - s.To) - Damping * s.V) * dt / 4;
                    s.X += s.V * dt / 4;
                }
            bool settled = new[] { w, h, r }.All(s => Math.Abs(s.X - s.To) < 0.2 && Math.Abs(s.V) < 2);
            if (settled) { Jump(w, w.To); Jump(h, h.To); Jump(r, r.To); }
            ApplyShape();
            if (settled) StopLoop();
        }

        void ApplyShape()
        {
            pill.Width = w.X;
            pill.Height = h.X;
            pill.CornerRadius = new CornerRadius(r.X);
            clip.Rect = new Rect((WinW - w.X) / 2, Inset, w.X, h.X);
            clip.RadiusX = clip.RadiusY = r.X;
        }
    }

    // ---- prayer dashboard: today, location and time zone, tracking analytics ----

    internal class PrayerDashboard : Window
    {
        const double WinW = 1120, Side = 24, Gap = 12, ContentW = WinW - 2 * Side, CardPad = 16, SettingsH = 104;

        static SolidColorBrush Rgb(byte r, byte g, byte b, byte a = 255)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        static readonly Brush Ink = Rgb(240, 239, 236), Soft = Rgb(195, 194, 183), Muted = Rgb(160, 158, 151);
        // Blue and orange instead of green and red: red and green fail colorblind separation here (deltaE 4.1 vs 26.8).
        static readonly Brush PrayedInk = Rgb(57, 135, 229), MissedInk = Rgb(217, 89, 38), Warning = Rgb(250, 178, 25);
        static readonly Brush GridLine = Rgb(255, 255, 255, 18), BaseLine = Rgb(255, 255, 255, 56), Faint = Rgb(255, 255, 255, 16);
        static readonly Brush CardFill = Rgb(255, 255, 255, 12), CardLine = Rgb(255, 255, 255, 26), MissedWash = Rgb(217, 89, 38, 56), PopupFill = Rgb(38, 38, 42);
        static readonly Color SelectedFill = Color.FromArgb(40, 255, 255, 255), HoverFill = Color.FromArgb(22, 255, 255, 255), None = Color.FromArgb(0, 255, 255, 255);
        static readonly Style FocusRing = MakeFocusRing();

        const string TickGlyph = "\uE73E", CrossGlyph = "\uE711", ClockGlyph = "\uE916", ChevronGlyph = "\uE70D", CloseGlyph = "\uE8BB";

        class Place { public string Name; public double Lat, Lon; public Place(string n, double a, double o) { Name = n; Lat = a; Lon = o; } }

        static readonly Place[] Places =
        {
            new Place("Batticaloa", 7.717, 81.70), new Place("Kattankudy", 7.675, 81.729), new Place("Kalmunai", 7.409, 81.835),
            new Place("Akkaraipattu", 7.217, 81.85), new Place("Ampara", 7.301, 81.675), new Place("Trincomalee", 8.571, 81.233),
            new Place("Colombo", 6.927, 79.861), new Place("Kandy", 7.291, 80.636), new Place("Galle", 6.053, 80.221),
            new Place("Jaffna", 9.668, 80.007), new Place("Kurunegala", 7.487, 80.365), new Place("Puttalam", 8.036, 79.828),
            new Place("Mannar", 8.981, 79.904), new Place("Anuradhapura", 8.312, 80.414),
        };

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly Border root = new Border();
        readonly ScaleTransform rootScale = new ScaleTransform(0.97, 0.97);
        readonly TextBlock subtitle = T("", 13, Muted);
        readonly Grid todayRow = new Grid();
        readonly StackPanel rangeRow = new StackPanel { Orientation = Orientation.Horizontal };
        readonly StackPanel analytics = new StackPanel();
        readonly Border settings = new Border { Height = 0, ClipToBounds = true };
        readonly TextBox latBox = Field(), lonBox = Field();
        readonly TextBlock settingsNote = T("", 12.5, Soft);
        readonly TimeZoneInfo[] zones = TimeZoneInfo.GetSystemTimeZones().ToArray();
        int zoneIndex;
        int range = 30;
        bool settingsOpen;
        string signature;

        internal PrayerDashboard()
        {
            Title = "Prayers";
            Width = WinW;
            Height = Math.Min(816, SystemParameters.WorkArea.Height - 16);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            Foreground = Ink;

            var titleBlock = new StackPanel { Orientation = Orientation.Horizontal };
            var moon = PrayerIsland.Moon(26);
            moon.Margin = new Thickness(0, 0, 12, 0);
            titleBlock.Children.Add(moon);
            var titles = new StackPanel();
            titles.Children.Add(T("Prayers", 22, Ink, FontWeights.SemiBold));
            titles.Children.Add(subtitle);
            titleBlock.Children.Add(titles);

            var closeFill = new SolidColorBrush(None);
            var close = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = closeFill, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = Glyph(CloseGlyph, 11, Soft) };
            Clickable(close, "Close dashboard", FadeAndClose);
            close.MouseEnter += (s, e) => Anim(closeFill, SelectedFill);
            close.MouseLeave += (s, e) => Anim(closeFill, None);

            var header = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, Gap + 2) };
            header.Children.Add(titleBlock);
            header.Children.Add(close);
            header.MouseLeftButtonDown += (s, e) => DragMove();

            for (int i = 0; i < 5; i++) todayRow.ColumnDefinitions.Add(new ColumnDefinition());
            todayRow.Margin = new Thickness(0, 0, 0, Gap);

            var filters = new Grid { Margin = new Thickness(0, 0, 0, Gap) };
            filters.Children.Add(rangeRow);
            var settingsButton = Pill("Location & time zone", false, ToggleSettings);
            settingsButton.HorizontalAlignment = HorizontalAlignment.Right;
            filters.Children.Add(settingsButton);

            BuildSettings();

            var body = new StackPanel();
            body.Children.Add(todayRow);
            body.Children.Add(filters);
            body.Children.Add(settings);
            body.Children.Add(analytics);
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };

            var layout = new Grid { Margin = new Thickness(Side, 18, Side, 16) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(scroll, 1);
            layout.Children.Add(header);
            layout.Children.Add(scroll);

            root.Child = layout;
            root.CornerRadius = new CornerRadius(8);
            root.BorderThickness = new Thickness(1);
            root.Background = new LinearGradientBrush(Color.FromArgb(22, 255, 255, 255), Color.FromArgb(6, 255, 255, 255), 90);
            root.BorderBrush = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90);
            root.Opacity = 0;
            root.RenderTransform = rootScale;
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            Content = root;

            Render(true);

            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { FadeAndClose(); e.Handled = true; } };
            SourceInitialized += (s, e) => MakeGlass(new WindowInteropHelper(this).Handle, unchecked((int)0xE61F1C1C));
            Loaded += (s, e) =>
            {
                Anim(root, OpacityProperty, 1, 220, Out);
                Anim(rootScale, ScaleTransform.ScaleXProperty, 1, 380, Spring);
                Anim(rootScale, ScaleTransform.ScaleYProperty, 1, 380, Spring);
            };
            var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            refresh.Tick += (s, e) => { if (Signature(CurrentSlots(Prayers.Now), Prayers.Now) != signature) Render(false); };
            refresh.Start();
            Closed += (s, e) => refresh.Stop();
        }

        void FadeAndClose()
        {
            PrayerIsland.Go(root, OpacityProperty, 0, 130, Out, 0, (s, e) => Close(), null);
        }

        // ---- small building blocks ----

        static TextBlock T(string text, double size, Brush color, FontWeight? weight = null)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeights.Normal };
        }

        static TextBlock Glyph(string glyph, double size, Brush color)
        {
            return new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        static TextBox Field()
        {
            return new TextBox { Width = 92, Height = 34, Padding = new Thickness(8, 7, 8, 0), FontSize = 13, Background = Faint, Foreground = Ink, CaretBrush = Ink, BorderBrush = CardLine, BorderThickness = new Thickness(1) };
        }

        static Style MakeFocusRing()
        {
            var ring = new FrameworkElementFactory(typeof(Border));
            ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            ring.SetValue(Border.BorderBrushProperty, Brushes.White);
            ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            var style = new Style(typeof(Control));
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = ring }));
            return style;
        }

        // Mouse and keyboard behaviour shared by everything that acts like a button.
        static void Clickable(Border b, string name, Action click)
        {
            b.Cursor = Cursors.Hand;
            b.Focusable = true;
            b.FocusVisualStyle = FocusRing;
            AutomationProperties.SetName(b, name);
            b.MouseLeftButtonDown += (s, e) => e.Handled = true; // don't start window drag
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            b.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; click(); } };
        }

        static Border Card(UIElement child, double padding = CardPad)
        {
            return new Border { Child = child, Background = CardFill, BorderBrush = CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(padding) };
        }

        static Border Pill(string label, bool selected, Action click)
        {
            var fill = new SolidColorBrush(selected ? SelectedFill : None);
            var pill = new Border { Background = fill, BorderBrush = selected ? Brushes.Transparent : CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 7), Margin = new Thickness(0, 0, 6, 0), Child = T(label, 13, selected ? Ink : Soft, FontWeights.SemiBold) };
            Clickable(pill, label, click);
            pill.MouseEnter += (s, e) => Anim(fill, selected ? SelectedFill : HoverFill);
            pill.MouseLeave += (s, e) => Anim(fill, selected ? SelectedFill : None);
            return pill;
        }

        static void Tip(FrameworkElement target, string value, string detail)
        {
            var body = new StackPanel();
            body.Children.Add(T(value, 13, Ink, FontWeights.SemiBold));
            body.Children.Add(T(detail, 12, Soft));
            target.ToolTip = new ToolTip { Content = body, Background = PopupFill, BorderBrush = CardLine, Padding = new Thickness(10, 7, 10, 8) };
            ToolTipService.SetInitialShowDelay(target, 60);
            ToolTipService.SetBetweenShowDelay(target, 0);
        }

        static string Hm(DateTime t) { return t.ToString("h:mm tt", Inv); }
        static string Md(DateTime t) { return t.ToString("MMM d", Inv); }
        static string Minutes(double m) { return m < 60 ? Math.Round(m) + " min" : (int)(m / 60) + "h " + (int)Math.Round(m % 60) + "m"; }
        static string Percent(int part, int whole) { return whole == 0 ? "\u2014" : Math.Round(100.0 * part / whole) + "%"; }

        static string StatusLabel(int status)
        {
            switch (status)
            {
                case Tracker.Prayed: return "Prayed";
                case Tracker.Missed: return "Missed";
                case Tracker.Pending: return "Time now";
                case Tracker.Upcoming: return "Upcoming";
                default: return "Not tracked";
            }
        }

        static string Utc(DateTime when)
        {
            var o = Prayers.Zone.GetUtcOffset(when);
            return "UTC" + (o < TimeSpan.Zero ? "-" : "+") + o.ToString(@"hh\:mm", Inv);
        }

        // ---- location and time zone ----

        void BuildSettings()
        {
            var zoneLabels = zones.Select(z => z.DisplayName).ToArray();
            zoneIndex = Math.Max(0, Array.FindIndex(zones, z => z.Id == Prayers.Zone.Id));
            latBox.Text = Prayers.Lat.ToString(Inv);
            lonBox.Text = Prayers.Lon.ToString(Inv);

            Action<int> setZone;
            var zoneDrop = Dropdown(392, zoneLabels, zoneIndex, i => zoneIndex = i, "Time zone", out setZone);
            Action<int> ignore;
            int placeIndex = Math.Max(0, Array.FindIndex(Places, x => x.Name == Prayers.Place));
            var placeDrop = Dropdown(190, Places.Select(x => x.Name).ToArray(), placeIndex, i =>
            {
                latBox.Text = Places[i].Lat.ToString(Inv);
                lonBox.Text = Places[i].Lon.ToString(Inv);
                int sriLanka = Array.FindIndex(zones, z => z.Id == "Sri Lanka Standard Time");
                if (sriLanka >= 0) { zoneIndex = sriLanka; setZone(sriLanka); }
            }, "Location", out ignore);

            var save = Pill("Save", true, SaveSettings);
            save.VerticalAlignment = VerticalAlignment.Bottom;
            save.Margin = new Thickness(0, 0, 0, 4);
            settingsNote.VerticalAlignment = VerticalAlignment.Bottom;
            settingsNote.Margin = new Thickness(12, 0, 0, 8);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Labelled("Location", placeDrop));
            row.Children.Add(Labelled("Time zone", zoneDrop));
            row.Children.Add(Labelled("Latitude", latBox));
            row.Children.Add(Labelled("Longitude", lonBox));
            row.Children.Add(save);
            row.Children.Add(settingsNote);
            settings.Child = new Border { Margin = new Thickness(0, 0, 0, Gap), Child = Card(row) };
        }

        static FrameworkElement Labelled(string label, FrameworkElement field)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var caption = T(label, 12, Muted);
            caption.Margin = new Thickness(2, 0, 0, 4);
            stack.Children.Add(caption);
            stack.Children.Add(field);
            return stack;
        }

        // WPF's ComboBox can't be themed for dark glass without re-templating it, so this is a small replacement.
        static Border Dropdown(double width, string[] labels, int selected, Action<int> pick, string name, out Action<int> setIndex)
        {
            int current = selected;
            var text = T(labels[current], 13, Ink);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(0, 0, 18, 0);
            var chevron = Glyph(ChevronGlyph, 10, Soft);
            chevron.HorizontalAlignment = HorizontalAlignment.Right;
            var inner = new Grid();
            inner.Children.Add(text);
            inner.Children.Add(chevron);
            var box = new Border { Width = width, Height = 34, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0, 10, 0), Background = Faint, BorderBrush = CardLine, BorderThickness = new Thickness(1), Child = inner };

            var list = new StackPanel();
            var popup = new Popup
            {
                PlacementTarget = box, Placement = PlacementMode.Bottom, VerticalOffset = 4, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
                Child = new Border { Width = width, Background = PopupFill, BorderBrush = CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Child = new ScrollViewer { MaxHeight = 300, Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden } },
            };
            var closedAt = DateTime.MinValue;
            popup.Closed += (s, e) => closedAt = DateTime.UtcNow;

            Action open = () =>
            {
                // A click on the box while open closes the popup first; don't reopen it from that same click.
                if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 200) return;
                list.Children.Clear();
                Border currentRow = null;
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    var fill = new SolidColorBrush(i == current ? SelectedFill : None);
                    var label = T(labels[i], 13, Ink);
                    label.TextTrimming = TextTrimming.CharacterEllipsis;
                    var row = new Border { Background = fill, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 7), Child = label };
                    bool isCurrent = i == current;
                    Clickable(row, labels[i], () =>
                    {
                        current = index;
                        text.Text = labels[index];
                        popup.IsOpen = false;
                        pick(index);
                    });
                    row.MouseEnter += (s, e) => Anim(fill, isCurrent ? SelectedFill : HoverFill);
                    row.MouseLeave += (s, e) => Anim(fill, isCurrent ? SelectedFill : None);
                    list.Children.Add(row);
                    if (isCurrent) currentRow = row;
                }
                popup.IsOpen = true;
                var target = currentRow;
                list.Dispatcher.BeginInvoke(new Action(() => { target.BringIntoView(); target.Focus(); }), DispatcherPriority.Loaded);
            };
            Clickable(box, name, open);
            Action<int> set = i => { current = i; text.Text = labels[i]; };
            setIndex = set;
            return box;
        }

        internal void ToggleSettings()
        {
            settingsOpen = !settingsOpen;
            Anim(settings, HeightProperty, settingsOpen ? SettingsH : 0, 340, settingsOpen ? Spring : Out);
        }

        void SaveSettings()
        {
            double lat, lon;
            if (!double.TryParse(latBox.Text, NumberStyles.Float, Inv, out lat) || !double.TryParse(lonBox.Text, NumberStyles.Float, Inv, out lon) || !Prayers.ValidLocation(lat, lon))
            {
                settingsNote.Text = "Enter latitude -90 to 90, longitude -180 to 180.";
                return;
            }
            var match = Places.FirstOrDefault(x => Math.Abs(x.Lat - lat) < 0.001 && Math.Abs(x.Lon - lon) < 0.001);
            Prayers.Place = match != null ? match.Name : "Custom location";
            Prayers.Lat = lat;
            Prayers.Lon = lon;
            Prayers.Zone = zones[zoneIndex];
            settingsNote.Text = Prayers.SaveSettings() ? "Saved." : "Applied, but could not be saved to disk.";
            Render(false);
        }

        // ---- data to screen ----

        Slot[] CurrentSlots(DateTime now) { return Tracker.Slots(now.Date.AddDays(1 - range), range, now); }

        string Signature(Slot[] slots, DateTime now)
        {
            return now.Date.ToString("yyyyMMdd", Inv) + range + Prayers.Place + Prayers.Zone.Id + Prayers.Lat + Prayers.Lon + string.Concat(slots.Select(x => x.Status));
        }

        internal void SetRange(int days)
        {
            range = days;
            Render(true);
        }

        void Toggle(Slot slot)
        {
            if (slot.Status == Tracker.Upcoming) return;
            if (slot.Status == Tracker.Prayed) Tracker.Unmark(slot.Key);
            else Tracker.Mark(slot.Key, slot.Status == Tracker.Pending ? Prayers.Now : (DateTime?)null); // stamps only real, in-time answers
            Render(false);
        }

        void Render(bool animate)
        {
            var now = Prayers.Now;
            var slots = CurrentSlots(now);
            signature = Signature(slots, now);
            var today = slots.Skip((range - 1) * 5).ToArray();

            var upcoming = today.FirstOrDefault(x => x.Status == Tracker.Upcoming);
            var nextName = upcoming != null ? upcoming.Index : 0;
            var nextTime = upcoming != null ? upcoming.Start : Prayers.For(now.AddDays(1), Prayers.TzAt(now.AddDays(1)))[0];
            subtitle.Text = Prayers.Place + "  \u00B7  " + Utc(now) + "  \u00B7  Next: " + Prayers.SlotNames[nextName] + " " + Hm(nextTime);

            RenderToday(today);

            rangeRow.Children.Clear();
            foreach (var days in new[] { 7, 30, 90 })
            {
                int chosen = days;
                rangeRow.Children.Add(Pill(days + " days", days == range, () => SetRange(chosen)));
            }

            analytics.Children.Clear();
            analytics.Children.Add(Tiles(slots, now));
            var charts = new Grid { Margin = new Thickness(0, Gap, 0, Gap) };
            charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrendCardW) });
            charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SmallCardW + Gap) });
            charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SmallCardW + Gap) });
            var trend = TrendCard(slots, animate);
            var byPrayer = ByPrayerCard(slots, animate);
            var delay = DelayCard(slots, animate);
            byPrayer.Margin = delay.Margin = new Thickness(Gap, 0, 0, 0);
            Grid.SetColumn(byPrayer, 1);
            Grid.SetColumn(delay, 2);
            charts.Children.Add(trend);
            charts.Children.Add(byPrayer);
            charts.Children.Add(delay);
            analytics.Children.Add(charts);
            analytics.Children.Add(HistoryCard(slots));
        }

        const double SmallCardW = 262, TrendCardW = ContentW - 2 * (SmallCardW + Gap);

        void RenderToday(Slot[] today)
        {
            todayRow.Children.Clear();
            foreach (var slot in today)
            {
                var current = slot;
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(T(Prayers.SlotNames[slot.Index], 15, Ink, FontWeights.SemiBold));
                names.Children.Add(T(Hm(slot.Start), 13, Soft));
                var status = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var icon = StatusIcon(slot.Status);
                if (icon != null) { icon.Margin = new Thickness(0, 0, 5, 0); status.Children.Add(icon); }
                status.Children.Add(T(StatusLabel(slot.Status), 12.5, Soft));
                var inner = new Grid();
                inner.Children.Add(names);
                inner.Children.Add(status);
                var card = Card(inner, 12);
                card.Padding = new Thickness(14, 10, 14, 11);
                card.Margin = new Thickness(slot.Index == 0 ? 0 : Gap / 2, 0, slot.Index == 4 ? 0 : Gap / 2, 0);
                if (slot.Status == Tracker.Pending) card.BorderBrush = Warning;
                if (slot.Status != Tracker.Upcoming)
                {
                    Clickable(card, Prayers.SlotNames[slot.Index] + ", " + StatusLabel(slot.Status) + ". Change status.", () => Toggle(current));
                    Tip(card, StatusLabel(slot.Status), slot.Status == Tracker.Prayed ? "Click to undo" : "Click to mark prayed");
                }
                Grid.SetColumn(card, slot.Index);
                todayRow.Children.Add(card);
            }
        }

        static TextBlock StatusIcon(int status)
        {
            switch (status)
            {
                case Tracker.Prayed: return Glyph(TickGlyph, 12, PrayedInk);
                case Tracker.Missed: return Glyph(CrossGlyph, 11, MissedInk);
                case Tracker.Pending: return Glyph(ClockGlyph, 12, Warning);
                default: return null;
            }
        }

        FrameworkElement Tiles(Slot[] slots, DateTime now)
        {
            int prayed = slots.Count(x => x.Status == Tracker.Prayed), missed = slots.Count(x => x.Status == Tracker.Missed), total = prayed + missed;

            var before = Tracker.Slots(now.Date.AddDays(1 - 2 * range), range, now);
            int beforePrayed = before.Count(x => x.Status == Tracker.Prayed), beforeTotal = beforePrayed + before.Count(x => x.Status == Tracker.Missed);
            string change = "Last " + range + " days";
            if (total > 0 && beforeTotal >= range * 5 / 2) // a nearly empty earlier period would give a meaningless swing
            {
                var points = (int)Math.Round(100.0 * prayed / total - 100.0 * beforePrayed / beforeTotal);
                change = (points > 0 ? "\u25B2 " : points < 0 ? "\u25BC " : "") + Math.Abs(points) + " pts vs previous " + range + " days";
            }

            var days = (int)(now.Date - Tracker.Since.Date).TotalDays + 1;
            int current, best;
            Tracker.Streaks(Tracker.Slots(Tracker.Since.Date, days, now).Select(x => x.Status).ToArray(), out current, out best);

            var delays = slots.Where(x => x.Delay.HasValue).Select(x => x.Delay.Value).ToArray();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tiles = new[]
            {
                Tile("Completion", Percent(prayed, total), change, 48),
                Tile("Prayed", prayed.ToString(Inv), "of " + total + " finished prayers", 30),
                Tile("Missed", missed.ToString(Inv), "no click before next prayer", 30),
                Tile("Current streak", current.ToString(Inv), "in a row, best " + best, 30),
                Tile("Time to pray", delays.Length == 0 ? "\u2014" : Minutes(delays.Average()), "average after prayer time", 30),
            };
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i].Margin = new Thickness(i == 0 ? 0 : Gap / 2, 0, i == 4 ? 0 : Gap / 2, 0);
                Grid.SetColumn(tiles[i], i);
                grid.Children.Add(tiles[i]);
            }
            return grid;
        }

        static Border Tile(string label, string value, string note, double size)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(T(label, 13, Soft));
            stack.Children.Add(T(value, size, Ink, FontWeights.SemiBold));
            stack.Children.Add(T(note, 12, Muted));
            var card = Card(stack);
            card.Height = 112;
            card.Padding = new Thickness(CardPad, 8, CardPad, 8);
            return card;
        }

        static FrameworkElement CardHeader(string title, string detail, UIElement right = null)
        {
            var left = new StackPanel();
            left.Children.Add(T(title, 14, Ink, FontWeights.SemiBold));
            left.Children.Add(T(detail, 12, Muted));
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.Children.Add(left);
            if (right != null) grid.Children.Add(right);
            return grid;
        }

        static StackPanel Legend(params object[] swatchThenLabel)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            for (int i = 0; i < swatchThenLabel.Length; i += 2)
            {
                var swatch = (FrameworkElement)swatchThenLabel[i];
                swatch.Margin = new Thickness(i == 0 ? 0 : 12, 0, 5, 0);
                swatch.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(swatch);
                row.Children.Add(T((string)swatchThenLabel[i + 1], 12, Soft));
            }
            return row;
        }

        static Border Swatch(Brush fill, Brush line = null)
        {
            return new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = fill, BorderBrush = line, BorderThickness = new Thickness(line == null ? 0 : 1.5) };
        }

        static void Place2(Canvas canvas, UIElement child, double x, double y)
        {
            Canvas.SetLeft(child, x);
            Canvas.SetTop(child, y);
            canvas.Children.Add(child);
        }

        // Daily prayed and missed as stacked columns on one 0 to 5 scale.
        Border TrendCard(Slot[] slots, bool animate)
        {
            const double h = 160, left = 22, top = 4, bottom = 20;
            double w = TrendCardW - 2 * CardPad - 2, plotW = w - left, plotH = h - top - bottom;
            var canvas = new Canvas { Width = w, Height = h };
            for (int k = 0; k <= 5; k++)
            {
                double y = top + plotH * (1 - k / 5.0);
                Place2(canvas, new Border { Width = plotW, Height = 1, Background = k == 0 ? BaseLine : GridLine, SnapsToDevicePixels = true }, left, y);
                var tick = T(k.ToString(Inv), 11, Muted);
                Place2(canvas, tick, 4, y - 8);
            }
            double band = plotW / range, barW = Math.Min(18, Math.Max(2, band * 0.68)), bx = (band - barW) / 2, corner = Math.Min(4, barW / 2);
            int step = range <= 7 ? 1 : range <= 30 ? 5 : 15;
            for (int d = 0; d < range; d++)
            {
                var day = slots.Skip(d * 5).Take(5).ToArray();
                int prayed = day.Count(x => x.Status == Tracker.Prayed), missed = day.Count(x => x.Status == Tracker.Missed);
                double pH = plotH * prayed / 5, mH = plotH * missed / 5, gap = prayed > 0 && missed > 0 ? 2 : 0;
                var wash = new SolidColorBrush(None);
                var scale = new ScaleTransform(1, 1);
                var column = new Canvas { Width = band, Height = plotH, Background = wash, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scale };
                if (prayed > 0) Place2(column, new Border { Width = barW, Height = pH, Background = PrayedInk, CornerRadius = new CornerRadius(missed > 0 ? 0 : corner, missed > 0 ? 0 : corner, 0, 0) }, bx, plotH - pH);
                if (missed > 0) Place2(column, new Border { Width = barW, Height = Math.Max(1, mH - gap), Background = MissedInk, CornerRadius = new CornerRadius(corner, corner, 0, 0) }, bx, plotH - pH - mH);
                column.MouseEnter += (s, e) => Anim(wash, HoverFill);
                column.MouseLeave += (s, e) => Anim(wash, None);
                Tip(column, prayed + " prayed, " + missed + " missed", Md(day[0].Day));
                Place2(canvas, column, left + d * band, top);
                if (animate) PrayerIsland.Go(scale, ScaleTransform.ScaleYProperty, 1, 420, Out, d * 280 / range, null, 0);
                if ((range - 1 - d) % step == 0)
                {
                    var label = T(d == range - 1 ? "Today" : Md(day[0].Day), 11, Muted);
                    label.TextAlignment = TextAlignment.Center;
                    label.Width = 56;
                    Place2(canvas, label, Math.Min(w - 56, Math.Max(left - 8, left + d * band + band / 2 - 28)), h - 16);
                }
            }
            var stack = new StackPanel();
            stack.Children.Add(CardHeader("Daily prayers", "Prayed and missed each day", Legend(Swatch(PrayedInk), "Prayed", Swatch(MissedInk), "Missed")));
            stack.Children.Add(canvas);
            return Card(stack);
        }

        // One blue series: length answers the question, so hue doesn't also need to.
        static FrameworkElement HBars(string[] labels, double?[] values, double max, Func<double, string> format, string[] details, double w, bool animate)
        {
            const double rowH = 31, labelW = 58, tipW = 56;
            var canvas = new Canvas { Width = w, Height = rowH * 5 + 4 };
            double scaleX = (w - labelW - tipW) / Math.Max(max, 0.001);
            Place2(canvas, new Border { Width = 1, Height = rowH * 5, Background = BaseLine, SnapsToDevicePixels = true }, labelW, 2);
            for (int i = 0; i < 5; i++)
            {
                double y = 2 + i * rowH;
                Place2(canvas, T(labels[i], 12.5, Soft), 0, y + 7);
                var wash = new SolidColorBrush(None);
                var row = new Border { Width = w, Height = rowH, Background = wash };
                row.MouseEnter += (s, e) => Anim(wash, HoverFill);
                row.MouseLeave += (s, e) => Anim(wash, None);
                Tip(row, values[i].HasValue ? format(values[i].Value) : "No data", labels[i] + ": " + details[i]);
                Place2(canvas, row, 0, y);
                if (!values[i].HasValue) { Place2(canvas, T("No data", 12, Muted), labelW + 7, y + 7); continue; }
                double barW = Math.Max(2, values[i].Value * scaleX);
                var scale = new ScaleTransform(1, 1);
                var bar = new Border { Width = barW, Height = 14, Background = PrayedInk, CornerRadius = new CornerRadius(0, 4, 4, 0), RenderTransform = scale, RenderTransformOrigin = new Point(0, 0.5), IsHitTestVisible = false };
                Place2(canvas, bar, labelW + 1, y + 8.5);
                if (animate) PrayerIsland.Go(scale, ScaleTransform.ScaleXProperty, 1, 460, Out, i * 50, null, 0);
                var value = T(format(values[i].Value), 12.5, Ink, FontWeights.SemiBold);
                value.IsHitTestVisible = false;
                Place2(canvas, value, labelW + barW + 7, y + 7);
            }
            return canvas;
        }

        Border ByPrayerCard(Slot[] slots, bool animate)
        {
            var values = new double?[5];
            var details = new string[5];
            for (int i = 0; i < 5; i++)
            {
                int index = i, prayed = slots.Count(x => x.Index == index && x.Status == Tracker.Prayed), total = prayed + slots.Count(x => x.Index == index && x.Status == Tracker.Missed);
                values[i] = total == 0 ? (double?)null : 100.0 * prayed / total;
                details[i] = prayed + " of " + total + " prayed";
            }
            var known = Enumerable.Range(0, 5).Where(i => values[i].HasValue).ToArray();
            string note = "Share prayed";
            if (known.Length > 1 && values[known[0]] != known.Max(i => values[i]) || known.Length > 1 && known.Min(i => values[i]) != known.Max(i => values[i]))
                note = "Lowest: " + Prayers.SlotNames[known.OrderBy(i => values[i]).First()];
            var stack = new StackPanel();
            stack.Children.Add(CardHeader("By prayer", note));
            stack.Children.Add(HBars(Prayers.SlotNames, values, 100, v => Math.Round(v) + "%", details, SmallCardW - 2 * CardPad - 2, animate));
            return Card(stack);
        }

        Border DelayCard(Slot[] slots, bool animate)
        {
            var values = new double?[5];
            var details = new string[5];
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                var delays = slots.Where(x => x.Index == index && x.Delay.HasValue).Select(x => x.Delay.Value).ToArray();
                values[i] = delays.Length == 0 ? (double?)null : delays.Average();
                details[i] = "average of " + delays.Length + (delays.Length == 1 ? " answer" : " answers");
            }
            var stack = new StackPanel();
            stack.Children.Add(CardHeader("Time to pray", "Average minutes after prayer time"));
            stack.Children.Add(HBars(Prayers.SlotNames, values, Math.Max(10, values.Max() ?? 10), Minutes, details, SmallCardW - 2 * CardPad - 2, animate));
            return Card(stack);
        }

        // Every prayer on its own cell: also the readable table behind the charts, and where mistakes get corrected.
        Border HistoryCard(Slot[] slots)
        {
            const double labelW = 60, rowH = 23, cellH = 20, headH = 18;
            double w = ContentW - 2 * CardPad - 2, band = Math.Min(46, (w - labelW) / range), cellW = band - 2;
            var canvas = new Canvas { Width = w, Height = 5 * rowH + headH };
            int step = range <= 7 ? 1 : range <= 30 ? 5 : 15;
            for (int i = 0; i < 5; i++) Place2(canvas, T(Prayers.SlotNames[i], 12.5, Soft), 0, i * rowH + 1);
            foreach (var slot in slots)
            {
                var current = slot;
                int d = (int)(slot.Day - slots[0].Day).TotalDays;
                var cell = new Border { Width = cellW, Height = cellH, CornerRadius = new CornerRadius(Math.Min(4, cellW / 2)) };
                Brush iconColor = null;
                string glyph = null;
                if (slot.Status == Tracker.Prayed) { cell.Background = PrayedInk; iconColor = Brushes.White; glyph = TickGlyph; }
                else if (slot.Status == Tracker.Missed) { cell.Background = MissedWash; cell.BorderBrush = MissedInk; cell.BorderThickness = new Thickness(1.5); iconColor = Ink; glyph = CrossGlyph; }
                else if (slot.Status == Tracker.Pending) { cell.Background = Brushes.Transparent; cell.BorderBrush = Warning; cell.BorderThickness = new Thickness(1.5); }
                else cell.Background = Faint;
                if (glyph != null && cellW >= 18) cell.Child = Glyph(glyph, 9, iconColor);
                var name = Prayers.SlotNames[slot.Index] + ", " + Md(slot.Day);
                Tip(cell, StatusLabel(slot.Status), name + (slot.Status == Tracker.Upcoming ? "" : " \u00B7 click to change"));
                if (slot.Status != Tracker.Upcoming) Clickable(cell, name + ", " + StatusLabel(slot.Status) + ". Change status.", () => Toggle(current));
                else AutomationProperties.SetName(cell, name + ", " + StatusLabel(slot.Status));
                Place2(canvas, cell, labelW + d * band + 1, slot.Index * rowH);
                if (slot.Index == 4 && (range - 1 - d) % step == 0)
                {
                    var label = T(d == range - 1 ? "Today" : Md(slot.Day), 11, Muted);
                    label.TextAlignment = TextAlignment.Center;
                    label.Width = 56;
                    Place2(canvas, label, Math.Min(w - 56, labelW + d * band + band / 2 - 28), 5 * rowH + 1);
                }
            }
            var stack = new StackPanel();
            stack.Children.Add(CardHeader("History", "Tracking since " + Md(Tracker.Since) + ". Click a cell to correct it, such as a prayer made while the PC was off.",
                Legend(Swatch(PrayedInk), "Prayed", Swatch(MissedWash, MissedInk), "Missed", Swatch(Brushes.Transparent, Warning), "Time now", Swatch(Faint), "Upcoming or not tracked")));
            stack.Children.Add(canvas);
            return Card(stack);
        }
    }
}
