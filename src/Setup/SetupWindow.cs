using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

// First-run setup: a welcome page, then one page per group of features, each explained with its own on/off
// switch, then the shortcuts. Shown once (Settings.SetupDone); About in the Control Center runs it again.
// Every switch writes its setting at once, like the Control Center, so closing early keeps what was chosen.
class SetupWindow : DashboardWindow
{
    static SetupWindow open;

    readonly List<FrameworkElement> steps = new List<FrameworkElement>();
    readonly Border pageHost = new Border();
    readonly Border root = new Border();
    readonly ScaleTransform rootScale = new ScaleTransform(0.97, 0.97);
    readonly StackPanel dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Grid footer = new Grid { Margin = new Thickness(28, 0, 28, 22) };
    readonly TextBlock nextText = T("Next", 13.5, Brushes.White, FontWeights.SemiBold);
    Border back;
    int step;

    internal static void Open()
    {
        if (open == null)
        {
            open = new SetupWindow();
            open.Closed += (s, e) => open = null;
            open.Show();
        }
        open.Activate();
    }

    SetupWindow()
    {
        Title = "Welcome to WinIsle";
        Width = 720;
        Height = Math.Min(660, SystemParameters.WorkArea.Height - 16);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; // above the topmost todo panel
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = Ink;

        steps.Add(Welcome());
        steps.Add(Features("Reminders", "The island asks at the right moment, with buttons to answer",
            Feature("Water", "Every 90 minutes the island asks whether you've had a drink. \"Not yet\" asks again in 10 minutes.", Settings.WaterOn,
                () => Preview(WaterReminder.Sample())),
            Feature("Battery alerts", "An alarm at 15% to plug in the charger, and at 95% to unplug it, which helps the battery last.", Settings.BatteryOn,
                () => Preview(BatteryReminder.Sample(true))),
            Feature("Charging animation", "A battery fills on the island when you plug in, and drains when you unplug.", Settings.ChargeAnim,
                () => { if (Program.Island != null) Program.Island.ShowCharging(72); }),
            Feature("Prayer times", "A reminder at each prayer time, worked out offline for your location, with a log and charts. Off also hides the Prayer page.", Settings.PrayerOn,
                () => Preview(PrayerReminder.Sample()))));
        steps.Add(Features("On the island", "What the pill at the top centre of the screen shows",
            Feature("Windows notifications", "Messages from every app appear on the island with Open and Dismiss. Windows' own banners are turned off so you don't see each one twice.", Settings.NotifyOn,
                () => Preview(WindowsNotifications.Sample())),
            Feature("Volume", "A bar shows the level when it changes. Scroll on it, or drag it, to adjust.", Settings.FlashVolume, null),
            Feature("Brightness", "The same bar for screen brightness, including automatic changes.", Settings.FlashBrightness, null),
            Feature("Bluetooth", "Shows when a device connects or disconnects, with its battery level when Windows knows it.", Settings.FlashBluetooth, null),
            Feature("Camera and microphone dots", "A green or orange dot while an app is using the camera or microphone. Hover it to see which app.", Settings.Dots, null)));
        steps.Add(Features("Tools", "Quick tools that live in the island",
            Feature("Clipboard history", "Everything you copy, searchable. Ctrl+Alt+V opens it and Enter pastes. It also records passwords you copy, so turn it off if you'd rather not.", Settings.ClipOn, null),
            Feature("Screenshots to the shelf", "New screenshots land on the file shelf, ready to drag into a chat or an email.", Settings.Screenshots, null),
            Feature("Volume keys", "The island takes the volume keys and shows its own bar instead of Windows' pop-up.", Settings.VolumeKeys, null),
            Feature("Mouse wheel volume", "Hold the mouse wheel down and scroll anywhere to change the volume.", Settings.WheelVolume, null),
            Info("Always there", "Hover the top centre for the media player, a timer, a camera mirror and the file shelf. Drag files to the top of the screen to hold them on the shelf.")));
        steps.Add(Features("Startup and sound", "How WinIsle starts and how it sounds",
            StartupFeature(),
            Feature("Todo panel on start", "A small task list that stays on top. Ctrl+Alt+T shows or hides it at any time.", Settings.ShowPanel, null),
            Feature("Sounds", "Chimes, alarms and timer ticks. Each kind can be turned off on its own in the Control Center.", Settings.SoundOn, null)));
        steps.Add(Done());

        // Footer: progress dots on the left, Back and Next on the right. Hidden on the welcome page.
        back = Pill("Back", false, () => GoTo(step - 1));
        var next = new Border { Background = SeriesBlue, CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 6, 20, 7), Child = nextText };
        Clickable(next, "Next", () => { if (step == steps.Count - 1) Finish(); else GoTo(step + 1); });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(back);
        buttons.Children.Add(next);
        footer.Children.Add(dots);
        footer.Children.Add(buttons);

