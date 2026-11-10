using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

// Streams the default camera to the UI thread for the mirror. Frames stay in memory; nothing is saved.
// All WinRT objects live on the camera's own thread, which also turns the camera off when stopped.
class Camera
{
    readonly Dispatcher ui;
    readonly Action<byte[], int, int> frame; // on the UI thread, with the lock held so the pixels can't change mid-copy
    readonly Action<string> failed;          // on the UI thread
    readonly object gate = new object();
    volatile bool running = true;
    byte[] pixels;
    int width, height;
    bool posted;

    Camera(Dispatcher ui, Action<byte[], int, int> frame, Action<string> failed)
    {
        this.ui = ui;
        this.frame = frame;
        this.failed = failed;
    }

    internal static Camera Start(Dispatcher ui, Action<byte[], int, int> frame, Action<string> failed)
    {
        var camera = new Camera(ui, frame, failed);
        new Thread(camera.Run) { IsBackground = true }.Start();
        return camera;
    }

    internal void Stop() { running = false; }

    void Run()
    {
        MediaCapture capture = null;
        MediaFrameReader reader = null;
        try
        {
            // Exclusive lets us pick a small format; shared still works while another app uses the camera.
            try { capture = Open(MediaCaptureSharingMode.ExclusiveControl); }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception) { capture = Open(MediaCaptureSharingMode.SharedReadOnly); }
            var source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source == null) throw new InvalidOperationException();
            var small = source.SupportedFormats
                .Where(f => f.VideoFormat.Width >= 480 && f.FrameRate.Denominator != 0 && f.FrameRate.Numerator / (double)f.FrameRate.Denominator >= 24)
                .OrderBy(f => f.VideoFormat.Width).FirstOrDefault();
            if (small != null) try { Wait(source.SetFormatAsync(small)); } catch (Exception) { } // shared mode can't change it
            reader = Wait(capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8));
            if (Wait(reader.StartAsync()) != MediaFrameReaderStartStatus.Success) throw new InvalidOperationException();
            var buffer = new Windows.Storage.Streams.Buffer(0);
            while (running)
            {
                using (var latest = reader.TryAcquireLatestFrame())
                    if (latest != null && latest.VideoMediaFrame != null && latest.VideoMediaFrame.SoftwareBitmap != null)
                        buffer = Deliver(latest.VideoMediaFrame.SoftwareBitmap, buffer);
                Thread.Sleep(30); // about 30 frames a second
            }
        }
        catch (Exception e)
        {
            if (running) ui.BeginInvoke(failed, e is UnauthorizedAccessException ? "Turn on camera access for desktop apps in Windows Settings" : "Camera unavailable");
        }
        finally
        {
            try { if (reader != null) { Wait(reader.StopAsync()); reader.Dispose(); } } catch (Exception) { }
            if (capture != null) capture.Dispose(); // turns the camera light off
        }
    }

    static MediaCapture Open(MediaCaptureSharingMode mode)
    {
        var capture = new MediaCapture();
        try
        {
            Wait(capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = mode,
            }));
            return capture;
        }
        catch (Exception) { capture.Dispose(); throw; }
    }

    Windows.Storage.Streams.Buffer Deliver(SoftwareBitmap bitmap, Windows.Storage.Streams.Buffer buffer)
    {
        var bgra = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? bitmap : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
        try
        {
            int w = bgra.PixelWidth, h = bgra.PixelHeight, size = w * h * 4;
            if (buffer.Capacity != size) buffer = new Windows.Storage.Streams.Buffer((uint)size);
            bgra.CopyToBuffer(buffer);
            lock (gate)
            {
                if (pixels == null || pixels.Length != size) pixels = new byte[size];
                using (var r = DataReader.FromBuffer(buffer)) r.ReadBytes(pixels);
                width = w;
                height = h;
                if (posted) return buffer; // the UI hasn't drawn the last one yet: it will pick this up instead
                posted = true;
            }
            ui.BeginInvoke(new Action(Show));
            return buffer;
        }
        finally { if (bgra != bitmap) bgra.Dispose(); }
    }

    void Show()
    {
        lock (gate)
        {
            posted = false;
            if (running) frame(pixels, width, height);
        }
    }

    static T Wait<T>(IAsyncOperation<T> op)
    {
        Await(op);
        return op.GetResults();
    }

    static void Wait(IAsyncAction op)
    {
        Await(op);
        op.GetResults();
    }

    static void Await(IAsyncInfo op)
    {
        var timer = Stopwatch.StartNew();
        while (op.Status == AsyncStatus.Started)
        {
            if (timer.ElapsedMilliseconds > 10000) throw new TimeoutException();
            Thread.Sleep(5);
        }
    }
}
