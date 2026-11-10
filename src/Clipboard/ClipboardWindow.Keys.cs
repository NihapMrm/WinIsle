using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

// Keys and outside clicks for the clipboard panel, which never takes the focus. While it is open, low-level
// hooks read the keys it uses (arrows, Enter, Esc, Delete, Backspace, Ctrl+P, Ctrl+1..9 and typed characters for
// the search) and keep them from the app underneath. Everything else passes through untouched: Shift, Ctrl, Alt
// and Windows on their own, every Alt combination (so Ctrl+Alt+V still closes it), and other Ctrl shortcuts.
// A click outside the panel closes it. Both hooks exist only while the panel is open.
partial class ClipboardWindow
{
    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern short GetKeyState(int key);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr process);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder text, int size, uint flags, IntPtr layout);

    const int KeyboardLowLevel = 13, MouseLowLevel = 14;
    const int KeyPress = 0x100, SysKeyPress = 0x104, LeftDown = 0x201, RightDown = 0x204, MiddleDown = 0x207;
    const int Back = 0x08, Enter = 0x0D, Escape = 0x1B, PageUp = 0x21, PageDown = 0x22, Up = 0x26, Down = 0x28, Delete = 0x2E;
    static readonly int[] Modifiers = { 0x10, 0x11, 0x12, 0x14, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }; // Shift, Ctrl, Alt, Caps, Windows

    HookProc keyProc, mouseProc; // kept so the delegates aren't collected while Windows calls them
    IntPtr keyHook, mouseHook;
    readonly HashSet<int> taken = new HashSet<int>(); // keys whose press we took, so their release is taken too

    void StartListening()
    {
        if (keyHook != IntPtr.Zero) return;
        keyProc = OnKey;
        mouseProc = OnMouse;
        var module = GetModuleHandle(null);
        keyHook = SetWindowsHookEx(KeyboardLowLevel, keyProc, module, 0);
        mouseHook = SetWindowsHookEx(MouseLowLevel, mouseProc, module, 0);
    }

    void StopListening()
    {
        if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        keyHook = mouseHook = IntPtr.Zero;
        taken.Clear();
    }

    // What a key press does in the panel, or null to let it through (or to type it into the search).
    internal static string Command(int key, bool ctrl, bool shift, bool searching)
    {
        if (ctrl)
        {
            if (key == 'P') return "pin";
            if (key >= '1' && key <= '9') return "paste" + (char)key;
            return null; // other Ctrl shortcuts belong to the app
        }
        switch (key)
        {
            case Down: return "down";
            case Up: return "up";
            case PageDown: return "pagedown";
            case PageUp: return "pageup";
            case Enter: return "paste";
            case Escape: return "escape";
            case Back: return "back";
            case Delete: return !searching || shift ? "remove" : null;
        }
        return null;
    }

    IntPtr OnKey(int code, IntPtr w, IntPtr l)
    {
        if (code < 0 || !open) return CallNextHookEx(keyHook, code, w, l);
        int key = Marshal.ReadInt32(l), scan = Marshal.ReadInt32(l, 4);        if (Array.IndexOf(Modifiers, key) >= 0) return CallNextHookEx(keyHook, code, w, l);
        bool down = (int)w == KeyPress || (int)w == SysKeyPress;
        if (!down) return taken.Remove(key) ? (IntPtr)1 : CallNextHookEx(keyHook, code, w, l);
        bool ctrl = Pressed(0x11), alt = Pressed(0x12), shift = Pressed(0x10);
        if (alt) return CallNextHookEx(keyHook, code, w, l);

        var command = Command(key, ctrl, shift, query.Length > 0);
        string typed = command == null && !ctrl ? Typed(key, scan, shift) : "";
        if (command == null && typed.Length == 0) return CallNextHookEx(keyHook, code, w, l);
        taken.Add(key);
        // Act after the hook returns: Windows gives a low-level hook only a moment.
        Dispatcher.BeginInvoke(new Action(() => { if (command != null) Run(command); else SetQuery(query + typed); }));
        return (IntPtr)1;
    }

    void Run(string command)
    {
        if (!open) return;
        var current = selected < rows.Count ? rows[selected].Item : null;
        switch (command)
        {
            case "down": Select(selected + 1); break;
            case "up": Select(selected - 1); break;
            case "pagedown": Select(selected + 5); break;
            case "pageup": Select(selected - 5); break;
            case "paste": if (current != null) Paste(current); break;
            case "escape": if (query.Length > 0) SetQuery(""); else Dismiss(); break;
            case "back": if (query.Length > 0) SetQuery(query.Substring(0, query.Length - 1)); break;
            case "pin": if (current != null) history.TogglePin(current); break;
            case "remove": if (current != null) history.Remove(current); break;
            default:
                int n = command[command.Length - 1] - '1'; // "paste1".."paste9"
                if (command.StartsWith("paste") && n >= 0 && n < rows.Count) Paste(rows[n].Item);
                break;
        }
    }

    // The character the key makes in the keyboard layout of the app in front, so any language types correctly.
    static string Typed(int key, int scan, bool shift)
    {
        var state = new byte[256];
        if (shift) state[0x10] = 0x80;
        if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 1; // Caps Lock
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
        var text = new StringBuilder(8);
        int n = ToUnicodeEx((uint)key, (uint)scan, state, text, text.Capacity, 4, layout); // 4: leave the keyboard's dead-key state alone
        if (n <= 0) return "";
        var typed = text.ToString(0, n);
        return typed.Length > 0 && typed[0] >= ' ' ? typed : "";
    }

    static bool Pressed(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    // A click anywhere outside the panel closes it, and still reaches whatever was clicked.
    IntPtr OnMouse(int code, IntPtr w, IntPtr l)
    {
        int message = (int)w;
        if (code >= 0 && open && (message == LeftDown || message == RightDown || message == MiddleDown))
        {
            int x = Marshal.ReadInt32(l), y = Marshal.ReadInt32(l, 4);
            double left = Left * dpi, top = Top * dpi, right = (Left + PanelW) * dpi, bottom = (Top + panel.ActualHeight) * dpi;
            if (x < left || x > right || y < top || y > bottom) Dispatcher.BeginInvoke(new Action(Dismiss));
        }
        return CallNextHookEx(mouseHook, code, w, l);
    }
}
