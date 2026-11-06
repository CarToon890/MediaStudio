using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using MediaStudio.ViewModels;
using Wpf.Ui.Controls;

namespace MediaStudio;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        SizeChanged += (_, _) => viewModel.IsCompactNavigation = ActualWidth < 1100;
        SourceInitialized += (_, _) => FitWorkArea();
        Loaded += async (_, _) => await viewModel.InitializeOnStartupAsync();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    private void FitWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return;
        var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new Point(info.Work.Left, info.Work.Top));
        var bottomRight = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));
        var availableWidth = bottomRight.X - topLeft.X;
        var availableHeight = bottomRight.Y - topLeft.Y;
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        Left = topLeft.X + Math.Max(0, (availableWidth - Width) / 2);
        Top = topLeft.Y + Math.Max(0, (availableHeight - Height) / 2);
    }
}
