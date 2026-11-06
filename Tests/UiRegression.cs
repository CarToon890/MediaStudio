using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaStudio.Models;
using MediaStudio.Services;
using MediaStudio.ViewModels;

internal static class UiRegression
{
    public static Task RunAsync(string root, string source, DependencyService dependency)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/MediaStudio;component/Themes/StudioResources.xaml") };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
            {
                MediaStudio.MainWindow? window = null;
                try
                {
                    var data = Directory.CreateDirectory(Path.Combine(root, "ui-settings")).FullName;
                    var output = Directory.CreateDirectory(Path.Combine(data, "โฟลเดอร์ผลลัพธ์สำหรับตรวจชื่อยาวและการตัดบรรทัด")).FullName;
                    File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(new AppSettings { DownloadDirectory = output, HasSeenEnginePrompt = true, HasCompletedOnboarding = true, LastVisitedTab = "Home" }));
                    var settings = new SettingsService(data);
                    var workspace = new MediaWorkspace(Path.Combine(data, "history.json"));
                    var ffmpeg = new FFmpegService(dependency);
                    var downloader = new DownloaderViewModel(new PreviewDownloader(dependency), settings, dependency, workspace);
                    var converter = new ConverterViewModel(ffmpeg, settings, dependency, workspace);
                    var video = new TrimmerViewModel(ffmpeg, settings, dependency, workspace);
                    var audio = new AudioTrimmerViewModel(ffmpeg, settings, dependency, workspace);
                    var location = new OutputLocationViewModel(settings);
                    var settingsVm = new SettingsViewModel(settings, dependency, new NoConsent(), new AppUpdateService(), downloader, converter, video, audio, location);
                    var vm = new MainViewModel(new HomeViewModel(workspace), downloader, converter, video, audio, new WorkspaceViewModel(workspace), settingsVm, dependency, settings, workspace, new NoConsent(), location);
                    window = new MediaStudio.MainWindow(vm) { ShowActivated = false, MaxWidth = 1040 };
                    window.Show();
                    window.Left = -30000; window.Top = -30000;
                    var capture = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-v1.1.2")).FullName;
                    var matrix = new[] { (Name: "1366-125", Width: 1092d, Height: 570d, Scale: 1.25), (Name: "1920-150", Width: 1280d, Height: 680d, Scale: 1.5), (Name: "2560-200", Width: 1280d, Height: 680d, Scale: 2d), (Name: "minimum", Width: 960d, Height: 540d, Scale: 1d) };
                    foreach (var size in matrix)
                    {
                        // A hosted runner may clamp top-level windows to its small virtual desktop.
                        // Verify native resize behavior against the actual width, then render the
                        // content at the requested logical size independently of that desktop.
                        vm.IsCompactNavigation = window.ActualWidth < 1100;
                        window.Width = 960;
                        await Flush();
                        window.Width = size.Width; window.Height = size.Height;
                        await Flush();
                        Check(vm.IsCompactNavigation == (window.ActualWidth < 1100), "Native compact navigation breakpoint");
                        vm.IsCompactNavigation = size.Width < 1100;
                        foreach (var page in new[] { "Home", "Downloader", "Converter", "Trimmer", "AudioTrimmer", "Workspace", "Settings" })
                        {
                            vm.CurrentTab = page;
                            await Flush();
                            Capture(window, Path.Combine(capture, size.Name + "-" + page + "-empty.png"), size.Scale, size.Width, size.Height);
                            CheckHorizontalBounds(window);
                        }
                        Check(vm.IsCompactNavigation == (size.Width < 1100), "Compact navigation breakpoint");
                    }
                    for (var i = 0; i < 50; i++)
                    {
                        downloader.DownloadQueue.Add(new DownloadItem { Title = $"ไฟล์ตัวอย่างชื่อยาวสำหรับตรวจการจัดวาง_{i}_ต้นฉบับสำหรับแปลงและดาวน์โหลด.mp4", OutputPath = source, IsAudioOnly = i % 2 == 0, Status = i % 3 == 0 ? TaskState.Completed : TaskState.Failed, StatusMessage = "ตรวจสอบชื่อไฟล์และโฟลเดอร์ปลายทางแล้วลองใหม่" });
                        converter.ConversionList.Add(new ConversionItem { FileName = $"รายการทดสอบชื่อยาวสำหรับตรวจการตัดบรรทัด_{i}.mp4", SourceFilePath = source, OutputPath = source, Status = i % 3 == 0 ? TaskState.Completed : TaskState.Queued, StatusMessage = "พร้อมแปลงไฟล์" });
                    }
                    converter.IsCompressEnabled = true;
                    vm.CurrentTab = "Downloader";
                    await Flush();
                    downloader.UrlInput = "https://example.test/preview";
                    await downloader.FetchInfoCommand.ExecuteAsync(null);
                    Check(downloader.HasThumbnail && downloader.HasAuthor, "Downloader must preserve thumbnail and author preview");
                    await video.LoadFileAsync(source);
                    await audio.LoadFileAsync(source);
                    workspace.Register(source, "ตรวจการส่งต่อ", 1);
                    converter.InputContext = video.InputContext = audio.InputContext = "เปิดจากคลังงาน • ไฟล์ผลลัพธ์ที่บันทึกไว้";
                    foreach (var size in matrix)
                    {
                        // A hosted runner may clamp top-level windows to its small virtual desktop.
                        // Verify native resize behavior against the actual width, then render the
                        // content at the requested logical size independently of that desktop.
                        vm.IsCompactNavigation = window.ActualWidth < 1100;
                        window.Width = 960;
                        await Flush();
                        window.Width = size.Width; window.Height = size.Height;
                        await Flush();
                        Check(vm.IsCompactNavigation == (window.ActualWidth < 1100), "Native compact navigation breakpoint");
                        vm.IsCompactNavigation = size.Width < 1100;
                        foreach (var page in new[] { "Home", "Downloader", "Converter", "Trimmer", "AudioTrimmer", "Workspace", "Settings" })
                        {
                            vm.CurrentTab = page;
                            await Flush();
                            Capture(window, Path.Combine(capture, size.Name + "-" + page + "-content.png"), size.Scale, size.Width, size.Height);
                            CheckHorizontalBounds(window);
                        }
                    }
                    vm.CurrentTab = "Downloader";
                    await Flush();
                    var url = Descendants(window).OfType<FrameworkElement>().First(x => x.Name == "UrlTextBox");
                    Check(url.Focusable && System.Windows.Automation.AutomationProperties.GetName(url) == "ลิงก์วิดีโอ", "URL keyboard/accessibility target");
                    Check(Descendants(window).OfType<MediaStudio.Views.ResultActionsView>().Any(), "Shared result actions must render");
                    // Verify actual control bindings, not just visual existence.
                    var actions = Descendants(window).OfType<MediaStudio.Views.ResultActionsView>().First(x => x.IsVisible);
                    Check(actions.OpenFileCommand != null && actions.OpenFolderCommand != null && actions.CommandParameter is DownloadItem, "Queue action commands/parameters must resolve through the ListBox");
                    var transfer = Descendants(actions).OfType<Button>().First(x => x.Content as string == "ส่งต่อ");
                    transfer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Flush();
                    Check(transfer.ContextMenu!.Items.OfType<MenuItem>().Any(x => x.Command != null), "Transfer popup commands must resolve across popup boundary");
                    Check(transfer.ContextMenu.Items.OfType<MenuItem>().Where(x => x.Visibility == Visibility.Visible).All(x => x.Command != null && x.IsEnabled), "Unsupported transfer destinations must not be displayed");
                    transfer.ContextMenu.IsOpen = false;
                    vm.CurrentTab = "Converter";
                    converter.ConversionList[0].TargetFormat = "GIF";
                    await Flush();
                    var gifActions = Descendants(window).OfType<MediaStudio.Views.ResultActionsView>().First(x => x.IsVisible && x.CommandParameter == converter.ConversionList[0]);
                    Check(!gifActions.HasTransferTargets, "GIF results must not expose an empty transfer menu");
                    converter.ConversionList[0].TargetFormat = "MP4";
                    await Flush();
                    Check(gifActions.HasTransferTargets, "Compatible destinations must reappear when result type changes");
                    // Preserve numeric time fields as a keyboard alternative to waveform dragging.
                    Check(Descendants(window).OfType<TextBox>().Any(x => System.Windows.Automation.AutomationProperties.GetName(x) == "เวลาเริ่มเสียงเป็นวินาที"), "Audio numeric keyboard alternative missing");
                    File.WriteAllText(Path.Combine(capture, "README.txt"), "WPF render matrix: logical window sizes represent available work areas at the named display scales; RenderTargetBitmap uses matching DPI. These are simulated DPI layout captures, not a change to Windows display settings. Empty/content states, 50 queue items, actual media analysis and command bindings checked. Real multi-monitor movement and screen-reader behavior require separate device checks.");
                    Console.WriteLine("PASS: WPF UI empty/content layout at 4 sizes, 50-item queues, real trim analysis and result/popup command bindings");
                    completion.SetResult();
                }
                catch (Exception error) { completion.SetException(error); }
                finally { window?.Close(); app.Shutdown(); }
            }));
            Dispatcher.Run();
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    private static async Task Flush() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void CheckHorizontalBounds(Window window)
    {
        var root = (FrameworkElement)window.Content;
        foreach (var control in Descendants(root).OfType<Control>().Where(x => x.IsVisible && x.Focusable && x.ActualWidth > 0))
        {
            var bounds = control.TransformToAncestor(root).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
            Check(bounds.Left >= -2 && bounds.Right <= root.ActualWidth + 2, $"Horizontal clipping: {control.GetType().Name} {control.Name}, {bounds}, root {root.ActualWidth}");
        }
    }
    private static void Capture(Window window, string file, double scale, double logicalWidth, double logicalHeight)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(logicalWidth, logicalHeight));
        root.Arrange(new Rect(0, 0, logicalWidth, logicalHeight));
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            // Include the opaque desktop backing surface; root content alone contains translucent theme brushes.
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 32)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            context.Pop();
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class PreviewDownloader(DependencyService dependency) : YtDlpService(dependency)
    {
        public override Task<MediaMetadata?> GetMediaInfoAsync(string url, CancellationToken ct = default) => Task.FromResult<MediaMetadata?>(new MediaMetadata { Title = "วิดีโอตัวอย่างชื่อยาวสำหรับตรวจชื่อไฟล์ คุณภาพ และการจัดวางบนจอเล็ก", Author = "ผู้สร้างตัวอย่างสำหรับทดสอบ", ThumbnailUrl = Path.Combine(Environment.CurrentDirectory, "Assets", "MediaStudio.png"), Duration = TimeSpan.FromSeconds(123) });
    }
    private sealed class NoConsent : IConfirmationService
    {
        public bool ConfirmEngineDownload(bool isRefresh) => false;
        public bool ConfirmApplicationUpdate(string currentVersion, string newVersion) => false;
    }
}
