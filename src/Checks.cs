using System;
using System.IO;
using System.Linq;

// WinIsle.exe --check: exit code is the number of failures. Reference: aladhan.com, same location and angles.
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
    // "Later" past the end of a prayer skips it: each ends when the next time starts (Sunrise ends Fajr, Fajr ends Isha).
    Func<int, DateTime> endAt = h => Prayers.EndOf(Prayers.Current(day.AddHours(h), 5.5), 5.5);
    if (endAt(16) != t[4] || endAt(5) != t[1] || endAt(3) != t[0]) fails++;
    if (IslandResult.Span(TimeSpan.FromHours(2)) != "2 hr" || IslandResult.Span(TimeSpan.FromMinutes(90)) != "90 min" || IslandResult.Span(TimeSpan.FromMinutes(30)) != "30 min") fails++;

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
    return fails + SettingsFailures() + LiveReminderFailures() + NotifyClipSettingFailures() + FlashSettingFailures() + VolumeSettingFailures() + SoundSettingFailures() + PreviewFailures() + ControlCenterFailures() + DataMoveFailures() + SoundFailures() + NotificationFailures() + BannerFailures() + MediaFailures() + BatteryFailures() + TimerFailures() + ClipboardFailures() + ShelfFailures() + WatcherFailures();
    }

    // Flashes and watchers: the pure parts (names, levels, matching).
    static int WatcherFailures()
    {
        int fails = 0;
        if (PrivacyWatcher.Friendly(@"C:#Program Files#Zoom#bin#Zoom.exe") != "Zoom" || PrivacyWatcher.Friendly("Microsoft.WindowsCamera_8wekyb3d8bbwe") != "WindowsCamera") fails++;
        // In use = started, not stopped, and (for desktop apps) still running: a closed app's stale record doesn't count.
        Func<string, bool> onlyZoom = name => name == "Zoom";
        const string zoom = @"C:#Program Files#Zoom#bin#Zoom.exe", powerpoint = @"C:#Program Files#Microsoft Office#root#Office16#POWERPNT.EXE";
        if (!PrivacyWatcher.Active(zoom, 5, 0, onlyZoom) || PrivacyWatcher.Active(powerpoint, 5, 0, onlyZoom)) fails++;
        if (PrivacyWatcher.Active(zoom, 5, 9, onlyZoom) || PrivacyWatcher.Active(zoom, null, 0, onlyZoom)) fails++;
        if (!PrivacyWatcher.Active("Microsoft.WindowsCamera_8wekyb3d8bbwe", 5, 0, onlyZoom)) fails++; // Store apps: Windows keeps these right
        // Charging sound: 1.4 s, full volume at its peak but never clipping, silent at the end.
        var chime = IslandSound.ChargeWav();
        int chimePeak = 0;
        for (int i = 44; i < chime.Length; i += 2) chimePeak = Math.Max(chimePeak, Math.Abs((int)BitConverter.ToInt16(chime, i)));
        if ((chime.Length - 44) / 2 != 61740 || Math.Abs(chimePeak - 0.32 * short.MaxValue) > 2 || BitConverter.ToInt16(chime, chime.Length - 2) != 0) fails++;
        var unplug = IslandSound.UnplugWav(); // 1.1 s, quieter, same rules
        int unplugPeak = 0;
        for (int i = 44; i < unplug.Length; i += 2) unplugPeak = Math.Max(unplugPeak, Math.Abs((int)BitConverter.ToInt16(unplug, i)));
        if ((unplug.Length - 44) / 2 != 48510 || Math.Abs(unplugPeak - 0.26 * short.MaxValue) > 2 || BitConverter.ToInt16(unplug, unplug.Length - 2) != 0) fails++;
        var loud = VolumeWatcher.Notice(0.65f, false);
        var mute = VolumeWatcher.Notice(0.65f, true);
        if (loud.Time != "65%" || Math.Abs(loud.Level.Value - 0.65) > 0.001 || mute.Time != "Muted" || mute.Level != 0 || loud.Key != mute.Key) fails++;
        if (BrightnessWatcher.Notice(40).Level != 0.4) fails++;
        // Volume keys handled here: 2% steps like Windows, snapping to the 2% grid, stopping at 0 and 100%.
        if (Math.Abs(VolumeWatcher.Step(0.12, true) - 0.14) > 1e-9 || Math.Abs(VolumeWatcher.Step(0.131, false) - 0.12) > 1e-9) fails++;
        if (VolumeWatcher.Step(1, true) != 1 || VolumeWatcher.Step(0, false) != 0 || Math.Abs(VolumeWatcher.Step(0.99, true) - 1) > 1e-9) fails++;
        // Wheel-held scrolling: whole notches either way, small touchpad deltas kept until they make one.
        int wheel = 0;
        if (VolumeWatcher.Notches(ref wheel, 120) != 1 || VolumeWatcher.Notches(ref wheel, -240) != -2 || VolumeWatcher.Notches(ref wheel, 60) != 0 || VolumeWatcher.Notches(ref wheel, 60) != 1 || wheel != 0) fails++;
        var nodes = new[]
        {
            new System.Collections.Generic.KeyValuePair<string, object>("OPPO F19 Pro Hands-Free HF", null),
            new System.Collections.Generic.KeyValuePair<string, object>("Redmi Note 13 Hands-Free HF", (byte)20),
        };
        if (BluetoothWatcher.BatteryFor("Redmi Note 13", nodes) != 20 || BluetoothWatcher.BatteryFor("OPPO F19 Pro", nodes) != null || BluetoothWatcher.BatteryFor("Pixel", nodes) != null) fails++;
        if (BluetoothWatcher.Notice("Buds", true, 60).Time != "60%" || BluetoothWatcher.Notice("Buds", true, null).Time != "Connected" || BluetoothWatcher.Notice("Buds", false, null).Time != "Disconnected") fails++;
        if (!ScreenshotWatcher.IsPicture(@"C:\x\Screenshot 2026.PNG") || ScreenshotWatcher.IsPicture(@"C:\x\notes.txt")) fails++;
        return fails;
    }

    // Settings: round-trip, clamping, damaged files, the shelf migration, Changed. Never touches the real file.
    static int SettingsFailures()
    {
        int fails = 0;
        var root = Path.Combine(Path.GetTempPath(), "WinIsleSettings" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.txt");
        try
        {
            Settings.Load(path);
            if (Settings.Num(Settings.FlashSeconds) != 2.2 || Settings.Text(Settings.LastPage) != "General" || !Settings.On(Settings.WaterOn)) fails++; // missing file: defaults
            int changed = 0;
            Action count = () => changed++;
            Settings.Changed += count;
            Settings.Set(Settings.WaterEvery, 30);
            Settings.Set(Settings.Hanafi, true);
            Settings.Set(Settings.CallWords, "ring");
            Settings.Changed -= count;
            if (changed != 3) fails++;
            Settings.Load(path);
            if (Settings.Num(Settings.WaterEvery) != 30 || !Settings.On(Settings.Hanafi) || Settings.Text(Settings.CallWords) != "ring") fails++; // round-trip
            Settings.Set(Settings.BatteryLow, 999);
            Settings.Set(Settings.WaterEvery, 1);
            if (Settings.Num(Settings.BatteryLow) != 50 || Settings.Num(Settings.WaterEvery) != 15) fails++; // clamped
            if (Settings.Min(Settings.WaterEvery) != 15 || Settings.Max(Settings.WaterEvery) != 480) fails++;

            File.WriteAllText(path, "garbage\nwater.every=abc\nbattery.low=999\nwater.on=maybe\n=\nprayer.offset.2=5\nprayer.offset.2=7");
            Settings.Load(path);
            if (Settings.Num(Settings.WaterEvery) != 90 || Settings.Num(Settings.BatteryLow) != 50 || !Settings.On(Settings.WaterOn) || Settings.Num(Settings.PrayerOffset(2)) != 7) fails++;
            File.WriteAllText(path, "");
            Settings.Load(path);
            if (Settings.Num(Settings.FlashSeconds) != 2.2 || Settings.Text(Settings.LastPage) != "General") fails++;

            // An older shelf-settings.txt carries over once.
            var old = Path.Combine(root, "migrate");
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "shelf-settings.txt"), "screenshots=on");
            Settings.Load(Path.Combine(old, "settings.txt"));
            if (!Settings.On(Settings.Screenshots)) fails++;
            Settings.ResetAll();
            if (Settings.On(Settings.Screenshots) || Settings.Num(Settings.WaterEvery) != 90) fails++;

            // The notification thread reads settings while Reset runs on the UI thread: no read may ever fail.
            int readFailures = 0;
            bool stop = false;
            var reader = new System.Threading.Thread(() =>
            {
                while (!stop) try { Settings.On(Settings.NotifyOn); Settings.On(Settings.HideBanners); } catch (Exception) { readFailures++; }
            });
            reader.Start();
            for (int i = 0; i < 3000; i++) Settings.ResetAll();
            stop = true;
            reader.Join();
            if (readFailures > 0) fails++;
        }
        catch (Exception) { fails++; }
        finally
        {
            Settings.Load(Path.Combine(root, "none", "settings.txt")); // later checks see defaults
            Directory.Delete(root, true);
        }
        return fails;
    }

    // Reminders and prayer times follow the Control Center's settings the moment they change.
    static int LiveReminderFailures()
    {
        int fails = 0;
        Settings.Load(Path.Combine(Path.GetTempPath(), "WinIsleNone" + Guid.NewGuid().ToString("N"), "settings.txt"));
        try
        {
            Settings.Set(Settings.BatteryLow, 30);
            if (BatteryReminder.Problem(new BatteryReminder.Power { Percent = 25, Charging = false }) != "low") fails++;
            Settings.Set(Settings.BatteryFull, 80);
            if (BatteryReminder.Problem(new BatteryReminder.Power { Percent = 85, Charging = true }) != "full") fails++;
            Settings.Set(Settings.BatteryOn, false);
            if (new BatteryReminder(() => new BatteryReminder.Power { Percent = 5 }).Due() != null) fails++;
            Settings.Set(Settings.BatteryOn, true);
            if (new BatteryReminder(() => new BatteryReminder.Power { Percent = 5 }).Due() == null) fails++;

            var water = new WaterReminder();
            water.From = DateTime.UtcNow.AddMinutes(-200);
            Settings.Set(Settings.WaterOn, false);
            if (water.Due() != null) fails++;
            Settings.Set(Settings.WaterOn, true);
            if (water.Due() == null) fails++;
            water.From = DateTime.UtcNow.AddMinutes(-20);
            if (water.Due() != null) fails++;            // 90 min not up yet
            Settings.Set(Settings.WaterEvery, 15);
            if (water.Due() == null) fails++;            // the shorter interval applies to the wait in progress

            var day = new DateTime(2026, 11, 9);
            var shafii = Prayers.For(day, 5.5);
            Settings.Set(Settings.Hanafi, true);
            var hanafi = Prayers.For(day, 5.5);
            if ((hanafi[3] - shafii[3]).TotalMinutes < 30) fails++;
            Settings.Set(Settings.PrayerOffset(2), 5);
            if ((Prayers.For(day, 5.5)[2] - shafii[2]).TotalMinutes != 5) fails++;
            Settings.Set(Settings.FajrAngle, 15);
            if (Prayers.For(day, 5.5)[0] <= shafii[0]) fails++;   // a smaller angle means a later Fajr
        }
        catch (Exception) { fails++; }
        finally { Settings.ResetAll(); }
        return fails;
    }

    // Call words from the Control Center, and the clipboard's size and clearing.
    static int NotifyClipSettingFailures()
    {
        int fails = 0;
        Settings.Load(Path.Combine(Path.GetTempPath(), "WinIsleNone" + Guid.NewGuid().ToString("N"), "settings.txt"));
        try
        {
            var calls = WindowsNotifications.CallPattern("incoming call, voice call, video call, calling");
            if (!calls.IsMatch("Incoming voice call") || !calls.IsMatch("Video call") || !calls.IsMatch("calling…") || calls.IsMatch("Recall the meeting")) fails++;
            if (WindowsNotifications.CallPattern("").IsMatch("calling") || WindowsNotifications.CallPattern(" , ").IsMatch("calling")) fails++;
            if (WindowsNotifications.CallPattern("a.b").IsMatch("axb") || !WindowsNotifications.CallPattern("a.b").IsMatch("a.b")) fails++;
            Settings.Set(Settings.CallWords, "ring ring");
            if (!WindowsNotifications.IsCall(new WindowsNotifications.Incoming { Lines = new[] { "Ring ring" } })) fails++;
            if (WindowsNotifications.IsCall(new WindowsNotifications.Incoming { Lines = new[] { "Incoming voice call" } })) fails++;

            Settings.Set(Settings.ClipKeep, 10);
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var history = new ClipboardHistory();
            history.Add(ClipItem.FromText("pinned", t0));
            history.TogglePin(history.Items[0]);
            for (int i = 0; i < 12; i++) history.Add(ClipItem.FromText("n" + i, t0.AddMinutes(i + 1)));
            if (history.Items.Count != 11) fails++;
            history.ClearUnpinned();
            if (history.Items.Count != 1 || history.Items[0].Text != "pinned") fails++;
        }
        catch (Exception) { fails++; }
        finally { Settings.ResetAll(); }
        return fails;
    }

    // Each flash can be switched off in the Control Center; screenshots and others always pass.
    static int FlashSettingFailures()
    {
        int fails = 0;
        Settings.Load(Path.Combine(Path.GetTempPath(), "WinIsleNone" + Guid.NewGuid().ToString("N"), "settings.txt"));
        try
        {
            var volume = VolumeWatcher.Notice(0.5f, false);
            var brightness = BrightnessWatcher.Notice(40);
            var bluetooth = BluetoothWatcher.Notice("Buds", true, 80);
            var screenshot = new IslandNotice { Key = "screenshot" };
            if (!Program.Allowed(volume) || !Program.Allowed(brightness) || !Program.Allowed(bluetooth) || !Program.Allowed(screenshot)) fails++;
            Settings.Set(Settings.FlashVolume, false);
            if (Program.Allowed(volume) || !Program.Allowed(brightness)) fails++;
            Settings.Set(Settings.FlashBrightness, false);
            if (Program.Allowed(brightness) || !Program.Allowed(bluetooth)) fails++;
            Settings.Set(Settings.FlashBluetooth, false);
            if (Program.Allowed(bluetooth) || !Program.Allowed(screenshot)) fails++;
        }
        catch (Exception) { fails++; }
        finally { Settings.ResetAll(); }
        return fails;
    }

    // Volume step size and whether the island takes the volume keys follow the Control Center.
    static int VolumeSettingFailures()
    {
        int fails = 0;
        Settings.Load(Path.Combine(Path.GetTempPath(), "WinIsleNone" + Guid.NewGuid().ToString("N"), "settings.txt"));
        try
        {
            if (!VolumeWatcher.TakeKeys()) fails++;
            Settings.Set(Settings.VolumeKeys, false);
            if (VolumeWatcher.TakeKeys()) fails++;
            Settings.Set(Settings.VolumeKeys, true);
            Settings.Set(Settings.FlashVolume, false);
            if (VolumeWatcher.TakeKeys()) fails++; // no pill to show: Windows' own pop-up comes back
            Settings.Set(Settings.VolumeStep, 5);
            if (Math.Abs(VolumeWatcher.Step(0.50, true) - 0.55) > 1e-9 || Math.Abs(VolumeWatcher.Step(0.52, true) - 0.55) > 1e-9 || VolumeWatcher.Step(0.02, false) != 0) fails++;
        }
        catch (Exception) { fails++; }
        finally { Settings.ResetAll(); }
        return fails;
    }

    // Sound volume scales every sound's peak; the switches silence all sounds or one kind.
    static int SoundSettingFailures()
    {
        int fails = 0;
        Settings.Load(Path.Combine(Path.GetTempPath(), "WinIsleNone" + Guid.NewGuid().ToString("N"), "settings.txt"));
        Func<byte[], int> peak = wav => { int p = 0; for (int i = 44; i < wav.Length; i += 2) p = Math.Max(p, Math.Abs((int)BitConverter.ToInt16(wav, i))); return p; };
        try
        {
            Settings.Set(Settings.SoundVolume, 50);
            if (Math.Abs(peak(IslandSound.ChargeWav()) - 0.16 * short.MaxValue) > 2) fails++;
            Settings.Set(Settings.SoundVolume, 100);
            if (Math.Abs(peak(IslandSound.ChargeWav()) - 0.32 * short.MaxValue) > 2) fails++;
            if (!IslandSound.Allowed(IslandSound.Alarm) || !IslandSound.Allowed(IslandSound.Tick)) fails++;
            Settings.Set(Settings.SoundAlarms, false);
            if (IslandSound.Allowed(IslandSound.Alarm) || !IslandSound.Allowed(IslandSound.Water)) fails++;
            Settings.Set(Settings.SoundAlarms, true);
            Settings.Set(Settings.SoundOn, false);
            if (IslandSound.Allowed(IslandSound.Alarm) || IslandSound.Allowed(IslandSound.Notify) || IslandSound.Allowed(IslandSound.Charge)) fails++;
            Settings.Set(Settings.SoundOn, true);
            Settings.Set(Settings.SoundVolume, 0);
            if (IslandSound.Allowed(IslandSound.Appear)) fails++;
        }
        catch (Exception) { fails++; }
        finally { Settings.ResetAll(); }
        return fails;
    }

    // Previews show a reminder's island once and change nothing real.
    static int PreviewFailures()
    {
        int fails = 0;
        var preview = new PreviewReminder();
        if (preview.Due() != null) fails++;
        var sample = WaterReminder.Sample();
        preview.Show(sample);
        if (preview.Due() != sample || !preview.StillCurrent(sample)) fails++;
        var result = preview.Answer(sample, true);
        if (result.Left != "Preview" || result.Right != sample.Title || preview.Due() != null) fails++;

        var water = new WaterReminder();
        water.From = DateTime.UtcNow.AddMinutes(-5);
        WaterReminder.Sample();
        if (water.Due() != null || sample.Key != "water") fails++;
        var prayer = PrayerReminder.Sample();
        if (Tracker.Has(prayer.Key) || prayer.Title == null) fails++;
        if (BatteryReminder.Sample(true).Key != "low" || BatteryReminder.Sample(false).Key != "full") fails++;
        if (WindowsNotifications.Sample().Title == null) fails++;
        return fails;
    }

    // Control Center: stepper arithmetic snaps to the step grid and stays in range; Prayer sits under Clipboard.
    static int ControlCenterFailures()
    {
        int fails = 0;
        if (ControlCenter.StepValue(90, 5, 15, 480, 1) != 95 || ControlCenter.StepValue(15, 5, 15, 480, -1) != 15) fails++;
        if (Math.Abs(ControlCenter.StepValue(2.2, 0.2, 1, 6, 1) - 2.4) > 1e-9 || ControlCenter.StepValue(92, 5, 15, 480, 1) != 95) fails++;
        if (ControlCenter.StepValue(478, 5, 15, 480, 1) != 480 || ControlCenter.StepValue(-29, 1, -30, 30, -1) != -30) fails++;
        if (Array.IndexOf(ControlCenter.Pages, "Prayer") != Array.IndexOf(ControlCenter.Pages, "Clipboard") + 1 || ControlCenter.Pages.Length != 11) fails++;
        if (!Startup.LinkPath.EndsWith(@"\Microsoft\Windows\Start Menu\Programs\Startup\WinIsle.lnk", StringComparison.OrdinalIgnoreCase)) fails++;
        return fails;
    }

    // Shelf: holds each file once, survives a restart, and drops files that were moved or deleted.
    static int ShelfFailures()
    {
        int fails = 0;
        var root = Path.Combine(Path.GetTempPath(), "WinIsleShelf" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string a = Path.Combine(root, "a.txt"), b = Path.Combine(root, "b.pdf"), list = Path.Combine(root, "shelf.txt");
            File.WriteAllText(a, "a");
            File.WriteAllText(b, "b");
            var shelf = new FileShelf(list);
            if (shelf.Add(new[] { a, b, a.ToUpperInvariant() }) != 2 || shelf.Count != 2) fails++;
            if (shelf.Add(new[] { root }) != 1) fails++;                       // folders can be held too
            if (new FileShelf(list).Count != 3) fails++;                      // read back after a restart
            File.Delete(b);
            shelf.Prune();
            if (shelf.Count != 2 || shelf.Paths.Contains(b)) fails++;         // moved away: off the shelf
            shelf.Remove(a);
            if (shelf.Count != 1) fails++;
            shelf.Clear();
            if (new FileShelf(list).Count != 0 || !File.Exists(a)) fails++;   // clearing never touches the files

            // Screenshot switch: the Control Center's setting, so the shelf header and the Control Center agree.
            if (new FileShelf(list).AutoScreenshots) fails++;
            Settings.Set(Settings.Screenshots, true);
            if (!new FileShelf(list).AutoScreenshots) fails++;
            new FileShelf(list).AutoScreenshots = false;
            if (Settings.On(Settings.Screenshots)) fails++;
        }
        finally { Directory.Delete(root, true); }
        return fails;
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
        // Panel keys: navigation and actions are taken; Delete only removes when not searching (or with Shift);
        // other Ctrl shortcuts and plain letters are not commands.
        if (ClipboardWindow.Command(0x28, false, false, false) != "down" || ClipboardWindow.Command(0x0D, false, false, true) != "paste" || ClipboardWindow.Command(0x1B, false, false, false) != "escape") fails++;
        if (ClipboardWindow.Command('P', true, false, false) != "pin" || ClipboardWindow.Command('3', true, false, false) != "paste3" || ClipboardWindow.Command('C', true, false, false) != null) fails++;
        if (ClipboardWindow.Command(0x2E, false, false, false) != "remove" || ClipboardWindow.Command(0x2E, false, false, true) != null || ClipboardWindow.Command(0x2E, false, true, true) != "remove") fails++;
        if (ClipboardWindow.Command('A', false, false, false) != null || ClipboardWindow.Command(0x08, false, false, true) != "back") fails++;
        // File previews: a real file gives a picture, a missing one gives null (shown as a plain tile), never a crash.
        var explorer = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), "explorer.exe");
        if (AppIcon.ForFile(explorer) == null || AppIcon.ForFile(@"C:\no\such\file.png") != null) fails++;
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
        var root = @"Software\WinIsleCheck" + Guid.NewGuid().ToString("N");
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
        if (AppIcon.For("WinIsle.NoSuchApp") != null || AppIcon.For("") != null || single.Logo != null) fails++;

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
        var root = Path.Combine(Path.GetTempPath(), "WinIsleCheck" + Guid.NewGuid().ToString("N"));
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
