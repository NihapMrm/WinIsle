using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

// File shelf: drag files to the top edge of the screen and the island opens to hold them.
// Drag them back out to any folder or app, or click one to open it. The shelf holds paths, not copies.
partial class IslandWindow
{
    const double ShelfW = 404, ShelfR = 28, TileW = 86, ShelfListMax = 300;
    internal static readonly Geometry ShelfIcon = Geometry.Parse("M3,13 H8 L9.5,15.5 H14.5 L16,13 H21 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 Z M5.5,4 H18.5 L21,12 H15 L13.5,14 H10.5 L9,12 H3 Z");
    static readonly Color ShelfAccent = Color.FromRgb(110, 180, 255);

    readonly WrapPanel shelfTiles = new WrapPanel { Width = ShelfW - 2 * MediaPad };
    readonly TextBlock shelfCount = new TextBlock { FontSize = 12, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) };
    readonly System.Windows.Shapes.Rectangle dropOutline = new System.Windows.Shapes.Rectangle { RadiusX = 16, RadiusY = 16, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)) };
    readonly Dictionary<string, ImageSource> shelfThumbs = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
    Grid shelfLayer, dropZone;
    bool shelfOpen;    // the shelf is showing
    bool dragInside;   // a file drag is over the island right now
    bool draggingOut;  // a drag started from the shelf, so it can't drop onto itself

    // Called once from the constructor; returns the shelf's layer for the island's content.
    Grid InitShelf()
    {
        shelfLayer = BuildShelf();
        AllowDrop = true;
        DragEnter += (s, e) => OnDrag(e);
        DragOver += (s, e) => OnDrag(e);
        DragLeave += (s, e) =>
        {
            dragInside = false;
            HighlightDrop(false);
            // Dragged away without dropping: fold back up, unless the drag comes straight back in.
            After(450, () => { if (shelfOpen && !dragInside && !IsMouseOver) ClosePlayer(); });
        };
        Drop += OnDrop;
        return shelfLayer;
    }

    Grid BuildShelf()
    {
        var layer = Layer(ShelfW - 2 * MediaPad, double.NaN, MediaPad);
        var column = new StackPanel();
        var clear = Color.FromArgb(0, 255, 255, 255);
        var dim = Color.FromArgb(40, 255, 255, 255);

        var header = new Grid { Height = 32 };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(RoundButton(Glyph(BackIcon), 30, clear, dim, ShelfBack));
        left.Children.Add(new TextBlock { Text = "Shelf", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        left.Children.Add(shelfCount);
        header.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var dragAll = MakeButton(Label("Drag all", Colors.White), Color.FromArgb(28, 255, 255, 255), Color.FromArgb(60, 255, 255, 255), () => { });
        dragAll.Width = 78; dragAll.Height = 28; dragAll.CornerRadius = new CornerRadius(14);
        dragAll.ToolTip = "Drag every file out at once";
        ((TextBlock)dragAll.Child).FontSize = 12;
        DragOut(dragAll, () => shelf.Paths.ToArray());
        right.Children.Add(dragAll);
        var clearAll = RoundButton(new TextBlock { Text = "", FontFamily = Icons, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 30, clear, dim, () => { shelf.Clear(); RefreshShelf(); });
        clearAll.ToolTip = "Clear the shelf (files stay where they are)";
        clearAll.Margin = new Thickness(4, 0, 0, 0);
        right.Children.Add(clearAll);
        header.Children.Add(right);
        column.Children.Add(header);

        dropZone = new Grid { Height = 104, Margin = new Thickness(0, 10, 0, 0) };
        dropZone.Children.Add(dropOutline);
        var hint = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        hint.Children.Add(new System.Windows.Shapes.Path { Data = ShelfIcon, Width = 24, Height = 24, Stretch = Stretch.Uniform, Fill = new SolidColorBrush(ShelfAccent), Opacity = 0.9 });
        hint.Children.Add(new TextBlock { Text = "Drop files here", FontSize = 13, Opacity = 0.75, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        hint.Children.Add(new TextBlock { Text = "They stay where they are until you drag them out", FontSize = 11, Opacity = 0.45, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        dropZone.Children.Add(hint);
        column.Children.Add(dropZone);

        column.Children.Add(new ScrollViewer { Content = shelfTiles, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = ShelfListMax, Margin = new Thickness(0, 10, 0, 0) });
        layer.Children.Add(column);
        return layer;
    }

    // ---- open / close ----

    void OpenShelf()
    {
        if (shelfOpen || shelf == null || shown != null || !mediaShown || mirrorOn) return;
        if (!mediaOpen)
        {
            mediaOpen = true;
            if (timerPeekShown) { Conceal(timerPeek); timerPeekShown = false; }
            CaptureBackdrop();
        }
        if (timerOpen) { timerOpen = false; Conceal(timerLayer); }
        Conceal(mediaLayer);
        shelfOpen = true;
        RefreshShelf();
        Reveal(shelfLayer, 100);
    }

    void ShelfBack()
    {
        if (!shelfOpen) return;
        shelfOpen = false;
        Conceal(shelfLayer);
        HangFromLid(MediaW, MediaH, MediaR);
        Reveal(mediaLayer, 120);
    }

    void CloseShelf()
    {
        if (!shelfOpen) return;
        shelfOpen = false;
        Conceal(shelfLayer);
    }

    // ---- contents ----

    void RefreshShelf()
    {
        if (shelf == null) return;
        shelf.Prune();
        shelfTiles.Children.Clear();
        foreach (var path in shelf.Paths) shelfTiles.Children.Add(ShelfTile(path));
        shelfCount.Text = shelf.Count == 0 ? "" : shelf.Count + (shelf.Count == 1 ? " file" : " files");
        dropZone.Visibility = shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        shelfTiles.Visibility = shelf.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (shelfOpen)
        {
            shelfLayer.Measure(new Size(ShelfW - 2 * MediaPad, double.PositiveInfinity));
            HangFromLid(ShelfW, shelfLayer.DesiredSize.Height + 2 * MediaPad, ShelfR);
        }
    }

    UIElement ShelfTile(string path)
    {
        ImageSource thumb;
        if (!shelfThumbs.TryGetValue(path, out thumb)) shelfThumbs[path] = thumb = AppIcon.ForFile(path); // the Shell lookup is slow; once per file
        var tile = new Grid { Width = TileW, Margin = new Thickness(0, 0, 4, 8), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = path };
        var stack = new StackPanel();
        var picture = ClipboardWindow.Tile(thumb, 56, 56, Stretch.Uniform);
        picture.Margin = new Thickness(0);
        picture.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(picture);
        var name = Path.GetFileName(path.TrimEnd('\\'));
        stack.Children.Add(new TextBlock { Text = name == "" ? path : name, FontSize = 11, Opacity = 0.85, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30, Margin = new Thickness(2, 5, 2, 0) });
        tile.Children.Add(stack);

        var remove = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Opacity = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -5, 8, 0), Background = new SolidColorBrush(Color.FromArgb(230, 60, 60, 66)), ToolTip = "Remove from shelf",
            Child = new TextBlock { Text = "", FontFamily = Icons, FontSize = 8, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        remove.MouseLeftButtonUp += (s, e) => { e.Handled = true; shelf.Remove(path); RefreshShelf(); };
        tile.Children.Add(remove);
        tile.MouseEnter += (s, e) => Anim(remove, OpacityProperty, 1, 140, Out);
        tile.MouseLeave += (s, e) => Anim(remove, OpacityProperty, 0, 140, Out);

        DragOut(tile, () => new[] { path });
        tile.MouseLeftButtonUp += (s, e) => { try { Process.Start(path); } catch (Exception) { } }; // a click opens it
        return tile;
    }

    // Dragging an element starts a real file drag. Windows decides copy or move at the target, as with Explorer.
    void DragOut(UIElement element, Func<string[]> files)
    {
        Point? start = null;
        element.PreviewMouseLeftButtonDown += (s, e) => start = e.GetPosition(this);
        element.PreviewMouseLeftButtonUp += (s, e) => start = null;
        element.PreviewMouseMove += (s, e) =>
        {
            if (start == null || e.LeftButton != MouseButtonState.Pressed) return;
            var moved = e.GetPosition(this) - start.Value;
            if (Math.Abs(moved.X) < 5 && Math.Abs(moved.Y) < 5) return;
            start = null;
            var paths = files();
            if (paths.Length == 0) return;
            draggingOut = true;
            try { DragDrop.DoDragDrop(element, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch (Exception) { }
            finally { draggingOut = false; RefreshShelf(); } // a move takes the file off the shelf
        };
    }

    // A new screenshot (from ScreenshotWatcher): onto the shelf, with a flash showing its thumbnail.
    internal void AddScreenshot(string path)
    {
        if (shelf == null || shelf.Add(new[] { path }) == 0) return;
        var thumb = shelfThumbs[path] = AppIcon.ForFile(path);
        if (shelfOpen) RefreshShelf();
        Flash(new IslandNotice { Key = "screenshot", Title = "Added to shelf", Time = "Screenshot", Logo = thumb, Icon = ShelfIcon, Accent = ShelfAccent, Sound = IslandSound.Tick });
    }

    // ---- dropping in ----

    void OnDrag(DragEventArgs e)
    {
        e.Handled = true;
        if (shelf == null || shown != null || draggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Link; // the shelf holds a reference; the source keeps its file
        dragInside = true;
        if (!shelfOpen) OpenShelf();
        HighlightDrop(true);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        dragInside = false;
        HighlightDrop(false);
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths == null || shelf == null || draggingOut || !shelfOpen) return;
        e.Effects = DragDropEffects.Link;
        e.Handled = true;
        if (shelf.Add(paths) > 0) IslandSound.Tick.Play();
        RefreshShelf();
        // After a drop the mouse may never "enter" again, so fold up by ourselves once it has moved away.
        After(1800, () => { if (shelfOpen && !IsMouseOver && !dragInside) ClosePlayer(); });
    }

    void HighlightDrop(bool on)
    {
        Anim((SolidColorBrush)dropOutline.Stroke, on ? ShelfAccent : Color.FromArgb(80, 255, 255, 255));
        pill.BorderBrush = on ? new SolidColorBrush(ShelfAccent) : LidBorder;
    }
}
