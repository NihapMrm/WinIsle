using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
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
    // The window starts at the top of the screen so the media pill can tuck above it; Inset puts reminders 8 px down.
    const double WinW = 500, WinH = 420, Inset = 8, BlurMargin = 40; // tall enough for an expanded notification; empty pixels pass clicks through
    const int ExpandedLines = 16;
    const double SeedW = 76, SeedH = 26;
    const double CompactW = 244, CompactH = 44;
    // Media: a pill tucked above the screen edge with PeekShown px showing, opening into a player on hover.
    const double PeekW = 150, PeekH = 20, PeekShown = 6, PeekTop = PeekShown - Inset - PeekH, GoneTop = -(Inset + PeekH + 4);
    const double MediaW = 380, MediaH = 160, MediaR = 30, MediaPad = 14;

    static readonly MediaInfo Idle = new MediaInfo { Title = "Nothing playing", Artist = "", AppId = "" };
    static readonly Geometry PlayIcon =Geometry.Parse("M7,4.5 L19.5,12 L7,19.5 Z");
    static readonly Geometry PauseIcon = Geometry.Parse("M6.5,4.5 H10 V19.5 H6.5 Z M14,4.5 H17.5 V19.5 H14 Z");
    static readonly Geometry PreviousIcon = Geometry.Parse("M6,5 H8 V19 H6 Z M19,5 L9.5,12 L19,19 Z");
    static readonly Geometry NextIcon = Geometry.Parse("M16,5 H18 V19 H16 Z M5,5 L14.5,12 L5,19 Z");

    // The open island's dimensions. Corner radius = button radius + padding, so the curves are concentric.
    internal class IslandSize
    {
        public double Width, Pad, Tile, ButtonH, Gap, TitleFont, DetailFont, LineH, TextW, IconSize, ButtonFont;
        public double OpenH { get { return 3 * Pad + Tile + ButtonH; } }
        public double OpenR { get { return ButtonH / 2 + Pad; } }
    }

    internal static readonly IslandSize Regular = new IslandSize { Width = 430, Pad = 14, Tile = 44, ButtonH = 40, Gap = 12, TitleFont = 20, DetailFont = 12.5, LineH = 17, TextW = 246, IconSize = 24, ButtonFont = 14 };
    internal static readonly IslandSize Small = new IslandSize { Width = 360, Pad = 12, Tile = 36, ButtonH = 32, Gap = 10, TitleFont = 16, DetailFont = 11.5, LineH = 15.5, TextW = 206, IconSize = 20, ButtonFont = 12.5 };

    readonly IReminder[] reminders; // highest priority first
    readonly MediaSession media;    // null in demos
    readonly SpringValue w = new SpringValue(), h = new SpringValue(), r = new SpringValue(), y = new SpringValue();
    readonly Border pill = new Border();
    readonly Image backdrop = new Image { Stretch = Stretch.Fill, Margin = new Thickness(-BlurMargin), Effect = new BlurEffect { Radius = 36, RenderingBias = RenderingBias.Performance } };
    readonly Grid stage = new Grid { Opacity = 0 };
    readonly RectangleGeometry clip = new RectangleGeometry();
    readonly TextBlock compactLeft = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 122, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock compactRight = new TextBlock { FontSize = 13, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 18, 0), MaxWidth = 104, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock nameText = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock detailText = new TextBlock { Opacity = 0.8, Margin = new Thickness(0, -2, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock timeText = new TextBlock { FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    readonly System.Windows.Shapes.Ellipse glow = new System.Windows.Shapes.Ellipse { Opacity = 0 };
    readonly System.Windows.Shapes.Path smallIcon = MakeIcon(18), bigIcon = MakeIcon(0);
    readonly Image smallLogo = new Image { Width = 20, Height = 20 }, bigLogo = new Image { Stretch = Stretch.UniformToFill };
    readonly System.Windows.Shapes.Path check = new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse("M3,10.5 L8,15.5 L17,5"), Width = 20, Height = 20, Opacity = 0,
        Stroke = new SolidColorBrush(Green), StrokeThickness = 2.4,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        StrokeDashArray = new DoubleCollection { 9, 9 }, StrokeDashOffset = 9,
    };
    Grid compact, header, actions, tile;
    TextBlock yesLabel, noLabel;
    Border yesButton, noButton;

    // Media player parts.
    readonly Border art = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock mediaTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock mediaArtist = new TextBlock { FontSize = 12.5, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Image mediaApp = new Image { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 2, 0, 0) };
    readonly TextBlock elapsed = new TextBlock { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock remaining = new TextBlock { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    readonly Grid seekTrack = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 8, 0) };
    readonly Border seekFill = new Border { Height = 4, Width = 0, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Windows.Shapes.Path playGlyph = new System.Windows.Shapes.Path { Data = PlayIcon, Width = 16, Height = 16, Stretch = Stretch.Uniform, Fill = new SolidColorBrush(Color.FromRgb(18, 18, 20)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    Grid mediaLayer, timeline;
    MediaInfo mediaNow;   // last snapshot shown
    byte[] artBytes;      // whose image art shows
    bool mediaShown;      // peek or player is on screen
    bool mediaOpen;       // the player is open under the mouse

    IntPtr hwnd;
    double dpi = 1;
    bool looping, open;
    TimeSpan lastFrame;
    IslandNotice shown; // what the island is displaying; null while hidden
    int shownIndex;     // whose notice that is, in reminders
    bool confirmed;
    bool expanded;       // the notice was clicked open to show all of it
    IslandSize size;     // the shown notice's layout

    // Raised after the island has gone following a yes answer.
    internal event Action Confirmed;

    internal IslandWindow(MediaSession media, params IReminder[] reminders)
    {
        this.media = media;
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
        Top = area.Top;

        pill.HorizontalAlignment = HorizontalAlignment.Center;
        pill.VerticalAlignment = VerticalAlignment.Top;
        pill.Margin = new Thickness(0, Inset, 0, 0);
        pill.BorderThickness = new Thickness(1);
        pill.Background = new LinearGradientBrush(Color.FromArgb(165, 38, 38, 42), Color.FromArgb(150, 12, 12, 14), 90);
        pill.BorderBrush = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90);

        // Compact: icon and label on the left, detail on the right.
        var iconSlot = new Grid { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) };
        iconSlot.Children.Add(smallIcon);
        iconSlot.Children.Add(smallLogo);
        RenderOptions.SetBitmapScalingMode(smallLogo, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(bigLogo, BitmapScalingMode.HighQuality);
        iconSlot.Children.Add(check);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(iconSlot);
        left.Children.Add(compactLeft);
        compact = Layer(CompactW, CompactH, Inset);
        compact.Children.Add(left);
        compact.Children.Add(compactRight);

        // Open: tile, name and time on top, the two actions underneath.
        tile = new Grid();
        tile.Children.Add(glow);
        tile.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)) });
        bigIcon.HorizontalAlignment = HorizontalAlignment.Center;
        bigIcon.VerticalAlignment = VerticalAlignment.Center;
        tile.Children.Add(bigIcon);
        tile.Children.Add(bigLogo);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(nameText);
        titles.Children.Add(detailText);
        var who = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        who.Children.Add(tile);
        who.Children.Add(titles);
        header = Layer(0, 0, 0);
        header.Background = Brushes.Transparent; // the whole header is clickable, not only the text
        header.MouseLeftButtonUp += (s, e) => Expand();
        header.Children.Add(who);
        header.Children.Add(timeText);

        actions = Layer(0, 0, 0);
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var notYet = MakeButton(Label("Not yet", Colors.White), Color.FromArgb(38, 255, 255, 255), Color.FromArgb(64, 255, 255, 255), () => Answer(false));
        var yes = MakeButton(Label("Yes", Color.FromRgb(4, 40, 16)), Green, Color.FromRgb(76, 224, 112), () => Answer(true));
        yesLabel = (TextBlock)yes.Child;
        noLabel = (TextBlock)notYet.Child;
        yesButton = yes;
        noButton = notYet;
        Grid.SetColumn(yes, 2);
        actions.Children.Add(notYet);
        actions.Children.Add(yes);

        mediaLayer = BuildPlayer();

        var content = new Grid { Width = WinW, Height = WinH, Clip = clip };
        content.Children.Add(compact);
        content.Children.Add(header);
        content.Children.Add(actions);
        content.Children.Add(mediaLayer);
        var glass = new Grid { Width = WinW, Height = WinH, Clip = clip }; // clip outside the blur so the edge stays crisp
        glass.Children.Add(backdrop);
        stage.Children.Add(glass);
        stage.Children.Add(pill);
        stage.Children.Add(content);
        Content = stage;

        // Hovering the peek opens the player; leaving puts it back.
        MouseEnter += (s, e) => { if (shown == null && mediaShown && !mediaOpen) OpenPlayer(); };
        MouseLeave += (s, e) => After(350, () => { if (mediaOpen && !IsMouseOver) ClosePlayer(); });

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

    static TextBlock Label(string text, Color ink)
    {
        return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    static Border MakeButton(UIElement child, Color rest, Color hover, Action click)
    {
        var fill = new SolidColorBrush(rest);
        var scale = new ScaleTransform(1, 1);
        var b = new Border
        {
            Background = fill, Cursor = Cursors.Hand,
            RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5),
            Child = child,
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

    static Border RoundButton(UIElement glyph, double diameter, Color rest, Color hover, Action click)
    {
        var b = MakeButton(glyph, rest, hover, click);
        b.Width = b.Height = diameter;
        b.CornerRadius = new CornerRadius(diameter / 2);
        return b;
    }

    static System.Windows.Shapes.Path Glyph(Geometry icon)
    {
        return new System.Windows.Shapes.Path { Data = icon, Width = 16, Height = 16, Stretch = Stretch.Uniform, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    // Art and names, the timeline, then previous / play-pause / next.
    Grid BuildPlayer()
    {
        var layer = Layer(MediaW - 2 * MediaPad, MediaH - 2 * MediaPad, MediaPad); // the open player starts at the screen edge, not at Inset
        var column = new StackPanel();

        var track = new Grid { Height = 56 };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        track.ColumnDefinitions.Add(new ColumnDefinition());
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(mediaTitle);
        names.Children.Add(mediaArtist);
        Grid.SetColumn(names, 1);
        Grid.SetColumn(mediaApp, 2);
        RenderOptions.SetBitmapScalingMode(mediaApp, BitmapScalingMode.HighQuality);
        track.Children.Add(art);
        track.Children.Add(names);
        track.Children.Add(mediaApp);
        column.Children.Add(track);

        timeline = new Grid { Height = 16, Margin = new Thickness(0, 12, 0, 8) };
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        timeline.ColumnDefinitions.Add(new ColumnDefinition());
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        seekTrack.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
        seekTrack.Children.Add(seekFill);
        seekTrack.MouseLeftButtonUp += (s, e) => SeekTo(e.GetPosition(seekTrack).X / seekTrack.ActualWidth);
        Grid.SetColumn(seekTrack, 1);
        Grid.SetColumn(remaining, 2);
        timeline.Children.Add(elapsed);
        timeline.Children.Add(seekTrack);
        timeline.Children.Add(remaining);
        column.Children.Add(timeline);

        var clear = Color.FromArgb(0, 255, 255, 255);
        var dim = Color.FromArgb(40, 255, 255, 255);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 40 };
        var play = RoundButton(playGlyph, 40, Color.FromArgb(235, 255, 255, 255), Colors.White, () =>
        {
            playGlyph.Data = playGlyph.Data == PauseIcon ? PlayIcon : PauseIcon; // flips at once; the next read confirms
            media.PlayPause();
        });
        play.Margin = new Thickness(18, 0, 18, 0);
        controls.Children.Add(RoundButton(Glyph(PreviousIcon), 36, clear, dim, () => media.Previous()));
        controls.Children.Add(play);
        controls.Children.Add(RoundButton(Glyph(NextIcon), 36, clear, dim, () => media.Next()));
        column.Children.Add(controls);

        layer.Children.Add(column);
        return layer;
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
            if (!open || expanded) return; // an expanded notice stays until a button is clicked
            // A higher-priority reminder takes over; this one stays due and returns afterwards.
            if ((due != null && dueIndex < shownIndex && !shown.Sticky) || !reminders[shownIndex].StillCurrent(shown)) Dismiss();
            return;
        }
        // Reminders wait while the player is open under the mouse; calls don't.
        if (due != null && (!mediaOpen || due.Sticky))
        {
            if (mediaShown) HidePlayer();
            Present(due, dueIndex);
            return;
        }
        UpdatePlayer();
    }

    internal void Answer(bool yes)
    {
        if (!open) return;
        open = false;
        (yes ? IslandSound.Yes : IslandSound.NotYet).Play();
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
            smallIcon.Opacity = smallLogo.Opacity = 0;
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
        noLabel.Text = notice.NoLabel ?? "Not yet";
        expanded = false;
        header.Cursor = notice.More == null ? null : Cursors.Hand;
        size = notice.Small ? Small : Regular;
        double extra = Fit(size, 3);
        smallIcon.Data = bigIcon.Data = notice.Icon;
        smallIcon.Fill = bigIcon.Fill = new SolidColorBrush(accent);
        glow.Fill = new RadialGradientBrush(Color.FromArgb(120, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
        nameText.Text = compactLeft.Text = notice.Title;
        timeText.Text = compactRight.Text = notice.Time;
        smallLogo.Source = bigLogo.Source = notice.Logo;
        smallLogo.Opacity = 1;
        smallIcon.Opacity = bigIcon.Opacity = notice.Logo == null ? 1 : 0;
        check.Opacity = 0;
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13); // back on top of other topmost windows: NOSIZE | NOMOVE | NOACTIVATE
        CaptureBackdrop();
        (notice.Sound ?? IslandSound.Appear).Play();
        Jump(w, SeedW); Jump(h, SeedH); Jump(r, SeedH / 2); Jump(y, 0);
        Morph(CompactW, CompactH, CompactH / 2);
        Go(stage, OpacityProperty, 1, 140, Out, 0, null, null);
        Reveal(compact, 90);
        After(620, () =>
        {
            open = true;
            Morph(size.Width, size.OpenH + extra, size.OpenR);
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

    // Clicking the notice shows all of it: the island grows and the buttons slide down.
    void Expand()
    {
        if (!open || expanded || shown.More == null) return;
        expanded = true;
        double from = actions.Margin.Top;
        detailText.Text = shown.More;
        double extra = Fit(size, ExpandedLines);
        actions.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, from, 0, 0), actions.Margin, TimeSpan.FromMilliseconds(420)) { EasingFunction = Spring });
        Morph(size.Width, size.OpenH + extra, size.OpenR);
    }

    // Sizes the open layout. Returns the extra height wrapped detail text needs, up to the given number of lines.
    double Fit(IslandSize s, int lines)
    {
        nameText.FontSize = timeText.FontSize = s.TitleFont;
        nameText.MaxWidth = detailText.MaxWidth = s.TextW;
        detailText.FontSize = s.DetailFont;
        detailText.MaxHeight = lines * s.LineH;
        tile.Width = tile.Height = glow.Width = glow.Height = bigLogo.Width = bigLogo.Height = s.Tile;
        tile.Margin = new Thickness(0, 0, s.Gap, 0);
        bigLogo.Clip = new EllipseGeometry(new Point(s.Tile / 2, s.Tile / 2), s.Tile / 2, s.Tile / 2);
        bigIcon.Width = bigIcon.Height = s.IconSize;
        yesButton.CornerRadius = noButton.CornerRadius = new CornerRadius(s.ButtonH / 2);
        yesLabel.FontSize = noLabel.FontSize = s.ButtonFont;
        detailText.Measure(new Size(s.TextW, double.PositiveInfinity));
        double extra = Math.Max(0, detailText.DesiredSize.Height - s.LineH);
        header.Width = s.Width - 2 * s.Pad - 8;
        header.Height = s.Tile + extra;
        header.Margin = new Thickness(0, Inset + s.Pad, 0, 0);
        actions.Width = s.Width - 2 * s.Pad;
        actions.Height = s.ButtonH;
        actions.BeginAnimation(MarginProperty, null); // a finished expand animation would otherwise hold its value
        actions.Margin = new Thickness(0, Inset + 2 * s.Pad + s.Tile + extra, 0, 0);
        return extra;
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

    // ---- media player ----

    // The peek is always there (except in demos); with no media session it opens to "Nothing playing".
    void UpdatePlayer()
    {
        if (media == null) return;
        if (!mediaShown) ShowPeek();
        Fill(media.Now ?? Idle);
    }

    // Slides down from above the screen until only PeekShown px show.
    void ShowPeek()
    {
        mediaShown = true;
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        CaptureBackdrop();
        Jump(w, PeekW); Jump(h, PeekH); Jump(r, PeekH / 2); Jump(y, GoneTop);
        ApplyShape();
        stage.BeginAnimation(OpacityProperty, null);
        stage.Opacity = 1;
        Morph(PeekW, PeekH, PeekH / 2, PeekTop);
    }

    void OpenPlayer()
    {
        mediaOpen = true;
        CaptureBackdrop();
        if (mediaNow != null) Fill(mediaNow);
        // Flush with the screen's top edge: the top corners sit above the screen, so the player hangs from the lid.
        Morph(MediaW, MediaH + MediaR, MediaR, -Inset - MediaR);
        Reveal(mediaLayer, 120);
    }

    void ClosePlayer()
    {
        mediaOpen = false;
        Conceal(mediaLayer);
        Morph(PeekW, PeekH, PeekH / 2, PeekTop);
    }

    // Makes way for a reminder; the peek slides back in once it's gone.
    void HidePlayer()
    {
        mediaShown = mediaOpen = false;
        Conceal(mediaLayer);
        stage.BeginAnimation(OpacityProperty, null);
        stage.Opacity = 0;
    }

    void Fill(MediaInfo m)
    {
        mediaNow = m;
        mediaTitle.Text = m.Title == "" ? "Playing" : m.Title;
        mediaArtist.Text = m.Artist;
        mediaTitle.MaxWidth = mediaArtist.MaxWidth = MediaW - 2 * MediaPad - 68 - 26;
        if (m.Art != artBytes || art.Background == null)
        {
            artBytes = m.Art;
            var image = Decode(m.Art);
            art.Background = image == null ? (Brush)new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        mediaApp.Source = AppIcon.For(m.AppId ?? "");
        playGlyph.Data = m.Playing ? PauseIcon : PlayIcon;

        bool timed = m.End > TimeSpan.Zero;
        timeline.Visibility = timed ? Visibility.Visible : Visibility.Hidden; // some players report no length
        // Only while open: animating redraws the whole see-through window, which is costly to keep up behind the peek.
        if (!timed || !mediaOpen) return;
        var position = MediaSession.PositionAt(m, DateTimeOffset.Now);
        elapsed.Text = MediaSession.Clock(position);
        remaining.Text = "-" + MediaSession.Clock(m.End - position);
        // Glide to where the bar will be in a second, so it moves smoothly between reads.
        var ahead = MediaSession.PositionAt(m, DateTimeOffset.Now.AddSeconds(1));
        seekFill.BeginAnimation(WidthProperty, new DoubleAnimation(ahead.Ticks / (double)m.End.Ticks * seekTrack.ActualWidth, TimeSpan.FromSeconds(1)));
    }

    void SeekTo(double fraction)
    {
        var m = mediaNow;
        if (m == null || m.End <= TimeSpan.Zero || double.IsNaN(fraction)) return;
        fraction = Math.Max(0, Math.Min(1, fraction));
        seekFill.BeginAnimation(WidthProperty, null);
        seekFill.Width = fraction * seekTrack.ActualWidth;
        media.Seek(TimeSpan.FromTicks((long)(fraction * m.End.Ticks)));
    }

    static ImageSource Decode(byte[] bytes)
    {
        if (bytes == null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 112;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; } // a bad cover shows the plain tile
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
            backdrop.Source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
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

    // top moves the pill up from its resting place; the media peek uses it to tuck above the screen.
    void Morph(double width, double height, double radius, double top = 0)
    {
        w.To = width; h.To = height; r.To = radius; y.To = top;
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
        var springs = new[] { w, h, r, y };
        foreach (var s in springs)
            for (int i = 0; i < 4; i++) // substeps keep the spring stable on a slow frame
            {
                s.V += (-Stiffness * (s.X - s.To) - Damping * s.V) * dt / 4;
                s.X += s.V * dt / 4;
            }
        bool settled = springs.All(s => Math.Abs(s.X - s.To) < 0.2 && Math.Abs(s.V) < 2);
        if (settled) foreach (var s in springs) Jump(s, s.To);
        ApplyShape();
        if (settled) StopLoop();
    }

    void ApplyShape()
    {
        pill.Width = w.X;
        pill.Height = h.X;
        pill.CornerRadius = new CornerRadius(r.X);
        pill.Margin = new Thickness(0, Inset + y.X, 0, 0);
        clip.Rect = new Rect((WinW - w.X) / 2, Inset + y.X, w.X, h.X);
        clip.RadiusX = clip.RadiusY = r.X;
    }
}
