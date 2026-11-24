using NAudio.Dsp;
using NAudio.Wave;

namespace Auralith;

public sealed class EqualizerBand
{
    public float Frequency { get; set; }
    public float Bandwidth { get; set; }
    public float Gain { get; set; }
}

/// <summary>
/// Sample-provider equalizer built from peaking biquad filters. Filter state is
/// kept per channel so stereo imaging is preserved.
/// </summary>
public sealed class Equalizer : ISampleProvider
{
    readonly ISampleProvider source;
    readonly EqualizerBand[] bands;
    readonly BiQuadFilter[,] filters;
    readonly int channels;

    public Equalizer(ISampleProvider source, EqualizerBand[] bands)
    {
        this.source = source;
        this.bands = bands;
        channels = source.WaveFormat.Channels;
        filters = new BiQuadFilter[bands.Length, channels];
        WaveFormat = source.WaveFormat;
        Update();
    }

    public WaveFormat WaveFormat { get; }

    public void Update()
    {
        for (int b = 0; b < bands.Length; b++)
            for (int c = 0; c < channels; c++)
                filters[b, c] = BiQuadFilter.PeakingEQ(
                    WaveFormat.SampleRate, bands[b].Frequency, bands[b].Bandwidth, bands[b].Gain);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!bands.Any(b => b.Gain != 0f)) return read;

        for (int n = 0; n < read; n++)
        {
            int channel = n % channels;
            float sample = buffer[offset + n];
            for (int b = 0; b < bands.Length; b++)
                sample = filters[b, channel].Transform(sample);
            buffer[offset + n] = sample;
        }
        return read;
    }
}
