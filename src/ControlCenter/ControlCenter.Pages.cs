using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

// The settings pages: one card per feature. Every control writes its setting at once, and shows changes made
// elsewhere (Reset, the shelf's own switch) through the refreshers.
partial class ControlCenter
{
    const double CardsW = 760; // settings rows read badly across the full page width

    FrameworkElement BuildPage(string name)
    {
        switch (name)
        {
            case "Prayer": return PrayerPage();
            case "General": return Page(name, "Startup and shortcuts", GeneralCards());
            case "Water": return Page(name, "A reminder to drink", WaterCards());
            case "Battery": return Page(name, "Low and full alerts, and the charging animation", BatteryCards());
            case "Notifications": return Page(name, "Windows notifications on the island", NotificationCards());
            case "Flashes": return Page(name, "Brief messages on the island", FlashCards());
            case "Input": return Page(name, "Volume keys and the mouse wheel", InputCards());
            case "Clipboard": return Page(name, "Clipboard history (Ctrl+Alt+V)", ClipboardCards());
            case "Shelf": return Page(name, "Files held on the island", ShelfCards());
            case "Sounds": return Page(name, "Every sound the island makes", SoundCards());
            default: return Page(name, "WinIsle", AboutCards());
        }
    }

    static FrameworkElement Page(string name, string detail, params UIElement[] cards)
    {
        var title = new StackPanel();
        title.Children.Add(T(name, 22, Ink, FontWeights.SemiBold));
        title.Children.Add(T(detail, 13, Muted));
        var column = new StackPanel { Width = CardsW, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var card in cards) column.Children.Add(card);
        return PageFrame(title, column);
    }

    // ---- building blocks bound to settings ----

    Border Section(string title, string detail, UIElement right, params UIElement[] rows)
    {
        var body = new StackPanel();
        body.Children.Add(CardHeader(title, detail, right));
        foreach (var row in rows) body.Children.Add(row);
        var card = Card(body);
        card.Margin = new Thickness(0, 0, 0, Gap);
        return card;
    }

    UIElement Toggle(string key, string name)
    {
        Action<bool> set;
        var s = Switch(Settings.On(key), v => Settings.Set(key, v), name, out set);
        s.HorizontalAlignment = HorizontalAlignment.Right;
        s.VerticalAlignment = VerticalAlignment.Top;
        refreshers.Add(() => set(Settings.On(key)));
        return s;
    }

    UIElement Number(string key, double step, string unit, string name)
    {
        Action<double> set;
        var s = Stepper(Settings.Num(key), step, Settings.Min(key), Settings.Max(key), unit, v => Settings.Set(key, v), name, out set);
        refreshers.Add(() => set(Settings.Num(key)));
        return s;
    }

    UIElement Words(string key, double width, string name)
    {
        Action<string> set;
        var box = TextSetting(Settings.Text(key), width, v => Settings.Set(key, v), name, out set);
        refreshers.Add(() => set(Settings.Text(key)));
        return box;
    }

    static Grid Line(string label, string hint, UIElement control)
    {
        TextBlock ignore;
        return Row(label, hint, control, out ignore);
    }

    // A row that only means something while active() is true: dimmed and disabled otherwise, with its own hint.
    Grid Linked(string label, string hint, string offHint, UIElement control, Func<bool> active)
    {
        TextBlock hintText;
        var row = Row(label, hint, control, out hintText);
        Action apply = () =>
        {
            bool on = active();
            row.Opacity = on ? 1 : 0.4;
            row.IsEnabled = on;
            hintText.Text = on || offHint == null ? hint ?? "" : offHint;
            hintText.Visibility = hintText.Text == "" ? Visibility.Collapsed : Visibility.Visible;
        };
        refreshers.Add(apply);
        apply();
        return row;
    }

    Grid Dimmed(string masterKey, string label, string hint, UIElement control)
    {
        return Linked(label, hint, null, control, () => Settings.On(masterKey));
    }

    // A button that says what it did for a moment.
    static Border Button(string label, string done, Action click)
    {
        Border pill = null;
        pill = Pill(label, false, () =>
        {
            click();
            if (done == null) return;
            var text = (TextBlock)pill.Child;
            text.Text = done;
            After(1600, () => text.Text = label);
        });
        pill.VerticalAlignment = VerticalAlignment.Center;
        return pill;
    }

