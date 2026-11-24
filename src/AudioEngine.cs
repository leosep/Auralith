using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Auralith;

/// <summary>WASAPI output (optional exclusive mode) + 10-band EQ.</summary>
public sealed class AudioEngine : IDisposable
{
    public static readonly float[] Freqs = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    readonly EqualizerBand[] bands = Freqs.Select(f => new EqualizerBand { Frequency = f, Bandwidth = 0.8f, Gain = 0 }).ToArray();
    AudioFileReader? reader;
    WasapiOut? output;
    Equalizer? eq;

    public bool Exclusive { get; set; }
    public float Volume { get; private set; } = 0.8f;
    public event Action? Ended;

    public bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;
    public bool IsLoaded => reader != null;
    public TimeSpan Length => reader?.TotalTime ?? TimeSpan.Zero;
    public TimeSpan Position
    {
        get => reader?.CurrentTime ?? TimeSpan.Zero;
        set { if (reader != null) reader.CurrentTime = value; }
    }

    public void Load(string path)
    {
        Stop();
        reader = new AudioFileReader(path) { Volume = Volume };
        eq = new Equalizer(reader, bands);
        // The output device is only opened on Play(), so a restored paused session
        // does not hold the sound card (important in exclusive mode).
    }

    WasapiOut Create(bool exclusive)
    {
        var o = new WasapiOut(exclusive ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared, 100);
        try
        {
            IWaveProvider wp = exclusive ? new SampleToWaveProvider24(eq!) : new SampleToWaveProvider(eq!);
            o.Init(wp);
            return o;
        }
        catch { o.Dispose(); throw; }
    }

    public void Play()
    {
        if (reader == null) return;
        if (output == null)
        {
            try
            {
                output = Create(Exclusive);
            }
            catch when (Exclusive)
            {
                // Device refused exclusive format: fall back to shared mode.
                Exclusive = false;
                output = Create(false);
            }
            output.PlaybackStopped += (s, _) => { if (ReferenceEquals(s, output)) Ended?.Invoke(); };
        }
        output.Play();
    }

    public void Pause() => output?.Pause();

    public void Stop()
    {
        var o = output; output = null;       // detach first so Ended is ignored
        o?.Stop(); o?.Dispose();
        reader?.Dispose(); reader = null; eq = null;
    }

    public void SetVolume(float v) { Volume = v; if (reader != null) reader.Volume = v; }

    public void SetBand(int i, float gainDb) { bands[i].Gain = gainDb; eq?.Update(); }

    public void Dispose() => Stop();
}
