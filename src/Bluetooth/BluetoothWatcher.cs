using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;

// Flashes the island when a Bluetooth device connects or disconnects, with its battery when Windows knows it.
// All WinRT calls stay on one background thread, as with the media and notification readers.
static class BluetoothWatcher
{
    // Battery level as Windows shows it in Settings; published on the device's hands-free profile node.
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    static readonly System.Windows.Media.Geometry Rune = System.Windows.Media.Geometry.Parse("F0 M11,2 L17,7.5 L12.5,12 L17,16.5 L11,22 V14.5 L7,18 L5.8,16.8 L10.6,12 L5.8,7.2 L7,6 L11,9.5 Z M13,6.6 V10.3 L15,8.4 Z M13,13.7 V17.4 L15,15.6 Z");
    static readonly System.Windows.Media.Color Accent = System.Windows.Media.Color.FromRgb(10, 132, 255);

    internal static void Watch(Dispatcher ui, Action<IslandNotice> flash)
    {
        new Thread(() => Poll(ui, flash)) { IsBackground = true }.Start();
    }

    // ponytail: polls every 3 s; WinRT events don't compile with this build's references.
    static void Poll(Dispatcher ui, Action<IslandNotice> flash)
    {
        Dictionary<string, string> before = null; // id -> name; null until the first read, so startup doesn't flash
        while (true)
        {
            try
            {
                var now = Connected();
                if (before != null)
                {
                    foreach (var id in now.Keys.Where(k => !before.ContainsKey(k)))
                    {
                        var name = now[id];
                        int? battery = Battery(name);
                        ui.BeginInvoke(new Action(() => flash(Notice(name, true, battery))));
                    }
                    foreach (var id in before.Keys.Where(k => !now.ContainsKey(k)))
                    {
                        var name = before[id];
                        ui.BeginInvoke(new Action(() => flash(Notice(name, false, null))));
                    }
                }
                before = now;
            }
            catch (Exception) { } // Bluetooth off or missing: try again later
            Thread.Sleep(3000);
        }
    }

    static Dictionary<string, string> Connected()
    {
        var found = new Dictionary<string, string>();
        foreach (var selector in new[] { BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected) })
            foreach (var d in Wait(DeviceInformation.FindAllAsync(selector)))
                if (!found.Values.Contains(d.Name)) found[d.Id] = d.Name; // a device seen as both classic and LE counts once
        return found;
    }

    // Reads every device node (about 2 s), so only when something has just connected.
    static int? Battery(string device)
    {
        try
        {
            var nodes = Wait(DeviceInformation.FindAllAsync("", new[] { BatteryKey }, DeviceInformationKind.Device));
            return BatteryFor(device, nodes.Select(n => new KeyValuePair<string, object>(n.Name, n.Properties.ContainsKey(BatteryKey) ? n.Properties[BatteryKey] : null)));
        }
        catch (Exception) { return null; }
    }

    // The battery node is named after the device, e.g. "Redmi Note 13 Hands-Free HF".
    internal static int? BatteryFor(string device, IEnumerable<KeyValuePair<string, object>> nodes)
    {
        foreach (var node in nodes)
            if (node.Value != null && node.Key != null && node.Key.StartsWith(device, StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(node.Value);
        return null;
    }

    internal static IslandNotice Notice(string name, bool connected, int? battery)
    {
        return new IslandNotice
        {
            Key = "bluetooth", Title = name, Icon = Rune, Accent = Accent,
            Time = !connected ? "Disconnected" : battery.HasValue ? battery + "%" : "Connected",
            Sound = connected ? IslandSound.Notify : null,
        };
    }

    static T Wait<T>(IAsyncOperation<T> op)
    {
        var timer = Stopwatch.StartNew();
        while (op.Status == AsyncStatus.Started)
        {
            if (timer.ElapsedMilliseconds > 8000) throw new TimeoutException();
            Thread.Sleep(10);
        }
        return op.GetResults();
    }
}
