using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// Every switch and value the Control Center changes, kept in settings.txt as key=value lines. Features read
// these at the moment they act, so a change applies at once. A missing or damaged file never stops the app:
// unknown keys are kept, bad values fall back to the default, and numbers are clamped to their range.
static class Settings
{
    internal const string ShowPanel = "general.showPanel";
    internal const string WaterOn = "water.on", WaterEvery = "water.every", WaterSnooze = "water.snooze";
    internal const string PrayerOn = "prayer.on", PrayerSnooze = "prayer.snooze", FajrAngle = "prayer.fajrAngle", IshaAngle = "prayer.ishaAngle", Hanafi = "prayer.hanafi";
    internal const string BatteryOn = "battery.on", BatteryLow = "battery.low", BatteryFull = "battery.full", BatteryAgain = "battery.again";
    internal const string ChargeAnim = "battery.anim", ChargeSeconds = "battery.animSeconds";
    internal const string NotifyOn = "notify.on", HideBanners = "notify.hideBanners", NotifySeconds = "notify.seconds", CallWords = "notify.callWords";
    internal const string FlashVolume = "flash.volume", FlashBrightness = "flash.brightness", FlashBluetooth = "flash.bluetooth", FlashSeconds = "flash.seconds", Dots = "flash.dots";
    internal const string VolumeKeys = "input.volumeKeys", WheelVolume = "input.wheelVolume", VolumeStep = "input.volumeStep";
    internal const string ClipOn = "clip.on", ClipKeep = "clip.keep";
    internal const string Screenshots = "shelf.screenshots";
    internal const string SoundOn = "sound.on", SoundVolume = "sound.volume";
    internal const string SoundReminders = "sound.reminders", SoundAlarms = "sound.alarms", SoundNotifications = "sound.notifications", SoundFlashes = "sound.flashes", SoundTicks = "sound.ticks";
    internal const string LastPage = "ui.lastPage";

    // Minutes added to each prayer time (0..5 = Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha).
    internal static string PrayerOffset(int i) { return "prayer.offset." + i; }

    class Def { public object Default; public double Min, Max; }

    static readonly Dictionary<string, Def> Defs = new Dictionary<string, Def>();
    static readonly Dictionary<string, object> values = new Dictionary<string, object>();
    static readonly Dictionary<string, string> unknown = new Dictionary<string, string>(); // kept and written back
    static string file; // null until Load: nothing is saved (demo modes)

    internal static event Action Changed;

    static void Flag(string key, bool on) { Defs[key] = new Def { Default = on }; }
    static void Number(string key, double value, double min, double max) { Defs[key] = new Def { Default = value, Min = min, Max = max }; }
    static void Words(string key, string value) { Defs[key] = new Def { Default = value }; }

    static Settings()
    {
        Flag(ShowPanel, true);
        Flag(WaterOn, true); Number(WaterEvery, 90, 15, 480); Number(WaterSnooze, 10, 1, 120);
        Flag(PrayerOn, true); Number(PrayerSnooze, 10, 1, 60);
        for (int i = 0; i < 6; i++) Number(PrayerOffset(i), 0, -30, 30);
        Number(FajrAngle, 18, 12, 20); Number(IshaAngle, 18, 12, 20); Flag(Hanafi, false);
        Flag(BatteryOn, true); Number(BatteryLow, 15, 5, 50); Number(BatteryFull, 95, 60, 100); Number(BatteryAgain, 1, 1, 30);
        Flag(ChargeAnim, true); Number(ChargeSeconds, 5, 2, 10);
        Flag(NotifyOn, true); Flag(HideBanners, true); Number(NotifySeconds, 6, 2, 30);
        Words(CallWords, "incoming call, voice call, video call, calling");
        Flag(FlashVolume, true); Flag(FlashBrightness, true); Flag(FlashBluetooth, true); Number(FlashSeconds, 2.2, 1, 6); Flag(Dots, true);
        Flag(VolumeKeys, true); Flag(WheelVolume, true); Number(VolumeStep, 2, 1, 10);
        Flag(ClipOn, true); Number(ClipKeep, 60, 10, 500);
        Flag(Screenshots, false);
        Flag(SoundOn, true); Number(SoundVolume, 100, 0, 100);
        foreach (var kind in new[] { SoundReminders, SoundAlarms, SoundNotifications, SoundFlashes, SoundTicks }) Flag(kind, true);
        Words(LastPage, "General");
        Defaults();
    }

    // Overwrites in place, never clearing: the notification thread reads these while Reset runs on the UI
    // thread, and a key missing for a moment would throw there and close the app.
    static void Defaults()
    {
        unknown.Clear();
        foreach (var d in Defs) values[d.Key] = d.Value.Default;
    }

    internal static bool On(string key) { return (bool)values[key]; }
    internal static double Num(string key) { return (double)values[key]; }
    internal static string Text(string key) { return (string)values[key]; }
    internal static double Min(string key) { return Defs[key].Min; }
    internal static double Max(string key) { return Defs[key].Max; }

    internal static void Set(string key, bool value) { Store(key, value); }
    internal static void Set(string key, double value) { Store(key, Math.Max(Defs[key].Min, Math.Min(Defs[key].Max, value))); }
    internal static void Set(string key, string value) { Store(key, value ?? ""); }

    static void Store(string key, object value)
    {
        values[key] = value;
        Save();
        var changed = Changed;
        if (changed != null) changed();
    }

    internal static void Load() { Load(AppPaths.For("settings.txt")); }

    internal static void Load(string path)
    {
        Defaults();
        file = path;
        bool sawScreenshots = false;
        try
        {
            if (File.Exists(path))
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), raw = line.Substring(eq + 1).Trim();
                    Def def;
                    if (!Defs.TryGetValue(key, out def)) { unknown[key] = raw; continue; }
                    if (key == Screenshots) sawScreenshots = true;
                    object parsed = Parse(def, raw);
                    if (parsed != null) values[key] = parsed;
                }
        }
        catch (Exception) { } // unreadable: defaults
        // shelf-settings.txt from before the Control Center: carried over once.
        try
        {
            var shelf = Path.Combine(Path.GetDirectoryName(path), "shelf-settings.txt");
            if (!sawScreenshots && File.Exists(shelf) && File.ReadAllLines(shelf).Contains("screenshots=on")) { values[Screenshots] = true; Save(); }
        }
        catch (Exception) { }
    }

    // Null when the text doesn't fit the setting's type, so the default stays.
    static object Parse(Def def, string raw)
    {
        if (def.Default is bool) return raw == "true" ? (object)true : raw == "false" ? (object)false : null;
        if (def.Default is string) return raw;
        double n;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || double.IsNaN(n) || double.IsInfinity(n)) return null;
        return Math.Max(def.Min, Math.Min(def.Max, n));
    }

    internal static void ResetAll()
    {
        Defaults();
        Save();
        var changed = Changed;
        if (changed != null) changed();
    }

    static void Save()
    {
        if (file == null) return;
        try
        {
            var lines = Defs.Keys.Select(k => k + "=" + Format(values[k])).Concat(unknown.Select(u => u.Key + "=" + u.Value));
            File.WriteAllLines(file, lines);
        }
        catch (Exception) { } // applies for this session anyway
    }

    static string Format(object v)
    {
        if (v is bool) return (bool)v ? "true" : "false";
        if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
        return ((string)v).Replace("\r", " ").Replace("\n", " ");
    }
}
