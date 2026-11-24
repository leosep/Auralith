using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using TagLib;

namespace Auralith;

public sealed class Track : INotifyPropertyChanged
{
    public string Path { get; }
    public string Title { get; private set; }
    public string Artist { get; private set; }
    public string Album { get; private set; }
    public int Year { get; private set; }
    public TimeSpan Duration { get; private set; }

    public string DurationText => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"mm\:ss");

    public string AlbumLine
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Album)) return "Unknown album";
            return Year > 0 ? $"{Album}  ·  {Year}" : Album;
        }
    }

    public string Format
    {
        get
        {
            var ext = System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
            return ext is "FLAC" or "WAV" or "AIFF" or "AIF" ? "LOSSLESS" : ext;
        }
    }

    bool isCurrent;
    public bool IsCurrent
    {
        get => isCurrent;
        set
        {
            if (isCurrent == value) return;
            isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Track(string path)
    {
        Path = path;
        Title = System.IO.Path.GetFileNameWithoutExtension(path);
        Artist = "Unknown artist";
        Album = "";
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            if (!string.IsNullOrWhiteSpace(tag.Title)) Title = tag.Title.Trim();
            if (!string.IsNullOrWhiteSpace(tag.FirstPerformer)) Artist = tag.FirstPerformer.Trim();
            Album = tag.Album?.Trim() ?? "";
            Year = (int)tag.Year;
            Duration = file.Properties.Duration;
        }
        catch
        {
            // Unreadable tags are fine: fall back to the file name.
        }
    }

    /// <summary>Reads embedded cover art only when a track starts, keeping memory low for large queues.</summary>
    public BitmapImage? LoadArtwork()
    {
        try
        {
            using var file = TagLib.File.Create(Path);
            var pictures = file.Tag.Pictures;
            if (pictures is not { Length: > 0 }) return null;
            var data = pictures[0].Data.Data;
            if (data is not { Length: > 0 }) return null;

            using var stream = new MemoryStream(data);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public override string ToString() => Title;
}
