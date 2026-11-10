using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

// Palette and widgets shared by dashboard windows: cards, pills, dropdowns, legends, tooltips, bars.
abstract class DashboardWindow : GlassWindow
{
    protected const double Gap = 12, CardPad = 16;

    protected static SolidColorBrush Rgb(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    protected static readonly Brush Ink = Rgb(240, 239, 236), Soft = Rgb(195, 194, 183), Muted = Rgb(160, 158, 151);
    protected static readonly Brush SeriesBlue = Rgb(57, 135, 229);
    protected static readonly Brush GridLine = Rgb(255, 255, 255, 18), BaseLine = Rgb(255, 255, 255, 56), Faint = Rgb(255, 255, 255, 16);
    protected static readonly Brush CardFill = Rgb(255, 255, 255, 12), CardLine = Rgb(255, 255, 255, 26), PopupFill = Rgb(38, 38, 42);
    protected static readonly Color SelectedFill = Color.FromArgb(40, 255, 255, 255), HoverFill = Color.FromArgb(22, 255, 255, 255), None = Color.FromArgb(0, 255, 255, 255);
    static readonly Style FocusRing = MakeFocusRing();

    protected const string TickGlyph = "\uE73E", CrossGlyph = "\uE711", ClockGlyph = "\uE916", ChevronGlyph = "\uE70D", CloseGlyph = "\uE8BB";

    protected static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    protected static TextBlock T(string text, double size, Brush color, FontWeight? weight = null)
    {
        return new TextBlock { Text = text, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeights.Normal };
    }

    protected static TextBlock Glyph(string glyph, double size, Brush color)
    {
        return new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    protected static TextBox Field()
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
    protected static void Clickable(Border b, string name, Action click)
    {
        b.Cursor = Cursors.Hand;
        b.Focusable = true;
        b.FocusVisualStyle = FocusRing;
        AutomationProperties.SetName(b, name);
        b.MouseLeftButtonDown += (s, e) => e.Handled = true; // don't start window drag
        b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
        b.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; click(); } };
    }

    protected static Border Card(UIElement child, double padding = CardPad)
    {
        return new Border { Child = child, Background = CardFill, BorderBrush = CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(padding) };
    }

