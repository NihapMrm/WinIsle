using System;
using System.Linq;
using System.Windows;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--check")) Environment.Exit(Checks.Run());
        if (args.Contains("--restore-banners")) { WindowsNotifications.RestoreBanners(WindowsNotifications.SettingsKey); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        // --demo-power plays the whole battery story with a pretend battery: low alert at 15%, plug in (the
        // charging pill takes over), full alert at 95%, unplug (the unplug pill takes over). About 20 s.
        if (args.Contains("--demo-power"))
        {
            var power = new BatteryReminder.Power { Percent = 15, Charging = false };
            var demoIsland = new IslandWindow(null, null, null, new BatteryReminder(() => power));
            var steps = new Action[]
            {
                () => { },                                                                          // 0 s: low alert rings
                () => { power.Charging = true; demoIsland.ShowCharging(power.Percent); },             // 4 s: plug in
                () => { power.Percent = 95; },                                                     // 9 s: full alert rings
                () => { power.Charging = false; demoIsland.ShowUnplugged(power.Percent); },           // 14 s: unplug
                () => app.Shutdown(),                                                              // 19 s
            };
            int[] at = { 0, 4000, 9000, 14000, 19000 };
            demoIsland.Loaded += (s, e) =>
            {
                for (int i = 1; i < steps.Length; i++)
                {
                    var step = steps[i];
                    var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(at[i]) };
                    wait.Tick += (s2, e2) => { wait.Stop(); step(); };
                    wait.Start();
                }
            };
            app.Run(demoIsland);
            return;
        }
        // --demo-charge / --demo-unplug play the plug-in / unplug animation once (at 72%) and exit.
        if (args.Contains("--demo-charge") || args.Contains("--demo-unplug"))
        {
            bool unplug = args.Contains("--demo-unplug");
            var preview = new IslandWindow(null, null, null);
            preview.Loaded += (s, e) =>
            {
                var play = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                play.Tick += (s2, e2) => { play.Stop(); if (unplug) preview.ShowUnplugged(72); else preview.ShowCharging(72); };
                play.Start();
                var quit = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4500) };
                quit.Tick += (s2, e2) => app.Shutdown();
                quit.Start();
            };
            app.Run(preview);
            return;
        }
        // --demo and --demo-water preview one island alone, with "Not yet" coming back after 10 seconds.
        IReminder demo = args.Contains("--demo-water") ? new WaterReminder(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10), TimeSpan.Zero)
            : args.Contains("--demo") ? (IReminder)PrayerReminder.Demo() : null;
        if (demo == null)
        {
            AppPaths.Prepare();
            app.Run(new TodoPanel());
            return;
        }
        var island = new IslandWindow(null, null, null, demo);
        island.Confirmed += app.Shutdown;
        app.Run(island);
    }

    internal static ClipboardWindow ClipboardPanel; // Ctrl+Alt+V and the island's clipboard button
    internal static ClipboardHistory Clipboard;     // the Control Center's "Clear history"
    internal static IslandWindow Island;            // the Control Center's previews
    internal static PreviewReminder Preview;

    // Volume, brightness and Bluetooth flashes can each be switched off in the Control Center; others always show.
    internal static bool Allowed(IslandNotice n)
    {
        switch (n.Key)
        {
            case "volume": return Settings.On(Settings.FlashVolume);
            case "brightness": return Settings.On(Settings.FlashBrightness);
            case "bluetooth": return Settings.On(Settings.FlashBluetooth);
            default: return true;
        }
    }
    static VolumeWatcher volume; // Windows holds only a COM pointer, so the app keeps it alive

    internal static void StartReminders()
    {
        Settings.Load(); // first: every feature below reads it
        var clipboard = Clipboard = ClipboardHistory.Live();
        ClipboardPanel = new ClipboardWindow(clipboard);
        Prayers.LoadSettings();
        Tracker.Load();
        // Highest priority first. New reminders are added here.
        var timer = new CountdownTimer();
        Preview = new PreviewReminder(); // first: a Control Center preview goes ahead of everything
        var island = Island = new IslandWindow(MediaSession.Live(), timer, FileShelf.Live(), Preview, timer, new BatteryReminder(), PrayerReminder.Live(), new WaterReminder(), WindowsNotifications.Live());
        island.Show();
        // Brief flashes and the camera / mic dots. Each watcher fails quietly where the PC lacks the hardware.
        ChargeWatcher.Watch(island.ShowCharging, island.ShowUnplugged);
        volume = VolumeWatcher.Watch(island.Dispatcher, island.Flash);
        island.Volume = volume; // the player's volume bar and scrolling
        BrightnessWatcher.Watch(island.Dispatcher, island.Flash);
        BluetoothWatcher.Watch(island.Dispatcher, island.Flash);
        ScreenshotWatcher.Watch(island.Dispatcher, island.AddScreenshot);
        PrivacyWatcher.Watch(island);
        // A paste that can't reach the clipboard says which app is holding it, instead of failing silently.
        clipboard.Busy += holder => island.Flash(new IslandNotice
        {
            Key = "clipboard-busy", Title = "Clipboard busy", Time = holder, Accent = System.Windows.Media.Color.FromRgb(255, 159, 10),
            Icon = System.Windows.Media.Geometry.Parse("M12,2 L22,20 H2 Z M11,9 V14 H13 V9 Z M11,16 V18 H13 V16 Z"), Sound = IslandSound.NotYet,
        });
    }
}
