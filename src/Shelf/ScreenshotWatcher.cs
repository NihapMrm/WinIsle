using System;
using System.IO;
using System.Linq;
using System.Windows.Threading;

// New screenshots go on the shelf. Snipping Tool (Win+Shift+S, Print Screen) saves them to Pictures\Screenshots
// while "Automatically save screenshots" is on in its settings, which is the default.
static class ScreenshotWatcher
{
    static readonly string[] Pictures = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    static FileSystemWatcher watcher; // kept alive for the life of the app

    internal static string Folder()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }

    internal static bool IsPicture(string path)
    {
        return Pictures.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    internal static void Watch(Dispatcher ui, Action<string> added)
    {
        var folder = Folder();
        if (!Directory.Exists(folder)) return; // auto-save is off; nothing to watch
        try
        {
            watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
            FileSystemEventHandler created = (s, e) => Arrived(ui, added, e.FullPath);
            watcher.Created += created;
            watcher.Renamed += (s, e) => Arrived(ui, added, e.FullPath); // some tools write a temp name, then rename
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception) { watcher = null; }
    }

    // Waits a moment so the file is fully written before its thumbnail is read.
    static void Arrived(Dispatcher ui, Action<string> added, string path)
    {
        if (!IsPicture(path)) return;
        ui.BeginInvoke(new Action(() =>
        {
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            wait.Tick += (s, e) => { wait.Stop(); if (File.Exists(path)) added(path); };
            wait.Start();
        }));
    }
}
