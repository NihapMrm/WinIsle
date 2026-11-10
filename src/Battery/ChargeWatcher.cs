using System;
using System.Windows.Threading;

// Plugging the charger in or out plays the island's power animation with the battery level (IslandWindow.Charge.cs).
static class ChargeWatcher
{
    internal static void Watch(Action<int> pluggedIn, Action<int> unplugged)
    {
        bool? wasCharging = null; // unknown until the first read, so starting the app doesn't animate
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; // quick, so the pill answers the plug at once
        timer.Tick += (s, e) =>
        {
            var power = BatteryReminder.ReadSystem();
            if (power == null) return;
            if (wasCharging == false && power.Value.Charging) pluggedIn(power.Value.Percent);
            if (wasCharging == true && !power.Value.Charging) unplugged(power.Value.Percent);
            wasCharging = power.Value.Charging;
        };
        timer.Start();
    }
}
