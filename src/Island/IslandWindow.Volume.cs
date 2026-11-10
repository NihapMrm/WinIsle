using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

// The media player's volume bar. Scroll anywhere on the open player, or click / drag the bar, to change the
// volume. Scrolling elsewhere keeps its job: the timer wheels, the shelf's list and the volume or brightness pill.
partial class IslandWindow
{
    const double VolumeRowH = 18;

    internal VolumeWatcher Volume; // set by Program; null in demos (then the bar stays hidden)

    readonly Border volumeFill = new Border { Height = 4, Width = 0, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    readonly Grid volumeTrack = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 8, 0) };
    readonly TextBlock volumePercent = new TextBlock { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    readonly System.Windows.Shapes.Path volumeIcon = new System.Windows.Shapes.Path { Width = 13, Height = 13, Stretch = Stretch.Uniform, Fill = Brushes.White, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };

    UIElement BuildVolumeRow()
    {
        var row = new Grid { Height = VolumeRowH, Margin = new Thickness(0, 10, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        volumeTrack.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
        volumeTrack.Children.Add(volumeFill);
        volumeTrack.MouseLeftButtonDown += (s, e) => { volumeTrack.CaptureMouse(); DragVolume(e); e.Handled = true; };
        volumeTrack.MouseMove += (s, e) => { if (volumeTrack.IsMouseCaptured) DragVolume(e); };
        volumeTrack.MouseLeftButtonUp += (s, e) => volumeTrack.ReleaseMouseCapture();
        Grid.SetColumn(volumeTrack, 1);
        Grid.SetColumn(volumePercent, 2);
        row.Children.Add(volumeIcon);
        row.Children.Add(volumeTrack);
        row.Children.Add(volumePercent);
        return row;
    }

    void DragVolume(MouseEventArgs e)
    {
        if (Volume == null || volumeTrack.ActualWidth <= 0) return;
        double level = Math.Max(0, Math.Min(1, e.GetPosition(volumeTrack).X / volumeTrack.ActualWidth));
        ShowPlayerVolume(level, false); // at once; Windows' own report follows
        Volume.Set(level);
    }

    void RefreshPlayerVolume()
    {
        double level;
        bool muted;
        if (Volume != null && Volume.Current(out level, out muted)) ShowPlayerVolume(level, muted);
    }

    // Also fed by every volume change (keys, scrolling, other apps) through Flash.
    void ShowPlayerVolume(double level, bool muted)
    {
        double shown = muted ? 0 : level;
        volumeFill.Width = Math.Max(0, volumeTrack.ActualWidth * shown);
        volumePercent.Text = muted ? "Muted" : (int)Math.Round(level * 100) + "%";
        volumeIcon.Data = muted || shown == 0 ? VolumeWatcher.Muted : VolumeWatcher.Speaker;
    }

    // Scrolling on the open player (and nowhere else that already uses the wheel) changes the volume.
    bool VolumeScrollHere()
    {
        return Volume != null && mediaOpen && !timerOpen && !shelfOpen && !mirrorOn && shown == null;
    }
}
