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
    return fails + DataMoveFailures() + SoundFailures() + NotificationFailures() + BannerFailures() + MediaFailures() + BatteryFailures() + TimerFailures() + ClipboardFailures();
    }

    // Clipboard: copying again moves to the top, pins stay first and are never trimmed, search and ages read right.
    static int ClipboardFailures()
    {
        int fails = 0;
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var history = new ClipboardHistory(); // no pin file: checks never touch the real one
        history.Add(ClipItem.FromText("alpha", t0));
        history.Add(ClipItem.FromText("beta", t0.AddSeconds(1)));
        history.Add(ClipItem.FromText("alpha", t0.AddSeconds(2)));
        var items = history.Items;
        if (items.Count != 2 || items[0].Text != "alpha") fails++;
        history.TogglePin(items[1]);
        if (history.Items[0].Text != "beta" || !history.Items[0].Pinned) fails++;
        for (int i = 0; i < ClipboardHistory.Keep + 10; i++) history.Add(ClipItem.FromText("n" + i, t0.AddMinutes(i + 1)));
        var all = history.Items;
        if (all.Count != ClipboardHistory.Keep + 1 || all[0].Text != "beta" || all.Any(x => x.Text == "alpha")) fails++;

        if (!ClipboardHistory.Matches(ClipItem.FromText("Hello World", t0), "world") || ClipboardHistory.Matches(ClipItem.FromText("Hello", t0), "xyz") || !ClipboardHistory.Matches(ClipItem.FromText("Hello", t0), " ")) fails++;
        if (!ClipboardHistory.Matches(new ClipItem { Files = new[] { @"C:\work\Report.pdf" } }, "report") || ClipboardHistory.Matches(new ClipItem { Files = new[] { @"C:\work\Report.pdf" } }, "work")) fails++;
        if (!ClipItem.FromText(" https://example.com/a ", t0).IsLink || ClipItem.FromText("see https://example.com", t0).IsLink) fails++;
        if (ClipboardHistory.Ago(TimeSpan.FromSeconds(30)) != "now" || ClipboardHistory.Ago(TimeSpan.FromMinutes(5)) != "5m" || ClipboardHistory.Ago(TimeSpan.FromHours(3)) != "3h" || ClipboardHistory.Ago(TimeSpan.FromDays(2)) != "2d") fails++;
        return fails;
    }

    // Timer: counts down, pauses and resumes exactly, rings at zero, and only a stopped timer can be changed.
    static int TimerFailures()
    {
        int fails = 0;
        var t0 = DateTime.UtcNow.AddHours(-1);
        var timer = new CountdownTimer();
        timer.Set(TimeSpan.FromMinutes(10));
        if (timer.Remaining(t0) != TimeSpan.FromMinutes(10) || timer.Active) fails++;
        timer.Start(t0);
        if (timer.Remaining(t0.AddMinutes(3)) != TimeSpan.FromMinutes(7)) fails++;
        timer.Set(TimeSpan.FromMinutes(1));
        if (timer.Length != TimeSpan.FromMinutes(10)) fails++;            // can't change while active
        timer.Pause(t0.AddMinutes(3));
        if (timer.Remaining(t0.AddMinutes(50)) != TimeSpan.FromMinutes(7) || timer.Running || !timer.Active) fails++;
        timer.Start(t0.AddMinutes(50));                                    // resumes with 7 left: ended 2 minutes after t0 + 50
        var alarm = timer.Due();                                           // now is t0 + 60, past the end
        if (alarm == null || !alarm.Sticky || !alarm.LoopSound || alarm.Sound != IslandSound.Alarm) fails++;
        timer.Answer(alarm, true);                                         // +1 min
        if (timer.Due() != null || !timer.Running) fails++;
        timer.Answer(alarm, false);                                        // stop
        if (timer.Active || timer.Due() != null) fails++;
        timer.Set(TimeSpan.FromHours(30));
        if (timer.Length != CountdownTimer.Longest) fails++;
        timer.Set(TimeSpan.Zero);
        timer.Start(t0);
        if (timer.Active) fails++; // 00:00:00 doesn't start

        // Wheels wrap 59/00 both ways and roll the short way round.
        if (TimeWheel.Wrap(-1, 60) != 59 || TimeWheel.Wrap(60, 60) != 0 || TimeWheel.Wrap(25, 24) != 1) fails++;
        if (TimeWheel.Direction(5, 6, 60) != 1 || TimeWheel.Direction(0, 59, 60) != -1 || TimeWheel.Direction(59, 0, 60) != 1 || TimeWheel.Direction(7, 7, 60) != 0) fails++;
        return fails;
    }

    // Battery: alerts at 15% unplugged and 95% charging, snoozes on Dismiss, and clears when the charger changes.
    static int BatteryFailures()
    {
        int fails = 0;
        Func<int, bool, BatteryReminder.Power> at = (percent, charging) => new BatteryReminder.Power { Percent = percent, Charging = charging };
        if (BatteryReminder.Problem(at(15, false)) != "low" || BatteryReminder.Problem(at(16, false)) != null || BatteryReminder.Problem(at(10, true)) != null) fails++;
        if (BatteryReminder.Problem(at(95, true)) != "full" || BatteryReminder.Problem(at(94, true)) != null || BatteryReminder.Problem(at(100, false)) != null) fails++;

        var power = at(12, false);
        var battery = new BatteryReminder(() => power);
        var alert = battery.Due();
        if (alert == null || alert.Key != "low" || alert.Time != "12%" || !battery.StillCurrent(alert) || alert.Sound != IslandSound.Alarm || !alert.LoopSound) fails++;
        var full = new BatteryReminder(() => at(97, true)).Due();
        if (full.Sound != IslandSound.Alarm || !full.LoopSound) fails++;

        // The alarm loops cleanly: 1.2 s, silent at both ends so the seam doesn't click, beeping in between.
        var alarm = IslandSound.Beeps(0.5, 1200, 1319, 0, 1319, 160, 1319, 320, 1319, 480);
        if ((alarm.Length - 44) / 2 != 52920 || BitConverter.ToInt16(alarm, 44) != 0 || BitConverter.ToInt16(alarm, alarm.Length - 2) != 0) fails++;
        int loud = 0;
        for (int i = 44; i < alarm.Length; i += 2) if (Math.Abs((int)BitConverter.ToInt16(alarm, i)) > 8000) loud++;
        if (loud == 0) fails++;
        battery.Answer(alert, false);
        if (battery.Due() != null) fails++;            // snoozed for a minute
        power = at(12, true);
        if (battery.StillCurrent(alert) || battery.Due() != null) fails++; // plugged in: island closes
        power = at(12, false);
        if (battery.Due() == null) fails++;            // unplugged again: alerts at once, no leftover snooze
        if (new BatteryReminder(() => null).Due() != null) fails++; // no battery
        return fails;
    }

    // Media: the bar moves on between reads but never past the end, and stops while paused.
    static int MediaFailures()
    {
        int fails = 0;
        var t0 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var at = new DateTimeOffset(t0);
        var song = new MediaInfo { Playing = true, Position = TimeSpan.FromSeconds(60), End = TimeSpan.FromSeconds(200), Updated = at };
        if (MediaSession.PositionAt(song, at.AddSeconds(5)) != TimeSpan.FromSeconds(65)) fails++;
        if (MediaSession.PositionAt(song, at.AddMinutes(10)) != song.End) fails++;
        song.Playing = false;
        if (MediaSession.PositionAt(song, at.AddSeconds(5)) != TimeSpan.FromSeconds(60)) fails++;
        if (MediaSession.Clock(TimeSpan.FromSeconds(185)) != "3:05" || MediaSession.Clock(TimeSpan.FromSeconds(3723)) != "1:02:03" || MediaSession.Clock(TimeSpan.Zero) != "0:00") fails++;
        return fails;
    }

    // Hiding sets the value only on keys that exist; restoring clears it everywhere, including nested ids.
    static int BannerFailures()
    {
        var root = @"Software\MyTodoCheck" + Guid.NewGuid().ToString("N");
        Func<string, object> banner = id =>
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(root + "\\" + id))
                return key == null ? "missing" : key.GetValue(WindowsNotifications.BannerValue);
        };
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey(root + @"\App").Dispose();
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey(root + @"\Nested\Path\App").Dispose();
        try
        {
            int fails = 0;
            WindowsNotifications.HideBanner(root, "App");
            WindowsNotifications.HideBanner(root, @"Nested\Path\App");
            WindowsNotifications.HideBanner(root, "NotInstalled");
            WindowsNotifications.HideBanner(root, "");
            if (!Equals(banner("App"), 0) || !Equals(banner(@"Nested\Path\App"), 0)) fails++;
            if (!Equals(banner("NotInstalled"), "missing")) fails++;
            WindowsNotifications.RestoreBanners(root);
            if (banner("App") != null || banner(@"Nested\Path\App") != null) fails++;
            return fails;
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root); }
    }

    // Same-app notifications merge, a shown one expires, and the next app follows.
    static int NotificationFailures()
    {
        int fails = 0;
        var n = new WindowsNotifications(TimeSpan.Zero);
        Action<string, string[]> add = (app, lines) => n.Add(new WindowsNotifications.Incoming { App = app, AppId = app, Lines = lines });
        add("Chat", new[] { "Ann", "hello", "there" });
        add("Mail", new[] { "Invoice" });
        add("Chat", new[] { "Bob", "latest" });
        var first = n.Due();
        if (first.Title != "Chat" || first.Detail != "2 new · Bob latest") fails++;
        if (first.More != "Ann: hello there\nBob: latest") fails++; // clicking a merged group lists every message
        if (n.StillCurrent(first)) fails++;
        var second = n.Due();
        if (second.Title != "Invoice" || second.Detail != "Mail") fails++;
        if (n.Due() != second) fails++;
        n.Answer(second, false);
        if (n.Due() != null) fails++;
        add("Chat", new[] { "Ann", "hello", "there" });
        var single = n.Due();
        if (single.Title != "Ann" || single.Detail != "Chat · hello there" || single.Sound != IslandSound.Notify || single.NoLabel != "Dismiss") fails++;

        // Real app id gives a 96 px icon; unknown or empty ids give null, not a crash.
        var settings = AppIcon.For("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel") as System.Windows.Media.Imaging.BitmapSource;
        if (settings == null || settings.PixelWidth != 96) fails++;
        if (AppIcon.For("MyTodo.NoSuchApp") != null || AppIcon.For("") != null || single.Logo != null) fails++;

        // A call jumps the queue, never merges, and survives expiry until answered. Normal and missed ones expire.
        add("Chat", new[] { "Ann", "see you soon" });
        add("Chat", new[] { "Ben", "Incoming voice call" });
        n.Answer(single, false);
        var call = n.Due();
        if (call.Title != "Ben" || !call.Sticky || !n.StillCurrent(call) || n.Due() != call) fails++;
        n.Answer(call, false);
        var message = n.Due();
        if (message.Title != "Ann" || message.Sticky || n.StillCurrent(message)) fails++;
        Func<string[], bool> isCall = lines => WindowsNotifications.IsCall(new WindowsNotifications.Incoming { Lines = lines });
        if (!isCall(new[] { "Ann", "Video call" }) || !isCall(new[] { "Ann is calling" })) fails++;
        if (isCall(new[] { "Missed voice call" }) || isCall(new[] { "Ann", "lunch tomorrow?" }) || isCall(new[] { "Ann", new string('x', 40) + " video call" })) fails++;

        // Prayer and water keep the regular island; only notifications are small.
        var regular = IslandWindow.Regular;
        var small = IslandWindow.Small;
        if (regular.Width != 430 || regular.OpenH != 126 || regular.OpenR != 34) fails++;
        if (small.Width != 360 || small.OpenH != 104 || small.OpenR != 28) fails++;
        var water = new WaterReminder(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero).Due();
        if (!single.Small || water.Small) fails++;
        if (single.More != single.Detail || water.More != null) fails++; // only notifications expand

        // Open: exe-path ids launch the exe; every other id goes through the Apps folder.
        var explorerPath = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), "explorer.exe").Replace('\\', '/');
        if (WindowsNotifications.LaunchTarget(explorerPath).FileName != explorerPath) fails++;
        var packaged = WindowsNotifications.LaunchTarget("Pkg_abc!App");
        if (packaged.FileName != "explorer.exe" || packaged.Arguments != "\"shell:AppsFolder\\Pkg_abc!App\"") fails++;
        if (WindowsNotifications.LaunchTarget("C:/no/such/app.exe").FileName != "explorer.exe") fails++;
        return fails;
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
