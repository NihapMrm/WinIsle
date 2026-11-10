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

    // Hold the mouse wheel down and scroll, anywhere: volume. (WH_MOUSE_LL)
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    const int MouseLowLevel = 14, MiddleDown = 0x207, MiddleUp = 0x208, Wheel = 0x20A, Injected = 1;
    const uint SendMiddleDown = 0x20, SendMiddleUp = 0x40;
    KeyHook mouseHook;
    IntPtr mouseHandle;
    bool middleHeld, middleScrolled;
    int wheelSum;

    const int Render = 0, Multimedia = 1, AllContexts = 23;
    internal static readonly Geometry Speaker =Geometry.Parse("M3,9 H7 L12,5 V19 L7,15 H3 Z M14.5,8.6 A4.5,4.5 0 0 1 14.5,15.4 L13.6,14.3 A3,3 0 0 0 13.6,9.7 Z M16.6,6.2 A7.6,7.6 0 0 1 16.6,17.8 L15.7,16.7 A6.1,6.1 0 0 0 15.7,7.3 Z");
    internal static readonly Geometry Muted =Geometry.Parse("M3,9 H7 L12,5 V19 L7,15 H3 Z M14.6,9.4 L15.6,8.4 L17.8,10.6 L20,8.4 L21,9.4 L18.8,11.6 L21,13.8 L20,14.8 L17.8,12.6 L15.6,14.8 L14.6,13.8 L16.8,11.6 Z");
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
    internal void Set(double level)
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

        // The wheel press is held back: scrolling while it is down changes the volume; released without
        // scrolling, it is sent on as a normal middle click (open link in new tab, close tab still work).
        mouseHook = (code, w, l) =>
        {
            if (code < 0 || endpoint == null || (Marshal.ReadInt32(l, 12) & Injected) != 0) return CallNextHookEx(mouseHandle, code, w, l);
            int message = (int)w;
            if (message == MiddleDown) { middleHeld = true; middleScrolled = false; wheelSum = 0; return (IntPtr)1; }
            if (message == MiddleUp && middleHeld)
            {
                middleHeld = false;
                if (!middleScrolled) ui.BeginInvoke(new Action(() => { mouse_event(SendMiddleDown, 0, 0, 0, UIntPtr.Zero); mouse_event(SendMiddleUp, 0, 0, 0, UIntPtr.Zero); }));
                return (IntPtr)1;
            }
            if (message == Wheel && middleHeld)
            {
                middleScrolled = true;
                int delta = (short)((Marshal.ReadInt32(l, 8) >> 16) & 0xFFFF);
                ui.BeginInvoke(new Action(() => Scroll(delta)));
                return (IntPtr)1; // the page under the mouse doesn't scroll
            }
            return CallNextHookEx(mouseHandle, code, w, l);
        };
        mouseHandle = SetWindowsHookEx(MouseLowLevel, mouseHook, GetModuleHandle(null), 0);
    }

    // Each wheel notch is a 2% step; touchpad-style small deltas add up to a notch first.
    internal void Scroll(int delta)
    {
        int notches = Notches(ref wheelSum, delta);
        if (notches == 0 || endpoint == null) return;
        float level;
        bool muted;
        if (endpoint.GetMasterVolumeLevelScalar(out level) != 0 || endpoint.GetMute(out muted) != 0) return;
        double next = level;
        for (int i = 0; i < Math.Abs(notches); i++) next = Step(next, notches > 0);
        var context = Guid.Empty;
        endpoint.SetMasterVolumeLevelScalar((float)next, ref context);
        if (notches > 0 && muted) endpoint.SetMute(false, ref context);
        ShowCurrent(); // also at 0% and 100%, where nothing changes
    }

    // Whole notches in sum (120 each, up positive), keeping the remainder for next time.
    internal static int Notches(ref int sum, int delta)
    {
        sum += delta;
        int notches = sum / 120;
        sum -= notches * 120;
        return notches;
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

    // The speaker's level and mute now, for the player's volume bar; false when there is no speaker.
    internal bool Current(out double level, out bool muted)
    {
        level = 0;
        muted = false;
        float scalar;
        if (endpoint == null || endpoint.GetMasterVolumeLevelScalar(out scalar) != 0 || endpoint.GetMute(out muted) != 0) return false;
        level = scalar;
        return true;
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
