using System;
using System.Globalization;
using System.Windows.Media;

// Asks every 90 minutes (Control Center) whether you have had a drink.
class WaterReminder : IReminder
{
    public static TimeSpan Every { get { return TimeSpan.FromMinutes(Settings.Num(Settings.WaterEvery)); } }
    public static TimeSpan Snooze { get { return TimeSpan.FromMinutes(Settings.Num(Settings.WaterSnooze)); } }

    static readonly Color Accent = Color.FromRgb(120, 200, 255);
    static readonly Geometry DropShape = Geometry.Parse("M12,2 C12,2 5,10 5,15 A7,7 0 0 0 19,15 C19,10 12,2 12,2 Z");

    readonly TimeSpan? fixedEvery, fixedSnooze; // --demo-water only; the live reminder reads the settings each time
    internal DateTime From; // UTC: the last answer (or start), so a changed interval applies to the wait in progress
    bool snoozed;           // the last answer was "Not yet"

    public WaterReminder() { From = DateTime.UtcNow; }

    public WaterReminder(TimeSpan every, TimeSpan snooze, TimeSpan firstIn)
    {
        fixedEvery = every;
        fixedSnooze = snooze;
        From = DateTime.UtcNow + firstIn - every;
    }

    TimeSpan every { get { return fixedEvery ?? Every; } }
    TimeSpan snooze { get { return fixedSnooze ?? Snooze; } }

    public IslandNotice Due()
    {
        if (fixedEvery == null && !Settings.On(Settings.WaterOn)) return null;
        if (DateTime.UtcNow < From + (snoozed ? snooze : every)) return null;
        return Sample();
    }

    // What Due shows; also the Control Center's preview.
    internal static IslandNotice Sample()
    {
        return new IslandNotice
        {
            Key = "water", Title = "Drink water", Detail = "Have a glass of water", YesLabel = "Drank",
            Time = Prayers.Now.ToString("h:mm tt", CultureInfo.InvariantCulture), Icon = DropShape, Accent = Accent, Sound = IslandSound.Water,
        };
    }

    public bool StillCurrent(IslandNotice shown) { return true; }

    public IslandResult Answer(IslandNotice shown, bool yes)
    {
        From = DateTime.UtcNow;
        snoozed = !yes;
        return yes ? new IslandResult { Left = "Drank", Right = "Next in " + IslandResult.Span(every) } : IslandResult.NotYet(snooze);
    }
}
