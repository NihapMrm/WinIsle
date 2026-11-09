using System;
using System.Globalization;
using System.Windows.Media;

// Asks every 90 minutes whether you have had a drink.
class WaterReminder : IReminder
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(90), Snooze = TimeSpan.FromMinutes(10);

    static readonly Color Accent = Color.FromRgb(120, 200, 255);
    static readonly Geometry DropShape = Geometry.Parse("M12,2 C12,2 5,10 5,15 A7,7 0 0 0 19,15 C19,10 12,2 12,2 Z");

    readonly TimeSpan every, snooze;
    DateTime due; // UTC, so changing the prayer time zone can't shift it

    public WaterReminder() : this(Every, Snooze, Every) { }

    public WaterReminder(TimeSpan every, TimeSpan snooze, TimeSpan firstIn)
    {
        this.every = every;
        this.snooze = snooze;
        due = DateTime.UtcNow + firstIn;
    }

    public IslandNotice Due()
    {
        if (DateTime.UtcNow < due) return null;
        return new IslandNotice
        {
            Key = "water", Title = "Drink water", Detail = "Have a glass of water", YesLabel = "Drank",
            Time = Prayers.Now.ToString("h:mm tt", CultureInfo.InvariantCulture), Icon = DropShape, Accent = Accent,
        };
    }

    public bool StillCurrent(IslandNotice shown) { return true; }

    public IslandResult Answer(IslandNotice shown, bool yes)
    {
        due = DateTime.UtcNow + (yes ? every : snooze);
        return yes ? new IslandResult { Left = "Drank", Right = "Next in " + IslandResult.Span(every) } : IslandResult.NotYet(snooze);
    }
}
