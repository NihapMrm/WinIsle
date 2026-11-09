using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

class Slot { public DateTime Day, Start; public int Index, Status; public double? Delay; public string Key; }

// prayed.txt: a "since" line, then one line per prayer marked prayed. Missed is never stored:
// it is worked out from the times, so there is no job that can fail to run while the PC is off.
static class Tracker
{
    public const int Untracked = 0, Upcoming = 1, Pending = 2, Prayed = 3, Missed = 4;
    const string StampFormat = "yyyy-MM-dd HH:mm";

    static readonly string LogFile = AppPaths.For("prayed.txt");
    static readonly Dictionary<string, DateTime?> done = new Dictionary<string, DateTime?>();
    static bool unreadable; // never overwrite a log that exists but could not be read
    public static DateTime Since;

    public static void Load()
    {
        bool found = false;
        try
        {
            if (File.Exists(LogFile))
                foreach (var line in File.ReadAllLines(LogFile).Where(l => l.Trim().Length > 0))
                {
                    DateTime t;
                    var parts = line.Trim().Split('\t');
                    bool stamped = parts.Length > 1 && DateTime.TryParseExact(parts[1], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
                    if (line.StartsWith("since "))
                        found = DateTime.TryParseExact(line.Substring(6).Trim(), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out Since);
                    else if (stamped) { DateTime.TryParseExact(parts[1], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out t); done[parts[0]] = t; }
                    else done[parts[0]] = null;
                }
        }
        catch (Exception) { unreadable = true; }
        if (!found) { Since = Prayers.Now; Save(); }
    }

    static void Save()
    {
        if (unreadable) return;
        var lines = new[] { "since " + Since.ToString(StampFormat, CultureInfo.InvariantCulture) }
            .Concat(done.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Key + (d.Value.HasValue ? "\t" + d.Value.Value.ToString(StampFormat, CultureInfo.InvariantCulture) : "")));
        try
        {
            // Write to a temp file then swap, so a crash mid-write never truncates the history.
            var tmp = LogFile + ".tmp";
            File.WriteAllLines(tmp, lines);
            if (File.Exists(LogFile)) File.Replace(tmp, LogFile, null);
            else File.Move(tmp, LogFile);
        }
        catch (Exception) { }
    }

    public static bool Has(string key) { return done.ContainsKey(key); }
    public static void Mark(string key, DateTime? when) { done[key] = when; Save(); }
    public static void Unmark(string key) { done.Remove(key); Save(); }

    public static int Classify(bool isDone, DateTime start, DateTime end, DateTime since, DateTime now)
    {
        if (isDone) return Prayed;
        if (end <= since) return Untracked; // finished before tracking began
        return now < start ? Upcoming : now < end ? Pending : Missed;
    }

    // Five slots per day, oldest first.
    public static Slot[] Slots(DateTime from, int days, DateTime now)
    {
        var result = new Slot[days * 5];
        var times = Prayers.For(from, Prayers.TzAt(from));
        for (int d = 0; d < days; d++)
        {
            var day = from.Date.AddDays(d);
            var tomorrow = Prayers.For(day.AddDays(1), Prayers.TzAt(day.AddDays(1)));
            for (int i = 0; i < 5; i++)
            {
                var start = times[Prayers.SlotTimes[i]];
                var end = i < 4 ? times[Prayers.SlotTimes[i + 1]] : tomorrow[0];
                var key = new Prayer { Name = Prayers.SlotNames[i], At = start }.Key;
                DateTime? stamp;
                done.TryGetValue(key, out stamp);
                double? delay = stamp.HasValue && stamp.Value >= start ? (stamp.Value - start).TotalMinutes : (double?)null;
                result[d * 5 + i] = new Slot { Day = day, Start = start, Index = i, Key = key, Delay = delay, Status = Classify(done.ContainsKey(key), start, end, Since, now) };
            }
            times = tomorrow;
        }
        return result;
    }

    // Prayers in a row. Upcoming, current and untracked ones neither extend nor break a run.
    public static void Streaks(int[] statuses, out int current, out int best)
    {
        current = best = 0;
        foreach (var s in statuses)
        {
            if (s == Missed) current = 0;
            else if (s == Prayed) best = Math.Max(best, ++current);
        }
    }
}
