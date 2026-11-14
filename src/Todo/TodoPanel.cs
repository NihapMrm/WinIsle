// Always-on-top glass todo panel. Ctrl+Alt+T toggles, Esc / Ctrl+Q hide, Ctrl+Shift+Q quits.
// Also owns the app's hotkeys: Ctrl+Alt+P Control Center (Prayer page), Ctrl+Alt+S Control Center, Ctrl+Alt+V clipboard history.
// Build: build.cmd (uses the .NET Framework csc.exe that ships with Windows, C# 5).
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

class Item { public string Text; public int State; } // 0 todo, 1 ongoing, 2 done

class TodoPanel : GlassWindow
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

    // A second launch (Start menu, desktop icon) asks the running copy to show its panel.
    static readonly int ShowMessage = RegisterWindowMessage("WinIsle.ShowPanel");
    internal static void ShowRunning()
    {
        AllowSetForegroundWindow(-1); // ASFW_ANY: let the running copy come to the front
        PostMessage(new IntPtr(0xFFFF), ShowMessage, IntPtr.Zero, IntPtr.Zero); // HWND_BROADCAST reaches the hidden panel too
    }

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, VK_T = 0x54, VK_P = 0x50, VK_V = 0x56, VK_S = 0x53;

    static readonly string DataFile = AppPaths.For("todos.txt");
    static readonly string PosFile = AppPaths.For("position.txt");

    readonly Grid root = new Grid();
    Border panel; // glass layer; what show/hide animates
    readonly StackPanel list = new StackPanel();
    readonly TextBox input = new TextBox();
    readonly TextBlock count = new TextBlock();
    readonly TextBlock empty = new TextBlock();
    readonly ScaleTransform scale = new ScaleTransform(1, 1);
    readonly TranslateTransform slide = new TranslateTransform();
    bool hiding;
    TextBox editing; // inline editor of the row being renamed, if any

    internal TodoPanel()
    {
        Title = "Todo";
        Width = 340; Height = 440;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = new SolidColorBrush(Text);
        LoadPosition();

        // Header
        var header = new Grid { Margin = new Thickness(4, 0, 4, 12) };
        header.Children.Add(new TextBlock { Text = "Todo", FontSize = 20, FontWeight = FontWeights.SemiBold });
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Opacity = 0.55;
        count.Margin = new Thickness(0, 0, 92, 0);
        header.Children.Add(count);
        var prayersButton = HeaderButton(PrayerReminder.Moon(15), "Prayers (Ctrl+Alt+P)", () => ControlCenter.Open("Prayer"));
        prayersButton.Margin = new Thickness(0, 0, 60, 0);
        Action showMoon = () => prayersButton.Visibility = Settings.On(Settings.PrayerOn) ? Visibility.Visible : Visibility.Collapsed;
        showMoon();
        Settings.Changed += showMoon;
        header.Children.Add(prayersButton);
        var gearGlyph = new TextBlock { Text = "", FontFamily = Icons, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var gearButton = HeaderButton(gearGlyph, "Control Center (Ctrl+Alt+S)", () => ControlCenter.Open(null));
        gearButton.Margin = new Thickness(0, 0, 30, 0);
        header.Children.Add(gearButton);
        // Close only hides the panel, so the reminders keep running; Ctrl+Alt+T brings it back.
        var closeGlyph = new TextBlock { Text = "", FontFamily = Icons, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(HeaderButton(closeGlyph, "Close (Ctrl+Alt+T to open)", HidePanel));

        // Input with placeholder
        var placeholder = new TextBlock { Text = "Add a task…", Opacity = 0.45, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        input.Background = Brushes.Transparent;
        input.BorderThickness = new Thickness(0);
        input.Foreground = Foreground;
        input.CaretBrush = Foreground;
        input.FontSize = 14;
        input.TextChanged += (s, e) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.KeyDown += OnInputKey;
        var inputGrid = new Grid();
        inputGrid.Children.Add(placeholder);
        inputGrid.Children.Add(input);
        placeholder.FontSize = 14;
        var inputBox = new Border
        {
            Child = inputGrid,
            Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
        };

        // List
        empty.Text = "All clear";
        empty.Opacity = 0.4;
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.Margin = new Thickness(0, 40, 0, 0);
        var listHost = new Grid();
        listHost.Children.Add(empty);
        listHost.Children.Add(list);
        var scroll = new ScrollViewer { Content = listHost, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };

        root.Margin = new Thickness(14);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(inputBox, 1);
        Grid.SetRow(scroll, 2);
        root.Children.Add(header);
        root.Children.Add(inputBox);
        root.Children.Add(scroll);
        // Glass layer over the blurred desktop: the island's dark tint, so white text stays readable
        // even over a white page, plus a hairline highlight border.
        var glass = new Border
        {
            Child = root,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(Color.FromArgb(150, 34, 34, 38), Color.FromArgb(135, 14, 14, 16), 90),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90),
        };
        glass.RenderTransformOrigin = new Point(0.5, 0);
        var tg = new TransformGroup();
        tg.Children.Add(scale);
        tg.Children.Add(slide);
        glass.RenderTransform = tg;
        glass.Opacity = 0;
        panel = glass;
        Content = glass;

        foreach (var item in Load()) AddRow(item, false);
        UpdateCount();

        // Drag from anywhere that isn't a row or the input.
        MouseLeftButtonDown += (s, e) => { DragMove(); SavePosition(); };
        PreviewKeyDown += (s, e) =>
        {
            if (editing != null) return; // let the inline editor handle Esc / Enter
            // Ctrl+Q only hides, so the hotkey keeps working. Ctrl+Shift+Q really exits.
            if (e.Key == Key.Q && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) Close();
            else if (e.Key == Key.Escape || (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)) { HidePanel(); e.Handled = true; }
        };
        SourceInitialized += (s, e) => InitNative();
        Loaded += (s, e) => ShowPanel();
        Closing += (s, e) => SavePosition();
    }

    // Round 26 px icon button for the header's right side.
    static Border HeaderButton(UIElement icon, string tip, Action click)
    {
        var fill = new SolidColorBrush(Colors.Transparent);
        var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = fill, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = tip, Child = icon };
        b.MouseEnter += (s, e) => Anim(fill, Color.FromArgb(40, 255, 255, 255));
        b.MouseLeave += (s, e) => Anim(fill, Colors.Transparent);
        b.MouseLeftButtonDown += (s, e) => e.Handled = true; // don't start window drag
        b.MouseLeftButtonUp += (s, e) => click();
        return b;
    }

    void InitNative()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Tool window = hidden from Alt+Tab (GWL_EXSTYLE: +WS_EX_TOOLWINDOW, -WS_EX_APPWINDOW).
        SetWindowLong(hwnd, -20, (GetWindowLong(hwnd, -20) | 0x80) & ~0x40000);
        var src = HwndSource.FromHwnd(hwnd);
        MakeGlass(hwnd, 0x50181818); // alpha 0x50 over near-black

        if (!RegisterHotKey(hwnd, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_T))
        {
            MessageBox.Show("Ctrl+Alt+T is already used by another app. Close that app, then start WinIsle again.", "WinIsle");
            Application.Current.Shutdown();
            return;
        }
        src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == ShowMessage) { ShowPanel(); handled = true; }
            else if (msg == WM_HOTKEY)
            {
                int id = w.ToInt32();
                if (id == 2) ControlCenter.Open("Prayer");
                else if (id == 4) ControlCenter.Open(null);
                else if (id == 3) { if (Program.ClipboardPanel != null) Program.ClipboardPanel.Toggle(); }
                else Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        });
        RegisterHotKey(hwnd, 2, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_P); // optional: the moon button also opens it
        RegisterHotKey(hwnd, 3, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_V); // clipboard history; the island button also opens it
        RegisterHotKey(hwnd, 4, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_S); // Control Center; the gear buttons also open it
        // The panel is topmost and would sit on top of the Control Center.
        ControlCenter.Opening += () => { if (IsVisible && !hiding) HidePanel(); };
        Program.StartReminders(); // after the hotkey check, so a second instance never reminds twice or touches the log
        if (!Settings.On(Settings.ShowPanel)) Dispatcher.BeginInvoke(new Action(HidePanel)); // "Show todo panel on start" is off
    }

    // ---- show / hide ----

    void Toggle()
    {
        if (IsVisible && !hiding && IsActive) HidePanel();
        else ShowPanel();
    }

    void ShowPanel()
    {
        hiding = false;
        Show();
        Activate();
        input.Focus();
        Anim(panel, OpacityProperty, 1, 220, Out);
        Anim(scale, ScaleTransform.ScaleXProperty, 1, 380, Spring, 0.94);
        Anim(scale, ScaleTransform.ScaleYProperty, 1, 380, Spring, 0.94);
        Anim(slide, TranslateTransform.YProperty, 0, 380, Spring, -12);
    }

    void HidePanel()
    {
        hiding = true;
        SavePosition();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = Out };
        fade.Completed += (s, e) => { if (hiding) Hide(); };
        panel.BeginAnimation(OpacityProperty, fade);
        Anim(scale, ScaleTransform.ScaleXProperty, 0.96, 150, Out);
        Anim(scale, ScaleTransform.ScaleYProperty, 0.96, 150, Out);
        Anim(slide, TranslateTransform.YProperty, -8, 150, Out);
    }

    // ---- rows ----

    void OnInputKey(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var text = input.Text.Trim();
        if (text.Length == 0) return;
        AddRow(new Item { Text = text }, true);
        input.Clear();
        Save();
    }

    void AddRow(Item item, bool animate)
    {
        var tick = new TextBlock { Text = "", FontFamily = Icons, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        var checkFill = new SolidColorBrush(Colors.Transparent);
        var checkRing = new SolidColorBrush(Ring);
        var dotScale = new ScaleTransform(0, 0);
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Blue), RenderTransform = dotScale, RenderTransformOrigin = new Point(0.5, 0.5) };
        var mark = new Grid();
        mark.Children.Add(dot);
        mark.Children.Add(tick);
        var check = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), BorderBrush = checkRing, Background = checkFill, Child = mark, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0), Cursor = Cursors.Hand };
        var label = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var del = new TextBlock { Text = "", FontFamily = Icons, FontSize = 10, Opacity = 0, Padding = new Thickness(6), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(label, 1);
        Grid.SetColumn(del, 2);
        grid.Children.Add(check);
        grid.Children.Add(label);
        grid.Children.Add(del);

        var hover = new SolidColorBrush(Colors.Transparent);
        var row = new Border { Child = grid, Background = hover, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 7, 4, 7), Margin = new Thickness(0, 1, 0, 1), Tag = item, Cursor = Cursors.Hand };
        var move = new TranslateTransform();
        row.RenderTransform = move;

        // todo: plain ring. ongoing: blue ring + blue dot. done: filled green + tick.
        Action applyState = () =>
        {
            bool ongoing = item.State == 1, done = item.State == 2;
            Anim(checkFill, done ? Green : Color.FromArgb(0, Green.R, Green.G, Green.B));
            Anim(checkRing, done ? Green : ongoing ? Blue : Ring);
            Anim(dotScale, ScaleTransform.ScaleXProperty, ongoing ? 1 : 0, 260, ongoing ? Spring : Out);
            Anim(dotScale, ScaleTransform.ScaleYProperty, ongoing ? 1 : 0, 260, ongoing ? Spring : Out);
            Anim(tick, OpacityProperty, done ? 1 : 0, 180, Out);
            Anim(label, OpacityProperty, done ? 0.45 : 1, 180, Out);
            label.TextDecorations = done ? TextDecorations.Strikethrough : null;
        };
        tick.Opacity = item.State == 2 ? 1 : 0;
        applyState();

        row.MouseEnter += (s, e) => { Anim(hover, Color.FromArgb(22, 255, 255, 255)); Anim(del, OpacityProperty, 0.7, 150, Out); };
        row.MouseLeave += (s, e) => { Anim(hover, Colors.Transparent); Anim(del, OpacityProperty, 0, 150, Out); };
        Action<int> advance = step =>
        {
            item.State = (item.State + step) % 3; // todo -> ongoing -> done -> todo
            applyState();
            Anim(check, Border.WidthProperty, 18, 260, Spring, 14);
            Save();
        };
        // Click anywhere on the row advances the state. Double-click on the text undoes that
        // first click and opens an inline editor instead. Enter / click away saves, Esc cancels.
        row.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true; // don't start window drag
            if (editing != null || del.IsMouseOver) return;
            if (e.ClickCount == 1 || check.IsMouseOver) { advance(1); return; }
            if (e.ClickCount != 2) return;
            advance(2); // = one step back, undoing the first click
            // ponytail: first click of a double-click flashes the next state briefly; a click-delay timer avoids it but makes every tick laggy.
            var editor = new TextBox
            {
                Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Margin = new Thickness(-2, 0, 0, 0), Foreground = Foreground, CaretBrush = Foreground,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(editor, 1);
            bool ended = false;
            Action<bool> end = commit =>
            {
                if (ended) return;
                ended = true;
                editing = null;
                var text = editor.Text.Trim();
                if (commit && text.Length > 0 && text != item.Text) { item.Text = text; label.Text = text; Save(); }
                grid.Children.Remove(editor);
                label.Visibility = Visibility.Visible;
            };
            editor.PreviewKeyDown += (s2, e2) =>
            {
                if (e2.Key == Key.Enter) { end(true); input.Focus(); e2.Handled = true; }
                else if (e2.Key == Key.Escape) { end(false); input.Focus(); e2.Handled = true; }
            };
            editor.LostKeyboardFocus += (s2, e2) => end(true);
            label.Visibility = Visibility.Hidden;
            grid.Children.Add(editor);
            editing = editor;
            // Focus after layout, otherwise the just-added editor can't take focus yet.
            Dispatcher.BeginInvoke(new Action(() => { editor.Focus(); editor.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        };
        del.MouseLeftButtonUp += (s, e) => { e.Handled = true; RemoveRow(row); };

        list.Children.Add(row);
        if (animate)
        {
            Anim(row, OpacityProperty, 1, 260, Out, 0);
            Anim(move, TranslateTransform.YProperty, 0, 340, Spring, -10);
        }
    }

    void RemoveRow(Border row)
    {
        row.Tag = null;
        row.IsHitTestVisible = false;
        Save();
        Anim(row, OpacityProperty, 0, 160, Out);
        Anim((TranslateTransform)row.RenderTransform, TranslateTransform.XProperty, 30, 200, Out);
        var collapse = new DoubleAnimation(row.ActualHeight, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out, BeginTime = TimeSpan.FromMilliseconds(120) };
        collapse.Completed += (s, e) => list.Children.Remove(row);
        row.BeginAnimation(HeightProperty, collapse);
    }

    void UpdateCount()
    {
        var items = list.Children.OfType<Border>().Select(b => b.Tag).OfType<Item>().ToList();
        count.Text = items.Count(i => i.State != 2) + " left";
        Anim(empty, OpacityProperty, items.Count == 0 ? 0.4 : 0, 200, Out);
    }

    // ---- persistence: todos.txt, one "[ ] todo" / "[-] ongoing" / "[x] done" per line ----

    static readonly string[] Marks = { "[ ] ", "[-] ", "[x] " };

    static Item[] Load()
    {
        if (!File.Exists(DataFile)) return new Item[0];
        return File.ReadAllLines(DataFile).Where(l => l.Trim().Length > 0).Select(l =>
        {
            int state = Array.FindIndex(Marks, m => l.StartsWith(m));
            return new Item { Text = state >= 0 ? l.Substring(4) : l.Trim(), State = Math.Max(state, 0) };
        }).ToArray();
    }

    void Save()
    {
        var lines = list.Children.OfType<Border>().Select(b => b.Tag).OfType<Item>()
            .Select(i => Marks[i.State] + i.Text).ToArray();
        // Write to a temp file then swap, so a crash mid-write never truncates the list.
        var tmp = DataFile + ".tmp";
        File.WriteAllLines(tmp, lines);
        if (File.Exists(DataFile)) File.Replace(tmp, DataFile, null);
        else File.Move(tmp, DataFile);
        UpdateCount();
    }

    void LoadPosition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 40;
        try
        {
            var p = File.ReadAllText(PosFile).Split(',');
            double x = double.Parse(p[0]), y = double.Parse(p[1]);
            // Ignore saved spots that are now off-screen (monitor unplugged etc).
            if (x >= SystemParameters.VirtualScreenLeft && y >= SystemParameters.VirtualScreenTop &&
                x + 40 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                y + 40 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            { Left = x; Top = y; }
        }
        catch (Exception) { }
    }

    void SavePosition()
    {
        try { File.WriteAllText(PosFile, (int)Left + "," + (int)Top); } catch (Exception) { }
    }
}
