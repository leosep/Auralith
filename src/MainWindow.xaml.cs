using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Auralith;

public partial class MainWindow : Window
{
    static readonly string[] Ext = { ".mp3", ".flac", ".wav", ".aiff", ".aif", ".m4a", ".aac", ".wma", ".ogg" };

    readonly AudioEngine engine = new();
    readonly ObservableCollection<Track> list = new();
    readonly HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Random rng = new();
    readonly Slider[] bandSliders = new Slider[AudioEngine.Freqs.Length];
    int current = -1;
    int repeat; // 0 off, 1 all, 2 one
    bool syncingSeek;
    bool vizRunning;

    public MainWindow()
    {
        InitializeComponent();
        PlaylistBox.ItemsSource = list;
        timer.Tick += (_, _) => UpdateTime();
        // Must not block the audio thread: it is the thread that raises Ended.
        engine.Ended += () => Dispatcher.BeginInvoke(() => Advance(1, auto: true));
        StateChanged += (_, _) => UpdateMaxGlyph();
        Closed += (_, _) => engine.Dispose();
        BuildEq();
        VolText.Text = "80";
        RefreshQueue();

        var args = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();
        if (args.Length > 0) { AddFiles(args); PlayIndex(0); }
    }

    int AddFiles(IEnumerable<string> files)
    {
        var oldCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        int added = 0;
        try
        {
            foreach (var f in files)
            {
                if (!Ext.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())) continue;
                if (!known.Add(f)) continue;
                list.Add(new Track(f));
                added++;
            }
        }
        finally
        {
            Mouse.OverrideCursor = oldCursor;
        }
        if (added > 0) RefreshQueue();
        return added;
    }

    void PlayIndex(int i)
    {
        if (i < 0 || i >= list.Count) return;
        int previous = current;
        try
        {
            engine.Load(list[i].Path);
            engine.Play();

            if (previous >= 0 && previous < list.Count) list[previous].IsCurrent = false;
            current = i;
            var track = list[i];
            track.IsCurrent = true;

            PlaylistBox.SelectedIndex = i;
            PlaylistBox.ScrollIntoView(track);
            TitleText.Text = track.Title;
            ArtistText.Text = track.Artist;
            AlbumText.Text = track.AlbumLine;
            DurationText.Text = " / " + track.DurationText;
            QualityText.Text = track.Format;
            ArtBrush.ImageSource = track.LoadArtwork();
            ExclBtn.IsChecked = engine.Exclusive;
            SetMode(engine.Exclusive);
            syncingSeek = true; Seek.Value = 0; syncingSeek = false;
            TimeText.Text = "00:00";
            timer.Start();
            PlayBtn.Content = "\uE769";
            StartViz();
            Title = $"{track.Title} — Auralith";
        }
        catch (Exception ex)
        {
            current = previous;
            if (previous >= 0 && previous < list.Count) list[previous].IsCurrent = true;
            MessageBox.Show(this, $"Cannot play:\n{list[i].Path}\n\n{ex.Message}", "Auralith",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Advance(int dir, bool auto = false)
    {
        if (list.Count == 0) return;
        if (auto && repeat == 2) { PlayIndex(current); return; }
        if (auto && repeat == 0 && list.Count == 1) { StopPlayback(); return; }

        int n;
        if (ShuffleBtn.IsChecked == true)
        {
            do n = rng.Next(list.Count); while (list.Count > 1 && n == current);
        }
        else
        {
            n = current + dir;
            if (n >= list.Count)
            {
                if (repeat == 1 || !auto) n = 0; else { StopPlayback(); return; }
            }
            if (n < 0) n = list.Count - 1;
        }
        PlayIndex(n);
    }

    void StopPlayback()
    {
        engine.Stop();
        timer.Stop();
        if (current >= 0 && current < list.Count) list[current].IsCurrent = false;
        PlayBtn.Content = "\uE768";
        syncingSeek = true; Seek.Value = 0; syncingSeek = false;
        TimeText.Text = "00:00";
        StopViz();
    }

    void UpdateTime()
    {
        if (!engine.IsLoaded) return;
        var len = engine.Length;
        var pos = engine.Position;
        TimeText.Text = Fmt(pos);
        if (!Seek.IsMouseCaptureWithin && len.TotalSeconds > 0)
        {
            syncingSeek = true;
            Seek.Value = pos.TotalSeconds / len.TotalSeconds;
            syncingSeek = false;
        }
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    void Play_Click(object s, RoutedEventArgs e)
    {
        if (!engine.IsLoaded) { PlayIndex(Math.Max(PlaylistBox.SelectedIndex, 0)); return; }
        if (engine.IsPlaying)
        {
            engine.Pause();
            PlayBtn.Content = "\uE768";
            StopViz();
        }
        else
        {
            engine.Play();
            PlayBtn.Content = "\uE769";
            StartViz();
        }
    }

    void Stop_Click(object s, RoutedEventArgs e) => StopPlayback();

    void Next_Click(object s, RoutedEventArgs e) => Advance(1);

    void Prev_Click(object s, RoutedEventArgs e)
    {
        if (engine.IsLoaded && engine.Position.TotalSeconds > 3) engine.Position = TimeSpan.Zero;
        else Advance(-1);
    }

    void Seek_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        // While not syncing from the timer, a mouse click/drag or keyboard arrow is a real seek.
        if (!syncingSeek && engine.IsLoaded && engine.Length.TotalSeconds > 0)
            engine.Position = TimeSpan.FromSeconds(e.NewValue * engine.Length.TotalSeconds);
    }

    void Vol_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        engine.SetVolume((float)e.NewValue);
        if (VolText != null) VolText.Text = Math.Round(e.NewValue * 100).ToString();
    }

    void Repeat_Click(object s, RoutedEventArgs e)
    {
        repeat = (repeat + 1) % 3;
        RepeatBtn.IsChecked = repeat != 0;
        RepeatBtn.Content = repeat == 2 ? "\uE8ED" : "\uE8EE";
        RepeatBtn.ToolTip = repeat switch { 0 => "Repeat: off", 1 => "Repeat: all", _ => "Repeat: one" };
    }

    void Add_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Audio|" + string.Join(";", Ext.Select(x => "*" + x))
        };
        if (dlg.ShowDialog(this) != true) return;
        bool wasEmpty = list.Count == 0;
        if (AddFiles(dlg.FileNames) > 0 && wasEmpty) PlayIndex(0);
    }

    void Clear_Click(object s, RoutedEventArgs e)
    {
        StopPlayback();
        list.Clear();
        known.Clear();
        current = -1;
        ResetNowPlaying();
        RefreshQueue();
    }

    void Playlist_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (PlaylistBox.SelectedIndex >= 0) PlayIndex(PlaylistBox.SelectedIndex);
    }

    void Window_DragEnter(object s, DragEventArgs e) => UpdateDropState(e);
    void Window_DragOver(object s, DragEventArgs e) => UpdateDropState(e);

    void UpdateDropState(DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    void Window_DragLeave(object s, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    void Window_Drop(object s, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        var files = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                    files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(x => x));
                else files.Add(path);
            }
            catch
            {
                // An inaccessible subfolder must not abort the whole drop.
            }
        }

        bool wasEmpty = list.Count == 0;
        if (AddFiles(files) > 0 && wasEmpty) PlayIndex(0);
    }

    void Window_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && PlaylistBox.SelectedIndex >= 0)
        {
            int i = PlaylistBox.SelectedIndex;
            if (i == current)
            {
                StopPlayback();
                current = -1;
                ResetNowPlaying();
            }
            known.Remove(list[i].Path);
            list.RemoveAt(i);
            if (i < current) current--;
            RefreshQueue();
            if (list.Count == 0) { current = -1; ResetNowPlaying(); }
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.FocusedElement is not ButtonBase)
        {
            Play_Click(s, e);
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Add_Click(s, e);
            e.Handled = true;
        }
        else if (e.Key == Key.MediaPlayPause) { Play_Click(s, e); e.Handled = true; }
        else if (e.Key == Key.MediaNextTrack) { Advance(1); e.Handled = true; }
        else if (e.Key == Key.MediaPreviousTrack) { Advance(-1); e.Handled = true; }
    }

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Max_Click(object s, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void UpdateMaxGlyph()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaxBtn.Content = maximized ? "\uE923" : "\uE922";
        MaxBtn.ToolTip = maximized ? "Restore" : "Maximize";
    }

    void Close_Click(object s, RoutedEventArgs e) => Close();

    void BuildEq()
    {
        var muted = (Brush)FindResource("Muted");
        var dim = (Brush)FindResource("Dim");
        var style = (Style)FindResource("EqStyle");

        for (int i = 0; i < bandSliders.Length; i++)
        {
            int idx = i;
            var gain = new TextBlock
            {
                Text = "0",
                FontSize = 9,
                Foreground = dim,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var sl = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -12,
                Maximum = 12,
                Value = 0,
                Height = 84,
                Style = style,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsMoveToPointEnabled = true
            };
            sl.ValueChanged += (_, e) =>
            {
                engine.SetBand(idx, (float)e.NewValue);
                gain.Text = e.NewValue.ToString("+0;-0;0");
            };
            sl.MouseDoubleClick += (_, _) => sl.Value = 0;

            var f = AudioEngine.Freqs[i];
            var label = f >= 1000 ? $"{f / 1000:0.#}k" : $"{f:0}";
            sl.ToolTip = $"{label} Hz — double-click to reset";
            AutomationProperties.SetName(sl, $"{label} Hz equalizer band");

            var freq = new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(gain);
            sp.Children.Add(sl);
            sp.Children.Add(freq);
            EqGrid.Children.Add(sp);
        }
    }

    void Eq_Click(object s, RoutedEventArgs e)
    {
        EqPanel.Visibility = EqBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateLayout();
        if (PlaylistBox.SelectedItem != null) PlaylistBox.ScrollIntoView(PlaylistBox.SelectedItem);
    }

    void EqReset_Click(object s, RoutedEventArgs e)
    {
        foreach (var band in bandSliders) band.Value = 0;
    }

    void Excl_Click(object s, RoutedEventArgs e)
    {
        engine.Exclusive = ExclBtn.IsChecked == true;
        if (engine.IsLoaded && current >= 0)
        {
            var pos = engine.Position;
            bool wasPlaying = engine.IsPlaying;
            PlayIndex(current);
            engine.Position = pos;
            if (!wasPlaying) { engine.Pause(); PlayBtn.Content = "\uE768"; StopViz(); }
        }
        else
        {
            SetMode(engine.Exclusive);
        }
    }

    void SetMode(bool exclusive)
    {
        ModeText.Text = exclusive ? "EXCLUSIVE" : "SHARED";
        ModeChip.Background = (Brush)FindResource(exclusive ? "ExclusiveChip" : "SharedChip");
        var fg = (Brush)FindResource(exclusive ? "Accent" : "SharedDot");
        ModeDot.Fill = fg;
        ModeText.Foreground = fg;
    }

    void RefreshQueue()
    {
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var total = TimeSpan.FromTicks(list.Sum(t => t.Duration.Ticks));
        QueueText.Text = list.Count == 0
            ? "EMPTY"
            : $"{list.Count} TRACK{(list.Count == 1 ? "" : "S")}  ·  {Fmt(total)}";
    }

    void ResetNowPlaying()
    {
        TitleText.Text = "Nothing playing";
        ArtistText.Text = "Add or drop music to begin";
        AlbumText.Text = "";
        TimeText.Text = "00:00";
        DurationText.Text = " / 00:00";
        QualityText.Text = "HI-FI";
        ArtBrush.ImageSource = null;
        SetMode(engine.Exclusive);
        Title = "Auralith";
    }

    void StartViz()
    {
        if (vizRunning) return;
        vizRunning = true;
        ((Storyboard)FindResource("LogoEq")).Begin(this, true);
        ((Storyboard)FindResource("GlowPulse")).Begin(this, true);
    }

    void StopViz()
    {
        if (!vizRunning) return;
        vizRunning = false;
        ((Storyboard)FindResource("LogoEq")).Stop(this);
        ((Storyboard)FindResource("GlowPulse")).Stop(this);
    }
}
