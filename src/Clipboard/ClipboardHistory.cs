using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// One thing that was copied: text, an image or a list of files.
class ClipItem
{
    public string Text;
    public BitmapSource Image;
    public string[] Files;
    public ImageSource[] Thumbs; // previews of the first few files, taken when copied; null entries for missing files
    public DateTime Copied; // UTC
    public bool Pinned;
    public string Key;      // same key = same content, so copying it again moves it to the top

    public bool IsLink
    {
        get
        {
            if (Text == null) return false;
            var t = Text.Trim();
            return !t.Contains("\n") && (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static ClipItem FromText(string text, DateTime copied) { return new ClipItem { Text = text, Key = "text:" + text, Copied = copied }; }
}

// Everything copied since the app started (in memory only), plus pinned text that is kept on disk.
class ClipboardHistory
{
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    internal const int Keep = 60; // unpinned items; pinned ones are never dropped
    internal const int MaxThumbs = 4; // file previews per item; the rest show as "+N"
    const int WM_CLIPBOARDUPDATE = 0x031D;

    readonly List<ClipItem> items = new List<ClipItem>();
    string pinFile; // null in checks, so they never touch the real file
    HwndSource listener;
    DateTime ignoreUntil; // our own paste changes the clipboard too

    internal event Action Changed;

    // Pinned first, then newest first.
    internal List<ClipItem> Items
    {
        get { return items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.Copied).ToList(); }
    }

    internal static ClipboardHistory Live()
    {
        var history = new ClipboardHistory { pinFile = AppPaths.For("clipboard-pins.txt") };
        history.LoadPins();
        // A message-only window that Windows tells about every clipboard change.
        history.listener = new HwndSource(new HwndSourceParameters("MyTodo clipboard") { ParentWindow = new IntPtr(-3) });
        history.listener.AddHook(history.Hook);
        AddClipboardFormatListener(history.listener.Handle);
        return history;
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && DateTime.UtcNow >= ignoreUntil) Capture(4);
        return IntPtr.Zero;
    }

    // The app that copied may still hold the clipboard open for a moment, so reading retries.
    void Capture(int tries)
    {
        try
        {
            var item = Read();
            if (item != null) Add(item);
        }
        catch (Exception)
        {
            if (tries > 1) Later(70, () => Capture(tries - 1));
        }
    }

    internal void Add(ClipItem item)
    {
        var same = items.FirstOrDefault(i => i.Key == item.Key);
        if (same != null) same.Copied = item.Copied;
        else items.Add(item);
        Trim();
        Raise();
    }

    void Trim()
    {
        foreach (var old in items.Where(i => !i.Pinned).OrderByDescending(i => i.Copied).Skip(Keep).ToList()) items.Remove(old);
    }

    internal void Remove(ClipItem item)
    {
        items.Remove(item);
        if (item.Pinned) SavePins();
        Raise();
    }

    internal void TogglePin(ClipItem item)
    {
        item.Pinned = !item.Pinned;
        SavePins();
        Trim();
        Raise();
    }

    void Raise() { if (Changed != null) Changed(); }

    internal static bool Matches(ClipItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        query = query.Trim();
        if (item.Text != null) return item.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        if (item.Files != null) return item.Files.Any(f => Path.GetFileName(f).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        return "image".StartsWith(query, StringComparison.OrdinalIgnoreCase);
    }

    // "now", "5m", "3h", "2d"
    internal static string Ago(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "now";
        if (t.TotalHours < 1) return (int)t.TotalMinutes + "m";
        if (t.TotalDays < 1) return (int)t.TotalHours + "h";
        return (int)t.TotalDays + "d";
    }

    // Puts the item back on the clipboard and pastes it into the window that was in front.
    internal void Paste(ClipItem item, IntPtr target)
    {
        ignoreUntil = DateTime.UtcNow.AddMilliseconds(500);
        for (int attempt = 0; ; attempt++)
        {
            try { Put(item); break; }
            catch (Exception) { if (attempt == 5) return; Thread.Sleep(40); } // another app is holding the clipboard
        }
        item.Copied = DateTime.UtcNow;
        Raise();
        if (target == IntPtr.Zero) return;
        SetForegroundWindow(target);
        Later(90, () =>
        {
            const byte Control = 0x11, V = 0x56;
            const uint Up = 2;
            keybd_event(Control, 0, 0, UIntPtr.Zero);
            keybd_event(V, 0, 0, UIntPtr.Zero);
            keybd_event(V, 0, Up, UIntPtr.Zero);
            keybd_event(Control, 0, Up, UIntPtr.Zero);
        });
    }

    static void Put(ClipItem item)
    {
        if (item.Files != null)
        {
            var files = new StringCollection();
            files.AddRange(item.Files);
            Clipboard.SetFileDropList(files);
        }
        else if (item.Image != null) Clipboard.SetImage(item.Image);
        else Clipboard.SetText(item.Text);
    }

    static ClipItem Read()
    {
        var data = Clipboard.GetDataObject();
        if (data == null) return null; // records everything, including copies password managers mark private (your choice)
        var now = DateTime.UtcNow;
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
                return new ClipItem { Files = files, Thumbs = files.Take(MaxThumbs).Select(AppIcon.ForFile).ToArray(), Key = "files:" + string.Join("|", files), Copied = now };
        }
        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = data.GetData(DataFormats.UnicodeText) as string;
            if (!string.IsNullOrWhiteSpace(text)) return ClipItem.FromText(text, now);
        }
        if (Clipboard.ContainsImage())
        {
            var image = Clipboard.GetImage();
            if (image != null)
            {
                var copy = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                var frozen = new WriteableBitmap(copy);
                frozen.Freeze();
                return new ClipItem { Image = frozen, Key = "image:" + Fingerprint(frozen), Copied = now };
            }
        }
        return null;
    }

    // Same picture copied twice gives the same key.
    static string Fingerprint(BitmapSource image)
    {
        int stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        uint hash = 2166136261;
        for (int i = 0; i < pixels.Length; i += 31) hash = (hash ^ pixels[i]) * 16777619;
        return image.PixelWidth + "x" + image.PixelHeight + ":" + hash;
    }

    // ---- pins: clipboard-pins.txt, one Base64 UTF-8 text per line, newest first ----

    void LoadPins()
    {
        try
        {
            if (!File.Exists(pinFile)) return;
            var lines = File.ReadAllLines(pinFile);
            var stamp = DateTime.UtcNow;
            foreach (var line in lines.Where(l => l.Length > 0))
            {
                var item = ClipItem.FromText(Encoding.UTF8.GetString(Convert.FromBase64String(line)), stamp);
                item.Pinned = true;
                items.Add(item);
                stamp = stamp.AddSeconds(-1); // keeps the saved order
            }
        }
        catch (Exception) { } // a damaged file just starts without pins
    }

    // ponytail: only text pins are saved; pinned images and files last until the app closes.
    void SavePins()
    {
        if (pinFile == null) return;
        try
        {
            var lines = Items.Where(i => i.Pinned && i.Text != null).Select(i => Convert.ToBase64String(Encoding.UTF8.GetBytes(i.Text))).ToArray();
            File.WriteAllLines(pinFile, lines);
        }
        catch (Exception) { }
    }

    static void Later(int ms, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (s, e) => { t.Stop(); action(); };
        t.Start();
    }
}
