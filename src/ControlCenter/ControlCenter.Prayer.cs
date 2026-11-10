using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

// The Prayer page: today, tracking analytics and history. Its location row and charts live in their own partial
// files; its reminder settings card is in ControlCenter.Pages.cs.
partial class ControlCenter
{
    const double ContentW = PageW - 48, PrayerLocationH = 104; // 48: the page's side margins

    // Blue and orange instead of green and red: red and green fail colorblind separation here (deltaE 4.1 vs 26.8).
    static readonly Brush PrayedInk = SeriesBlue, MissedInk = Rgb(217, 89, 38), Warning = Rgb(250, 178, 25);
    static readonly Brush MissedWash = Rgb(217, 89, 38, 56);

    readonly TextBlock subtitle = T("", 13, Muted);
    readonly Grid todayRow = new Grid();
    readonly StackPanel rangeRow = new StackPanel { Orientation = Orientation.Horizontal };
    readonly StackPanel analytics = new StackPanel();
    readonly StackPanel prayerSettings = new StackPanel(); // the reminder settings card, under the dashboard
    readonly Border prayerLocation = new Border { Height = 0, ClipToBounds = true };
    readonly TextBox latBox = Field(), lonBox = Field();
    readonly TextBlock prayerLocationNote = T("", 12.5, Soft);
    readonly TimeZoneInfo[] zones = TimeZoneInfo.GetSystemTimeZones().ToArray();
    int zoneIndex;
    int range = 30;
    bool prayerLocationOpen;
    string signature;
    DispatcherTimer prayerRefresh;

    FrameworkElement BuildPrayerPage()
    {
        var titleBlock = new StackPanel { Orientation = Orientation.Horizontal };
        var moon = PrayerReminder.Moon(26);
        moon.Margin = new Thickness(0, 0, 12, 0);
        titleBlock.Children.Add(moon);
        var titles = new StackPanel();
        titles.Children.Add(T("Prayers", 22, Ink, FontWeights.SemiBold));
        titles.Children.Add(subtitle);
        titleBlock.Children.Add(titles);

        todayRow.ColumnDefinitions.Clear();
        for (int i = 0; i < 5; i++) todayRow.ColumnDefinitions.Add(new ColumnDefinition());
        todayRow.Margin = new Thickness(0, 0, 0, Gap);

        var filters = new Grid { Margin = new Thickness(0, 0, 0, Gap) };
        filters.Children.Add(rangeRow);
        var locationButton = Pill("Location & time zone", false, TogglePrayerLocation);
        locationButton.HorizontalAlignment = HorizontalAlignment.Right;
        filters.Children.Add(locationButton);

        BuildPrayerLocation();

        var body = new StackPanel();
        body.Children.Add(todayRow);
        body.Children.Add(filters);
        body.Children.Add(prayerLocation);
        body.Children.Add(analytics);
        body.Children.Add(prayerSettings);

        PrayerRender(true);
        prayerRefresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        prayerRefresh.Tick += (s, e) => { if (Signature(CurrentSlots(Prayers.Now), Prayers.Now) != signature) PrayerRender(false); };
        prayerRefresh.Start();
        return PageFrame(titleBlock, body);
    }

    void StopPrayer() { if (prayerRefresh != null) prayerRefresh.Stop(); }

    static string StatusLabel(int status)
    {
        switch (status)
        {
            case Tracker.Prayed: return "Prayed";
            case Tracker.Missed: return "Missed";
            case Tracker.Pending: return "Time now";
            case Tracker.Upcoming: return "Upcoming";
            default: return "Not tracked";
        }
    }

    static string Utc(DateTime when)
    {
        var o = Prayers.Zone.GetUtcOffset(when);
        return "UTC" + (o < TimeSpan.Zero ? "-" : "+") + o.ToString(@"hh\:mm", Inv);
    }

    // ---- data to screen ----

    Slot[] CurrentSlots(DateTime now) { return Tracker.Slots(now.Date.AddDays(1 - range), range, now); }

    string Signature(Slot[] slots, DateTime now)
    {
        return now.Date.ToString("yyyyMMdd", Inv) + range + Prayers.Place + Prayers.Zone.Id + Prayers.Lat + Prayers.Lon + string.Concat(slots.Select(x => x.Status));
    }

    internal void SetRange(int days)
    {
        range = days;
        PrayerRender(true);
    }

    void TogglePrayer(Slot slot)
    {
        if (slot.Status == Tracker.Upcoming) return;
        if (slot.Status == Tracker.Prayed) Tracker.Unmark(slot.Key);
        else Tracker.Mark(slot.Key, slot.Status == Tracker.Pending ? Prayers.Now : (DateTime?)null); // stamps only real, in-time answers
        PrayerRender(false);
    }

