using System;
using System.IO;
using System.Linq;

// TodoPanel.exe --check: exit code is the number of failures. Reference: aladhan.com, same location and angles.
static class Checks
{
    public static int Run()
    {
    var day = new DateTime(2026, 11, 9);
    var t = Prayers.For(day, 5.5);
    int[] expected = { 4 * 60 + 41, 5 * 60 + 53, 11 * 60 + 47, 15 * 60 + 9, 17 * 60 + 41, 18 * 60 + 53 };
    int fails = t.Where((x, i) => Math.Abs(x.TimeOfDay.TotalMinutes - expected[i]) > 1 + Math.Abs(Prayers.Offset[i])).Count();
    Func<int, string> at = h => { var p = Prayers.Current(day.AddHours(h), 5.5); return p == null ? null : p.Key; };
    if (at(3) != "2026-11-08 Isha") fails++;
    if (at(8) != null) fails++;
    if (at(16) != "2026-11-09 Asr") fails++;
    if (at(23) != "2026-11-09 Isha") fails++;

    // A prayer is Missed exactly when the next prayer starts, unless Prayed was clicked.
    DateTime start = day.AddHours(12), end = day.AddHours(15), since = day;
    if (Tracker.Classify(false, start, end, since, day.AddHours(11)) != Tracker.Upcoming) fails++;
    if (Tracker.Classify(false, start, end, since, day.AddHours(14)) != Tracker.Pending) fails++;
    if (Tracker.Classify(false, start, end, since, end) != Tracker.Missed) fails++;
    if (Tracker.Classify(true, start, end, since, end) != Tracker.Prayed) fails++;
    if (Tracker.Classify(false, start, end, day.AddHours(16), day.AddHours(17)) != Tracker.Untracked) fails++;
    int current, best;
    Tracker.Streaks(new[] { 3, 3, 3, 4, 3, 0, 3, 2 }, out current, out best);
    if (current != 2 || best != 3) fails++;
    if (WaterReminder.Every != TimeSpan.FromMinutes(90)) fails++;
    if (WaterReminder.Snooze != TimeSpan.FromMinutes(10)) fails++;
    if (Prayers.ValidLocation(91, 0) || Prayers.ValidLocation(0, 181) || !Prayers.ValidLocation(7.7, 81.7)) fails++;
    return fails + DataMoveFailures() + SoundFailures();
    }

    // A chime must be valid PCM, hit its volume exactly (no clipping) and end silent (no click).
    static int SoundFailures()
    {
        var wav = IslandSound.Wav(0.3, 500, 440, 0, 660, 100);
        int peak = 0, fails = 0;
        for (int i = 44; i < wav.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, i)));
        if (System.Text.Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || BitConverter.ToInt32(wav, 40) != wav.Length - 44) fails++;
        if ((wav.Length - 44) / 2 != 22050) fails++;
        if (Math.Abs(peak - 0.3 * short.MaxValue) > 2) fails++;
        if (Math.Abs((int)BitConverter.ToInt16(wav, wav.Length - 2)) > 8) fails++;

        // A water drop's pitch rises: more zero crossings in its second 30 ms than its first.
        var drop = IslandSound.Drops(0.3, 200, 800, 0);
        Func<int, int, int> crossings = (from, to) =>
        {
            int n = 0;
            for (int i = from; i < to; i++) if (BitConverter.ToInt16(drop, 44 + 2 * i) < 0 != BitConverter.ToInt16(drop, 46 + 2 * i) < 0) n++;
            return n;
        };
        if (crossings(0, 1323) >= crossings(1323, 2646)) fails++;
        if (new WaterReminder(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero).Due().Sound != IslandSound.Water) fails++;
        return fails;
    }

    // Moving data must never lose any: existing targets stay, sources stay.
    static int DataMoveFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "MyTodoCheck" + Guid.NewGuid().ToString("N"));
        string from = Path.Combine(root, "from"), to = Path.Combine(root, "to");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        try
        {
            File.WriteAllText(Path.Combine(from, "todos.txt"), "old");
            File.WriteAllText(Path.Combine(from, "position.txt"), "1,2");
            File.WriteAllText(Path.Combine(to, "todos.txt"), "new");
            AppPaths.CopyMissing(from, to);
            int fails = 0;
            if (File.ReadAllText(Path.Combine(to, "todos.txt")) != "new") fails++;
            if (File.ReadAllText(Path.Combine(to, "position.txt")) != "1,2") fails++;
            if (!File.Exists(Path.Combine(from, "position.txt"))) fails++;
            if (File.Exists(Path.Combine(to, "prayed.txt"))) fails++;
            return fails;
        }
        finally { Directory.Delete(root, true); }
    }
}
