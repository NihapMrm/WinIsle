using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;

// Asks to plug in at 15% and to unplug at 95%, again every minute, until the charger is changed. All three
// are Control Center settings.
class BatteryReminder : IReminder
{
    public static int Low { get { return (int)Settings.Num(Settings.BatteryLow); } }
    public static int Full { get { return (int)Settings.Num(Settings.BatteryFull); } }
    public static TimeSpan Again { get { return TimeSpan.FromMinutes(Settings.Num(Settings.BatteryAgain)); } }

    internal struct Power { public int Percent; public bool Charging; }

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    internal static readonly Geometry BatteryShape =Geometry.Parse("M4,7 H17 A2,2 0 0 1 19,9 V15 A2,2 0 0 1 17,17 H4 A2,2 0 0 1 2,15 V9 A2,2 0 0 1 4,7 Z M20,10 H21 A1,1 0 0 1 22,11 V13 A1,1 0 0 1 21,14 H20 Z");
    static readonly Color LowAccent = Color.FromRgb(217, 89, 38), FullAccent = Color.FromRgb(52, 199, 89);

    readonly Func<Power?> read;
    DateTime snoozedUntil; // UTC; cleared whenever the battery is fine again

    public BatteryReminder() : this(ReadSystem) { }

    internal BatteryReminder(Func<Power?> read) { this.read = read; }

    // Null on a PC without a battery.
    internal static Power? ReadSystem()
    {
        SystemPowerStatus s;
        if (!GetSystemPowerStatus(out s) || s.BatteryLifePercent > 100 || (s.BatteryFlag & 128) != 0) return null;
        return new Power { Percent = s.BatteryLifePercent, Charging = s.ACLineStatus == 1 };
    }

    // "low", "full" or null when nothing needs doing.
    internal static string Problem(Power p)
    {
        return p.Percent <= Low && !p.Charging ? "low" : p.Percent >= Full && p.Charging ? "full" : null;
    }

    string Problem()
    {
        var p = read();
        return p == null ? null : Problem(p.Value);
    }

    public IslandNotice Due()
    {
        if (!Settings.On(Settings.BatteryOn)) return null;
        var p = read();
        var problem = p == null ? null : Problem(p.Value);
        if (problem == null) { snoozedUntil = DateTime.MinValue; return null; }
        if (DateTime.UtcNow < snoozedUntil) return null;
        return Notice(problem == "low", p.Value.Percent);
    }

    static IslandNotice Notice(bool low, int percent)
    {
        return new IslandNotice
        {
            Key = low ? "low" : "full", Title = low ? "Battery low" : "Battery charged",
            Detail = low ? "Plug in the charger" : "Unplug the charger",
            Time = percent + "%", YesLabel = "Settings", NoLabel = "Dismiss",
            Icon = BatteryShape, Accent = low ? LowAccent : FullAccent,
            Sound = IslandSound.Alarm, LoopSound = true, // rings until answered or the charger changes, again every minute
        };
    }

    // The Control Center's previews, at the current thresholds.
    internal static IslandNotice Sample(bool low) { return Notice(low, low ? Low : Full); }

    // Plugging in or unplugging closes the island by itself.
    public bool StillCurrent(IslandNotice shown) { return Problem() == shown.Key; }

    public IslandResult Answer(IslandNotice shown, bool yes)
    {
        snoozedUntil = DateTime.UtcNow + Again;
        if (yes) try { Process.Start("ms-settings:batterysaver"); } catch (Exception) { }
        return yes ? new IslandResult { Left = "Opening", Right = "Battery settings" } : new IslandResult { Left = "Dismissed", Right = "Back in " + IslandResult.Span(Again) };
    }
}
