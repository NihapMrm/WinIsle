using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

// Shows notifications from every app on the island. Lowest priority, closes by itself, and keeps nothing on disk.
class WindowsNotifications : IReminder
{
    internal class Incoming { public string App, AppId; public string[] Lines; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    internal const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    internal const string BannerValue = "ShowBanner";

    static readonly System.Text.RegularExpressions.Regex CallWords = new System.Text.RegularExpressions.Regex(
        @"\b(incoming\b.*\bcall|voice call|video call|calling)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static readonly TimeSpan Visible = TimeSpan.FromSeconds(6);
    static readonly Color Accent = Color.FromRgb(180, 170, 255);
    static readonly Geometry Bell = Geometry.Parse("M12,2 C8.7,2 6,4.7 6,8 V12 L4,16 H20 L18,12 V8 C18,4.7 15.3,2 12,2 Z M9.5,18 A2.5,2.5 0 0 0 14.5,18 Z");

    readonly ConcurrentQueue<Incoming> incoming = new ConcurrentQueue<Incoming>();
    readonly List<Incoming> waiting = new List<Incoming>();
    readonly TimeSpan visible;
    IslandNotice current;
    DateTime until;

    internal WindowsNotifications(TimeSpan visible) { this.visible = visible; }

    // Must be called on the UI thread: Windows asks for the permission from there.
    internal static WindowsNotifications Live()
    {
        var live = new WindowsNotifications(Visible);
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Unspecified) { var asked = listener.RequestAccessAsync(); }
            new Thread(live.Poll) { IsBackground = true }.Start();
        }
        catch (Exception) { } // No listener on this Windows: prayer and water still work.
        return live;
    }

    internal void Add(Incoming item) { incoming.Enqueue(item); }

    // ponytail: polls once a second because NotificationChanged is reported to fail in unpackaged apps.
    void Poll()
    {
        HashSet<uint> seen = null; // null on the first read, so old notifications don't replay at startup
        while (true)
        {
            try
            {
                var listener = UserNotificationListener.Current; // fetched on this thread, not passed from the UI thread
                if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
                {
                    var read = listener.GetNotificationsAsync(NotificationKinds.Toast);
                    while (read.Status == Windows.Foundation.AsyncStatus.Started) Thread.Sleep(5);
                    var now = read.GetResults();
                    if (seen != null)
                        foreach (var n in now.Where(x => !seen.Contains(x.Id)).OrderBy(x => x.CreationTime))
                        {
                            var item = Parse(n);
                            Add(item);
                            HideBanner(SettingsKey, item.AppId); // covers apps that first appear after startup
                        }
                    else
                        using (var settings = Registry.CurrentUser.OpenSubKey(SettingsKey)) // only once reading works, so notifications can't vanish
                            foreach (var app in settings.GetSubKeyNames()) HideBanner(SettingsKey, app);
                    seen = new HashSet<uint>(now.Select(x => x.Id));
                }
            }
            catch (Exception) { } // A failed read retries on the next pass.
            Thread.Sleep(1000);
        }
    }

    // Turns off the Windows banner for one app. Its notifications still reach the island and Notification Center.
    internal static void HideBanner(string root, string appId)
    {
        if (appId == "") return;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(root + "\\" + appId, true))
                if (key != null) key.SetValue(BannerValue, 0, RegistryValueKind.DWord);
        }
        catch (Exception) { } // A locked setting just leaves that app's banner on.
    }

    // TodoPanel.exe --restore-banners. Walks nested keys too: some app ids contain backslashes.
    internal static void RestoreBanners(string root)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(root, true))
        {
            if (key == null) return;
            key.DeleteValue(BannerValue, false);
            foreach (var name in key.GetSubKeyNames()) RestoreBanners(root + "\\" + name);
        }
    }

    // Apps can send odd or partial notifications, so each field fails on its own.
    static Incoming Parse(UserNotification n)
    {
        var item = new Incoming { App = "Notification", AppId = "", Lines = new string[0] };
        try { item.AppId = n.AppInfo.AppUserModelId; item.App = n.AppInfo.DisplayInfo.DisplayName; } catch (Exception) { }
        try
        {
            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            item.Lines = binding.GetTextElements().Select(t => t.Text.Replace('\r', ' ').Replace('\n', ' ').Trim()).Where(t => t != "").ToArray();
        }
        catch (Exception) { }
        return item;
    }

    public IslandNotice Due()
    {
        Incoming item;
        while (incoming.TryDequeue(out item)) waiting.Add(item);
        if (current != null && DateTime.UtcNow >= until) current = null; // ran out while a prayer or water reminder took over
        if (current != null || waiting.Count == 0) return current;
        var head = waiting.FirstOrDefault(IsCall) ?? waiting[0]; // a call goes ahead of the queue and never merges
        var group = IsCall(head) ? new List<Incoming> { head } : waiting.Where(x => x.App == head.App && !IsCall(x)).ToList();
        waiting.RemoveAll(group.Contains);
        current = Build(group);
        until = DateTime.MaxValue; // the clock starts when the island first asks StillCurrent
        return current;
    }

    // ponytail: listeners can't see a toast's call flag, so this reads English text. Other languages close
    // normally, and a short chat line that sounds like a call stays until Dismiss.
    internal static bool IsCall(Incoming item)
    {
        return !item.Lines.Any(l => l.IndexOf("missed", StringComparison.OrdinalIgnoreCase) >= 0)
            && item.Lines.Any(l => l.Length <= 40 && CallWords.IsMatch(l));
    }

    static IslandNotice Build(List<Incoming> group)
    {
        var last = group[group.Count - 1];
        string title, detail;
        if (group.Count > 1)
        {
            title = last.App;
            detail = group.Count + " new · " + string.Join(" ", last.Lines);
        }
        else
        {
            var body = string.Join(" ", last.Lines.Skip(1));
            title = last.Lines.Length > 0 ? last.Lines[0] : last.App;
            detail = body == "" ? last.App : last.App + " · " + body;
        }
        return new IslandNotice
        {
            Key = last.AppId, Title = title, Detail = detail, YesLabel = "Open", NoLabel = "Dismiss",
            Time = Prayers.Now.ToString("h:mm tt", CultureInfo.InvariantCulture), Icon = Bell, Logo = AppIcon.For(last.AppId), Accent = Accent, Sound = IslandSound.Notify, Small = true, Sticky = IsCall(last),
        };
    }

    public bool StillCurrent(IslandNotice shown)
    {
        if (shown.Sticky) return true;
        if (until == DateTime.MaxValue) until = DateTime.UtcNow + visible;
        if (DateTime.UtcNow < until) return true;
        current = null;
        return false;
    }

    public IslandResult Answer(IslandNotice shown, bool yes)
    {
        current = null;
        return new IslandResult { Left = !yes ? "Dismissed" : Launch(shown.Key) ? "Opening" : "Can't open", Right = shown.Title };
    }

    static bool Launch(string appId)
    {
        if (appId == "") return false;
        try
        {
            AllowSetForegroundWindow(-1); // the island never takes focus, so it must pass on its click's right to come to the front
            Process.Start(LaunchTarget(appId));
            return true;
        }
        catch (Exception) { return false; }
    }

    // Some apps register their exe path as their id, which the Apps folder can't open.
    internal static ProcessStartInfo LaunchTarget(string appId)
    {
        if (File.Exists(appId) && Path.GetExtension(appId).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return new ProcessStartInfo(appId) { WorkingDirectory = Path.GetDirectoryName(appId) };
        return new ProcessStartInfo("explorer.exe", "\"shell:AppsFolder\\" + appId.Replace("\"", "") + "\"");
    }
}
