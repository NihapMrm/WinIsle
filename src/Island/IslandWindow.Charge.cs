using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

// The charging animation, about 3 seconds: the island springs into a wide pill, a battery fills to the level
// with a glow behind it and a shine sweeping across, a lightning bolt pops in, and the percentage counts up.
partial class IslandWindow
{
    const double ChargeW = 320, ChargeH = 64, ChargeR = 32, BatteryInnerW = 38;
    const int ChargeMs = 3200;
    static readonly Geometry Bolt = Geometry.Parse("M13.5,2 L5,13.5 H11 L9.5,22 L19,9.5 H12.8 Z");
    static readonly Color ChargeGreen = Color.FromRgb(48, 209, 88), ChargeLow = Color.FromRgb(255, 120, 60);

    readonly Border batteryFill = new Border { Height = 16, Width = 0, CornerRadius = new CornerRadius(3.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), ClipToBounds = true };
    readonly TranslateTransform shine = new TranslateTransform(-24, 0);
    readonly ScaleTransform boltPop = new ScaleTransform(0, 0);
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
        boltPath.RenderTransform = boltPop;
        boltPath.Margin = new Thickness(0, 0, 4, 0);
        shell.Children.Add(boltPath);
        battery.Children.Add(shell);
        layer.Children.Add(battery);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        words.Children.Add(chargeTitle);
        words.Children.Add(new TextBlock { Text = "Power connected", FontSize = 11.5, Opacity = 0.6, Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(words, 1);
        layer.Children.Add(words);
        Grid.SetColumn(chargePercent, 2);
        layer.Children.Add(chargePercent);
        return layer;
    }

    // From ChargeWatcher when the charger is plugged in.
    internal void ShowCharging(int percent)
    {
        if (shown != null) return; // a reminder or another flash is showing
        if (mirrorOn || timerOpen || shelfOpen || (mediaOpen && IsMouseOver)) return;
        if (mediaShown) HidePlayer();
        inUseDots.Visibility = Visibility.Hidden;
        flashing = charging = true;
        shown = new IslandNotice { Key = "charging" }; // holds the island like any flash until it times out
        open = confirmed = false;
        flashUntil = DateTime.UtcNow.AddMilliseconds(ChargeMs);

        double level = Math.Max(0, Math.Min(100, percent)) / 100.0;
        var color = percent <= 20 ? ChargeLow : ChargeGreen;
        var fill = new LinearGradientBrush(Color.FromArgb(255, (byte)Math.Min(255, color.R + 40), (byte)Math.Min(255, color.G + 30), (byte)Math.Min(255, color.B + 30)), color, 0);
        batteryFill.Background = fill;
        chargePercent.Foreground = new SolidColorBrush(color);
        chargeGlow.Fill = new RadialGradientBrush(Color.FromArgb(140, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
        chargePercent.Text = "0%";

        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        CaptureBackdrop();
        IslandSound.Charge.Play();
        Jump(w, SeedW); Jump(h, SeedH); Jump(r, SeedH / 2); Jump(y, 0);
        Morph(ChargeW, ChargeH, ChargeR);
        Go(stage, OpacityProperty, 1, 140, Out, 0, null, null);
        Reveal(chargeLayer, 120);

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

    static DoubleAnimationUsingKeyFrames Pop()
    {
        var pop = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(180) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }));
        return pop;
    }

    // Counts 0% up to the level with the same ease-out as the fill, so the two move together.
    void CountUp(int percent, int delayMs, int durationMs)
    {
        var start = DateTime.UtcNow.AddMilliseconds(delayMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        tick.Tick += (s, e) =>
        {
            if (!charging) { tick.Stop(); return; }
            double t = (DateTime.UtcNow - start).TotalMilliseconds / durationMs;
            if (t < 0) return;
            if (t >= 1) { chargePercent.Text = percent + "%"; tick.Stop(); return; }
            chargePercent.Text = (int)Math.Round(percent * ease.Ease(t)) + "%";
        };
        tick.Start();
    }

    // Part of Dismiss: put the charging layer away.
    void EndCharging()
    {
        if (!charging) return;
        charging = false;
        Conceal(chargeLayer);
    }
}
