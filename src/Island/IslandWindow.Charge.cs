using System;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

// The charging animation, about 5 seconds: the island springs into a wide pill, a battery fills to the level
// with a glow behind it and a shine sweeping across, a lightning bolt pops in, and the percentage counts up.
partial class IslandWindow
{
    const double ChargeW = 320, ChargeH = 64, ChargeR = 32, BatteryInnerW = 38;
    static int ChargeMs { get { return (int)(Settings.Num(Settings.ChargeSeconds) * 1000); } } // Control Center
    static readonly Geometry Bolt = Geometry.Parse("M13.5,2 L5,13.5 H11 L9.5,22 L19,9.5 H12.8 Z");
    static readonly Color ChargeGreen = Color.FromRgb(48, 209, 88), ChargeLow = Color.FromRgb(255, 120, 60);

    readonly Border batteryFill = new Border { Height = 16, Width = 0, CornerRadius = new CornerRadius(3.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), ClipToBounds = true };
    readonly TranslateTransform shine = new TranslateTransform(-24, 0);
    readonly ScaleTransform boltPop = new ScaleTransform(0, 0);
    readonly TranslateTransform boltDrop = new TranslateTransform(); // the bolt falls away when unplugged
    readonly TextBlock chargeSubtitle = new TextBlock { FontSize = 11.5, Opacity = 0.6, Margin = new Thickness(0, 1, 0, 0) };
    static readonly Color OnBattery = Color.FromRgb(235, 235, 240);
    readonly System.Windows.Shapes.Path boltPath = new System.Windows.Shapes.Path { Data = Bolt, Width = 13, Height = 17, Stretch = Stretch.Uniform, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
    readonly System.Windows.Shapes.Ellipse chargeGlow = new System.Windows.Shapes.Ellipse { Width = 84, Height = 52, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock chargePercent = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock chargeTitle = new TextBlock { Text = "Charging", FontSize = 14, FontWeight = FontWeights.SemiBold };
    Grid chargeLayer;
    bool charging; // the charging animation is on the island

    Grid BuildCharge()
    {
        var layer = Layer(ChargeW - 36, ChargeH - 16, Inset + 8);
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        layer.ColumnDefinitions.Add(new ColumnDefinition());
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var battery = new Grid { Width = 56, Height = 48 };
        battery.Children.Add(chargeGlow);
        var shell = new Grid { Width = 50, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        shell.Children.Add(new Border { Width = 46, Height = 24, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1.6), BorderBrush = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left });
        shell.Children.Add(new Border { Width = 3, Height = 9, CornerRadius = new CornerRadius(0, 1.5, 1.5, 0), Background = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        // The shine is a soft white band inside the fill, so the fill's edges clip it.
        var band = new System.Windows.Shapes.Rectangle { Width = 18, RenderTransform = shine, HorizontalAlignment = HorizontalAlignment.Left };
        var bandBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        bandBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        bandBrush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.5));
        bandBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        band.Fill = bandBrush;
        batteryFill.Child = band;
        shell.Children.Add(batteryFill);
        var boltMotion = new TransformGroup();
        boltMotion.Children.Add(boltPop);
        boltMotion.Children.Add(boltDrop);
        boltPath.RenderTransform = boltMotion;
        boltPath.Margin = new Thickness(0, 0, 4, 0);
        shell.Children.Add(boltPath);
        battery.Children.Add(shell);
        layer.Children.Add(battery);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        words.Children.Add(chargeTitle);
        words.Children.Add(chargeSubtitle);
        Grid.SetColumn(words, 1);
        layer.Children.Add(words);
        System.Windows.Documents.Typography.SetNumeralAlignment(chargePercent, FontNumeralAlignment.Tabular); // equal-width digits, so the live count doesn't jitter
        Grid.SetColumn(chargePercent, 2);
        layer.Children.Add(chargePercent);
        return layer;
    }

    // The shared start of the plug-in and unplug animations: take the island, set the words, spring it open.
    // False when something else is on the island; the moment then passes without an animation.
    bool BeginPower(string title, string subtitle, System.Media.SoundPlayer sound)
    {
        // Takes over at once from a battery alert (the charger is the answer to it) or any flash; other
        // reminders keep the island and this moment passes without an animation.
        bool takeOver = shown != null;
        if (takeOver && !(flashing || shown.Key == "low" || shown.Key == "full")) return false;
        if (mirrorOn || timerOpen || shelfOpen || (mediaOpen && IsMouseOver && !takeOver)) return false;
        if (takeOver)
        {
            stageGen++; // what was scheduled for the old pill (opening, closing) no longer applies
            StopRinging();
            glow.BeginAnimation(OpacityProperty, null);
            expanded = false;
            Conceal(compact);
            Conceal(header);
            Conceal(actions);
            if (charging) Conceal(chargeLayer);
        }
        else if (mediaShown) HidePlayer();
        inUseDots.Visibility = Visibility.Hidden;
        flashing = charging = true;
        shown = new IslandNotice { Key = "charging" }; // holds the island like any flash until it times out
        open = confirmed = false;
        flashUntil = DateTime.UtcNow.AddMilliseconds(ChargeMs);
        chargeTitle.Text = title;
        chargeSubtitle.Text = subtitle;
        // Clear what the other animation left behind.
        foreach (var held in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) boltPop.BeginAnimation(held, null);
        boltDrop.BeginAnimation(TranslateTransform.YProperty, null);
        boltDrop.Y = 0;
        boltPath.BeginAnimation(OpacityProperty, null);
        boltPath.Opacity = 1;
        shine.BeginAnimation(TranslateTransform.XProperty, null);
        shine.X = -24;
        batteryFill.BeginAnimation(WidthProperty, null);

        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        if (!takeOver) CaptureBackdrop(); // taking over keeps the glass already showing
        IslandSound.Play(sound);
        if (!takeOver) { Jump(w, SeedW); Jump(h, SeedH); Jump(r, SeedH / 2); Jump(y, 0); } // else morph from the alert's shape
        Morph(ChargeW, ChargeH, ChargeR);
        Go(stage, OpacityProperty, 1, 140, Out, 0, null, null);
        Reveal(chargeLayer, 120);
        return true;
    }

    // From ChargeWatcher when the charger is plugged in.
    internal void ShowCharging(int percent)
    {
        if (!BeginPower("Charging", "Power connected", IslandSound.Charge)) return;
        double level = Math.Max(0, Math.Min(100, percent)) / 100.0;
        var color = percent <= 20 ? ChargeLow : ChargeGreen;
        var fill = new LinearGradientBrush(Color.FromArgb(255, (byte)Math.Min(255, color.R + 40), (byte)Math.Min(255, color.G + 30), (byte)Math.Min(255, color.B + 30)), color, 0);
        batteryFill.Background = fill;
        chargePercent.Foreground = new SolidColorBrush(color);
        chargeGlow.Fill = new RadialGradientBrush(Color.FromArgb(140, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
        SetPercent(0);

        // Bolt pops in with an overshoot, then settles.
        boltPop.BeginAnimation(ScaleTransform.ScaleXProperty, Pop());
        boltPop.BeginAnimation(ScaleTransform.ScaleYProperty, Pop());
        // Glow swells, then rests softly.
        var glow = new DoubleAnimationUsingKeyFrames();
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        chargeGlow.BeginAnimation(OpacityProperty, glow);
        // Fill rises to the level while the number counts up with it.
        var rise = new DoubleAnimation(0, BatteryInnerW * level, TimeSpan.FromMilliseconds(1000)) { BeginTime = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        batteryFill.BeginAnimation(WidthProperty, rise);
        CountUp(percent, 250, 1000);
        // One shine sweep once it's full.
        shine.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-24, BatteryInnerW + 6, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(1350), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });

        WaitOut(); // dismisses after ChargeMs, like other flashes
    }

    // From ChargeWatcher when the charger is unplugged: the reverse. The battery starts green and glowing at its
    // level, the glow fades out, the fill cools to white (orange at 20% and below), and the bolt shrinks,
    // drops and fades away.
    internal void ShowUnplugged(int percent)
    {
        if (!BeginPower("On battery", "Power disconnected", IslandSound.Unplug)) return;
        double level = Math.Max(0, Math.Min(100, percent)) / 100.0;
        var settle = percent <= 20 ? ChargeLow : OnBattery;
        var cool = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var fill = new SolidColorBrush(ChargeGreen);
        batteryFill.Background = fill;
        batteryFill.Width = BatteryInnerW * level;
        fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(settle, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(450), EasingFunction = cool });
        var number = new SolidColorBrush(ChargeGreen);
        chargePercent.Foreground = number;
        number.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(settle, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(450), EasingFunction = cool });
        double exactLevel, unusedRate, unusedWatts; // ponytail: one quick driver read on the UI thread; move off-thread if it ever stutters
        SetPercent(ReadBatteryLive(out exactLevel, out unusedRate, out unusedWatts) ? exactLevel : percent);

        chargeGlow.Fill = new RadialGradientBrush(Color.FromArgb(140, ChargeGreen.R, ChargeGreen.G, ChargeGreen.B), Color.FromArgb(0, ChargeGreen.R, ChargeGreen.G, ChargeGreen.B));
        chargeGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(1100)) { BeginTime = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        // The bolt is there at first, then lets go.
        var away = new CubicEase { EasingMode = EasingMode.EaseIn };
        var start = TimeSpan.FromMilliseconds(380);
        foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            boltPop.BeginAnimation(axis, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(420)) { BeginTime = start, EasingFunction = away, FillBehavior = FillBehavior.HoldEnd });
        boltDrop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 7, TimeSpan.FromMilliseconds(420)) { BeginTime = start, EasingFunction = away });
        boltPath.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = start + TimeSpan.FromMilliseconds(60), EasingFunction = away });

        WaitOut();
    }

    // Shows the level with the whole number large and the decimals and % small, sitting on the same baseline.
    void SetPercent(double level)
    {
        var text = level.ToString("F3");
        int dot = text.IndexOf(System.Globalization.NumberFormatInfo.CurrentInfo.NumberDecimalSeparator, StringComparison.Ordinal);
        chargePercent.Inlines.Clear();
        chargePercent.Inlines.Add(new System.Windows.Documents.Run(text.Substring(0, dot)));
        chargePercent.Inlines.Add(new System.Windows.Documents.Run(text.Substring(dot) + "%") { FontSize = 14 });
    }

    static DoubleAnimationUsingKeyFrames Pop()
    {
        var pop = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(180) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }));
        return pop;
    }

    // Counts 0% up to the level with the same ease-out as the fill, then keeps climbing at the battery's real
    // charge rate. The exact level and rate come from the battery driver, read off the UI thread once a second
    // (the rate is often 0 for the first moment after plug-in). Until a read lands, or when the driver doesn't
    // report them, it shows the whole-number percent and holds still.
    void CountUp(int percent, int delayMs, int durationMs)
    {
        var start = DateTime.UtcNow.AddMilliseconds(delayMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        double level = percent, perSec = 0; // the level at levelAt, and how fast it rises (% per second)
        var levelAt = DateTime.UtcNow;
        var lastRead = DateTime.MinValue;
        bool reading = false, exact = false;
        Func<DateTime, double> now = at => level + perSec * Math.Max(0, (at - levelAt).TotalSeconds);
        // Three decimals at ~60 fps: at a typical 20-60 W the thousandths move several times a second.
        var tick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        tick.Tick += (s, e) =>
        {
            if (!charging) { tick.Stop(); return; }
            var at = DateTime.UtcNow;
            if (!reading && (at - lastRead).TotalSeconds >= 1)
            {
                reading = true;
                lastRead = at;
                Task.Run(() =>
                {
                    double p, r, watts;
                    bool ok = ReadBatteryLive(out p, out r, out watts);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        reading = false;
                        if (!ok || !charging) return;
                        var landed = DateTime.UtcNow;
                        // The driver's capacity updates in steps, so never let a stale read pull the number back.
                        level = exact ? Math.Max(p, now(landed)) : p;
                        levelAt = landed;
                        perSec = Math.Max(0, r);
                        exact = true;
                        if (watts > 0) chargeSubtitle.Text = "Power connected \u00B7 " + watts.ToString("0.#") + " W";
                    }));
                });
            }
            double t = (at - start).TotalMilliseconds / durationMs;
            if (t < 0) return;
            SetPercent(now(at) * (t < 1 ? ease.Ease(t) : 1));
        };
        tick.Start();
    }

    // The battery's exact level (%), charge rate (% per second) and charging power (W), summed over all
    // batteries; false when the driver doesn't report capacities.
    static bool ReadBatteryLive(out double percent, out double perSec, out double watts)
    {
        percent = perSec = watts = 0;
        try
        {
            double remaining = 0, rate = 0, full = 0; // mWh, mW, mWh
            using (var q = new ManagementObjectSearcher(@"root\wmi", "SELECT RemainingCapacity, ChargeRate FROM BatteryStatus"))
                foreach (ManagementObject b in q.Get()) { remaining += Convert.ToDouble(b["RemainingCapacity"]); rate += Convert.ToDouble(b["ChargeRate"]); }
            using (var q = new ManagementObjectSearcher(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                foreach (ManagementObject b in q.Get()) full += Convert.ToDouble(b["FullChargedCapacity"]);
            if (full <= 0 || remaining <= 0 || remaining > full * 1.1) return false;
            percent = Math.Min(100, remaining / full * 100);
            perSec = rate / full * 100 / 3600;
            watts = rate / 1000;
            return true;
        }
        catch { return false; }
    }

    // Part of Dismiss: put the charging layer away.
    void EndCharging()
    {
        if (!charging) return;
        charging = false;
        Conceal(chargeLayer);
    }
}
