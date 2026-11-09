using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

// Dynamic Island style pill at the top centre of the screen. Knows nothing about prayers or water.

// A fixed per-pixel transparent window: curves are fully anti-aliased, and clicks on its empty
// pixels fall through to whatever is underneath. DWM acrylic can't be used here because it
// fills the whole window rectangle and ignores window regions.
class IslandWindow : GlassWindow
{
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    class SpringValue { public double X, V, To; }

    const double Stiffness = 260, Damping = 24; // slightly underdamped: a small settle, no wobble
    const double WinW = 500, WinH = 170, Inset = 2, BlurMargin = 40;
    const double SeedW = 76, SeedH = 26;
    const double CompactW = 244, CompactH = 44;
    const double OpenW = 430, OpenH = 126, OpenR = 34; // OpenR = button radius 20 + inset 14, so the curves are concentric

    readonly IReminder[] reminders; // highest priority first
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
    readonly System.Windows.Shapes.Path smallIcon = MakeIcon(18), bigIcon = MakeIcon(24);
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
    IslandNotice shown; // what the island is displaying; null while hidden
    int shownIndex;     // whose notice that is, in reminders
    bool confirmed;

    // Raised after the island has gone following a yes answer.
    internal event Action Confirmed;

    internal IslandWindow(params IReminder[] reminders)
    {
        this.reminders = reminders;
        Title = "Reminder";
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
        var yes = MakeButton("Yes", Green, Color.FromRgb(76, 224, 112), Color.FromRgb(4, 40, 16), () => Answer(true));
        yesLabel = (TextBlock)yes.Child;
        Grid.SetColumn(yes, 2);
        actions.Children.Add(notYet);
        actions.Children.Add(yes);

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

    static System.Windows.Shapes.Path MakeIcon(double size)
    {
        return new System.Windows.Shapes.Path { Width = size, Height = size, Stretch = Stretch.Uniform };
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
        IslandNotice due = null;
        int dueIndex = -1;
        for (int i = 0; i < reminders.Length && due == null; i++)
        {
            due = reminders[i].Due();
            if (due != null) dueIndex = i;
        }
        if (shown != null)
        {
            if (!open) return;
            // A higher-priority reminder takes over; this one stays due and returns afterwards.
            if ((due != null && dueIndex < shownIndex) || !reminders[shownIndex].StillCurrent(shown)) Dismiss();
            return;
        }
        if (due != null) Present(due, dueIndex);
    }

    internal void Answer(bool yes)
    {
        if (!open) return;
        open = false;
        glow.BeginAnimation(OpacityProperty, null);
        var result = reminders[shownIndex].Answer(shown, yes);
        confirmed = yes;
        compactLeft.Text = result.Left;
        compactRight.Text = result.Right;
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

    internal void Present(IslandNotice notice, int index)
    {
        shown = notice;
        shownIndex = index;
        confirmed = false;
        var accent = notice.Accent;
        detailText.Text = notice.Detail;
        yesLabel.Text = notice.YesLabel;
        smallIcon.Data = bigIcon.Data = notice.Icon;
        smallIcon.Fill = bigIcon.Fill = new SolidColorBrush(accent);
        glow.Fill = new RadialGradientBrush(Color.FromArgb(120, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
        nameText.Text = compactLeft.Text = notice.Title;
        timeText.Text = compactRight.Text = notice.Time;
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
            if (confirmed && Confirmed != null) Confirmed();
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
