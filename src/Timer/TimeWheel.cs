using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

// One rolling number column of the timer picker (hours, minutes or seconds).
// Scroll it, or click a faded neighbour, to step. Numbers roll in with a motion blur and a small overshoot.
class TimeWheel : Grid
{
    const double RowH = 28, ColumnW = 56;
    static readonly IEasingFunction Settle = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };

    readonly int count; // 24 for hours, 60 for minutes and seconds
    readonly TextBlock[] rows = new TextBlock[7]; // centre is rows[3]; the outer two roll in and out of view
    readonly TranslateTransform roll = new TranslateTransform();
    readonly BlurEffect blur = new BlurEffect { Radius = 0 };
    int value, wheel;

    internal event Action Stepped; // the user changed the value
    internal bool Locked;          // while a timer runs the wheels only show the countdown

    internal TimeWheel(int count)
    {
        this.count = count;
        Width = ColumnW;
        Height = 5 * RowH;
        ClipToBounds = true;
        Background = Brushes.Transparent; // the whole column takes the wheel, not only the digits
        var stack = new StackPanel { RenderTransform = roll, Effect = blur, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -RowH, 0, 0) };
        for (int i = 0; i < rows.Length; i++)
        {
            int offset = i - 3, distance = Math.Abs(offset);
            var row = new TextBlock
            {
                Height = RowH, Width = ColumnW, TextAlignment = TextAlignment.Center,
                LineHeight = RowH, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                FontSize = distance == 0 ? 26 : distance == 1 ? 16 : 13,
                FontWeight = distance == 0 ? FontWeights.Medium : FontWeights.Normal,
                Opacity = distance == 0 ? 1 : distance == 1 ? 0.5 : distance == 2 ? 0.22 : 0,
                Background = Brushes.Transparent,
            };
            if (distance == 1 || distance == 2)
            {
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonUp += (s, e) => Step(offset);
            }
            rows[i] = row;
            stack.Children.Add(row);
        }
        Children.Add(stack);
        // A mouse notch is 120; touchpads send small amounts that add up to the same.
        MouseWheel += (s, e) =>
        {
            e.Handled = true;
            if (Locked) return;
            wheel += e.Delta;
            for (; wheel >= 120; wheel -= 120) Step(-1); // scrolling up brings the smaller numbers above into the centre
            for (; wheel <= -120; wheel += 120) Step(1);
        };
        Show();
    }

    internal int Value { get { return value; } }

    internal static int Wrap(int v, int count) { return ((v % count) + count) % count; }

    // +1 when new is just after old (numbers roll up), -1 when just before, going the short way round 59/00.
    internal static int Direction(int from, int to, int count)
    {
        int ahead = Wrap(to - from, count);
        return ahead == 0 ? 0 : ahead <= count / 2 ? 1 : -1;
    }

    // Programmatic change, e.g. the countdown; rolls only when asked.
    internal void Set(int to, bool animate)
    {
        to = Wrap(to, count);
        int direction = Direction(value, to, count);
        value = to;
        Show();
        if (animate && direction != 0) Roll(direction);
    }

    void Step(int by)
    {
        if (Locked) return;
        value = Wrap(value + by, count);
        Show();
        Roll(Math.Sign(by));
        if (Stepped != null) Stepped();
    }

    void Show()
    {
        for (int i = 0; i < rows.Length; i++) rows[i].Text = Wrap(value + i - 3, count).ToString("00");
    }

    // Starts one row away (showing the old number in the centre), then springs into place while the blur clears.
    void Roll(int direction)
    {
        roll.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(direction * RowH, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = Settle });
        blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(220)));
    }
}