        var layout = new Grid { Background = Brushes.Transparent };
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(footer, 1);
        layout.Children.Add(pageHost);
        layout.Children.Add(footer);
        layout.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { } };

        root.Child = layout;
        root.CornerRadius = new CornerRadius(8);
        root.BorderThickness = new Thickness(1);
        root.Background = new LinearGradientBrush(Color.FromArgb(22, 255, 255, 255), Color.FromArgb(6, 255, 255, 255), 90);
        root.BorderBrush = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90);
        root.Opacity = 0;
        root.RenderTransform = rootScale;
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        Content = root;

        GoTo(0);
        PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Finish(); e.Handled = true; } };
        Closed += (s, e) => { if (!Settings.On(Settings.SetupDone)) Settings.Set(Settings.SetupDone, true); };
        SourceInitialized += (s, e) => MakeGlass(new WindowInteropHelper(this).Handle, unchecked((int)0xE61F1C1C));
        Loaded += (s, e) =>
        {
            Anim(root, OpacityProperty, 1, 220, Out);
            Anim(rootScale, ScaleTransform.ScaleXProperty, 1, 380, Spring);
            Anim(rootScale, ScaleTransform.ScaleYProperty, 1, 380, Spring);
        };
    }

    void GoTo(int index)
    {
        step = Math.Max(0, Math.Min(steps.Count - 1, index));
        pageHost.Child = steps[step];
        Anim(steps[step], OpacityProperty, 1, 180, Out, 0);
        footer.Visibility = step == 0 ? Visibility.Hidden : Visibility.Visible;
        back.Visibility = step <= 1 ? Visibility.Hidden : Visibility.Visible;
        nextText.Text = step == steps.Count - 1 ? "Finish" : "Next";
        dots.Children.Clear();
        for (int i = 1; i < steps.Count; i++)
            dots.Children.Add(new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), Fill = i == step ? Ink : Rgb(255, 255, 255, 60) });
    }

    void Finish()
    {
        Settings.Set(Settings.SetupDone, true);
        Go(root, OpacityProperty, 0, 130, Out, 0, (s, e) => Close(), null);
    }

    // ---- pages ----

    FrameworkElement Welcome()
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 460 };
        var logo = Logo();
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(logo);
        var title = T("Welcome to WinIsle", 28, Ink, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 22, 0, 8);
        column.Children.Add(title);
        var text = Wrapped("A Dynamic Island for Windows. This short setup explains each feature and lets you turn it on or off. It takes about a minute.", 14.5, Soft);
        text.TextAlignment = TextAlignment.Center;
        column.Children.Add(text);

        var start = new Border { Background = SeriesBlue, CornerRadius = new CornerRadius(20), Padding = new Thickness(28, 9, 28, 10), Margin = new Thickness(0, 28, 0, 10), HorizontalAlignment = HorizontalAlignment.Center, Child = T("Get started", 15, Brushes.White, FontWeights.SemiBold) };
        Clickable(start, "Get started", () => GoTo(1));
        column.Children.Add(start);
        var skip = Pill("Skip and keep the defaults", false, Finish);
        skip.HorizontalAlignment = HorizontalAlignment.Center;
        skip.Margin = new Thickness(0);
        column.Children.Add(skip);
        return new Grid { Children = { column } };
    }

    FrameworkElement Done()
    {
        var column = new StackPanel { Margin = new Thickness(28, 26, 28, 0) };
        column.Children.Add(T("You're all set", 24, Ink, FontWeights.SemiBold));
        var lead = Wrapped("The island is the thin glass line at the top centre of your screen. These shortcuts work anywhere in Windows:", 13.5, Muted);
        lead.Margin = new Thickness(0, 4, 0, Gap + 4);
        column.Children.Add(lead);
        var keys = new StackPanel();
        foreach (var k in new[] { "Ctrl+Alt+S|Control Center: change anything from this setup, and more", "Ctrl+Alt+T|Show or hide the todo panel", "Ctrl+Alt+V|Clipboard history", "Ctrl+Alt+P|Prayer page, while prayer is on" })
        {
            var parts = k.Split('|');
            keys.Children.Add(Line(parts[1], T(parts[0], 13, Soft, FontWeights.SemiBold)));
        }
        column.Children.Add(Card(keys));
        var open = Pill("Open Control Center", false, () => { Finish(); ControlCenter.Open("General"); });
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, Gap, 0, 0);
        column.Children.Add(open);
        return column;
    }

    static FrameworkElement Features(string title, string detail, params UIElement[] cards)
    {
        var column = new StackPanel { Margin = new Thickness(28, 26, 28, 0) };
        column.Children.Add(T(title, 24, Ink, FontWeights.SemiBold));
        var sub = T(detail, 13.5, Muted);
        sub.Margin = new Thickness(0, 2, 0, Gap + 4);
        column.Children.Add(sub);
        foreach (var c in cards) column.Children.Add(c);
        return new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    }

    // ---- cards ----

    // A feature: name, what it does, its switch, and "Try it" where the island can show a preview.
    static Border Feature(string title, string text, string key, Action tryIt)
    {
        Action<bool> ignore;
        return FeatureCard(title, text, Switch(Settings.On(key), v => Settings.Set(key, v), title, out ignore), tryIt);
    }

    static Border StartupFeature()
    {
        Action<bool> set = null;
        var s = Switch(Startup.Enabled, v => { Startup.Enabled = v; set(Startup.Enabled); }, "Start with Windows", out set);
        return FeatureCard("Start with Windows", "WinIsle opens when you sign in, so reminders and the island are always there.", s, null);
    }

    static Border FeatureCard(string title, string text, Border toggle, Action tryIt)
    {
        var left = new StackPanel { Margin = new Thickness(0, 0, 64, 0) };
        left.Children.Add(T(title, 14, Ink, FontWeights.SemiBold));
        var body = Wrapped(text, 12.5, Soft);
        body.Margin = new Thickness(0, 2, 0, 0);
        left.Children.Add(body);
        if (tryIt != null)
        {
            var tryPill = Pill("Try it", false, tryIt);
            tryPill.HorizontalAlignment = HorizontalAlignment.Left;
            tryPill.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(tryPill);
        }
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.VerticalAlignment = VerticalAlignment.Top;
        var grid = new Grid();
        grid.Children.Add(left);
        grid.Children.Add(toggle);
        var card = Card(grid, 14);
        card.Margin = new Thickness(0, 0, 0, 8);
        return card;
    }

    static Border Info(string title, string text)
    {
        var left = new StackPanel();
        left.Children.Add(T(title, 14, Ink, FontWeights.SemiBold));
        left.Children.Add(Wrapped(text, 12.5, Soft));
        var card = Card(left, 14);
        card.Background = Brushes.Transparent;
        card.Margin = new Thickness(0, 0, 0, 8);
        return card;
    }

    static Grid Line(string label, UIElement control)
    {
        TextBlock ignore;
        return Row(label, null, control, out ignore);
    }

    static TextBlock Wrapped(string text, double size, Brush color)
    {
        var t = T(text, size, color);
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    static void Preview(IslandNotice n) { if (Program.Preview != null) Program.Preview.Show(n); }

    // The logo, drawn: a gradient tile with the island pill and two waves.
    static FrameworkElement Logo()
    {
        var canvas = new Canvas { Width = 96, Height = 96 };
        var tile = new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(22), Background = new LinearGradientBrush(Color.FromRgb(0x63, 0x66, 0xF1), Color.FromRgb(0x0E, 0xA5, 0xE9), 45) };
        canvas.Children.Add(tile);
        var pill = new Border { Width = 60, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x16)) };
        Canvas.SetLeft(pill, 18); Canvas.SetTop(pill, 26);
        canvas.Children.Add(pill);
        var bar = new Border { Width = 22, Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White };
        Canvas.SetLeft(bar, 28); Canvas.SetTop(bar, 34);
        canvas.Children.Add(bar);
        var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) };
        Canvas.SetLeft(dot, 65); Canvas.SetTop(dot, 33);
        canvas.Children.Add(dot);
        canvas.Children.Add(Wave("M22,60 q6.5,-4.5 13,0 t13,0 t13,0 t13,0", 0.85));
        canvas.Children.Add(Wave("M28.5,71 q6.5,-4.5 13,0 t13,0 t13,0", 0.5));
        return canvas;
    }

    static Path Wave(string data, double opacity)
    {
        return new Path { Data = Geometry.Parse(data), Stroke = Brushes.White, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = opacity };
    }
}
