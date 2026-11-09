using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

// Daily, by-prayer, time-to-pray and history cards.
partial class PrayerDashboard : DashboardWindow
{
    // Daily prayed and missed as stacked columns on one 0 to 5 scale.
    Border TrendCard(Slot[] slots, bool animate)
    {
        const double h = 160, left = 22, top = 4, bottom = 20;
        double w = TrendCardW - 2 * CardPad - 2, plotW = w - left, plotH = h - top - bottom;
        var canvas = new Canvas { Width = w, Height = h };
        for (int k = 0; k <= 5; k++)
        {
            double y = top + plotH * (1 - k / 5.0);
            Place2(canvas, new Border { Width = plotW, Height = 1, Background = k == 0 ? BaseLine : GridLine, SnapsToDevicePixels = true }, left, y);
            var tick = T(k.ToString(Inv), 11, Muted);
            Place2(canvas, tick, 4, y - 8);
        }
        double band = plotW / range, barW = Math.Min(18, Math.Max(2, band * 0.68)), bx = (band - barW) / 2, corner = Math.Min(4, barW / 2);
        int step = range <= 7 ? 1 : range <= 30 ? 5 : 15;
        for (int d = 0; d < range; d++)
        {
            var day = slots.Skip(d * 5).Take(5).ToArray();
            int prayed = day.Count(x => x.Status == Tracker.Prayed), missed = day.Count(x => x.Status == Tracker.Missed);
            double pH = plotH * prayed / 5, mH = plotH * missed / 5, gap = prayed > 0 && missed > 0 ? 2 : 0;
            var wash = new SolidColorBrush(None);
            var scale = new ScaleTransform(1, 1);
            var column = new Canvas { Width = band, Height = plotH, Background = wash, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scale };
            if (prayed > 0) Place2(column, new Border { Width = barW, Height = pH, Background = PrayedInk, CornerRadius = new CornerRadius(missed > 0 ? 0 : corner, missed > 0 ? 0 : corner, 0, 0) }, bx, plotH - pH);
            if (missed > 0) Place2(column, new Border { Width = barW, Height = Math.Max(1, mH - gap), Background = MissedInk, CornerRadius = new CornerRadius(corner, corner, 0, 0) }, bx, plotH - pH - mH);
            column.MouseEnter += (s, e) => Anim(wash, HoverFill);
            column.MouseLeave += (s, e) => Anim(wash, None);
            Tip(column, prayed + " prayed, " + missed + " missed", Md(day[0].Day));
            Place2(canvas, column, left + d * band, top);
            if (animate) Go(scale, ScaleTransform.ScaleYProperty, 1, 420, Out, d * 280 / range, null, 0);
            if ((range - 1 - d) % step == 0)
            {
                var label = T(d == range - 1 ? "Today" : Md(day[0].Day), 11, Muted);
                label.TextAlignment = TextAlignment.Center;
                label.Width = 56;
                Place2(canvas, label, Math.Min(w - 56, Math.Max(left - 8, left + d * band + band / 2 - 28)), h - 16);
            }
        }
        var stack = new StackPanel();
        stack.Children.Add(CardHeader("Daily prayers", "Prayed and missed each day", Legend(Swatch(PrayedInk), "Prayed", Swatch(MissedInk), "Missed")));
        stack.Children.Add(canvas);
        return Card(stack);
    }
    Border ByPrayerCard(Slot[] slots, bool animate)
    {
        var values = new double?[5];
        var details = new string[5];
        for (int i = 0; i < 5; i++)
        {
            int index = i, prayed = slots.Count(x => x.Index == index && x.Status == Tracker.Prayed), total = prayed + slots.Count(x => x.Index == index && x.Status == Tracker.Missed);
            values[i] = total == 0 ? (double?)null : 100.0 * prayed / total;
            details[i] = prayed + " of " + total + " prayed";
        }
        var known = Enumerable.Range(0, 5).Where(i => values[i].HasValue).ToArray();
        string note = "Share prayed";
        if (known.Length > 1 && values[known[0]] != known.Max(i => values[i]) || known.Length > 1 && known.Min(i => values[i]) != known.Max(i => values[i]))
            note = "Lowest: " + Prayers.SlotNames[known.OrderBy(i => values[i]).First()];
        var stack = new StackPanel();
        stack.Children.Add(CardHeader("By prayer", note));
        stack.Children.Add(HBars(Prayers.SlotNames, values, 100, v => Math.Round(v) + "%", details, SmallCardW - 2 * CardPad - 2, animate));
        return Card(stack);
    }

