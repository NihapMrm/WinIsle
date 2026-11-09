using System;
using System.IO;
using System.Linq;
using System.Media;

// Island chimes, synthesized here so there are no audio files to ship.
// Tune by changing the arguments below: peak volume (0 to 1), length in ms, then pairs of note hertz and start ms.
static class IslandSound
{
    const int Rate = 44100;

    public static readonly SoundPlayer Appear = Load(Wav(0.32, 800, 784, 0, 1175, 110));
    public static readonly SoundPlayer Yes = Load(Wav(0.36, 850, 659, 0, 784, 90, 1047, 180));
    public static readonly SoundPlayer NotYet = Load(Wav(0.24, 500, 587, 0, 440, 90));
    public static readonly SoundPlayer Water = Load(Drops(0.4, 800, 900, 0, 1250, 170, 760, 340));

    static SoundPlayer Load(byte[] wav)
    {
        var player = new SoundPlayer(new MemoryStream(wav));
        player.Load();
        return player;
    }

    // Each note is a soft bell: quick attack, exponential decay.
    internal static byte[] Wav(double peak, int ms, params double[] notes)
    {
        return Mix(peak, ms, notes, (hz, t) =>
        {
            double phase = 2 * Math.PI * hz * t;
            return Math.Min(t / 0.005, 1) * Math.Exp(-t * 9) * (Math.Sin(phase) + 0.22 * Math.Sin(2 * phase));
        });
    }

    // Each drop is a short pop whose pitch rises, like a bubble reaching the surface.
    internal static byte[] Drops(double peak, int ms, params double[] drops)
    {
        return Mix(peak, ms, drops, (hz, t) => Math.Min(t / 0.003, 1) * Math.Exp(-t * 26) * Math.Sin(2 * Math.PI * hz * (t + 6 * t * t)));
    }

    // Adds up one voice per (hertz, start ms) pair and encodes 16-bit mono PCM.
    static byte[] Mix(double peak, int ms, double[] pairs, Func<double, double, double> voice)
    {
        var mix = new double[Rate * ms / 1000];
        for (int n = 0; n < pairs.Length; n += 2)
        {
            int start = (int)(pairs[n + 1] * Rate / 1000);
            for (int i = start; i < mix.Length; i++) mix[i] += voice(pairs[n], (i - start) / (double)Rate);
        }
        double scale = peak * short.MaxValue / mix.Max(x => Math.Abs(x));
        double fade = Rate * 0.04; // the last 40 ms reach silence, so playback ends without a click
        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            w.Write("RIFF".ToCharArray()); w.Write(36 + mix.Length * 2); w.Write("WAVEfmt ".ToCharArray());
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(mix.Length * 2);
            for (int i = 0; i < mix.Length; i++) w.Write((short)(mix[i] * scale * Math.Min((mix.Length - 1 - i) / fade, 1)));
            w.Flush();
            return stream.ToArray();
        }
    }
}
