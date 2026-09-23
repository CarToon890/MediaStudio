using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MediaStudio.ViewModels;

namespace MediaStudio.Views;

public partial class AudioTrimmerView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public AudioTrimmerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => DrawWaveform();
        _timer.Tick += (_, _) =>
        {
            if (DataContext is not AudioTrimmerViewModel vm) return;
            vm.CurrentPositionSeconds = AudioPlayer.Position.TotalSeconds;
            if (vm.CurrentPositionSeconds >= vm.EndSeconds) StopAtSelectionStart(vm);
        };
        var player = new MediaElement { Name = "AudioPlayer", LoadedBehavior = MediaState.Manual, Visibility = Visibility.Collapsed };
        RegisterName("AudioPlayer", player);
        ((Grid)Content).Children.Add(player);
    }

    private MediaElement AudioPlayer => (MediaElement)FindName("AudioPlayer");
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioTrimmerViewModel vm) return;
        vm.RequestSeek += Seek;
        vm.RequestTogglePlay += TogglePlay;
        vm.WaveformPeaks.CollectionChanged += PeaksChanged;
        vm.PropertyChanged += ViewModelPropertyChanged;
        DrawWaveform();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AudioTrimmerViewModel vm)
        {
            vm.RequestSeek -= Seek;
            vm.RequestTogglePlay -= TogglePlay;
            vm.WaveformPeaks.CollectionChanged -= PeaksChanged;
            vm.PropertyChanged -= ViewModelPropertyChanged;
        }
        _timer.Stop(); AudioPlayer.Stop();
    }
    private void PeaksChanged(object? sender, NotifyCollectionChangedEventArgs e) => DrawWaveform();
    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioTrimmerViewModel.PreviewFilePath) && DataContext is AudioTrimmerViewModel vm && File.Exists(vm.PreviewFilePath))
            AudioPlayer.Source = new Uri(vm.PreviewFilePath);
    }
    private void Seek(TimeSpan time) => AudioPlayer.Position = time;
    private void TogglePlay()
    {
        if (DataContext is not AudioTrimmerViewModel vm || !vm.HasLoadedFile) return;
        if (vm.IsPlaying) { AudioPlayer.Pause(); _timer.Stop(); vm.IsPlaying = false; }
        else { if (AudioPlayer.Position.TotalSeconds < vm.StartSeconds || AudioPlayer.Position.TotalSeconds >= vm.EndSeconds) AudioPlayer.Position = TimeSpan.FromSeconds(vm.StartSeconds); AudioPlayer.Play(); _timer.Start(); vm.IsPlaying = true; }
    }
    private void StopAtSelectionStart(AudioTrimmerViewModel vm) { AudioPlayer.Pause(); AudioPlayer.Position = TimeSpan.FromSeconds(vm.StartSeconds); vm.IsPlaying = false; _timer.Stop(); }
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (WaveformHost != null) { WaveformHost.Width = Math.Max(WaveformScroller.ActualWidth, 600) * e.NewValue; DrawWaveform(); } }
    private void DrawWaveform()
    {
        if (WaveformCanvas == null || DataContext is not AudioTrimmerViewModel vm) return;
        WaveformCanvas.Children.Clear();
        var width = Math.Max(WaveformHost?.ActualWidth ?? 0, 600);
        var height = Math.Max(WaveformCanvas.ActualHeight, 180);
        WaveformCanvas.Width = width;
        if (vm.WaveformPeaks.Count == 0) return;
        var step = width / vm.WaveformPeaks.Count;
        for (var i = 0; i < vm.WaveformPeaks.Count; i++)
        {
            var barHeight = Math.Max(2, vm.WaveformPeaks[i] * (height - 12));
            var line = new Line { X1 = i * step, X2 = i * step, Y1 = (height - barHeight) / 2, Y2 = (height + barHeight) / 2, Stroke = new SolidColorBrush(Color.FromRgb(48, 174, 239)), StrokeThickness = Math.Max(1, step * .65) };
            WaveformCanvas.Children.Add(line);
        }
    }
}
