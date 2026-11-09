using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

// The location and time zone row.
partial class PrayerDashboard : DashboardWindow
{
    class Place { public string Name; public double Lat, Lon; public Place(string n, double a, double o) { Name = n; Lat = a; Lon = o; } }

    static readonly Place[] Places =
    {
        new Place("Batticaloa", 7.717, 81.70), new Place("Kattankudy", 7.675, 81.729), new Place("Kalmunai", 7.409, 81.835),
        new Place("Akkaraipattu", 7.217, 81.85), new Place("Ampara", 7.301, 81.675), new Place("Trincomalee", 8.571, 81.233),
        new Place("Colombo", 6.927, 79.861), new Place("Kandy", 7.291, 80.636), new Place("Galle", 6.053, 80.221),
        new Place("Jaffna", 9.668, 80.007), new Place("Kurunegala", 7.487, 80.365), new Place("Puttalam", 8.036, 79.828),
        new Place("Mannar", 8.981, 79.904), new Place("Anuradhapura", 8.312, 80.414),
    };

    void BuildSettings()
    {
        var zoneLabels = zones.Select(z => z.DisplayName).ToArray();
        zoneIndex = Math.Max(0, Array.FindIndex(zones, z => z.Id == Prayers.Zone.Id));
        latBox.Text = Prayers.Lat.ToString(Inv);
        lonBox.Text = Prayers.Lon.ToString(Inv);

        Action<int> setZone;
        var zoneDrop = Dropdown(392, zoneLabels, zoneIndex, i => zoneIndex = i, "Time zone", out setZone);
        Action<int> ignore;
        int placeIndex = Math.Max(0, Array.FindIndex(Places, x => x.Name == Prayers.Place));
        var placeDrop = Dropdown(190, Places.Select(x => x.Name).ToArray(), placeIndex, i =>
        {
            latBox.Text = Places[i].Lat.ToString(Inv);
            lonBox.Text = Places[i].Lon.ToString(Inv);
            int sriLanka = Array.FindIndex(zones, z => z.Id == "Sri Lanka Standard Time");
            if (sriLanka >= 0) { zoneIndex = sriLanka; setZone(sriLanka); }
        }, "Location", out ignore);

        var save = Pill("Save", true, SaveSettings);
        save.VerticalAlignment = VerticalAlignment.Bottom;
        save.Margin = new Thickness(0, 0, 0, 4);
        settingsNote.VerticalAlignment = VerticalAlignment.Bottom;
        settingsNote.Margin = new Thickness(12, 0, 0, 8);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Labelled("Location", placeDrop));
        row.Children.Add(Labelled("Time zone", zoneDrop));
        row.Children.Add(Labelled("Latitude", latBox));
        row.Children.Add(Labelled("Longitude", lonBox));
        row.Children.Add(save);
        row.Children.Add(settingsNote);
        settings.Child = new Border { Margin = new Thickness(0, 0, 0, Gap), Child = Card(row) };
    }

    internal void ToggleSettings()
    {
        settingsOpen = !settingsOpen;
        Anim(settings, HeightProperty, settingsOpen ? SettingsH : 0, 340, settingsOpen ? Spring : Out);
    }

    void SaveSettings()
    {
        double lat, lon;
        if (!double.TryParse(latBox.Text, NumberStyles.Float, Inv, out lat) || !double.TryParse(lonBox.Text, NumberStyles.Float, Inv, out lon) || !Prayers.ValidLocation(lat, lon))
        {
            settingsNote.Text = "Enter latitude -90 to 90, longitude -180 to 180.";
            return;
        }
        var match = Places.FirstOrDefault(x => Math.Abs(x.Lat - lat) < 0.001 && Math.Abs(x.Lon - lon) < 0.001);
        Prayers.Place = match != null ? match.Name : "Custom location";
        Prayers.Lat = lat;
        Prayers.Lon = lon;
        Prayers.Zone = zones[zoneIndex];
        settingsNote.Text = Prayers.SaveSettings() ? "Saved." : "Applied, but could not be saved to disk.";
        Render(false);
    }
}
