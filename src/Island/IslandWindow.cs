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
partial class IslandWindow : GlassWindow // the file shelf lives in IslandWindow.Shelf.cs
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
    const double MediaW = 380, MediaH = 188, MediaR = 30, MediaPad = 14; // 160 + the volume row
    const double MirrorW = 360, MirrorH = 270, MirrorR = 28; // 4:3, the camera's shape
    const double TimerW = 340, TimerH = 220, TimerR = 30;
    const double TimerPeekW = 120, TimerPeekH = 30; // the peek grows to this while a timer runs, showing the time left

    // A play triangle's weight sits left of its box centre (centroid is a third in), so it is nudged right to look centred.
    static readonly Transform PlayNudge = new TranslateTransform(1.5, 0);
    static readonly Brush TopLitBorder =new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90);
    static readonly Brush LidBorder = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    static readonly MediaInfo Idle =new MediaInfo { Title = "Nothing playing", Artist = "", AppId = "" };
    static readonly Geometry PlayIcon =Geometry.Parse("M7,4.5 L19.5,12 L7,19.5 Z");
    static readonly Geometry PauseIcon = Geometry.Parse("M6.5,4.5 H10 V19.5 H6.5 Z M14,4.5 H17.5 V19.5 H14 Z");
    static readonly Geometry PreviousIcon = Geometry.Parse("M6,5 H8 V19 H6 Z M19,5 L9.5,12 L19,19 Z");
    static readonly Geometry NextIcon = Geometry.Parse("M16,5 H18 V19 H16 Z M5,5 L14.5,12 L5,19 Z");
    static readonly Geometry StopIcon = Geometry.Parse("M7,7 H17 V17 H7 Z");
    static readonly Geometry BackIcon = Geometry.Parse("M15,5 L8,12 L15,19 L16.5,17.5 L11,12 L16.5,6.5 Z");
    static readonly Geometry MirrorIcon =Geometry.Parse("F0 M12,2 A6.5,8 0 1 1 11.99,2 Z M12,4 A4.5,6 0 1 1 11.99,4 Z M11,18 H13 V20.5 H11 Z M8,20.5 H16 V22 H8 Z");

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
    Grid compact, header, actions, laterRow, tile;
    TextBlock yesLabel, noLabel;
    Border yesButton, noButton, laterButton;
    Border[] laterChips; // 30 min, 1, 2, 3 hr: replace the actions after "Later"
    TextBlock dndGlyph;  // the player's Do not disturb button, lit while on

    // Media player parts.
    readonly Border art = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock mediaTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock mediaArtist = new TextBlock { FontSize = 12.5, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Image mediaApp = new Image { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 2, 0, 0) };
    readonly TextBlock elapsed = new TextBlock { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock remaining = new TextBlock { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    readonly Grid seekTrack = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 8, 0) };
    readonly Border seekFill = new Border { Height = 4, Width = 0, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Windows.Shapes.Path playGlyph = new System.Windows.Shapes.Path { Data = PlayIcon, RenderTransform = PlayNudge, Width = 16, Height = 16, Stretch = Stretch.Uniform, Fill = new SolidColorBrush(Color.FromRgb(18, 18, 20)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    Grid mediaLayer, timeline;
    MediaInfo mediaNow;   // last snapshot shown
    byte[] artBytes;      // whose image art shows
    bool mediaShown;      // peek or player is on screen
    bool mediaOpen;       // the player is open under the mouse

    // Mirror: the camera, flipped, filling the island.
    readonly Image mirrorImage = new Image { Stretch = Stretch.UniformToFill, RenderTransform = new ScaleTransform(-1, 1), RenderTransformOrigin = new Point(0.5, 0.5) };
    readonly TextBlock mirrorText = new TextBlock { FontSize = 13, Opacity = 0.8, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 0, 24, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    Grid mirrorLayer;
    WriteableBitmap mirrorBitmap;
    Camera camera;
    bool mirrorOn;        // the island is a mirror and the camera is on

    // Timer panel, and the countdown peek shown while a timer runs.
    readonly CountdownTimer timer; // null in demos
    readonly FileShelf shelf;      // null in demos
    readonly System.Windows.Shapes.Path pillIcon = new System.Windows.Shapes.Path { Width = 13, Height = 13, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    readonly TimeWheel hoursWheel = new TimeWheel(24), minutesWheel = new TimeWheel(60), secondsWheel = new TimeWheel(60);
    readonly TextBlock timerPeekText = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Windows.Shapes.Path timerPlayGlyph = new System.Windows.Shapes.Path { Data = PlayIcon, RenderTransform = PlayNudge, Width = 16, Height = 16, Stretch = Stretch.Uniform, Fill = new SolidColorBrush(Color.FromRgb(18, 18, 20)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    Grid timerLayer, timerPeek;
    bool timerOpen;       // the timer panel is open under the mouse
    bool timerPeekShown;  // the peek is the countdown pill rather than the thin line

    IntPtr hwnd;
    double dpi = 1;
    bool looping, open;
    TimeSpan lastFrame;
    IslandNotice shown; // what the island is displaying; null while hidden
    int shownIndex;     // whose notice that is, in reminders
    bool confirmed;
    bool expanded;       // the notice was clicked open to show all of it
    IslandSize size;     // the shown notice's layout
    System.Media.SoundPlayer ringing; // a looping sound to stop when the island closes
    int stageGen; // bumped when a pill takes over the island, so steps scheduled for the old one are skipped

    // Raised after the island has gone following a yes answer.
    internal event Action Confirmed;

    internal IslandWindow(MediaSession media, CountdownTimer timer, FileShelf shelf, params IReminder[] reminders)
    {
        this.media = media;
        this.timer = timer;
        this.shelf = shelf;
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
        pill.BorderBrush = TopLitBorder;

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
        left.Children.Add(BuildLevelBar());
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
        // Not yet | Later (only when the notice offers it) | Yes
        for (int i = 0; i < 5; i++) actions.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 1 ? new GridLength(8) : new GridLength(1, GridUnitType.Star) });
        Color soft = Color.FromArgb(38, 255, 255, 255), softHover = Color.FromArgb(64, 255, 255, 255);
        var notYet = MakeButton(Label("Not yet", Colors.White), soft, softHover, () => Answer(false));
        var yes = MakeButton(Label("Yes", Color.FromRgb(4, 40, 16)), Green, Color.FromRgb(76, 224, 112), () => Answer(true));
        laterButton = MakeButton(Label("Later", Colors.White), soft, softHover, ShowLater);
        yesLabel = (TextBlock)yes.Child;
        noLabel = (TextBlock)notYet.Child;
        yesButton = yes;
        noButton = notYet;
        Grid.SetColumn(laterButton, 2);
        Grid.SetColumn(yes, 4);
        actions.Children.Add(notYet);
        actions.Children.Add(laterButton);
        actions.Children.Add(yes);

        laterRow = Layer(0, 0, 0);
        laterChips = new Border[IslandNotice.LaterChoices.Length];
        for (int i = 0; i < laterChips.Length; i++)
        {
            var after = IslandNotice.LaterChoices[i];
            if (i > 0) laterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            laterRow.ColumnDefinitions.Add(new ColumnDefinition());
            var chip = laterChips[i] = MakeButton(Label(IslandResult.Span(after), Colors.White), soft, softHover, () => AnswerLater(after));
            Grid.SetColumn(chip, 2 * i);
            laterRow.Children.Add(chip);
        }

        mediaLayer = BuildPlayer();
        mirrorLayer = Layer(MirrorW, MirrorH, 0); // starts at the screen edge, like the player
        mirrorLayer.Background = Brushes.Black;
        mirrorLayer.Cursor = Cursors.Hand;
        mirrorLayer.Children.Add(mirrorImage);
        mirrorLayer.Children.Add(mirrorText);
        mirrorLayer.MouseLeftButtonUp += (s, e) => CloseMirror(); // click the mirror to turn the camera off

        var content = new Grid { Width = WinW, Height = WinH, Clip = clip };
        content.Children.Add(compact);
        content.Children.Add(header);
        content.Children.Add(actions);
        content.Children.Add(laterRow);
        content.Children.Add(mediaLayer);
        content.Children.Add(mirrorLayer);
        timerLayer = BuildTimer();
        content.Children.Add(timerLayer);
        timerPeek = Layer(TimerPeekW, TimerPeekH, 0);
        var peekRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        peekRow.Children.Add(pillIcon);
        peekRow.Children.Add(timerPeekText);
        timerPeek.Children.Add(peekRow);
        content.Children.Add(timerPeek);
        content.Children.Add(InitShelf());
        content.Children.Add(chargeLayer = BuildCharge());
        content.Children.Add(BuildInUseDots()); // inside the island, at its right end
        var glass = new Grid { Width = WinW, Height = WinH, Clip = clip }; // clip outside the blur so the edge stays crisp
        glass.Children.Add(backdrop);
        stage.Children.Add(glass);
        stage.Children.Add(pill);
        stage.Children.Add(content);
        Content = stage;

        // Hovering the peek opens the player; leaving puts it back.
        // A shelf pill opens the shelf; otherwise the player.
        MouseEnter += (s, e) =>
        {
            if (shown != null || !mediaShown || mediaOpen) return;
            if (timerPeekShown && !timer.Active && shelf.Count > 0) OpenShelf(); else OpenPlayer();
        };
        // The mirror and the timer panel stay open; the back arrow or a click closes them.
        MouseLeave += (s, e) => After(350, () => { if (mediaOpen && !mirrorOn && !timerOpen && !IsMouseOver) ClosePlayer(); });

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
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Do not disturb, top right: a bell, slashed and purple while on.
        dndGlyph = new TextBlock { FontFamily = Icons, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dnd = RoundButton(dndGlyph, 30, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(40, 255, 255, 255), () => Settings.Set(Settings.Dnd, !Settings.On(Settings.Dnd)));
        dnd.VerticalAlignment = VerticalAlignment.Top;
        dnd.Margin = new Thickness(4, -4, -4, 0);
        Grid.SetColumn(dnd, 3);
        track.Children.Add(dnd);
        Action showDnd = () =>
        {
            bool on = Settings.On(Settings.Dnd);
            dndGlyph.Text = on ? "" : ""; // RingerSilent (bell, slashed) : Ringer (bell)
            dndGlyph.Foreground = on ? new SolidColorBrush(Color.FromRgb(175, 130, 255)) : Brushes.White;
        };
        showDnd();
        Settings.Changed += showDnd; // also switched from the Control Center
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
            ShowPlay(playGlyph, playGlyph.Data == PauseIcon); // flips at once; the next read confirms
            media.PlayPause();
        });
        play.Margin = new Thickness(18, 0, 18, 0);
        controls.Children.Add(RoundButton(Glyph(PreviousIcon), 36, clear, dim, () => media.Previous()));
        controls.Children.Add(play);
        controls.Children.Add(RoundButton(Glyph(NextIcon), 36, clear, dim, () => media.Next()));
        var controlsRow = new Grid { Height = 40 };
        controlsRow.Children.Add(controls);
        var rightTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        rightTools.Children.Add(RoundButton(Glyph(ShelfIcon), 34, clear, dim, OpenShelf));
        rightTools.Children.Add(RoundButton(Glyph(MirrorIcon), 34, clear, dim, OpenMirror));
        var gearGlyph = new TextBlock { Text = "", FontFamily = Icons, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        rightTools.Children.Add(RoundButton(gearGlyph, 34, clear, dim, () => ControlCenter.Open(null)));
        controlsRow.Children.Add(rightTools);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        tools.Children.Add(RoundButton(Glyph(CountdownTimer.Icon), 34, clear, dim, OpenTimer));
        var clipboardGlyph = new TextBlock { Text = "", FontFamily = Icons, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(RoundButton(clipboardGlyph, 34, clear, dim, () => { if (Program.ClipboardPanel != null) Program.ClipboardPanel.Toggle(); }));
        controlsRow.Children.Add(tools);
        column.Children.Add(controlsRow);
        column.Children.Add(BuildVolumeRow());

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
            var n = reminders[i].Due(); // asked even when muted: notifications drop what arrives meanwhile
            if (n != null && !Muted(i)) { due = n; dueIndex = i; }
        }
        if (shown != null)
        {
            if (!open || expanded) return; // an expanded notice stays until a button is clicked
            // A higher-priority reminder takes over; this one stays due and returns afterwards (also after Do not disturb).
            if ((due != null && dueIndex < shownIndex && !shown.Sticky) || !reminders[shownIndex].StillCurrent(shown) || Muted(shownIndex)) Dismiss();
            return;
        }
        // Reminders wait while the mirror is open or the mouse is on the island (so an open timer panel
        // left alone doesn't hold them back forever); calls don't wait.
        if (due != null && (!(mirrorOn || (mediaOpen && IsMouseOver)) || due.Sticky))
        {
            if (mediaShown) HidePlayer();
            Present(due, dueIndex);
            return;
        }
        UpdatePlayer();
    }

    // Do not disturb holds every reminder and notification; the timer alarm and Control Center previews still show.
    bool Muted(int i)
    {
        return Settings.On(Settings.Dnd) && !(reminders[i] is PreviewReminder) && !(reminders[i] is CountdownTimer);
    }

    // "Later" swaps the buttons for the choices.
    void ShowLater()
    {
        if (!open) return;
        laterRow.Margin = actions.Margin;
        Conceal(actions);
        Reveal(laterRow, 60);
    }

    void AnswerLater(TimeSpan after)
    {
        if (!open) return;
        shown.LaterBy = after;
        Answer(false);
    }

    internal void Answer(bool yes)
    {
        if (!open) return;
        open = false;
        StopRinging();
        IslandSound.Play(yes ? IslandSound.Yes : IslandSound.NotYet);
        glow.BeginAnimation(OpacityProperty, null);
        var result = reminders[shownIndex].Answer(shown, yes);
        confirmed = yes;
        compactLeft.Text = result.Left;
        compactRight.Text = result.Right;
        Conceal(header);
        Conceal(actions);
        Conceal(laterRow);
        Morph(CompactW, CompactH, CompactH / 2);
        Reveal(compact, 110);
        if (yes)
        {
            smallIcon.Opacity = smallLogo.Opacity = 0;
            check.Opacity = 1;
            Go(check, System.Windows.Shapes.Shape.StrokeDashOffsetProperty, 0, 360, Out, 180, null, 9);
        }
        int gen = stageGen;
        After(1700, () => { if (gen == stageGen) Dismiss(); });
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
        actions.ColumnDefinitions[2].Width = notice.Later ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        actions.ColumnDefinitions[3].Width = new GridLength(notice.Later ? 8 : 0);
        laterButton.Visibility = notice.Later ? Visibility.Visible : Visibility.Collapsed;
        expanded = false;
        header.Cursor = notice.More == null ? null : Cursors.Hand;
        size = notice.Small ? Small : Regular;
        double extra = Fit(size, 3);
        smallIcon.Data = bigIcon.Data = notice.Icon;
        smallIcon.Fill = bigIcon.Fill = new SolidColorBrush(accent);
        glow.Fill = new RadialGradientBrush(Color.FromArgb(120, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
        levelBar.Visibility = Visibility.Collapsed; // only flashes have a level
        inUseDots.Visibility = Visibility.Hidden;   // the dots come back with the peek
        nameText.Text = compactLeft.Text = notice.Title;
        timeText.Text = compactRight.Text = notice.Time;
        smallLogo.Source = bigLogo.Source = notice.Logo;
        smallLogo.Opacity = 1;
        smallIcon.Opacity = bigIcon.Opacity = notice.Logo == null ? 1 : 0;
        check.Opacity = 0;
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13); // back on top of other topmost windows: NOSIZE | NOMOVE | NOACTIVATE
        CaptureBackdrop();
        var sound = notice.Sound ?? IslandSound.Appear;
        if (notice.LoopSound) { IslandSound.Loop(sound); ringing = sound; } else IslandSound.Play(sound);
        Jump(w, SeedW); Jump(h, SeedH); Jump(r, SeedH / 2); Jump(y, 0);
        Morph(CompactW, CompactH, CompactH / 2);
        Go(stage, OpacityProperty, 1, 140, Out, 0, null, null);
        Reveal(compact, 90);
        int gen = stageGen;
        After(620, () =>
        {
            if (gen != stageGen) return; // something took the island over meanwhile
            open = true;
            Morph(size.Width, size.OpenH + extra, size.OpenR);
            Conceal(compact);
            Reveal(header, 70);
            Reveal(actions, 140);            glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(1600))
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
        foreach (var b in laterChips.Concat(new[] { yesButton, noButton, laterButton }))
        {
            b.CornerRadius = new CornerRadius(s.ButtonH / 2);
            ((TextBlock)b.Child).FontSize = s.ButtonFont;
        }
        detailText.Measure(new Size(s.TextW, double.PositiveInfinity));
        double extra = Math.Max(0, detailText.DesiredSize.Height - s.LineH);
        header.Width = s.Width - 2 * s.Pad - 8;
        header.Height = s.Tile + extra;
        header.Margin = new Thickness(0, Inset + s.Pad, 0, 0);
        actions.Width = s.Width - 2 * s.Pad;
        actions.Height = laterRow.Height = s.ButtonH;
        laterRow.Width = actions.Width;
        actions.BeginAnimation(MarginProperty, null); // a finished expand animation would otherwise hold its value
        actions.Margin = new Thickness(0, Inset + 2 * s.Pad + s.Tile + extra, 0, 0);
        return extra;
    }

    void StopRinging()
    {
        if (ringing == null) return;
        ringing.Stop();
        ringing = null;
    }

    void Dismiss()
    {
        EndCharging();
        open = false;
        StopRinging(); // closed by itself, e.g. the charger was plugged in
        glow.BeginAnimation(OpacityProperty, null);
        Conceal(compact);
        Conceal(header);
        Conceal(actions);
        Conceal(laterRow);
        Morph(SeedW, SeedH, SeedH / 2);
        Go(stage, OpacityProperty, 0, 200, Out, 80, null, null);
        int gen = stageGen;
        After(320, () =>
        {
            if (gen != stageGen) return; // taken over while closing: the new pill owns the island now
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
        if (timerOpen) RefreshTimer();
        if (shelf != null) shelf.Prune(); // files deleted or moved elsewhere leave the shelf and its count
        if (!mediaOpen && WantPill() != timerPeekShown) RestPeek(); // a timer started or ended, or the shelf filled or emptied
        if (timerPeekShown) FillPill();
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
        RestPeek();
    }

    // The resting peek: a pill while a timer runs (time left) or the shelf holds files (count), otherwise the thin line.
    void RestPeek()
    {
        bool pill = WantPill();
        if (pill)
        {
            FillPill();
            HangFromLid(TimerPeekW, TimerPeekH, TimerPeekH / 2);
            if (!timerPeekShown) Reveal(timerPeek, 100);
        }
        else
        {
            Morph(PeekW, PeekH, PeekH / 2, PeekTop);
            if (timerPeekShown) Conceal(timerPeek);
        }
        timerPeekShown = pill;
    }

    bool WantPill() { return (timer != null && timer.Active) || (shelf != null && shelf.Count > 0); }

    // The timer wins when both apply: it changes every second.
    void FillPill()
    {
        if (timer != null && timer.Active)
        {
            pillIcon.Data = CountdownTimer.Icon;
            pillIcon.Fill = new SolidColorBrush(Color.FromRgb(255, 179, 64));
            timerPeekText.Text = MediaSession.Clock(timer.Remaining(DateTime.UtcNow));
        }
        else
        {
            pillIcon.Data = ShelfIcon;
            pillIcon.Fill = new SolidColorBrush(ShelfAccent);
            timerPeekText.Text = shelf.Count + (shelf.Count == 1 ? " file" : " files");
        }
    }

    void OpenPlayer()
    {
        mediaOpen = true;
        if (timerPeekShown) { Conceal(timerPeek); timerPeekShown = false; }
        CaptureBackdrop();
        if (mediaNow != null) Fill(mediaNow);
        HangFromLid(MediaW, MediaH, MediaR);
        Reveal(mediaLayer, 120);
        RefreshPlayerVolume();
    }

    // Flush with the screen's top edge: the top corners sit above the screen, so the island hangs from the lid.
    void HangFromLid(double width, double height, double radius)
    {
        Morph(width, height + radius, radius, -Inset - radius);
    }

    void OpenMirror()
    {
        if (mirrorOn || !mediaOpen) return;
        mirrorOn = true;
        mirrorText.Text = "Starting camera…";
        mirrorText.Opacity = 0.8;
        Conceal(mediaLayer);
        HangFromLid(MirrorW, MirrorH, MirrorR);
        Reveal(mirrorLayer, 120);
        var mine = Camera.Start(Dispatcher, ShowFrame, message => { if (mirrorOn) mirrorText.Text = message; });
        camera = mine;
    }

    void ShowFrame(byte[] pixels, int width, int height)
    {
        if (!mirrorOn) return;
        if (mirrorBitmap == null || mirrorBitmap.PixelWidth != width || mirrorBitmap.PixelHeight != height)
        {
            mirrorBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            mirrorImage.Source = mirrorBitmap;
            mirrorText.Opacity = 0;
        }
        mirrorBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    }

    // Turns the camera off, then goes back to the player if the mouse is still there.
    void CloseMirror()
    {
        if (!mirrorOn) return;
        mirrorOn = false;
        camera.Stop();
        camera = null;
        Conceal(mirrorLayer);
        mirrorImage.Source = null;
        mirrorBitmap = null;
        if (IsMouseOver && mediaShown)
        {
            HangFromLid(MediaW, MediaH, MediaR);
            Reveal(mediaLayer, 120);
        }
        else ClosePlayer();
    }

    void ClosePlayer()
    {
        mediaOpen = false;
        if (timerOpen) { timerOpen = false; Conceal(timerLayer); }
        CloseShelf();
        Conceal(mediaLayer);
        RestPeek();
    }

    // ---- timer panel ----

    // hh : mm : ss wheels, then back / start-pause / reset.
    Grid BuildTimer()
    {
        var layer = Layer(TimerW - 2 * MediaPad, TimerH - 30, 16);
        var column = new StackPanel();
        var picker = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var wheel in new[] { hoursWheel, minutesWheel, secondsWheel })
        {
            if (picker.Children.Count > 0)
                picker.Children.Add(new TextBlock { Text = ":", FontSize = 24, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 4) });
            wheel.Stepped += () =>
            {
                IslandSound.Play(IslandSound.Tick);
                timer.Set(new TimeSpan(hoursWheel.Value, minutesWheel.Value, secondsWheel.Value));
                RefreshTimer();
            };
            picker.Children.Add(wheel);
        }
        column.Children.Add(picker);

        var clear = Color.FromArgb(0, 255, 255, 255);
        var dim = Color.FromArgb(40, 255, 255, 255);
        var row = new Grid { Height = 40 };
        var middle = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var startPause = RoundButton(timerPlayGlyph, 40, Color.FromArgb(235, 255, 255, 255), Colors.White, () =>
        {
            if (timer.Running) timer.Pause(DateTime.UtcNow); else timer.Start(DateTime.UtcNow);
            RefreshTimer();
        });
        middle.Children.Add(startPause);
        var reset = RoundButton(Glyph(StopIcon), 36, clear, dim, () => { timer.Reset(); RefreshTimer(); });
        reset.Margin = new Thickness(14, 0, 0, 0);
        middle.Children.Add(reset);
        row.Children.Add(middle);
        var back = RoundButton(Glyph(BackIcon), 34, clear, dim, BackToPlayer);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(back);
        column.Children.Add(row);

        layer.Children.Add(column);
        return layer;
    }

    // The wheels show the set length when stopped, and roll down with the countdown while active.
    void RefreshTimer()
    {
        var shown = timer.Remaining(DateTime.UtcNow);
        bool active = timer.Active;
        hoursWheel.Locked = minutesWheel.Locked = secondsWheel.Locked = active; // only a stopped timer can be changed
        hoursWheel.Set(shown.Hours, active);
        minutesWheel.Set(shown.Minutes, active);
        secondsWheel.Set(shown.Seconds, active);
        ShowPlay(timerPlayGlyph, !timer.Running);
    }

    static void ShowPlay(System.Windows.Shapes.Path glyph, bool play)
    {
        glyph.Data = play ? PlayIcon : PauseIcon;
        glyph.RenderTransform = play ? PlayNudge : Transform.Identity;
    }

    void OpenTimer()
    {
        if (timerOpen || !mediaOpen || timer == null) return;
        timerOpen = true;
        RefreshTimer();
        Conceal(mediaLayer);
        HangFromLid(TimerW, TimerH, TimerR);
        Reveal(timerLayer, 120);
    }

    void BackToPlayer()
    {
        if (!timerOpen) return;
        timerOpen = false;
        Conceal(timerLayer);
        HangFromLid(MediaW, MediaH, MediaR);
        Reveal(mediaLayer, 120);
    }

    // Makes way for a reminder; the peek slides back in once it's gone.
    void HidePlayer()
    {
        if (mirrorOn) { mirrorOn = false; camera.Stop(); camera = null; mirrorImage.Source = null; mirrorBitmap = null; Conceal(mirrorLayer); }
        if (timerOpen) { timerOpen = false; Conceal(timerLayer); }
        CloseShelf();
        if (timerPeekShown) { timerPeekShown = false; Conceal(timerPeek); }
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
        mediaTitle.MaxWidth = mediaArtist.MaxWidth = MediaW - 2 * MediaPad - 68 - 26 - 30; // art, app icon, Do not disturb
        if (m.Art != artBytes || art.Background == null)
        {
            artBytes = m.Art;
            var image = Decode(m.Art);
            art.Background = image == null ? (Brush)new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        mediaApp.Source = AppIcon.For(m.AppId ?? "");
        ShowPlay(playGlyph, !m.Playing);

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
        backdrop.Source = Snapshot(Left - BlurMargin, Top - BlurMargin, WinW + 2 * BlurMargin, WinH + 2 * BlurMargin, dpi);
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
        // The usual border is lit at the top and fades out at the bottom. Hanging from the lid, the top is off
        // screen, so an even border keeps the bottom curve visible; otherwise the shape looks shorter than it is.
        pill.BorderBrush = top < 0 ? LidBorder : TopLitBorder;
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
        PlaceDots();
    }
}
