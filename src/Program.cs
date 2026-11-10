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
        ChargeWatcher.Watch(island.Flash);
        volume = VolumeWatcher.Watch(island.Dispatcher, island.Flash);
        BrightnessWatcher.Watch(island.Dispatcher, island.Flash);
        BluetoothWatcher.Watch(island.Dispatcher, island.Flash);
        ScreenshotWatcher.Watch(island.Dispatcher, island.AddScreenshot);
        PrivacyWatcher.Watch(island);
    }
}
