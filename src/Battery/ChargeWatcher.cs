using System;
using System.Windows.Threading;

// Plugging in the charger plays the island's charging animation with the battery level (IslandWindow.Charge.cs).
static class ChargeWatcher
{
    internal static void Watch(Action<int> pluggedIn)
    {
        bool? wasCharging = null; // unknown until the first read, so starting the app plugged in doesn't animate
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (s, e) =>
        {
            var power = BatteryReminder.ReadSystem();
            if (power == null) return;
            if (wasCharging == false && power.Value.Charging) pluggedIn(power.Value.Percent);
            wasCharging = power.Value.Charging;
        };
        timer.Start();
    }
}
