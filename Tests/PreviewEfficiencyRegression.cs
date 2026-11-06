using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MediaStudio.ViewModels;
using MediaStudio.Views;

internal static class PreviewEfficiencyRegression
{
    public static async Task RunAsync(Window window, MainViewModel vm)
    {
        vm.CurrentTab = "AudioTrimmer";
        await Flush();
        var audioView = Descendants(window).OfType<AudioTrimmerView>().Single();
        var videoView = Descendants(window).OfType<TrimmerView>().Single();
        var canvas = (Canvas)audioView.FindName("WaveformCanvas");
        var overlay = (Canvas)audioView.FindName("WaveformOverlay");
        var audioTimer = Timer(audioView, "_timer");
        var videoTimer = Timer(videoView, "_positionTimer");
        Check(!audioTimer.IsEnabled && !videoTimer.IsEnabled, "Idle previews must have no running position timers");
        Check(Field(audioView, "_audioOutput") == null, "Importing audio must not open an output device");
        var originalStart = vm.AudioTrimmerVm.StartSeconds;
        vm.AudioTrimmerVm.StartSeconds = vm.AudioTrimmerVm.TotalSeconds / 4;
        vm.AudioTrimmerVm.SeekToStartCommand.Execute(null);
        Check(vm.AudioTrimmerVm.CurrentPositionSeconds == vm.AudioTrimmerVm.StartSeconds && Field(audioView, "_audioOutput") == null,
            "Keyboard seek must work before opening an audio device");
        vm.AudioTrimmerVm.StartSeconds = originalStart;

        var firstDrawing = canvas.Children[0];
        var peakCount = vm.AudioTrimmerVm.WaveformPeaks.Count;
        for (var i = 0; i < 512; i++) vm.AudioTrimmerVm.WaveformPeaks.Add(.5);
        Check(ReferenceEquals(firstDrawing, canvas.Children[0]), "A synchronous peak batch must not rebuild the waveform per peak");
        await Flush();
        Check(!ReferenceEquals(firstDrawing, canvas.Children[0]), "Visible peak changes must eventually render");

        var retainedOverlay = overlay.Children.Cast<UIElement>().ToArray();
        for (var i = 1; i <= 100; i++) vm.AudioTrimmerVm.CurrentPositionSeconds = vm.AudioTrimmerVm.TotalSeconds * i / 101;
        Check(retainedOverlay.SequenceEqual(overlay.Children.Cast<UIElement>()), "Playhead updates must reuse overlay visuals");
        var playhead = overlay.Children.OfType<System.Windows.Shapes.Line>().Last();
        Check(playhead.X1 > 0, "Reused playhead must still move");

        vm.AudioTrimmerVm.IsPlaying = true;
        audioTimer.Start(); // Exercise navigation cleanup without requiring physical audio hardware.
        vm.CurrentTab = "Home";
        await Flush();
        Check(!audioTimer.IsEnabled && !vm.AudioTrimmerVm.IsPlaying, "Hidden audio preview must stop");
        firstDrawing = canvas.Children[0];
        while (vm.AudioTrimmerVm.WaveformPeaks.Count > peakCount)
            vm.AudioTrimmerVm.WaveformPeaks.RemoveAt(vm.AudioTrimmerVm.WaveformPeaks.Count - 1);
        await Flush();
        Check(ReferenceEquals(firstDrawing, canvas.Children[0]), "Hidden waveform changes must not render");

        vm.CurrentTab = "AudioTrimmer";
        await Flush();
        Check(!ReferenceEquals(firstDrawing, canvas.Children[0]), "Returning to audio must render deferred changes");
        Check(!audioTimer.IsEnabled && Field(audioView, "_audioOutput") == null, "Navigation must not auto-resume playback or acquire a device");
        vm.AudioTrimmerVm.IsPlaying = true;
        audioTimer.Start();
        window.WindowState = WindowState.Minimized;
        await Flush();
        Check(!audioTimer.IsEnabled && !vm.AudioTrimmerVm.IsPlaying, "Minimized audio preview must stop");
        window.WindowState = WindowState.Normal;
        await Flush();
        Check(!audioTimer.IsEnabled, "Restoring the window must not resume audio automatically");

        vm.CurrentTab = "Trimmer";
        await Flush();
        vm.TrimmerVm.IsPlaying = true;
        videoTimer.Start();
        vm.CurrentTab = "Home";
        await Flush();
        Check(!videoTimer.IsEnabled && !vm.TrimmerVm.IsPlaying, "Hidden video preview must stop");
        vm.CurrentTab = "Trimmer";
        await Flush();
        vm.TrimmerVm.IsPlaying = true;
        videoTimer.Start();
        window.WindowState = WindowState.Minimized;
        await Flush();
        Check(!videoTimer.IsEnabled && !vm.TrimmerVm.IsPlaying, "Minimized video preview must stop");
        window.WindowState = WindowState.Normal;
        await Flush();
        Check(!videoTimer.IsEnabled, "Restoring the window must not resume video automatically");
        Check(vm.TrimmerVm.HasLoadedFile && vm.AudioTrimmerVm.CanTrim, "Preview suspension must preserve processing readiness");
        Console.WriteLine("PASS: coalesced waveform batches, retained playhead visuals, lazy audio device, hidden/minimized preview timers and processing readiness");
    }

    private static object? Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static DispatcherTimer Timer(object owner, string name) => (DispatcherTimer)Field(owner, name)!;
    private static async Task Flush() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