    protected static Border Pill(string label, bool selected, Action click)
    {
        var fill = new SolidColorBrush(selected ? SelectedFill : None);
        var pill = new Border { Background = fill, BorderBrush = selected ? Brushes.Transparent : CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 7), Margin = new Thickness(0, 0, 6, 0), Child = T(label, 13, selected ? Ink : Soft, FontWeights.SemiBold) };
        Clickable(pill, label, click);
        pill.MouseEnter += (s, e) => Anim(fill, selected ? SelectedFill : HoverFill);
        pill.MouseLeave += (s, e) => Anim(fill, selected ? SelectedFill : None);
        return pill;
    }

    protected static void Tip(FrameworkElement target, string value, string detail)
    {
        var body = new StackPanel();
        body.Children.Add(T(value, 13, Ink, FontWeights.SemiBold));
        body.Children.Add(T(detail, 12, Soft));
        target.ToolTip = new ToolTip { Content = body, Background = PopupFill, BorderBrush = CardLine, Padding = new Thickness(10, 7, 10, 8) };
        ToolTipService.SetInitialShowDelay(target, 60);
        ToolTipService.SetBetweenShowDelay(target, 0);
    }

    protected static string Hm(DateTime t) { return t.ToString("h:mm tt", Inv); }
    protected static string Md(DateTime t) { return t.ToString("MMM d", Inv); }
    protected static string Minutes(double m) { return m < 60 ? Math.Round(m) + " min" : (int)(m / 60) + "h " + (int)Math.Round(m % 60) + "m"; }
    protected static string Percent(int part, int whole) { return whole == 0 ? "\u2014" : Math.Round(100.0 * part / whole) + "%"; }

    protected static FrameworkElement Labelled(string label, FrameworkElement field)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        var caption = T(label, 12, Muted);
        caption.Margin = new Thickness(2, 0, 0, 4);
        stack.Children.Add(caption);
        stack.Children.Add(field);
        return stack;
    }

    // WPF's ComboBox can't be themed for dark glass without re-templating it, so this is a small replacement.
    protected static Border Dropdown(double width, string[] labels, int selected, Action<int> pick, string name, out Action<int> setIndex)
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

    // ---- settings widgets (Control Center) ----
    // Each takes its value and a callback, and hands back a setter so the page can show changes made elsewhere.

    static readonly Brush SwitchOff = Rgb(255, 255, 255, 40), SwitchOn = Rgb(48, 209, 88);

    // An on/off toggle. Space or Enter flips it from the keyboard.
    protected static Border Switch(bool on, Action<bool> changed, string name, out Action<bool> set)
    {
        bool state = on;
        var knobMove = new TranslateTransform(on ? 18 : 0, 0);
        var knob = new System.Windows.Shapes.Ellipse { Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), RenderTransform = knobMove };
        var fill = new SolidColorBrush(((SolidColorBrush)(on ? SwitchOn : SwitchOff)).Color);
        var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Background = fill, Child = knob, VerticalAlignment = VerticalAlignment.Center };
        Action<bool> show = v =>
        {
            state = v;
            Anim(knobMove, TranslateTransform.XProperty, v ? 18 : 0, 180, Out);
            Anim(fill, ((SolidColorBrush)(v ? SwitchOn : SwitchOff)).Color);
            AutomationProperties.SetItemStatus(track, v ? "On" : "Off");
        };
        Clickable(track, name, () => { show(!state); changed(state); });
        set = v => { if (v != state) show(v); };
        AutomationProperties.SetItemStatus(track, on ? "On" : "Off");
        return track;
    }

    // value + step * direction, snapped to the step grid and kept in range (92 up by 5 is 95, not 97).
    internal static double StepValue(double value, double step, double min, double max, int direction)
    {
        double k = value / step;
        double next = direction > 0 ? Math.Floor(k + 1e-9) + 1 : Math.Ceiling(k - 1e-9) - 1;
        return Math.Max(min, Math.Min(max, Math.Round(next * step, 6)));
    }

    static string Show(double value, double step) { return value.ToString(step < 1 ? "0.0#" : "0", CultureInfo.CurrentCulture); }

    // − value unit +. Holding a button repeats; the value can also be typed (Enter or leaving the box applies it).
    protected static FrameworkElement Stepper(double value, double step, double min, double max, string unit, Action<double> changed, string name, out Action<double> set)
    {
        double current = value;
        var box = new TextBox { Width = 52, Height = 30, FontSize = 13, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Background = Faint, Foreground = Ink, CaretBrush = Ink, BorderBrush = CardLine, BorderThickness = new Thickness(1), Text = Show(value, step) };
        AutomationProperties.SetName(box, name);
        Action<double> apply = v =>
        {
            v = Math.Max(min, Math.Min(max, v));
            box.Text = Show(v, step);
            if (v == current) return;
            current = v;
            changed(v);
        };
        Action commit = () =>
        {
            double typed;
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out typed) || double.TryParse(box.Text, NumberStyles.Float, Inv, out typed)) apply(typed);
            else box.Text = Show(current, step);
        };
        box.LostKeyboardFocus += (s, e) => commit();
        box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { commit(); e.Handled = true; }
            else if (e.Key == Key.Up || e.Key == Key.Down) { apply(StepValue(current, step, min, max, e.Key == Key.Up ? 1 : -1)); e.Handled = true; }
        };
        Func<string, int, Border> button = (glyph, direction) =>
        {
            var wash = new SolidColorBrush(None);
            var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = wash, Child = Glyph(glyph, 11, Soft) };
            var repeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
            Action stepOnce = () => apply(StepValue(current, step, min, max, direction));
            repeat.Tick += (s, e) => { repeat.Interval = TimeSpan.FromMilliseconds(70); stepOnce(); };
            // Not Clickable: press-and-hold needs the button-down itself.
            b.Cursor = Cursors.Hand;
            b.Focusable = true;
            b.FocusVisualStyle = FocusRing;
            AutomationProperties.SetName(b, (direction > 0 ? "Increase " : "Decrease ") + name);
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; b.CaptureMouse(); stepOnce(); repeat.Interval = TimeSpan.FromMilliseconds(420); repeat.Start(); };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; repeat.Stop(); b.ReleaseMouseCapture(); };
            b.LostMouseCapture += (s, e) => repeat.Stop();
            b.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) stepOnce(); };
            b.MouseEnter += (s, e) => Anim(wash, HoverFill);
            b.MouseLeave += (s, e) => Anim(wash, None);
            return b;
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(button("", -1)); // minus
        row.Children.Add(box);
        var unitText = T(unit, 12.5, Soft);
        unitText.VerticalAlignment = VerticalAlignment.Center;
        unitText.Margin = new Thickness(6, 0, 4, 0);
        unitText.MinWidth = 26;
        row.Children.Add(unitText);
        row.Children.Add(button("", 1)); // plus
        set = v => { current = v; if (!box.IsKeyboardFocused) box.Text = Show(v, step); };
        return row;
    }

    // A text setting: applied on Enter or when the box loses focus.
    protected static TextBox TextSetting(string value, double width, Action<string> changed, string name, out Action<string> set)
    {
        var box = Field();
        box.Width = width;
        box.Text = value;
        AutomationProperties.SetName(box, name);
        string current = value;
        Action commit = () => { if (box.Text != current) { current = box.Text; changed(current); } };
        box.LostKeyboardFocus += (s, e) => commit();
        box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { commit(); e.Handled = true; } };
        set = v => { current = v; if (!box.IsKeyboardFocused) box.Text = v; };
        return box;
    }

    // One settings line: label and hint on the left, the control on the right. Hint may be null.
    protected static Grid Row(string label, string hint, UIElement control, out TextBlock hintText)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        words.Children.Add(T(label, 13.5, Ink));
        hintText = T(hint ?? "", 12, Muted);
        hintText.TextWrapping = TextWrapping.Wrap;
        hintText.Visibility = hint == null ? Visibility.Collapsed : Visibility.Visible;
        words.Children.Add(hintText);
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6), MinHeight = 34 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(words);
        var c = control as FrameworkElement;
        if (c != null) c.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    protected static Border Tile(string label, string value, string note, double size)
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

    protected static FrameworkElement CardHeader(string title, string detail, UIElement right = null)
    {
        var left = new StackPanel();
        left.Children.Add(T(title, 14, Ink, FontWeights.SemiBold));
        left.Children.Add(T(detail, 12, Muted));
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.Children.Add(left);
        if (right != null) grid.Children.Add(right);
        return grid;
    }

    protected static StackPanel Legend(params object[] swatchThenLabel)
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

    protected static Border Swatch(Brush fill, Brush line = null)
    {
        return new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = fill, BorderBrush = line, BorderThickness = new Thickness(line == null ? 0 : 1.5) };
    }

    protected static void Place2(Canvas canvas, UIElement child, double x, double y)
    {
        Canvas.SetLeft(child, x);
        Canvas.SetTop(child, y);
        canvas.Children.Add(child);
    }

    // One blue series: length answers the question, so hue doesn't also need to.
    protected static FrameworkElement HBars(string[] labels, double?[] values, double max, Func<double, string> format, string[] details, double w, bool animate)
    {
        const double rowH = 31, labelW = 58, tipW = 56;
        var canvas = new Canvas { Width = w, Height = rowH * labels.Length + 4 };
        double scaleX = (w - labelW - tipW) / Math.Max(max, 0.001);
        Place2(canvas, new Border { Width = 1, Height = rowH * labels.Length, Background = BaseLine, SnapsToDevicePixels = true }, labelW, 2);
        for (int i = 0; i < labels.Length; i++)
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
            var bar = new Border { Width = barW, Height = 14, Background = SeriesBlue, CornerRadius = new CornerRadius(0, 4, 4, 0), RenderTransform = scale, RenderTransformOrigin = new Point(0, 0.5), IsHitTestVisible = false };
            Place2(canvas, bar, labelW + 1, y + 8.5);
            if (animate) Go(scale, ScaleTransform.ScaleXProperty, 1, 460, Out, i * 50, null, 0);
            var value = T(format(values[i].Value), 12.5, Ink, FontWeights.SemiBold);
            value.IsHitTestVisible = false;
            Place2(canvas, value, labelW + barW + 7, y + 7);
        }
        return canvas;
    }
}
