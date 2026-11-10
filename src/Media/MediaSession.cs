using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

// What is playing now, as one immutable snapshot the UI thread can read.
class MediaInfo
{
    public string Title, Artist, AppId;
    public byte[] Art; // same array until the track changes
    public bool Playing;
    public TimeSpan Position, End;
    public DateTimeOffset Updated; // when Position was measured
}

// Reads and controls the system's current media session (the one the Windows volume flyout shows).
// WinRT objects stay on one background thread: they failed when used from another thread.
class MediaSession
{
    internal static readonly TimeSpan KeepPaused = TimeSpan.FromMinutes(2);

    readonly ConcurrentQueue<Action<Session>> commands = new ConcurrentQueue<Action<Session>>();
    readonly AutoResetEvent wake = new AutoResetEvent(false);
    volatile MediaInfo now;

    internal MediaInfo Now { get { return now; } } // null when nothing is playing or recently paused

    internal static MediaSession Live()
    {
        var media = new MediaSession();
        new Thread(media.Poll) { IsBackground = true }.Start();
        return media;
    }

    internal void PlayPause() { Send(s => s.TryTogglePlayPauseAsync()); }
    internal void Next() { Send(s => s.TrySkipNextAsync()); }
    internal void Previous() { Send(s => s.TrySkipPreviousAsync()); }
    internal void Seek(TimeSpan to) { Send(s => s.TryChangePlaybackPositionAsync(to.Ticks)); }

    void Send(Action<Session> command) { commands.Enqueue(command); wake.Set(); }

    // Shown while playing, and for a while after pausing so it can be resumed from the island.
    internal static bool Visible(bool playing, DateTime lastPlaying, DateTime utcNow)
    {
        return playing || utcNow - lastPlaying < KeepPaused;
    }

    // Position moves on while playing; the session only reports it now and then.
    internal static TimeSpan PositionAt(MediaInfo m, DateTimeOffset at)
    {
        var position = m.Playing ? m.Position + (at - m.Updated) : m.Position;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position > m.End ? m.End : position;
    }

    // "3:05" or "1:02:03"
    internal static string Clock(TimeSpan t)
    {
        return t.Hours > 0 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
    }

    // ponytail: polls once a second because WinRT events don't compile with this build's references.
    void Poll()
    {
        GlobalSystemMediaTransportControlsSessionManager manager = null;
        string trackKey = null;
        byte[] art = null;
        var lastPlaying = DateTime.MinValue;
        while (true)
        {
            try
            {
                if (manager == null) manager = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                var session = manager.GetCurrentSession();
                Action<Session> command;
                bool acted = false;
                while (commands.TryDequeue(out command))
                {
                    if (session != null) command(session);
                    acted = true;
                }
                if (acted) Thread.Sleep(250); // let the player apply it before reading back

                MediaInfo next = null;
                if (session != null)
                {
                    var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    if (playing) lastPlaying = DateTime.UtcNow;
                    if (Visible(playing, lastPlaying, DateTime.UtcNow))
                    {
                        var props = Wait(session.TryGetMediaPropertiesAsync());
                        var key = session.SourceAppUserModelId + "\n" + props.Title + "\n" + props.Artist;
                        if (key != trackKey) { trackKey = key; art = ReadArt(props.Thumbnail); }
                        var timeline = session.GetTimelineProperties();
                        next = new MediaInfo
                        {
                            Title = props.Title, Artist = props.Artist, AppId = session.SourceAppUserModelId, Art = art, Playing = playing,
                            Position = timeline.Position, End = timeline.EndTime, Updated = timeline.LastUpdatedTime,
                        };
                    }
                }
                now = next;
            }
            catch (Exception) { manager = null; } // asks Windows again on the next pass
            wake.WaitOne(1000);
        }
    }

    static byte[] ReadArt(IRandomAccessStreamReference thumbnail)
    {
        if (thumbnail == null) return null;
        try
        {
            using (var stream = Wait(thumbnail.OpenReadAsync()))
            {
                if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024) return null;
                using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                {
                    Wait(reader.LoadAsync((uint)stream.Size));
                    var bytes = new byte[stream.Size];
                    reader.ReadBytes(bytes);
                    return bytes;
                }
            }
        }
        catch (Exception) { return null; }
    }

    static T Wait<T>(IAsyncOperation<T> op)
    {
        var timer = Stopwatch.StartNew();
        while (op.Status == AsyncStatus.Started)
        {
            if (timer.ElapsedMilliseconds > 3000) throw new TimeoutException();
            Thread.Sleep(5);
        }
        return op.GetResults();
    }
}
