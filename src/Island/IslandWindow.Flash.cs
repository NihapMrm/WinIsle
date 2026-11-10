using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

// Flashes: brief island messages with no buttons (charging, volume, brightness, Bluetooth, screenshots).
// A flash with the same key as the one showing updates it in place, so holding a volume key just moves the bar;
// each flash stays FlashMs after its last update. Also the camera / microphone in-use dots beside the island.
partial class IslandWindow
{
    const int FlashMs = 2200;
    const double LevelW = 120;

    readonly Border levelFill = new Border { Height = 6, Width = 0, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid levelBar = new Grid { Width = LevelW, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel inUseDots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
    readonly System.Windows.Shapes.Ellipse cameraDot = Dot(Color.FromRgb(48, 209, 88)), micDot = Dot(Color.FromRgb(255, 159, 10)); // as on a phone: green camera, orange mic
    bool flashing;
    DateTime flashUntil;
    DateTime adjustedAt;  // last change made from the pill itself
    int wheelSum;         // touchpads scroll in small steps; 120 is one notch

    const double DotSize = 3, DotGap = 3; // fits inside the 6 px peek with its 1 px border

    // A small dot with a soft glow in its own colour. The glow is static: animating it would redraw the whole
    // see-through island every frame.
    static System.Windows.Shapes.Ellipse Dot(Color color)
    {
        return new System.Windows.Shapes.Ellipse
        {
            Width = DotSize, Height = DotSize, Fill = new SolidColorBrush(color), Opacity = 0, Margin = new Thickness(0, 0, DotGap, 0), Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, ShadowDepth = 0, BlurRadius = 6, Opacity = 1 },
        };
    }

    // From the constructor: the level bar sits in the compact pill after the label; the dots sit outside the clip.
    // The bar is 6 px to look at but 18 px to grab. Click or drag it, or scroll anywhere on the pill.
    UIElement BuildLevelBar()
    {
        levelBar.Height = 18;
        levelBar.Background = Brushes.Transparent;
        levelBar.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
        levelFill.VerticalAlignment = VerticalAlignment.Center;
        levelBar.Children.Add(levelFill);
        levelBar.MouseLeftButtonDown += (s, e) =>
        {
            if (!Adjustable()) return;
            levelBar.CaptureMouse();
            Adjust(e.GetPosition(levelBar).X / LevelW);
            e.Handled = true;
        };
        levelBar.MouseMove += (s, e) => { if (levelBar.IsMouseCaptured) Adjust(e.GetPosition(levelBar).X / LevelW); };
        levelBar.MouseLeftButtonUp += (s, e) => levelBar.ReleaseMouseCapture();
        // On the window, so it only gets scrolls nothing else took (timer wheels and the shelf list take theirs).
        MouseWheel += (s, e) =>
        {
            if (!Adjustable())
            {
                if (VolumeScrollHere()) { e.Handled = true; Volume.Scroll(e.Delta); }
                return;
            }
            e.Handled = true;
            wheelSum += e.Delta;
            int notches = wheelSum / 120;
            wheelSum %= 120;
            if (notches != 0) Adjust((shown.Level ?? 0) + notches * 0.02); // 2% a notch, like the keys
        };
        return levelBar;
    }

    bool Adjustable() { return flashing && shown != null && shown.SetLevel != null; }

    // Moves the bar at once and asks Windows for the new level; Windows' own report follows and is ignored briefly.
    void Adjust(double level)
    {
        level = Math.Max(0, Math.Min(1, level));
        adjustedAt = DateTime.UtcNow;
        flashUntil = DateTime.UtcNow.AddMilliseconds(FlashMs);
        shown.Level = level;
        compactRight.Text = (int)Math.Round(level * 100) + "%";
        levelFill.BeginAnimation(WidthProperty, null);
        levelFill.Width = LevelW * level;
        shown.SetLevel(level);
    }

    UIElement BuildInUseDots()
    {
        inUseDots.Children.Add(cameraDot);
        inUseDots.Children.Add(micDot);
        return inUseDots;
    }

    internal void Flash(IslandNotice n)
    {
        if (n.Key == "volume" && n.Level.HasValue) ShowPlayerVolume(n.Level.Value, n.Time == "Muted"); // the player's bar follows every change
        if (shown != null && !flashing) return; // a reminder is on screen and matters more
        if (mirrorOn || timerOpen || shelfOpen || (mediaOpen && IsMouseOver)) return; // never pull the island from under the mouse
        flashUntil = DateTime.UtcNow.AddMilliseconds(FlashMs);
        if (charging) return; // let the charging animation finish; it's short
        if (flashing)
        {
            bool same = shown.Key == n.Key;
            // While dragging or scrolling, Windows' reports lag behind the bar; keep the pill's own value.
            if (same && (levelBar.IsMouseCaptured || DateTime.UtcNow - adjustedAt < TimeSpan.FromMilliseconds(500))) return;
            if (!same && n.Sound != null) n.Sound.Play();
            shown = n;
            FillCompact(n, same);
            return;
        }
        if (mediaShown) HidePlayer();
        inUseDots.Visibility = Visibility.Hidden; // they'd sit on the flash's text
        flashing = true;
        shown = n;
        open = false;
        confirmed = false;
        FillCompact(n, false);
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        CaptureBackdrop();
        if (n.Sound != null) n.Sound.Play();
        Jump(w, SeedW); Jump(h, SeedH); Jump(r, SeedH / 2); Jump(y, 0);
        Morph(CompactW, CompactH, CompactH / 2);
        Go(stage, OpacityProperty, 1, 140, Out, 0, null, null);
        Reveal(compact, 90);
        WaitOut();
    }

    void WaitOut()
    {
        After(200, () =>
        {
            if (!flashing) return;
            // Stays while the mouse is on an adjustable pill, so it can be scrolled or dragged.
            if (DateTime.UtcNow < flashUntil || (Adjustable() && IsMouseOver)) { WaitOut(); return; }
            flashing = false;
            Dismiss(); // the peek comes back on the next tick
        });
    }

    // update: same flash again, so the bar glides from where it is; otherwise it fills up from empty.
    void FillCompact(IslandNotice n, bool update)
    {
        var accent = new SolidColorBrush(n.Accent);
        compactLeft.Text = n.Level.HasValue ? "" : n.Title;
        compactRight.Text = n.Time ?? "";
        smallIcon.Data = n.Icon;
        smallIcon.Fill = accent;
        smallLogo.Source = n.Logo;
        smallLogo.Opacity = 1;
        smallIcon.Opacity = n.Logo == null ? 1 : 0;
        check.Opacity = 0;
        levelBar.Visibility = n.Level.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (!n.Level.HasValue) return;
        levelFill.Background = accent;
        double to = LevelW * Math.Max(0, Math.Min(1, n.Level.Value));
        levelFill.BeginAnimation(WidthProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(update ? 160 : 650)) { EasingFunction = Out, From = update ? (double?)null : 0 });
    }

