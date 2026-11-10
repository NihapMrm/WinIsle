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
        // --demo-charge plays the charging animation once (at 72%) and exits.
        if (args.Contains("--demo-charge"))
        {
            var preview = new IslandWindow(null, null, null);
            preview.Loaded += (s, e) =>
            {
                var play = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                play.Tick += (s2, e2) => { play.Stop(); preview.ShowCharging(72); };
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
    static VolumeWatcher volume; // Windows holds only a COM pointer, so the app keeps it alive

    internal static void StartReminders()
    {
        ClipboardPanel = new ClipboardWindow(ClipboardHistory.Live());
        Prayers.LoadSettings();
        Tracker.Load();
        // Highest priority first. New reminders are added here.
        var timer = new CountdownTimer();
        var island = new IslandWindow(MediaSession.Live(), timer, FileShelf.Live(), timer, new BatteryReminder(), PrayerReminder.Live(), new WaterReminder(), WindowsNotifications.Live());
        island.Show();
        // Brief flashes and the camera / mic dots. Each watcher fails quietly where the PC lacks the hardware.
        ChargeWatcher.Watch(island.ShowCharging);
        volume = VolumeWatcher.Watch(island.Dispatcher, island.Flash);
        BrightnessWatcher.Watch(island.Dispatcher, island.Flash);
        BluetoothWatcher.Watch(island.Dispatcher, island.Flash);
        ScreenshotWatcher.Watch(island.Dispatcher, island.AddScreenshot);
        PrivacyWatcher.Watch(island);
    }
}
