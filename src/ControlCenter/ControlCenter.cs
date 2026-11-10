using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

// One window for the whole app: a page list on the left, the chosen page on the right. The prayer dashboard
// is its first page (ControlCenter.Prayer*.cs); every other page is a column of settings cards
// (ControlCenter.Pages.cs). Settings apply the moment they change.
partial class ControlCenter : DashboardWindow
{
    internal static readonly string[] Pages = { "Prayer", "General", "Water", "Battery", "Notifications", "Flashes", "Input", "Clipboard", "Shelf", "Sounds", "About" };

    const double NavW = 200, PageW = 1120; // the page keeps the prayer dashboard's width, so its layout is unchanged

    static ControlCenter open; // one window at a time

    // Raised before the window shows, so the topmost todo panel can get out of its way.
    internal static event Action Opening;

    readonly Border root = new Border();
    readonly ScaleTransform rootScale = new ScaleTransform(0.97, 0.97);
    readonly StackPanel nav = new StackPanel();
    readonly Border pageHost = new Border();
    readonly Dictionary<string, FrameworkElement> built = new Dictionary<string, FrameworkElement>();
    readonly List<Action> refreshers = new List<Action>(); // re-show settings changed elsewhere (Reset, the shelf switch)
    readonly Action onSettingsChanged;
    string page;

    // Opens the window on a page (null: the page last shown), or brings it forward and switches page.
    internal static void Open(string page)
    {
        if (Opening != null) Opening();
        if (page == null || !Pages.Contains(page)) page = Pages.Contains(Settings.Text(Settings.LastPage)) ? Settings.Text(Settings.LastPage) : "General";
        if (open == null)
        {
            open = new ControlCenter();
            open.Closed += (s, e) => open = null;
            open.Show();
        }
        open.ShowPage(page);
        open.Activate();
    }

    ControlCenter()
    {
        Title = "MyTodo Control Center";
        Width = NavW + PageW;
        Height = Math.Min(816, SystemParameters.WorkArea.Height - 16);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = Ink;

        // Left: app title and the page list.
        var side = new StackPanel { Margin = new Thickness(16, 22, 8, 16) };
        var heading = T("Control Center", 18, Ink, FontWeights.SemiBold);
        heading.Margin = new Thickness(10, 0, 0, 16);
        side.Children.Add(heading);
        side.Children.Add(nav);
        var sideBox = new Border { Width = NavW, Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), BorderBrush = CardLine, BorderThickness = new Thickness(0, 0, 1, 0), Child = side };

        var closeFill = new SolidColorBrush(None);
        var close = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = closeFill, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 16, 0), Child = Glyph(CloseGlyph, 11, Soft) };
        Clickable(close, "Close Control Center", FadeAndClose);
        close.MouseEnter += (s, e) => Anim(closeFill, SelectedFill);
        close.MouseLeave += (s, e) => Anim(closeFill, None);

        var layout = new Grid { Background = Brushes.Transparent };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NavW) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(pageHost, 1);
        layout.Children.Add(sideBox);
        layout.Children.Add(pageHost);
        Grid.SetColumn(close, 1);
        layout.Children.Add(close);
        layout.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed && !(e.OriginalSource is TextBox)) try { DragMove(); } catch (InvalidOperationException) { } };

        root.Child = layout;
        root.CornerRadius = new CornerRadius(8);
        root.BorderThickness = new Thickness(1);
        root.Background = new LinearGradientBrush(Color.FromArgb(22, 255, 255, 255), Color.FromArgb(6, 255, 255, 255), 90);
        root.BorderBrush = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90);
        root.Opacity = 0;
        root.RenderTransform = rootScale;
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        Content = root;

        BuildNav();

        onSettingsChanged = () => { foreach (var r in refreshers) r(); };
        Settings.Changed += onSettingsChanged;
        Closed += (s, e) => { Settings.Changed -= onSettingsChanged; StopPrayer(); };

        PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { FadeAndClose(); e.Handled = true; } };
        SourceInitialized += (s, e) => MakeGlass(new WindowInteropHelper(this).Handle, unchecked((int)0xE61F1C1C));
        Loaded += (s, e) =>
        {
            Anim(root, OpacityProperty, 1, 220, Out);
            Anim(rootScale, ScaleTransform.ScaleXProperty, 1, 380, Spring);
            Anim(rootScale, ScaleTransform.ScaleYProperty, 1, 380, Spring);
        };
    }

    void BuildNav()
    {
        nav.Children.Clear();
        foreach (var name in Pages)
        {
            string target = name;
            bool selected = name == page;
            var fill = new SolidColorBrush(selected ? SelectedFill : None);
            var item = new Border { Background = fill, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 0, 0, 2), Child = T(name, 13.5, selected ? Ink : Soft, selected ? FontWeights.SemiBold : FontWeights.Normal) };
            Clickable(item, name, () => ShowPage(target));
            item.MouseEnter += (s, e) => { if (target != page) Anim(fill, HoverFill); };
            item.MouseLeave += (s, e) => { if (target != page) Anim(fill, None); };
            nav.Children.Add(item);
        }
    }

    void ShowPage(string name)
    {
        if (name == page) { if (name == "Prayer") PrayerRender(false); return; }
        page = name;
        if (Settings.Text(Settings.LastPage) != name) Settings.Set(Settings.LastPage, name);
        BuildNav();
        FrameworkElement content;
        if (!built.TryGetValue(name, out content)) built[name] = content = BuildPage(name);
        else if (name == "Prayer") PrayerRender(false); // current each time it is shown
        pageHost.Child = content;
        Anim(content, OpacityProperty, 1, 160, Out, 0);
    }

    // A page: its title and one-line description, then its content scrolling under them.
    static FrameworkElement PageFrame(UIElement title, UIElement body)
    {
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        var grid = new Grid { Margin = new Thickness(24, 18, 24, 16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var titleBox = new Border { Child = title, Margin = new Thickness(0, 0, 48, Gap + 2) };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(titleBox);
        grid.Children.Add(scroll);
        return grid;
    }

    void FadeAndClose()
    {
        Go(root, OpacityProperty, 0, 130, Out, 0, (s, e) => Close(), null);
    }
}