    // Camera / microphone in use: small dots inside the island at its right end, with the apps in a tooltip.
    // In the thin peek only the part of a dot that fits shows. Hidden while a reminder or flash is showing.
    internal void ShowInUse(IList<string> camera, IList<string> microphone)
    {
        SetDot(cameraDot, camera.Count > 0);
        SetDot(micDot, microphone.Count > 0);
        inUseDots.Visibility = shown == null ? Visibility.Visible : Visibility.Hidden;
        var lines = new List<string>();
        if (camera.Count > 0) lines.Add("Camera: " + string.Join(", ", camera));
        if (microphone.Count > 0) lines.Add("Microphone: " + string.Join(", ", microphone));
        inUseDots.ToolTip = lines.Count == 0 ? null : string.Join("\n", lines);
        PlaceDots();
    }

    void SetDot(System.Windows.Shapes.Ellipse dot, bool on)
    {
        if (on == (dot.Visibility == Visibility.Visible)) return;
        if (on) { dot.Visibility = Visibility.Visible; Anim(dot, OpacityProperty, 1, 200, Out, 0); }
        else { dot.Visibility = Visibility.Collapsed; dot.Opacity = 0; }
    }

    // Inside the island's right end: centred on the visible strip of the peek or a pill (the island's edge
    // cuts off what doesn't fit), near the top corner of a big panel.
    void PlaceDots()
    {
        int shownDots = (cameraDot.Visibility == Visibility.Visible ? 1 : 0) + (micDot.Visibility == Visibility.Visible ? 1 : 0);
        double width = shownDots * (DotSize + DotGap) - DotGap;
        double visibleTop = Math.Max(0, Inset + y.X), bottom = Inset + y.X + h.X;
        double visible = bottom - visibleTop;
        double top = visible < 12 ? visibleTop + 1                      // thin peek: up against the screen's top edge, clear of the border
                   : visible < 44 ? (visibleTop + bottom - DotSize) / 2 // pill: centred
                   : visibleTop + 8;                                    // big panel: near the top corner
        inUseDots.Margin = new Thickness((WinW + w.X) / 2 - 16 - width, top, 0, 0);
    }
}
