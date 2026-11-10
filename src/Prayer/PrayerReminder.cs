using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

// Shows the current prayer until it is answered or its time ends.
class PrayerReminder : IReminder
{
    static readonly Color Gold = Color.FromRgb(255, 214, 140);
    static readonly Geometry MoonShape = BuildMoon();

    readonly Func<Prayer> current;
    readonly Func<string, bool> isDone;
    readonly Action<string> markDone;
    readonly Func<TimeSpan> snooze; // live: the Control Center's snooze, read at each "Not yet"
    readonly bool live;             // the live reminder can be switched off in the Control Center
    string snoozedKey;
    DateTime snoozedUntil;

    PrayerReminder(Func<Prayer> current, Func<string, bool> isDone, Action<string> markDone, Func<TimeSpan> snooze, bool live)
    {
        this.live = live;
        this.current = current;
        this.isDone = isDone;
        this.markDone = markDone;
        this.snooze = snooze;
    }

    public static PrayerReminder Live()
    {
        return new PrayerReminder(() => Prayers.Current(Prayers.Now, Prayers.TzAt(Prayers.Now)), Tracker.Has, key => Tracker.Mark(key, Prayers.Now), () => TimeSpan.FromMinutes(Settings.Num(Settings.PrayerSnooze)), true);
    }

    // A fixed prayer that never touches the log, for --demo.
    public static PrayerReminder Demo()
    {
        var asr = new Prayer { Name = "Asr", At = DateTime.Now };
        string done = null;
        return new PrayerReminder(() => asr, key => key == done, key => done = key, () => TimeSpan.FromSeconds(10), false);
    }

    public IslandNotice Due()
    {
        if (live && !Settings.On(Settings.PrayerOn)) return null; // tracking and Missed rules carry on regardless
        var p = current();
        if (p == null || isDone(p.Key)) return null;
        if (p.Key == snoozedKey && Prayers.Now < snoozedUntil) return null;
        return new IslandNotice
        {
            Key = p.Key, Title = p.Name, Detail = "It's time to pray", YesLabel = "Prayed",
            Time = p.At.ToString("h:mm tt", CultureInfo.InvariantCulture), Icon = MoonShape, Accent = Gold,
        };
    }

    // Its time ended, or it was marked prayed from the dashboard. The next tick shows whatever is due.
    public bool StillCurrent(IslandNotice shown)
    {
        var p = current();
        return p != null && p.Key == shown.Key && !isDone(p.Key);
    }

    public IslandResult Answer(IslandNotice shown, bool yes)
    {
        if (!yes)
        {
            snoozedKey = shown.Key;
            snoozedUntil = Prayers.Now + snooze();
            return IslandResult.NotYet(snooze());
        }
        markDone(shown.Key);
        return new IslandResult { Left = "Prayed", Right = shown.Title };
    }

    static Geometry BuildMoon()
    {
        var crescent = Geometry.Combine(new EllipseGeometry(new Point(12, 12), 10, 10), new EllipseGeometry(new Point(16.2, 9.6), 8.3, 8.3), GeometryCombineMode.Exclude, null);
        var shape = new GeometryGroup();
        shape.Children.Add(crescent);
        shape.Children.Add(Geometry.Parse("M18.6,6.2 L19.5,8.5 L21.8,9.4 L19.5,10.3 L18.6,12.6 L17.7,10.3 L15.4,9.4 L17.7,8.5 Z"));
        return shape;
    }

    internal static System.Windows.Shapes.Path Moon(double size)
    {
        return new System.Windows.Shapes.Path { Data = MoonShape, Width = size, Height = size, Stretch = Stretch.Uniform, Fill = new SolidColorBrush(Gold) };
    }
}