    Border DelayCard(Slot[] slots, bool animate)
    {
        var values = new double?[5];
        var details = new string[5];
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            var delays = slots.Where(x => x.Index == index && x.Delay.HasValue).Select(x => x.Delay.Value).ToArray();
            values[i] = delays.Length == 0 ? (double?)null : delays.Average();
            details[i] = "average of " + delays.Length + (delays.Length == 1 ? " answer" : " answers");
        }
        var stack = new StackPanel();
        stack.Children.Add(CardHeader("Time to pray", "Average minutes after prayer time"));
        stack.Children.Add(HBars(Prayers.SlotNames, values, Math.Max(10, values.Max() ?? 10), Minutes, details, SmallCardW - 2 * CardPad - 2, animate));
        return Card(stack);
    }

    // Every prayer on its own cell: also the readable table behind the charts, and where mistakes get corrected.
    Border HistoryCard(Slot[] slots)
    {
        const double labelW = 60, rowH = 23, cellH = 20, headH = 18;
        double w = ContentW - 2 * CardPad - 2, band = Math.Min(46, (w - labelW) / range), cellW = band - 2;
        var canvas = new Canvas { Width = w, Height = 5 * rowH + headH };
        int step = range <= 7 ? 1 : range <= 30 ? 5 : 15;
        for (int i = 0; i < 5; i++) Place2(canvas, T(Prayers.SlotNames[i], 12.5, Soft), 0, i * rowH + 1);
        foreach (var slot in slots)
        {
            var current = slot;
            int d = (int)(slot.Day - slots[0].Day).TotalDays;
            var cell = new Border { Width = cellW, Height = cellH, CornerRadius = new CornerRadius(Math.Min(4, cellW / 2)) };
            Brush iconColor = null;
            string glyph = null;
            if (slot.Status == Tracker.Prayed) { cell.Background = PrayedInk; iconColor = Brushes.White; glyph = TickGlyph; }
            else if (slot.Status == Tracker.Missed) { cell.Background = MissedWash; cell.BorderBrush = MissedInk; cell.BorderThickness = new Thickness(1.5); iconColor = Ink; glyph = CrossGlyph; }
            else if (slot.Status == Tracker.Pending) { cell.Background = Brushes.Transparent; cell.BorderBrush = Warning; cell.BorderThickness = new Thickness(1.5); }
            else cell.Background = Faint;
            if (glyph != null && cellW >= 18) cell.Child = Glyph(glyph, 9, iconColor);
            var name = Prayers.SlotNames[slot.Index] + ", " + Md(slot.Day);
            Tip(cell, StatusLabel(slot.Status), name + (slot.Status == Tracker.Upcoming ? "" : " \u00B7 click to change"));
            if (slot.Status != Tracker.Upcoming) Clickable(cell, name + ", " + StatusLabel(slot.Status) + ". Change status.", () => Toggle(current));
            else AutomationProperties.SetName(cell, name + ", " + StatusLabel(slot.Status));
            Place2(canvas, cell, labelW + d * band + 1, slot.Index * rowH);
            if (slot.Index == 4 && (range - 1 - d) % step == 0)
            {
                var label = T(d == range - 1 ? "Today" : Md(slot.Day), 11, Muted);
                label.TextAlignment = TextAlignment.Center;
                label.Width = 56;
                Place2(canvas, label, Math.Min(w - 56, labelW + d * band + band / 2 - 28), 5 * rowH + 1);
            }
        }
        var stack = new StackPanel();
        stack.Children.Add(CardHeader("History", "Tracking since " + Md(Tracker.Since) + ". Click a cell to correct it, such as a prayer made while the PC was off.",
            Legend(Swatch(PrayedInk), "Prayed", Swatch(MissedWash, MissedInk), "Missed", Swatch(Brushes.Transparent, Warning), "Time now", Swatch(Faint), "Upcoming or not tracked")));
        stack.Children.Add(canvas);
        return Card(stack);
    }
}
