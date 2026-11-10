using System;
using System.Windows.Media;

// What the island shows for one reminder.
class IslandNotice
{
    public string Key, Title, Detail, Time, YesLabel, NoLabel; // a null NoLabel reads "Not yet"
    public Geometry Icon;
    public ImageSource Logo; // replaces Icon when set
    public Color Accent;
    public bool Small; // uses the smaller open island
    public bool Sticky; // stays until a button is clicked, even over a higher-priority reminder
    public System.Media.SoundPlayer Sound; // plays as the island appears; null uses IslandSound.Appear
}

// The two labels of the compact pill after an answer.
class IslandResult
{
    public string Left, Right;

    // "10 min" or "20 sec"
    public static string Span(TimeSpan t) { return t.TotalMinutes >= 1 ? (int)t.TotalMinutes + " min" : (int)t.TotalSeconds + " sec"; }
    public static IslandResult NotYet(TimeSpan snooze) { return new IslandResult { Left = "Not yet", Right = "Back in " + Span(snooze) }; }
}

// A feature that wants the island. To add one: implement this, then list it in Program.StartReminders.
interface IReminder
{
    IslandNotice Due();                      // what to show now, or null if nothing
    bool StillCurrent(IslandNotice shown);   // false closes the island while it waits for an answer
    IslandResult Answer(IslandNotice shown, bool yes);
}
