using System;
using System.Management;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;

// Flashes the island with the new screen brightness. Windows reports every change of the built-in screen,
// including automatic (adaptive) brightness. External monitors and desktops report nothing.
static class BrightnessWatcher
{
    static readonly Geometry Sun = Geometry.Parse("M12,8 A4,4 0 1 1 11.99,8 Z M11,2 H13 V5 H11 Z M11,19 H13 V22 H11 Z M2,11 H5 V13 H2 Z M19,11 H22 V13 H19 Z M4.2,5.6 L5.6,4.2 L7.7,6.3 L6.3,7.7 Z M16.3,17.7 L17.7,16.3 L19.8,18.4 L18.4,19.8 Z M4.2,18.4 L6.3,16.3 L7.7,17.7 L5.6,19.8 Z M16.3,6.3 L18.4,4.2 L19.8,5.6 L17.7,7.7 Z");
    static readonly Color Accent = Color.FromRgb(255, 214, 10);
    static ManagementEventWatcher watcher; // kept alive for the life of the app

    internal static void Watch(Dispatcher ui, Action<IslandNotice> flash)
    {
        try
        {
            watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.EventArrived += (s, e) =>
            {
                int percent = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
                ui.BeginInvoke(new Action(() =>
                {
                    var notice = Notice(percent);
                    notice.SetLevel = level => Set((int)Math.Round(level * 100));
                    flash(notice);
                }));
            };
            watcher.Start();
        }
        catch (Exception) { watcher = null; } // no built-in screen
    }

    // Setting brightness takes a moment, so it runs off the UI thread, and a drag only sends its latest value.
    static readonly object gate = new object();
    static int pending = -1;
    static bool setting;

    static void Set(int percent)
    {
        lock (gate)
        {
            pending = percent;
            if (setting) return;
            setting = true;
        }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            while (true)
            {
                int next;
                lock (gate)
                {
                    next = pending;
                    pending = -1;
                    if (next < 0) { setting = false; return; }
                }
                try
                {
                    using (var screens = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null))
                        foreach (ManagementObject screen in screens.GetInstances())
                            using (screen) screen.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)next });
                }
                catch (Exception) { } // the screen refused; the bar snaps back with the next report
            }
        });
    }

    internal static IslandNotice Notice(int percent)
    {
        return new IslandNotice { Key = "brightness", Title = "Brightness", Time = percent + "%", Level = percent / 100.0, Icon = Sun, Accent = Accent };
    }
}
