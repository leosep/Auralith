using System.IO;
using System.Text.Json;

namespace Auralith;

/// <summary>Everything Auralith remembers between runs.</summary>
public sealed class AppState
{
    public List<string> Playlist { get; set; } = new();
    public int CurrentIndex { get; set; } = -1;
    public double PositionSeconds { get; set; }
    public double Volume { get; set; } = 0.8;
    public float[] Eq { get; set; } = new float[AudioEngine.Freqs.Length];
    public int Repeat { get; set; }
    public bool Shuffle { get; set; }
    public bool Exclusive { get; set; }

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Auralith");

    public static string FilePath => Path.Combine(Dir, "state.json");

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppState();
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new AppState();
        }
        catch
        {
            // A corrupt or unreadable state file must never block startup.
            return new AppState();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            // Remembering the session is a convenience; never let it fail playback.
        }
    }
}
