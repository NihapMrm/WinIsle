// The Control Center's Preview buttons: shows a sample island once, ahead of every other reminder, and its
// buttons only close it. Nothing real changes: no water timer reset, no prayer marked, no app opened.
class PreviewReminder : IReminder
{
    IslandNotice pending;

    internal void Show(IslandNotice n) { pending = n; }

    public IslandNotice Due() { return pending; }

    public bool StillCurrent(IslandNotice shown) { return pending == shown; }

    public IslandResult Answer(IslandNotice shown, bool yes)
    {
        pending = null;
        return new IslandResult { Left = "Preview", Right = shown.Title };
    }
}
