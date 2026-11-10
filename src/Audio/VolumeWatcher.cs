using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;

// Windows calls this on the speaker's volume or mute change. Public: COM can only call public interfaces.
[ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr data);
}

// Flashes the island with the new volume whenever the default speaker's volume or mute changes, from the
// keyboard, the taskbar or any app. Follows the default speaker when it changes (headphones plugged in, etc.).
[ComVisible(true)]
public class VolumeWatcher : IAudioEndpointVolumeCallback
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    // COM calls go by position: every method up to the last one used must be listed, in order.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // Volume keys arrive even when the level can't move (0% or 100%), when Windows reports no change.
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, KeyHook hook, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    delegate IntPtr KeyHook(int code, IntPtr w, IntPtr l);
    const int KeyboardLowLevel = 13, KeyDown = 0x100, SysKeyDown = 0x104;
    const int VolumeMuteKey = 0xAD, VolumeDownKey = 0xAE, VolumeUpKey = 0xAF;
    KeyHook keyHook; // kept so the delegate isn't collected while Windows calls it
    IntPtr hookHandle;

    const int Render = 0, Multimedia = 1, AllContexts = 23;
    static readonly Geometry Speaker = Geometry.Parse("M3,9 H7 L12,5 V19 L7,15 H3 Z M14.5,8.6 A4.5,4.5 0 0 1 14.5,15.4 L13.6,14.3 A3,3 0 0 0 13.6,9.7 Z M16.6,6.2 A7.6,7.6 0 0 1 16.6,17.8 L15.7,16.7 A6.1,6.1 0 0 0 15.7,7.3 Z");
    static readonly Geometry Muted = Geometry.Parse("M3,9 H7 L12,5 V19 L7,15 H3 Z M14.6,9.4 L15.6,8.4 L17.8,10.6 L20,8.4 L21,9.4 L18.8,11.6 L21,13.8 L20,14.8 L17.8,12.6 L15.6,14.8 L14.6,13.8 L16.8,11.6 Z");
    static readonly Color Accent = Color.FromRgb(235, 235, 240);

    readonly Dispatcher ui;
    readonly Action<IslandNotice> flash;
    IAudioEndpointVolume endpoint;
    string deviceId;

    VolumeWatcher(Dispatcher ui, Action<IslandNotice> flash) { this.ui = ui; this.flash = flash; }

    internal static VolumeWatcher Watch(Dispatcher ui, Action<IslandNotice> flash)
    {
        var watcher = new VolumeWatcher(ui, flash);
        watcher.Follow();
        watcher.HookKeys();
        // ponytail: checks the default speaker every 5 s instead of implementing IMMNotificationClient.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (s, e) => watcher.Follow();
        timer.Start();
        return watcher; // keep a reference: Windows holds only a COM pointer to it
    }

    void Follow()
    {
        try
        {
            var devices = (IMMDeviceEnumerator)new DeviceEnumerator();
            IMMDevice device;
            if (devices.GetDefaultAudioEndpoint(Render, Multimedia, out device) != 0) return;
            string id;
            device.GetId(out id);
            if (id == deviceId) return;
            if (endpoint != null) endpoint.UnregisterControlChangeNotify(this);
            object activated;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, AllContexts, IntPtr.Zero, out activated) != 0) return;
            endpoint = (IAudioEndpointVolume)activated;
            endpoint.RegisterControlChangeNotify(this);
            deviceId = id;
        }
        catch (Exception) { } // no speaker: nothing to show
    }

    // On a Windows audio thread. The data starts with a GUID, then a BOOL muted and a float volume.
    public int OnNotify(IntPtr data)
    {
        bool muted = Marshal.ReadInt32(data, 16) != 0;
        float level = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 20)), 0);
        ui.BeginInvoke(new Action(() => Show(level, muted)));
        return 0;
    }

    void Show(float level, bool muted)
    {
        var notice = Notice(level, muted);
        notice.SetLevel = Set;
        flash(notice);
    }

    // From the pill: dragging or scrolling the bar. Turning it up from mute unmutes, as the keys do.
    void Set(double level)
    {
        if (endpoint == null) return;
        var context = Guid.Empty;
        endpoint.SetMasterVolumeLevelScalar((float)level, ref context);
        if (level > 0) endpoint.SetMute(false, ref context);
    }

    // The three volume keys are taken before Windows sees them and applied here, so Windows' own volume pop-up
    // never appears and the island's pill shows instead. Every other key passes straight through untouched.
    // If the app closes, the hook goes with it and Windows handles the keys again.
    void HookKeys()
    {
        keyHook = (code, w, l) =>
        {
            if (code >= 0 && endpoint != null)
            {
                int key = Marshal.ReadInt32(l);
                if (key == VolumeUpKey || key == VolumeDownKey || key == VolumeMuteKey)
                {
                    if ((int)w == KeyDown || (int)w == SysKeyDown) ui.BeginInvoke(new Action(() => Press(key))); // held keys repeat
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(hookHandle, code, w, l);
        };
        hookHandle = SetWindowsHookEx(KeyboardLowLevel, keyHook, GetModuleHandle(null), 0);
    }

    // What Windows does with these keys: 2% steps, up unmutes, mute toggles. Then the pill, even at 0% or 100%.
    void Press(int key)
    {
        float level;
        bool muted;
        if (endpoint == null || endpoint.GetMasterVolumeLevelScalar(out level) != 0 || endpoint.GetMute(out muted) != 0) return;
        var context = Guid.Empty;
        if (key == VolumeMuteKey) endpoint.SetMute(!muted, ref context);
        else
        {
            endpoint.SetMasterVolumeLevelScalar((float)Step(level, key == VolumeUpKey), ref context);
            if (key == VolumeUpKey && muted) endpoint.SetMute(false, ref context);
        }
        ShowCurrent();
    }

    // Next 2% mark up or down, within 0..1.
    internal static double Step(double level, bool up)
    {
        double next = Math.Round(level / 0.02) + (up ? 1 : -1);
        return Math.Max(0, Math.Min(1, next * 0.02));
    }

    void ShowCurrent()
    {
        if (endpoint == null) return;
        float level;
        bool muted;
        if (endpoint.GetMasterVolumeLevelScalar(out level) != 0 || endpoint.GetMute(out muted) != 0) return;
        Show(level, muted);
    }

    internal static IslandNotice Notice(float level, bool muted)
    {
        int percent = (int)Math.Round(level * 100);
        return new IslandNotice
        {
            Key = "volume", Title = "Volume", Time = muted ? "Muted" : percent + "%", Level = muted ? 0 : level,
            Icon = muted || percent == 0 ? Muted : Speaker, Accent = Accent,
        };
    }
}