    static StackPanel Buttons(params UIElement[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    static void Preview(IslandNotice n) { if (Program.Preview != null) Program.Preview.Show(n); }

    // ---- pages ----

    FrameworkElement PrayerPage()
    {
        var page = BuildPrayerPage();
        Action<int> setAsr;
        var asr = Dropdown(150, new[] { "Shafi'i", "Hanafi" }, Settings.On(Settings.Hanafi) ? 1 : 0, i => Settings.Set(Settings.Hanafi, i == 1), "Asr method", out setAsr);
        refreshers.Add(() => setAsr(Settings.On(Settings.Hanafi) ? 1 : 0));
        var offsets = new WrapPanel();
        string[] names = { "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha" };
        for (int i = 0; i < 6; i++)
        {
            var field = Labelled(names[i], (FrameworkElement)Number(Settings.PrayerOffset(i), 1, "min", names[i] + " offset"));
            field.Margin = new Thickness(0, 0, 18, 10);
            offsets.Children.Add(field);
        }
        var card = Section("Prayer reminders", "The island asks at each prayer time; times follow these settings. Turn prayer off on the General page", null,
            Line("Not yet brings it back after", null, Number(Settings.PrayerSnooze, 1, "min", "Prayer snooze")),
            Line("Asr method", "Shafi'i: shadow length 1. Hanafi: 2, later", asr),
            Line("Fajr angle", "Sun below the horizon, degrees", Number(Settings.FajrAngle, 0.5, "°", "Fajr angle")),
            Line("Isha angle", null, Number(Settings.IshaAngle, 0.5, "°", "Isha angle")),
            Line("Calibration", "Minutes added to each time, to match your mosque's timetable", new Border()),
            offsets,
            Buttons(Button("Preview", null, () => Preview(PrayerReminder.Sample()))));
        card.Width = ContentW;
        prayerSettings.Children.Add(card);

        // Times move as these change; redrawn shortly after the last change, so holding a stepper stays smooth.
        var redraw = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        redraw.Tick += (s, e) => { redraw.Stop(); PrayerRender(false); };
        string last = PrayerKey();
        refreshers.Add(() => { var now = PrayerKey(); if (now != last) { last = now; redraw.Stop(); redraw.Start(); } });
        return page;
    }

    static string PrayerKey()
    {
        var o = Prayers.Offset;
        return string.Join(",", o) + Settings.Num(Settings.FajrAngle) + Settings.Num(Settings.IshaAngle) + Settings.On(Settings.Hanafi);
    }

    UIElement[] GeneralCards()
    {
        Action<bool> setStartup = null;
        var startup = Switch(Startup.Enabled, v => { Startup.Enabled = v; setStartup(Startup.Enabled); }, "Start with Windows", out setStartup);
        var keys = new StackPanel();
        foreach (var k in new[] { "Ctrl+Alt+T|Show or hide the todo panel", "Ctrl+Alt+S|Control Center", "Ctrl+Alt+P|Control Center, Prayer page", "Ctrl+Alt+V|Clipboard history" })
        {
            var parts = k.Split('|');
            keys.Children.Add(Line(parts[1], null, T(parts[0], 13, Soft, FontWeights.SemiBold)));
        }
        return new UIElement[]
        {
            Section("Do not disturb", "Keeps reminders and notifications off the island", Toggle(Settings.Dnd, "Do not disturb"),
                Line("Still shows", "The timer alarm, previews and flashes. Reminders wait until it's off; notifications stay in Windows' Notification Center. Also the bell at the top of the island's player", new Border())),
            Section("Prayer", "Prayer times, reminders and the Prayer page. Off hides them all; your prayer log is kept", Toggle(Settings.PrayerOn, "Prayer")),
            Section("Startup", "How WinIsle starts", null,
                Line("Start with Windows", "A shortcut in your Startup folder", startup),
                Line("Show todo panel on start", "Applies next time WinIsle starts", Toggle(Settings.ShowPanel, "Show todo panel on start"))),
            Section("Shortcuts", "Work anywhere in Windows", null, keys),
        };
    }

    UIElement[] WaterCards()
    {
        return new UIElement[]
        {
            Section("Water reminder", "Asks whether you've had a drink", Toggle(Settings.WaterOn, "Water reminder"),
                Dimmed(Settings.WaterOn, "Every", "From your last answer", Number(Settings.WaterEvery, 5, "min", "Water interval")),
                Dimmed(Settings.WaterOn, "Not yet brings it back after", null, Number(Settings.WaterSnooze, 1, "min", "Water snooze")),
                Buttons(Button("Preview", null, () => Preview(WaterReminder.Sample())))),
        };
    }

    UIElement[] BatteryCards()
    {
        Func<int> percent = () => { var p = BatteryReminder.ReadSystem(); return p.HasValue ? p.Value.Percent : 72; };
        return new UIElement[]
        {
            Section("Battery alerts", "An alarm to plug in or unplug the charger", Toggle(Settings.BatteryOn, "Battery alerts"),
                Dimmed(Settings.BatteryOn, "Low at", "Unplugged at or below this", Number(Settings.BatteryLow, 1, "%", "Low battery")),
                Dimmed(Settings.BatteryOn, "Full at", "Plugged in at or above this", Number(Settings.BatteryFull, 1, "%", "Full battery")),
                Dimmed(Settings.BatteryOn, "Dismiss brings it back after", null, Number(Settings.BatteryAgain, 1, "min", "Battery repeat")),
                Buttons(Button("Preview low", null, () => Preview(BatteryReminder.Sample(true))), Button("Preview full", null, () => Preview(BatteryReminder.Sample(false))))),
            Section("Charging animation", "When the charger is plugged in or out", Toggle(Settings.ChargeAnim, "Charging animation"),
                Dimmed(Settings.ChargeAnim, "Shows for", null, Number(Settings.ChargeSeconds, 0.5, "s", "Charging animation length")),
                Buttons(Button("Preview charging", null, () => { if (Program.Island != null) Program.Island.ShowCharging(percent()); }),
                        Button("Preview unplug", null, () => { if (Program.Island != null) Program.Island.ShowUnplugged(percent()); }))),
        };
    }

    UIElement[] NotificationCards()
    {
        return new UIElement[]
        {
            Section("Notifications", "Messages from every app on the island", Toggle(Settings.NotifyOn, "Notifications on the island"),
                Dimmed(Settings.NotifyOn, "Shows for", "Calls stay until answered", Number(Settings.NotifySeconds, 1, "s", "Notification time")),
                Dimmed(Settings.NotifyOn, "Call words", "Comma-separated; these keep a notification until answered", Words(Settings.CallWords, 300, "Call words")),
                Linked("Hide Windows banners", "Avoids seeing each notification twice; they still reach the Notification Center",
                    "Banners are back while notifications are off the island", Toggle(Settings.HideBanners, "Hide Windows banners"), () => Settings.On(Settings.NotifyOn)),
                Buttons(Button("Preview", null, () => Preview(WindowsNotifications.Sample())))),
        };
    }

    UIElement[] FlashCards()
    {
        return new UIElement[]
        {
            Section("Flashes", "Brief messages without buttons", null,
                Line("Volume", null, Toggle(Settings.FlashVolume, "Volume flash")),
                Line("Brightness", null, Toggle(Settings.FlashBrightness, "Brightness flash")),
                Line("Bluetooth", "Connect and disconnect, with battery level", Toggle(Settings.FlashBluetooth, "Bluetooth flash")),
                Line("Camera and microphone dots", "Show when an app is using them", Toggle(Settings.Dots, "Camera and microphone dots")),
                Line("Flashes show for", null, Number(Settings.FlashSeconds, 0.2, "s", "Flash duration")),
                Buttons(Button("Preview volume", null, () =>
                {
                    double level; bool muted;
                    if (Program.Island != null && Program.volume != null && Program.volume.Current(out level, out muted)) Program.Island.Flash(VolumeWatcher.Notice((float)level, muted));
                }))),
        };
    }

    UIElement[] InputCards()
    {
        return new UIElement[]
        {
            Section("Volume", "Keys and mouse", null,
                Linked("Take over volume keys", "The island shows the level instead of Windows' pop-up",
                    "Windows handles the keys while the volume flash is off", Toggle(Settings.VolumeKeys, "Take over volume keys"), () => Settings.On(Settings.FlashVolume)),
                Line("Hold the wheel and scroll", "Changes the volume anywhere on screen", Toggle(Settings.WheelVolume, "Wheel volume")),
                Line("Each step", "Keys, wheel and scrolling on the island", Number(Settings.VolumeStep, 1, "%", "Volume step"))),
        };
    }

    UIElement[] ClipboardCards()
    {
        return new UIElement[]
        {
            Section("Clipboard history", "Records what you copy; pinned items are kept", Toggle(Settings.ClipOn, "Clipboard history"),
                Dimmed(Settings.ClipOn, "Items kept", "Pinned items don't count", Number(Settings.ClipKeep, 10, "", "Clipboard items kept")),
                Buttons(Button("Clear history", "Cleared", () => { if (Program.Clipboard != null) Program.Clipboard.ClearUnpinned(); }))),
        };
    }

    UIElement[] ShelfCards()
    {
        return new UIElement[]
        {
            Section("File shelf", "Holds files while you work; files themselves are never deleted", null,
                Line("Screenshots to shelf", "New Snipping Tool screenshots go onto the shelf", Toggle(Settings.Screenshots, "Screenshots to shelf")),
                Buttons(Button("Clear shelf", "Cleared", () => { if (Program.Island != null) Program.Island.ClearShelf(); }))),
        };
    }

    UIElement[] SoundCards()
    {
        Func<string, string, System.Media.SoundPlayer, Grid> kind = (label, key, sample) =>
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            var play = Button("▶", null, () => IslandSound.Play(sample));
            play.Margin = new Thickness(0, 0, 12, 0);
            controls.Children.Add(play);
            var toggle = (FrameworkElement)Toggle(key, label + " sounds");
            toggle.VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(toggle);
            return Dimmed(Settings.SoundOn, label, null, controls);
        };
        return new UIElement[]
        {
            Section("Sounds", "Generated by the app; there are no sound files", Toggle(Settings.SoundOn, "All sounds"),
                Dimmed(Settings.SoundOn, "Volume", null, Number(Settings.SoundVolume, 5, "%", "Sound volume")),
                kind("Reminders", Settings.SoundReminders, IslandSound.Appear),
                kind("Alarms", Settings.SoundAlarms, IslandSound.Alarm),
                kind("Notifications", Settings.SoundNotifications, IslandSound.Notify),
                kind("Charging", Settings.SoundFlashes, IslandSound.Charge),
                kind("Timer and shelf ticks", Settings.SoundTicks, IslandSound.Tick)),
        };
    }

    UIElement[] AboutCards()
    {
        var exe = Assembly.GetEntryAssembly().Location;
        var checkNote = T("", 12.5, Soft);
        checkNote.VerticalAlignment = VerticalAlignment.Center;
        checkNote.Margin = new Thickness(6, 8, 0, 0);
        var checks = Button("Run self-checks", null, () =>
        {
            checkNote.Text = "Running…";
            Task.Run(() =>
            {
                string result;
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo(exe, "--check") { UseShellExecute = false }))
                    {
                        p.WaitForExit();
                        result = p.ExitCode == 0 ? "All checks passed" : p.ExitCode + (p.ExitCode == 1 ? " check failed" : " checks failed");
                    }
                }
                catch (Exception) { result = "Couldn't run the checks"; }
                Dispatcher.BeginInvoke(new Action(() => checkNote.Text = result));
            });
        });
        var checkRow = Buttons(checks, checkNote);

        // Reset asks twice: the first click arms it for 3 seconds.
        bool armed = false;
        Border reset = null;
        reset = Pill("Reset all settings", false, () =>
        {
            var text = (TextBlock)reset.Child;
            if (!armed)
            {
                armed = true;
                text.Text = "Click again to reset";
                text.Foreground = Rgb(255, 159, 10);
                After(3000, () => { armed = false; text.Text = "Reset all settings"; text.Foreground = Soft; });
                return;
            }
            armed = false;
            Settings.ResetAll();
            text.Text = "Reset";
            text.Foreground = Soft;
        });

        return new UIElement[]
        {
            Section("WinIsle", "Built " + File.GetLastWriteTime(exe).ToString("MMM d, yyyy h:mm tt", Inv), null,
                Line("Data folder", AppPaths.Folder, Button("Open", null, () => { try { Process.Start("explorer.exe", "\"" + AppPaths.Folder + "\""); } catch (Exception) { } })),
                checkRow),
            Section("Reset and quit", "Reset puts every setting back to its default; your tasks, prayers and pins stay", null,
                Buttons(reset, Button("Quit WinIsle", null, () => Application.Current.Shutdown()))),
        };
    }
}
