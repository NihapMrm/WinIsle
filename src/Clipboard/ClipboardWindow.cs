using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

// Clipboard history: Ctrl+Alt+V, or the clipboard button on the island. Hangs from the top edge like the island.
// Type to search, arrows or the mouse to choose, Enter or a click to paste into the app you were in.
// The panel never takes the focus, so the text cursor stays where it was in your app and the paste lands there;
// its keys are read while it is open (ClipboardWindow.Keys.cs).
partial class ClipboardWindow : GlassWindow
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    const double PanelW = 440, WinH = 620, Radius = 26, BlurMargin = 40, ListMax = 390;
    static readonly Brush LidBorder = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    static readonly Color PinColor = Color.FromRgb(255, 179, 64);
    static readonly Color Chosen = Color.FromArgb(40, 255, 255, 255);

    class Row { public ClipItem Item; public Border View; public SolidColorBrush Fill; public UIElement Actions; }

    readonly ClipboardHistory history;
    readonly Grid root = new Grid { Width = PanelW, VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
    readonly Border panel = new Border();
    readonly Image backdrop = new Image { Stretch = Stretch.Fill, Width = PanelW + 2 * BlurMargin, Height = WinH + 2 * BlurMargin, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-BlurMargin, -BlurMargin, 0, 0), Effect = new BlurEffect { Radius = 36, RenderingBias = RenderingBias.Performance } };
    readonly RectangleGeometry clip = new RectangleGeometry { RadiusX = Radius, RadiusY = Radius };
    readonly TextBlock queryText = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock placeholder = new TextBlock { Text = "Type to search", Opacity = 0.42, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
    readonly Border caret = new Border { Width = 1.5, Height = 17, Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0) };
    string query = "";
    readonly TextBlock count = new TextBlock { FontSize = 11.5, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
    readonly TextBlock empty = new TextBlock { FontSize = 13, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 26) };
    readonly StackPanel list = new StackPanel();
    readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = ListMax, Margin = new Thickness(0, 10, 0, 8) };
    readonly TranslateTransform slide = new TranslateTransform();
    readonly ScaleTransform grow = new ScaleTransform(1, 1);
    readonly List<Row> rows = new List<Row>();
    int selected;
    double dpi = 1;
    bool open;

    internal ClipboardWindow(ClipboardHistory history)
    {
        this.history = history;
        Title = "Clipboard";
        Width = PanelW; Height = WinH;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = new SolidColorBrush(Text);

        // Glass: the blurred screen, clipped to the panel. The top corners sit above the screen edge.
        var glass = new Grid { Clip = clip };
        glass.Children.Add(backdrop);
        panel.Background = new LinearGradientBrush(Color.FromArgb(170, 38, 38, 42), Color.FromArgb(155, 12, 12, 14), 90);
        panel.BorderBrush = LidBorder;
        panel.BorderThickness = new Thickness(1, 0, 1, 1);
        panel.CornerRadius = new CornerRadius(0, 0, Radius, Radius);
        panel.SizeChanged += (s, e) => clip.Rect = new Rect(0, -Radius, PanelW, e.NewSize.Height + Radius);

        var content = new StackPanel { Margin = new Thickness(14, 14, 14, 12) };
        content.Children.Add(BuildSearch());
        var listHost = new Grid();
        listHost.Children.Add(empty);
        listHost.Children.Add(list);
        scroll.Content = listHost;
        content.Children.Add(scroll);
        content.Children.Add(BuildHints());
        panel.Child = content;

        root.Children.Add(glass);
        root.Children.Add(panel);
        var motion = new TransformGroup();
        motion.Children.Add(grow);
        motion.Children.Add(slide);
        root.RenderTransform = motion;
        root.RenderTransformOrigin = new Point(0.5, 0);
        Content = root;

        history.Changed += () => { if (open) Rebuild(false); };
        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x80 | 0x08000000); // TOOLWINDOW (not in Alt+Tab) | NOACTIVATE (never takes the focus)
            dpi = HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice.M11;
        };
        new WindowInteropHelper(this).EnsureHandle(); // dpi is needed for the first snapshot
    }

    UIElement BuildSearch()
    {
        var box = new Border { Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 8) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new TextBlock { Text = "\uE721", FontFamily = Icons, FontSize = 13, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        // Shows what has been typed, with a blinking caret; the keys come from ClipboardWindow.Keys.cs.
        var input = new StackPanel { Orientation = Orientation.Horizontal };
        input.Children.Add(queryText);
        input.Children.Add(caret);
        input.Children.Add(placeholder);
        Grid.SetColumn(input, 1);
        Grid.SetColumn(count, 2);
        row.Children.Add(icon);
        row.Children.Add(input);
        row.Children.Add(count);
        box.Child = row;
        return box;
    }

    static UIElement BuildHints()
    {
        var hints = new TextBlock { FontSize = 11, Opacity = 0.45, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        string[] pairs = { "\u2191\u2193", "choose", "\u21B5", "paste", "Ctrl+P", "pin", "Del", "remove", "Esc", "close" };
        for (int i = 0; i < pairs.Length; i += 2)
        {
            if (i > 0) hints.Inlines.Add(new Run("     "));
            hints.Inlines.Add(new Run(pairs[i]) { FontWeight = FontWeights.SemiBold });
            hints.Inlines.Add(new Run(" " + pairs[i + 1]));
        }
        return hints;
    }

    // ---- open / close ----

    internal void Toggle()
    {
        if (open) Dismiss(); else Open();
    }

    void Open()
    {
        open = true;
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - PanelW) / 2;
        Top = area.Top;
        backdrop.Source = Snapshot(Left - BlurMargin, Top - BlurMargin, PanelW + 2 * BlurMargin, WinH + 2 * BlurMargin, dpi);
        SetQuery("");
        selected = 0;
        Rebuild(true);
        Show(); // without taking the focus: the app underneath keeps its text cursor
        StartListening();
        caret.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(530)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        // Drops out of the top edge with a little spring.
        Anim(root, OpacityProperty, 1, 160, Out, 0);
        Anim(slide, TranslateTransform.YProperty, 0, 460, Spring, -28);
        Anim(grow, ScaleTransform.ScaleYProperty, 1, 460, Spring, 0.9);
        Anim(grow, ScaleTransform.ScaleXProperty, 1, 460, Spring, 0.96);
    }

    void Dismiss()
    {
        if (!open) return;
        open = false;
        StopListening();
        caret.BeginAnimation(OpacityProperty, null);
        Anim(root, OpacityProperty, 0, 130, Out);
        Anim(slide, TranslateTransform.YProperty, -14, 130, Out);
        After(140, () => { if (!open) Hide(); });
    }

    // The app never lost the focus, so the paste goes straight into it, at its text cursor.
    void Paste(ClipItem item)
    {
        open = false;
        StopListening();
        caret.BeginAnimation(OpacityProperty, null);
        root.Opacity = 0;
        Hide();
        history.Paste(item, GetForegroundWindow());
    }

    void SetQuery(string text)
    {
        query = text;
        queryText.Text = text;
        placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        selected = 0;
        if (open) Rebuild(false);
    }

    // ---- list ----

    void Rebuild(bool stagger)
    {
        var keep = selected >= 0 && selected < rows.Count ? rows[selected].Item : null;
        var all = history.Items;
        var visible = all.Where(i => ClipboardHistory.Matches(i, query)).ToList();
        list.Children.Clear();
        rows.Clear();
        for (int i = 0; i < visible.Count; i++)
        {
            var row = MakeRow(visible[i], i);
            rows.Add(row);
            list.Children.Add(row.View);
            if (stagger && i < 12)
            {
                var move = new TranslateTransform();
                row.View.RenderTransform = move;
                row.View.Opacity = 0;
                Go(row.View, OpacityProperty, 1, 220, Out, 60 + i * 22, null, 0);
                Go(move, TranslateTransform.YProperty, 0, 320, Spring, 60 + i * 22, null, -8);
            }
        }
        count.Text = query.Trim().Length == 0 ? all.Count + (all.Count == 1 ? " item" : " items") : visible.Count + " of " + all.Count;
        empty.Text = all.Count == 0 ? "Copy something and it shows up here" : "No matches";
        empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int index = keep == null ? -1 : visible.IndexOf(keep);
        Select(index >= 0 ? index : Math.Min(selected, visible.Count - 1), false);
    }

    void Select(int index, bool scrollTo = true)
    {
        if (rows.Count == 0) { selected = 0; return; }
        index = Math.Max(0, Math.Min(rows.Count - 1, index));
        if (selected >= 0 && selected < rows.Count && selected != index)
        {
            Anim(rows[selected].Fill, Colors.Transparent);
            Anim((FrameworkElement)rows[selected].Actions, OpacityProperty, 0, 120, Out);
        }
        selected = index;
        Anim(rows[index].Fill, Chosen);
        Anim((FrameworkElement)rows[index].Actions, OpacityProperty, 1, 160, Out);
        if (scrollTo) rows[index].View.BringIntoView();
    }

    Row MakeRow(ClipItem item, int index)
    {
        var fill = new SolidColorBrush(Colors.Transparent);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        if (item.Image != null)
        {
            double aspect = item.Image.PixelWidth / (double)Math.Max(1, item.Image.PixelHeight);
            middle.Children.Add(new Border
            {
                Height = 58, Width = Math.Max(58, Math.Min(240, 58 * aspect)), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left,
                Background = new ImageBrush(item.Image) { Stretch = Stretch.UniformToFill },
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
            });
        }
        else if (item.Files != null)
        {
            middle.Children.Add(FileStrip(item));
            middle.Children.Add(new TextBlock { Text = Preview(item), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) });
        }
        else
        {
            middle.Children.Add(new TextBlock
            {
                Text = Preview(item), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 38,
                Foreground = item.IsLink ? new SolidColorBrush(Color.FromRgb(120, 185, 255)) : Foreground,
            });
        }
        middle.Children.Add(new TextBlock { Text = Describe(item) + "  \u00B7  " + ClipboardHistory.Ago(DateTime.UtcNow - item.Copied), FontSize = 11, Opacity = 0.5, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(middle, 1);

        var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        badge.Child = new TextBlock { Text = Glyph(item), FontFamily = Icons, FontSize = 14, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (item.Pinned) right.Children.Add(new TextBlock { Text = "\uE718", FontFamily = Icons, FontSize = 12, Foreground = new SolidColorBrush(PinColor), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0 };
        actions.Children.Add(IconButton(item.Pinned ? "\uE77A" : "\uE718", item.Pinned ? "Unpin (Ctrl+P)" : "Pin (Ctrl+P)", () => history.TogglePin(item)));
        actions.Children.Add(IconButton("\uE74D", "Remove (Del)", () => history.Remove(item)));
        right.Children.Add(actions);
        Grid.SetColumn(right, 2);

        grid.Children.Add(badge);
        grid.Children.Add(middle);
        grid.Children.Add(right);

        var view = new Border { Child = grid, Background = fill, CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 7, 6, 7), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand };
        view.MouseMove += (s, e) => { if (selected != index) Select(index, false); }; // only real mouse moves, not the list scrolling under it
        view.MouseLeftButtonUp += (s, e) => Paste(item);
        return new Row { Item = item, View = view, Fill = fill, Actions = actions };
    }

    // One file: a large preview shaped like the picture. Several: square tiles, then "+N".
    static UIElement FileStrip(ClipItem item)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        var thumbs = item.Thumbs ?? new ImageSource[0];
        if (item.Files.Length == 1 && thumbs.Length == 1 && thumbs[0] != null)
        {
            var only = thumbs[0];
            double aspect = only.Width / Math.Max(1, only.Height);
            strip.Children.Add(Tile(only, Math.Max(58, Math.Min(240, 58 * aspect)), 58, Stretch.UniformToFill));
            return strip;
        }
        foreach (var thumb in thumbs) strip.Children.Add(Tile(thumb, 52, 52, Stretch.Uniform));
        int more = item.Files.Length - thumbs.Length;
        if (more > 0)
            strip.Children.Add(new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                Child = new TextBlock { Text = "+" + more, FontSize = 14, FontWeight = FontWeights.SemiBold, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        return strip;
    }

    // A rounded preview tile; a missing file shows a plain document glyph.
    internal static Border Tile(ImageSource image, double width, double height, Stretch stretch) // also the shelf's tiles
    {
        var tile = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
        if (image == null)
            tile.Child = new TextBlock { Text = "\uE8A5", FontFamily = Icons, FontSize = 18, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        else if (stretch == Stretch.UniformToFill)
            tile.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill }; // fills the rounded corners
        else
            tile.Child = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(5) }; // icons keep their shape
        return tile;
    }

    static Border IconButton(string glyph, string tip, Action click)
    {
        var fill = new SolidColorBrush(Colors.Transparent);
        var b = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = fill, Cursor = Cursors.Hand, ToolTip = tip };
        b.Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        b.MouseEnter += (s, e) => Anim(fill, Color.FromArgb(40, 255, 255, 255));
        b.MouseLeave += (s, e) => Anim(fill, Colors.Transparent);
        b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); }; // not a paste
        return b;
    }

    static string Glyph(ClipItem item)
    {
        if (item.Image != null) return "\uE91B";             // photo
        if (item.Files != null) return item.Files.Length > 1 ? "\uE8B7" : "\uE8A5"; // folder / document
        return item.IsLink ? "\uE71B" : "\uE8C8";           // link / copy
    }

    // Whitespace folded so a multi-line copy still previews as two readable lines.
    static string Preview(ClipItem item)
    {
        if (item.Files != null)
        {
            var names = item.Files.Select(Path.GetFileName).ToList();
            return names.Count <= 2 ? string.Join(", ", names) : names[0] + ", " + names[1] + " +" + (names.Count - 2);
        }
        var text = Regex.Replace(item.Text.Length > 400 ? item.Text.Substring(0, 400) : item.Text, @"\s+", " ").Trim();
        return text;
    }

    static string Describe(ClipItem item)
    {
        if (item.Image != null) return "Image  " + item.Image.PixelWidth + " \u00D7 " + item.Image.PixelHeight;
        if (item.Files != null) return item.Files.Length == 1 ? "File" : item.Files.Length + " files";
        if (item.IsLink) return "Link";
        int lines = item.Text.Split('\n').Length;
        return lines > 1 ? "Text  " + lines + " lines" : "Text  " + item.Text.Length + " chars";
    }

}
