using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MediaStudio.ViewModels;
using NAudio.Wave;

namespace MediaStudio.Views;

public partial class AudioTrimmerView : UserControl
{
    private enum DragTarget { None, Start, End, Playhead }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private AudioFileReader? _audioReader;
    private WaveOutEvent? _audioOutput;
    private DragTarget _dragTarget;
    private double _lastZoom = 1;
    private DispatcherOperation? _redraw;
    private bool _waveformDirty = true;
    private Line? _playhead;
    private Polygon? _playheadHandle;
    private Window? _hostWindow;
    private static readonly SolidColorBrush WaveformBrush = CreateWaveformBrush();

    private static SolidColorBrush CreateWaveformBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(72, 181, 238));
        brush.Freeze();
        return brush;
    }

    public AudioTrimmerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => RequestWaveformRedraw();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) SuspendPreview();
            else RequestWaveformRedraw();
        };
        _timer.Tick += (_, _) => UpdatePlaybackPosition();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioTrimmerViewModel vm) return;
        vm.RequestSeek += Seek;
        vm.RequestTogglePlay += TogglePlay;
        vm.WaveformPeaks.CollectionChanged += PeaksChanged;
        vm.PropertyChanged += ViewModelPropertyChanged;
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow != null) _hostWindow.StateChanged += HostWindowStateChanged;
        RequestWaveformRedraw();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AudioTrimmerViewModel vm)
        {
            vm.RequestSeek -= Seek;
            vm.RequestTogglePlay -= TogglePlay;
            vm.WaveformPeaks.CollectionChanged -= PeaksChanged;
            vm.PropertyChanged -= ViewModelPropertyChanged;
            vm.IsPlaying = false;
        }
        if (_hostWindow != null) _hostWindow.StateChanged -= HostWindowStateChanged;
        _hostWindow = null;
        SuspendPreview();
    }

    private bool CanRenderPreview => IsLoaded && IsVisible && _hostWindow?.WindowState != WindowState.Minimized;

    private void HostWindowStateChanged(object? sender, EventArgs e)
    {
        if (_hostWindow?.WindowState == WindowState.Minimized) SuspendPreview();
        else if (_waveformDirty) RequestWaveformRedraw();
    }

    private void SuspendPreview()
    {
        if (DataContext is AudioTrimmerViewModel vm) vm.IsPlaying = false;
        _redraw?.Abort();
        _redraw = null;
        _dragTarget = DragTarget.None;
        if (WaveformHost.IsMouseCaptured) WaveformHost.ReleaseMouseCapture();
        DisposePlayer();
    }

    private void PeaksChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestWaveformRedraw();

    private void RequestWaveformRedraw()
    {
        _waveformDirty = true;
        if (!CanRenderPreview || _redraw?.Status == DispatcherOperationStatus.Pending) return;
        _redraw = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _redraw = null;
            if (!CanRenderPreview) return;
            _waveformDirty = false;
            DrawWaveform();
        }));
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not AudioTrimmerViewModel vm) return;
        if (e.PropertyName == nameof(AudioTrimmerViewModel.PreviewFilePath))
        {
            vm.IsPlaying = false;
            DisposePlayer(); // Open the audio device only when playback is requested.
        }
        else if (e.PropertyName == nameof(AudioTrimmerViewModel.VolumePercent) && _audioReader is not null)
            _audioReader.Volume = (float)(vm.VolumePercent / 100d);
        else if (e.PropertyName is nameof(AudioTrimmerViewModel.StartSeconds) or nameof(AudioTrimmerViewModel.EndSeconds)
                 or nameof(AudioTrimmerViewModel.CurrentPositionSeconds) or nameof(AudioTrimmerViewModel.TotalSeconds))
        {
            if (!CanRenderPreview) { _waveformDirty = true; return; }
            if (e.PropertyName == nameof(AudioTrimmerViewModel.CurrentPositionSeconds))
            {
                UpdatePlayhead();
                if (vm.IsPlaying) KeepPlayheadVisible(vm.CurrentPositionSeconds);
            }
            else DrawOverlay();
        }
    }

    private void LoadPreview(string path)
    {
        DisposePlayer();
        try
        {
            _audioReader = new AudioFileReader(path);
            if (DataContext is AudioTrimmerViewModel vm)
                _audioReader.Volume = (float)(vm.VolumePercent / 100d);
            _audioOutput = new WaveOutEvent { DesiredLatency = 100 };
            _audioOutput.Init(_audioReader);
        }
        catch
        {
            DisposePlayer();
            if (DataContext is AudioTrimmerViewModel vm)
                vm.StatusText = "เล่นตัวอย่างเสียงไม่ได้ ตรวจสอบอุปกรณ์เสียงแล้วลองใหม่ ยังสามารถตัดและบันทึกไฟล์ได้";
        }
    }

    private void DisposePlayer()
    {
        _timer.Stop();
        _audioOutput?.Stop();
        _audioOutput?.Dispose();
        _audioReader?.Dispose();
        _audioOutput = null;
        _audioReader = null;
    }

    private void Seek(TimeSpan time)
    {
        if (DataContext is not AudioTrimmerViewModel vm) return;
        var seconds = Math.Clamp(time.TotalSeconds, 0, vm.TotalSeconds);
        if (_audioReader is not null)
        {
            _audioReader.CurrentTime = TimeSpan.FromSeconds(Math.Min(seconds, _audioReader.TotalTime.TotalSeconds));
            seconds = _audioReader.CurrentTime.TotalSeconds;
        }
        vm.CurrentPositionSeconds = seconds;
    }

    private void TogglePlay()
    {
        if (DataContext is not AudioTrimmerViewModel vm || !vm.HasLoadedFile || !CanRenderPreview) return;
        if (_audioReader is null && File.Exists(vm.PreviewFilePath))
        {
            LoadPreview(vm.PreviewFilePath);
            Seek(TimeSpan.FromSeconds(vm.CurrentPositionSeconds));
        }
        if (_audioOutput is null || _audioReader is null) return;
        if (vm.IsPlaying)
        {
            _audioOutput.Pause();
            _timer.Stop();
            vm.IsPlaying = false;
            return;
        }
        if (_audioReader.CurrentTime.TotalSeconds < vm.StartSeconds || _audioReader.CurrentTime.TotalSeconds >= vm.EndSeconds)
            _audioReader.CurrentTime = TimeSpan.FromSeconds(vm.StartSeconds);
        _audioOutput.Play();
        _timer.Start();
        vm.IsPlaying = true;
    }

    private void UpdatePlaybackPosition()
    {
        if (DataContext is not AudioTrimmerViewModel vm || _audioReader is null || _audioOutput is null) return;
        vm.CurrentPositionSeconds = _audioReader.CurrentTime.TotalSeconds;
        if (vm.CurrentPositionSeconds < vm.EndSeconds && _audioOutput.PlaybackState == PlaybackState.Playing) return;
        _audioOutput.Pause();
        _audioReader.CurrentTime = TimeSpan.FromSeconds(vm.StartSeconds);
        vm.CurrentPositionSeconds = vm.StartSeconds;
        vm.IsPlaying = false;
        _timer.Stop();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WaveformHost is null || WaveformScroller is null) return;
        var viewport = Math.Max(WaveformScroller.ViewportWidth, 600);
        var oldWidth = Math.Max(WaveformHost.ActualWidth, viewport * _lastZoom);
        var centerRatio = oldWidth <= 0 ? 0 : (WaveformScroller.HorizontalOffset + viewport / 2) / oldWidth;
        _lastZoom = e.NewValue;
        WaveformHost.Width = viewport * e.NewValue;
        WaveformHost.UpdateLayout();
        RequestWaveformRedraw();
        WaveformScroller.ScrollToHorizontalOffset(Math.Max(0, centerRatio * WaveformHost.Width - viewport / 2));
    }

    private void DrawWaveform()
    {
        if (WaveformCanvas is null || DataContext is not AudioTrimmerViewModel vm) return;
        WaveformCanvas.Children.Clear();
        var width = Math.Max(WaveformHost?.ActualWidth ?? 0, 600);
        WaveformCanvas.Width = width;
        DrawTimeline(width, vm.TotalSeconds);
        if (vm.WaveformPeaks.Count > 0)
        {
            var step = width / vm.WaveformPeaks.Count;
            const double center = 130;
            const double waveformHeight = 176;
            for (var i = 0; i < vm.WaveformPeaks.Count; i++)
            {
                var barHeight = Math.Max(2, vm.WaveformPeaks[i] * waveformHeight);
                WaveformCanvas.Children.Add(new Line
                {
                    X1 = i * step, X2 = i * step,
                    Y1 = center - barHeight / 2, Y2 = center + barHeight / 2,
                    Stroke = WaveformBrush,
                    StrokeThickness = Math.Max(1, step * .65)
                });
            }
        }
        DrawOverlay();
    }

    private void DrawTimeline(double width, double totalSeconds)
    {
        if (totalSeconds <= 0) return;
        var targetTicks = Math.Max(4, (int)(width / 110));
        var rawStep = totalSeconds / targetTicks;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(rawStep, .001))));
        var normalized = rawStep / magnitude;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        var secondsPerTick = nice * magnitude;
        for (var time = 0d; time <= totalSeconds + secondsPerTick / 2; time += secondsPerTick)
        {
            var x = time / totalSeconds * width;
            WaveformCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 21, Y2 = 29, Stroke = Brushes.Gray, StrokeThickness = 1 });
            var label = new TextBlock
            {
                Text = FormatTimelineTime(time), FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(178, 178, 178))
            };
            Canvas.SetLeft(label, Math.Min(x + 3, Math.Max(0, width - 54)));
            Canvas.SetTop(label, 3);
            WaveformCanvas.Children.Add(label);
        }
        WaveformCanvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = 29, Y2 = 29, Stroke = Brushes.DimGray, StrokeThickness = 1 });
    }

    private static string FormatTimelineTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private void DrawOverlay()
    {
        if (WaveformOverlay is null || WaveformHost is null || DataContext is not AudioTrimmerViewModel vm) return;
        WaveformOverlay.Children.Clear();
        _playhead = null;
        _playheadHandle = null;
        if (vm.TotalSeconds <= 0) return;
        var width = Math.Max(WaveformHost.ActualWidth, 600);
        WaveformOverlay.Width = width;
        var startX = SecondsToX(vm.StartSeconds, width, vm.TotalSeconds);
        var endX = SecondsToX(vm.EndSeconds, width, vm.TotalSeconds);
        var playX = SecondsToX(vm.CurrentPositionSeconds, width, vm.TotalSeconds);
        AddShade(0, startX);
        AddShade(endX, Math.Max(0, width - endX));
        var selection = new Rectangle { Width = Math.Max(0, endX - startX), Height = 194, Fill = new SolidColorBrush(Color.FromArgb(34, 30, 144, 255)) };
        Canvas.SetLeft(selection, startX); Canvas.SetTop(selection, 30); WaveformOverlay.Children.Add(selection);
        AddMarker(startX, Color.FromRgb(55, 200, 120), "เริ่ม");
        AddMarker(endX, Color.FromRgb(255, 167, 38), "สิ้นสุด");
        _playhead = new Line { X1 = playX, X2 = playX, Y1 = 30, Y2 = 224, Stroke = Brushes.White, StrokeThickness = 2 };
        WaveformOverlay.Children.Add(_playhead);
        _playheadHandle = new Polygon { Points = new PointCollection { new(-5, 30), new(5, 30), new(0, 38) }, Fill = Brushes.White };
        Canvas.SetLeft(_playheadHandle, playX);
        WaveformOverlay.Children.Add(_playheadHandle);
    }

    private void UpdatePlayhead()
    {
        if (_playhead is null || _playheadHandle is null || DataContext is not AudioTrimmerViewModel vm || vm.TotalSeconds <= 0) return;
        var x = SecondsToX(vm.CurrentPositionSeconds, Math.Max(WaveformHost.ActualWidth, 600), vm.TotalSeconds);
        _playhead.X1 = _playhead.X2 = x;
        Canvas.SetLeft(_playheadHandle, x);
    }

    private void AddShade(double x, double width)
    {
        var shade = new Rectangle { Width = Math.Max(0, width), Height = 194, Fill = new SolidColorBrush(Color.FromArgb(125, 0, 0, 0)) };
        Canvas.SetLeft(shade, x); Canvas.SetTop(shade, 30); WaveformOverlay.Children.Add(shade);
    }

    private void AddMarker(double x, Color color, string labelText)
    {
        WaveformOverlay.Children.Add(new Line { X1 = x, X2 = x, Y1 = 28, Y2 = 224, Stroke = new SolidColorBrush(color), StrokeThickness = 3 });
        var label = new Border
        {
            Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock { Text = labelText, Foreground = Brushes.Black, FontSize = 10, FontWeight = FontWeights.SemiBold }
        };
        Canvas.SetLeft(label, Math.Max(0, x - 14)); Canvas.SetTop(label, 31); WaveformOverlay.Children.Add(label);
    }

    private void WaveformHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AudioTrimmerViewModel vm || !vm.HasLoadedFile) return;
        var width = Math.Max(WaveformHost.ActualWidth, 600);
        var x = Math.Clamp(e.GetPosition(WaveformHost).X, 0, width);
        var startX = SecondsToX(vm.StartSeconds, width, vm.TotalSeconds);
        var endX = SecondsToX(vm.EndSeconds, width, vm.TotalSeconds);
        _dragTarget = Math.Abs(x - startX) <= 10 ? DragTarget.Start : Math.Abs(x - endX) <= 10 ? DragTarget.End : DragTarget.Playhead;
        WaveformHost.CaptureMouse();
        ApplyDrag(x, width, vm);
        e.Handled = true;
    }

    private void WaveformHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTarget == DragTarget.None || e.LeftButton != MouseButtonState.Pressed || DataContext is not AudioTrimmerViewModel vm) return;
        var width = Math.Max(WaveformHost.ActualWidth, 600);
        ApplyDrag(Math.Clamp(e.GetPosition(WaveformHost).X, 0, width), width, vm);
    }

    private void WaveformHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragTarget = DragTarget.None;
        WaveformHost.ReleaseMouseCapture();
    }

    private void ApplyDrag(double x, double width, AudioTrimmerViewModel vm)
    {
        var seconds = x / width * vm.TotalSeconds;
        switch (_dragTarget)
        {
            case DragTarget.Start: vm.StartSeconds = Math.Min(seconds, vm.EndSeconds - .01); break;
            case DragTarget.End: vm.EndSeconds = Math.Max(seconds, vm.StartSeconds + .01); break;
            default:
                vm.CurrentPositionSeconds = seconds;
                Seek(TimeSpan.FromSeconds(seconds));
                break;
        }
    }

    private void KeepPlayheadVisible(double seconds)
    {
        if (DataContext is not AudioTrimmerViewModel vm || vm.TotalSeconds <= 0) return;
        var x = SecondsToX(seconds, Math.Max(WaveformHost.ActualWidth, 600), vm.TotalSeconds);
        var left = WaveformScroller.HorizontalOffset;
        var right = left + WaveformScroller.ViewportWidth;
        if (x < left + 24 || x > right - 24)
            WaveformScroller.ScrollToHorizontalOffset(Math.Max(0, x - WaveformScroller.ViewportWidth / 2));
    }

    private static double SecondsToX(double seconds, double width, double totalSeconds)
        => Math.Clamp(seconds / Math.Max(totalSeconds, .01) * width, 0, width);
}
