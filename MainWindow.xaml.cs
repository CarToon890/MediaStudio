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
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => FitWorkArea(false)));
        SystemParameters.StaticPropertyChanged += OnDisplaySettingsChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnDisplaySettingsChanged;
        Loaded += async (_, _) => await viewModel.InitializeOnStartupAsync();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    private void OnDisplaySettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.WorkArea) or nameof(SystemParameters.VirtualScreenWidth) or nameof(SystemParameters.VirtualScreenHeight))
            Dispatcher.BeginInvoke(new Action(() => FitWorkArea(false)));
    }

    private void FitWorkArea(bool center = true)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return;
        var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new Point(info.Work.Left, info.Work.Top));
        var bottomRight = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));
        var availableWidth = bottomRight.X - topLeft.X;
        var availableHeight = bottomRight.Y - topLeft.Y;
        if (availableWidth <= 0 || availableHeight <= 0) return;
        // Minimum constraints must not defeat fitting a scaled notebook work area.
        MinWidth = Math.Min(960, availableWidth);
        MinHeight = Math.Min(540, availableHeight);
        if (WindowState != WindowState.Normal) return;
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        Left = center ? topLeft.X + (availableWidth - Width) / 2 : Math.Clamp(Left, topLeft.X, bottomRight.X - Width);
        Top = center ? topLeft.Y + (availableHeight - Height) / 2 : Math.Clamp(Top, topLeft.Y, bottomRight.Y - Height);
    }
}