    void PrayerRender(bool animate)
    {
        var now = Prayers.Now;
        var slots = CurrentSlots(now);
        signature = Signature(slots, now);
        var today = slots.Skip((range - 1) * 5).ToArray();

        var upcoming = today.FirstOrDefault(x => x.Status == Tracker.Upcoming);
        var nextName = upcoming != null ? upcoming.Index : 0;
        var nextTime = upcoming != null ? upcoming.Start : Prayers.For(now.AddDays(1), Prayers.TzAt(now.AddDays(1)))[0];
        subtitle.Text = Prayers.Place + "  \u00B7  " + Utc(now) + "  \u00B7  Next: " + Prayers.SlotNames[nextName] + " " + Hm(nextTime);

        RenderToday(today);

        rangeRow.Children.Clear();
        foreach (var days in new[] { 7, 30, 90 })
        {
            int chosen = days;
            rangeRow.Children.Add(Pill(days + " days", days == range, () => SetRange(chosen)));
        }

        analytics.Children.Clear();
        analytics.Children.Add(Tiles(slots, now));
        var charts = new Grid { Margin = new Thickness(0, Gap, 0, Gap) };
        charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrendCardW) });
        charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SmallCardW + Gap) });
        charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SmallCardW + Gap) });
        var trend = TrendCard(slots, animate);
        var byPrayer = ByPrayerCard(slots, animate);
        var delay = DelayCard(slots, animate);
        byPrayer.Margin = delay.Margin = new Thickness(Gap, 0, 0, 0);
        Grid.SetColumn(byPrayer, 1);
        Grid.SetColumn(delay, 2);
        charts.Children.Add(trend);
        charts.Children.Add(byPrayer);
        charts.Children.Add(delay);
        analytics.Children.Add(charts);
        analytics.Children.Add(HistoryCard(slots));
    }

    const double SmallCardW = 262, TrendCardW = ContentW - 2 * (SmallCardW + Gap);

    void RenderToday(Slot[] today)
    {
        todayRow.Children.Clear();
        foreach (var slot in today)
        {
            var current = slot;
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(T(Prayers.SlotNames[slot.Index], 15, Ink, FontWeights.SemiBold));
            names.Children.Add(T(Hm(slot.Start), 13, Soft));
            var status = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var icon = StatusIcon(slot.Status);
            if (icon != null) { icon.Margin = new Thickness(0, 0, 5, 0); status.Children.Add(icon); }
            status.Children.Add(T(StatusLabel(slot.Status), 12.5, Soft));
            var inner = new Grid();
            inner.Children.Add(names);
            inner.Children.Add(status);
            var card = Card(inner, 12);
            card.Padding = new Thickness(14, 10, 14, 11);
            card.Margin = new Thickness(slot.Index == 0 ? 0 : Gap / 2, 0, slot.Index == 4 ? 0 : Gap / 2, 0);
            if (slot.Status == Tracker.Pending) card.BorderBrush = Warning;
            if (slot.Status != Tracker.Upcoming)
            {
                Clickable(card, Prayers.SlotNames[slot.Index] + ", " + StatusLabel(slot.Status) + ". Change status.", () => TogglePrayer(current));
                Tip(card, StatusLabel(slot.Status), slot.Status == Tracker.Prayed ? "Click to undo" : "Click to mark prayed");
            }
            Grid.SetColumn(card, slot.Index);
            todayRow.Children.Add(card);
        }
    }

    static TextBlock StatusIcon(int status)
    {
        switch (status)
        {
            case Tracker.Prayed: return Glyph(TickGlyph, 12, PrayedInk);
            case Tracker.Missed: return Glyph(CrossGlyph, 11, MissedInk);
            case Tracker.Pending: return Glyph(ClockGlyph, 12, Warning);
            default: return null;
        }
    }

    FrameworkElement Tiles(Slot[] slots, DateTime now)
    {
        int prayed = slots.Count(x => x.Status == Tracker.Prayed), missed = slots.Count(x => x.Status == Tracker.Missed), total = prayed + missed;

        var before = Tracker.Slots(now.Date.AddDays(1 - 2 * range), range, now);
        int beforePrayed = before.Count(x => x.Status == Tracker.Prayed), beforeTotal = beforePrayed + before.Count(x => x.Status == Tracker.Missed);
        string change = "Last " + range + " days";
        if (total > 0 && beforeTotal >= range * 5 / 2) // a nearly empty earlier period would give a meaningless swing
        {
            var points = (int)Math.Round(100.0 * prayed / total - 100.0 * beforePrayed / beforeTotal);
            change = (points > 0 ? "\u25B2 " : points < 0 ? "\u25BC " : "") + Math.Abs(points) + " pts vs previous " + range + " days";
        }

        var days = (int)(now.Date - Tracker.Since.Date).TotalDays + 1;
        int current, best;
        Tracker.Streaks(Tracker.Slots(Tracker.Since.Date, days, now).Select(x => x.Status).ToArray(), out current, out best);

        var delays = slots.Where(x => x.Delay.HasValue).Select(x => x.Delay.Value).ToArray();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tiles = new[]
        {
            Tile("Completion", Percent(prayed, total), change, 48),
            Tile("Prayed", prayed.ToString(Inv), "of " + total + " finished prayers", 30),
            Tile("Missed", missed.ToString(Inv), "no click before next prayer", 30),
            Tile("Current streak", current.ToString(Inv), "in a row, best " + best, 30),
            Tile("Time to pray", delays.Length == 0 ? "\u2014" : Minutes(delays.Average()), "average after prayer time", 30),
        };
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].Margin = new Thickness(i == 0 ? 0 : Gap / 2, 0, i == 4 ? 0 : Gap / 2, 0);
            Grid.SetColumn(tiles[i], i);
            grid.Children.Add(tiles[i]);
        }
        return grid;
    }

}
