using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Pictures from the Windows Shell: an app's icon by its app id (as the Start menu does), or a file's thumbnail.
static class AppIcon
{
    const string FactoryId = "bcc18b79-ba16-442f-80c4-8a59c30c463b";
    const int IconOnly = 0x4;

    [ComImport, Guid(FactoryId), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(PixelSize size, int flags, out IntPtr bitmap); }

    [StructLayout(LayoutKind.Sequential)] struct PixelSize { public int Width, Height; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr context, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);

    static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();

    // Null when the app has no Shell entry, so the caller shows its own icon.
    // ponytail: runs on the UI thread and can take 200 ms, but only once per app per run because of the cache.
    internal static ImageSource For(string appId)
    {
        ImageSource icon;
        if (Cache.TryGetValue(appId, out icon)) return icon;
        return Cache[appId] = appId == "" ? null : Load("shell:AppsFolder\\" + appId, 96, IconOnly);
    }

    // A file's or folder's preview as Explorer shows it: a real thumbnail for pictures, videos and documents,
    // otherwise its icon. Null when the path is gone. Not cached: the file can change between copies.
    internal static ImageSource ForFile(string path)
    {
        return File.Exists(path) || Directory.Exists(path) ? Load(path, 128, 0) : null;
    }

    static ImageSource Load(string parsingName, int pixels, int flags)
    {
        try
        {
            IShellItemImageFactory item;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, new Guid(FactoryId), out item);
            IntPtr handle;
            if (item.GetImage(new PixelSize { Width = pixels, Height = pixels }, flags, out handle) != 0) return null;
            var image = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(handle);
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }
}
