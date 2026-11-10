using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;

// Which apps are using the camera or microphone right now, read from the record Windows keeps for its own
// privacy indicator: an app is in use while its LastUsedTimeStart is set and LastUsedTimeStop is 0.
static class PrivacyWatcher
{
    const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    internal static void Watch(IslandWindow island)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (s, e) =>
        {
            try { island.ShowInUse(InUse("webcam"), InUse("microphone")); } catch (Exception) { }
        };
        timer.Start();
    }

    internal static List<string> InUse(string capability)
    {
        var users = new List<string>();
        using (var root = Registry.CurrentUser.OpenSubKey(Store + capability))
        {
            if (root == null) return users;
            Collect(root, users);
            using (var desktop = root.OpenSubKey("NonPackaged")) if (desktop != null) Collect(desktop, users);
        }
        return users;
    }

    static void Collect(RegistryKey parent, List<string> users)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (name == "NonPackaged") continue;
            using (var app = parent.OpenSubKey(name))
            {
                if (app == null) continue;
                if (!Active(name, app.GetValue("LastUsedTimeStart") as long?, app.GetValue("LastUsedTimeStop") as long?, IsRunning)) continue;
                var friendly = Friendly(name);
                if (!users.Contains(friendly)) users.Add(friendly);
            }
        }
    }

    // Started and not stopped. A desktop app that closed or crashed without telling Windows keeps "not stopped"
    // forever (PowerPoint did), so for those the program must also still be running.
    internal static bool Active(string key, long? start, long? stop, Func<string, bool> isRunning)
    {
        if (!(start > 0 && stop == 0)) return false;
        return !key.Contains("#") || isRunning(Path.GetFileNameWithoutExtension(key.Substring(key.LastIndexOf('#') + 1)));
    }

    static bool IsRunning(string processName)
    {
        var found = System.Diagnostics.Process.GetProcessesByName(processName);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    // Desktop apps are stored as their path with # for \ ("C:#Program Files#Zoom#bin#Zoom.exe" -> "Zoom");
    // Store apps by package name ("Microsoft.WindowsCamera_8wekyb3d8bbwe" -> "WindowsCamera").
    internal static string Friendly(string key)
    {
        if (key.Contains("#")) return Path.GetFileNameWithoutExtension(key.Substring(key.LastIndexOf('#') + 1));
        var name = key.Split('_')[0];
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot + 1) : name;
    }
}
