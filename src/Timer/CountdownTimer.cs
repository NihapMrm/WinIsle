using System;
using System.Globalization;
using System.Windows.Media;

// The island's countdown timer. Set and started from the timer panel; rings through the island at zero.
class CountdownTimer : IReminder
{
    internal static readonly TimeSpan Longest = new TimeSpan(23, 59, 59), Snooze = TimeSpan.FromMinutes(1); // the picker's hh:mm:ss range

    internal static readonly Geometry Icon = Geometry.Parse("F0 M9,1.5 H15 V3.5 H9 Z M12,4.5 A8.5,8.5 0 1 1 11.99,4.5 Z M12,6.5 A6.5,6.5 0 1 1 11.99,6.5 Z M11.2,8 H12.8 V13.5 H11.2 Z");
    static readonly Color Accent = Color.FromRgb(255, 179, 64);

    TimeSpan length = TimeSpan.FromMinutes(5);
    DateTime? endsAt;  // UTC, while running
    TimeSpan? left;    // while paused

    internal TimeSpan Length { get { return length; } }
    internal bool Running { get { return endsAt != null; } }
    internal bool Active { get { return endsAt != null || left != null; } } // running or paused

    internal TimeSpan Remaining(DateTime utcNow)
    {
        if (endsAt != null) return endsAt.Value > utcNow ? endsAt.Value - utcNow : TimeSpan.Zero;
        return left ?? length;
    }

    internal void Start(DateTime utcNow)
    {
        var run = left ?? length;
        if (run <= TimeSpan.Zero) return; // 00:00:00 has nothing to count
        endsAt = utcNow + run;
        left = null;
    }
    internal void Pause(DateTime utcNow) { left = Remaining(utcNow); endsAt = null; }
    internal void Reset() { endsAt = null; left = null; }

    // Only while stopped: a running timer keeps the length it was started with.
    internal void Set(TimeSpan to)
    {
        if (Active) return;
        length = to < TimeSpan.Zero ? TimeSpan.Zero : to > Longest ? Longest : to;
    }

    public IslandNotice Due()
    {
        if (endsAt == null || DateTime.UtcNow < endsAt.Value) return null;
        return new IslandNotice
        {
            Key = "timer", Title = "Time's up", Detail = MediaSession.Clock(length) + " timer",
            Time = Prayers.Now.ToString("h:mm tt", CultureInfo.InvariantCulture), YesLabel = "+1 min", NoLabel = "Stop",
            Icon = Icon, Accent = Accent, Sound = IslandSound.Alarm, LoopSound = true, Sticky = true,
        };
    }

    public bool StillCurrent(IslandNotice shown) { return true; } // rings until answered

    public IslandResult Answer(IslandNotice shown, bool yes)
    {
        if (yes)
        {
            endsAt = DateTime.UtcNow + Snooze;
            return new IslandResult { Left = "+1 min", Right = "Timer running" };
        }
        Reset();
        return new IslandResult { Left = "Timer", Right = "Stopped" };
    }
}
