using System;
using System.Windows.Media;
using System.Windows.Threading;

// Plugging in the charger flashes the island with the battery level filling up, like a phone.
static class ChargeWatcher
{
    static readonly Color Accent = Color.FromRgb(48, 209, 88);

    internal static void Watch(Action<IslandNotice> flash)
    {
        bool? wasCharging = null; // unknown until the first read, so starting the app plugged in doesn't flash
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (s, e) =>
        {
            var power = BatteryReminder.ReadSystem();
            if (power == null) return;
            if (wasCharging == false && power.Value.Charging) flash(Notice(power.Value.Percent));
            wasCharging = power.Value.Charging;
        };
        timer.Start();
    }

    internal static IslandNotice Notice(int percent)
    {
        return new IslandNotice
        {
            Key = "charging", Title = "Charging", Time = percent + "%", Level = percent / 100.0,
            Icon = BatteryReminder.BatteryShape, Accent = Accent, Sound = IslandSound.Charge,
        };
    }
}
