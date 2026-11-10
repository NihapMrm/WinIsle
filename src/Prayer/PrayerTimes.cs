using System;
using System.Globalization;
using System.IO;
using System.Linq;

class Prayer
{
    public string Name;
    public DateTime At;
    public string Key { get { return At.ToString("yyyy-MM-dd ", CultureInfo.InvariantCulture) + Name; } }
}

// Prayer times computed offline from the sun's position.
static class Prayers
{
    // Changed from the dashboard and kept in prayer-settings.txt. Default: Eastern Province, Sri Lanka.
    public static string Place = "Batticaloa";
    public static double Lat = 7.717, Lon = 81.70;
    public static TimeZoneInfo Zone = TimeZoneInfo.Local;
    static readonly string SettingsFile = AppPaths.For("prayer-settings.txt");
    // Angles, Asr method (Shafi'i shadow length 1, Hanafi 2) and the per-prayer calibration minutes (Fajr,
    // Sunrise, Dhuhr, Asr, Maghrib, Isha) are Control Center settings.
    static double FajrAngle { get { return Settings.Num(Settings.FajrAngle); } }
    static double IshaAngle { get { return Settings.Num(Settings.IshaAngle); } }
    static double AsrShadow { get { return Settings.On(Settings.Hanafi) ? 2 : 1; } }
    public static double[] Offset { get { return Enumerable.Range(0, 6).Select(i => Settings.Num(Settings.PrayerOffset(i))).ToArray(); } }
    static readonly string[] Names = { "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha" };
    public static readonly string[] SlotNames = { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };
    public static readonly int[] SlotTimes = { 0, 2, 3, 4, 5 }; // index of each prayer in For()

    public static DateTime Now { get { return TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zone); } }
    public static double TzAt(DateTime day) { return Zone.GetUtcOffset(day).TotalHours; }
    public static bool ValidLocation(double lat, double lon) { return lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180; }

    public static void LoadSettings()
    {
        try
        {
            var p = File.ReadAllText(SettingsFile).Trim().Split('|');
            double lat = double.Parse(p[1], CultureInfo.InvariantCulture), lon = double.Parse(p[2], CultureInfo.InvariantCulture);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(p[3]);
            if (ValidLocation(lat, lon)) { Place = p[0]; Lat = lat; Lon = lon; Zone = zone; }
        }
        catch (Exception) { } // no file yet, or a damaged one: keep the defaults
    }

    public static bool SaveSettings()
    {
        try
        {
            File.WriteAllText(SettingsFile, string.Join("|", Place, Lat.ToString(CultureInfo.InvariantCulture), Lon.ToString(CultureInfo.InvariantCulture), Zone.Id));
            return true;
        }
        catch (Exception) { return false; }
    }

    static double Rad(double d) { return d * Math.PI / 180; }
    static double Deg(double r) { return r * 180 / Math.PI; }

    static void Sun(double jd, out double decl, out double eqt)
    {
        double d = jd - 2451545.0;
        double g = Rad(357.529 + 0.98560028 * d), q = 280.459 + 0.98564736 * d;
        double l = Rad(q + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g));
        double e = Rad(23.439 - 0.00000036 * d);
        double ra = Deg(Math.Atan2(Math.Cos(e) * Math.Sin(l), Math.Cos(l))) / 15;
        decl = Math.Asin(Math.Sin(e) * Math.Sin(l));
        eqt = q / 15 - ra;
        eqt -= Math.Round(eqt / 24) * 24;
    }

    // Local times for Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha. tz is hours east of UTC.
    public static DateTime[] For(DateTime day, double tz)
    {
        double jd = day.Date.ToOADate() + 2415018.5, lat = Rad(Lat);
        double[] solar = { 5, 6, 12, 13, 18, 18 };
        for (int pass = 0; pass < 3; pass++) // the sun moves during the day, so refine the guess
            for (int i = 0; i < 6; i++)
            {
                double decl, eqt;
                Sun(jd + (solar[i] - Lon / 15) / 24, out decl, out eqt);
                double noon = 12 - eqt;
                if (i == 2) { solar[i] = noon; continue; }
                double alt = i == 0 ? -FajrAngle : i == 5 ? -IshaAngle
                    : i == 3 ? Deg(Math.Atan(1 / (AsrShadow + Math.Tan(Math.Abs(lat - decl))))) : -0.833;
                // ponytail: clamped so far-north locations can't crash; add a high-latitude rule if one is ever used past about 48 degrees.
                double cosH = (Math.Sin(Rad(alt)) - Math.Sin(decl) * Math.Sin(lat)) / (Math.Cos(decl) * Math.Cos(lat));
                double hours = Deg(Math.Acos(Math.Max(-1, Math.Min(1, cosH)))) / 15;
                solar[i] = noon + (i < 2 ? -hours : hours);
            }
        // Rounded up so a reminder never fires before the time has actually entered.
        var offset = Offset;
        return solar.Select((h, i) => day.Date.AddMinutes(Math.Ceiling((h + tz - Lon / 15) * 60 + offset[i]))).ToArray();
    }

    // The prayer whose time is running now. Null between sunrise and Dhuhr, when none is due.
    public static Prayer Current(DateTime now, double tz)
    {
        var today = For(now, tz);
        int i = Array.FindLastIndex(today, t => t <= now);
        if (i == 1) return null;
        return i < 0 ? new Prayer { Name = Names[5], At = For(now.AddDays(-1), tz)[5] } // Isha runs past midnight
                     : new Prayer { Name = Names[i], At = today[i] };
    }
}
