using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MediaStudio.ViewModels;

namespace MediaStudio.Views;

public partial class TrimmerView : UserControl
{
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _isUserSeeking;
    private Window? _hostWindow;

    public TrimmerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _positionTimer.Tick += PositionTimer_Tick;
        IsVisibleChanged += (_, _) => { if (!IsVisible) PausePreview(); };
        PreviewPlayer.MediaFailed += (_, _) => PausePreview();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrimmerViewModel vm)
        {
            vm.RequestSeek -= OnRequestSeek;
            vm.RequestSeek += OnRequestSeek;

            vm.RequestTogglePlay -= OnRequestTogglePlay;
            vm.RequestTogglePlay += OnRequestTogglePlay;
            vm.PropertyChanged += ViewModelPropertyChanged;
        }
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow != null) _hostWindow.StateChanged += HostWindowStateChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PausePreview();
        if (DataContext is TrimmerViewModel vm)
        {
            vm.RequestSeek -= OnRequestSeek;
            vm.RequestTogglePlay -= OnRequestTogglePlay;
            vm.PropertyChanged -= ViewModelPropertyChanged;
        }
        if (_hostWindow != null) _hostWindow.StateChanged -= HostWindowStateChanged;
        _hostWindow = null;
        PreviewPlayer.Stop();
        PreviewPlayer.Close();
    }

    private void HostWindowStateChanged(object? sender, EventArgs e)
    {
        if (_hostWindow?.WindowState == WindowState.Minimized) PausePreview();
    }

    private void ViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrimmerViewModel.IsPlaying) && sender is TrimmerViewModel { IsPlaying: false })
        {
            _positionTimer.Stop();
            PreviewPlayer.Pause();
        }
    }

    private void PausePreview()
    {
        _positionTimer.Stop();
        PreviewPlayer.Pause();
        if (DataContext is TrimmerViewModel vm) vm.IsPlaying = false;
    }

    private void OnRequestSeek(TimeSpan position)
    {
        _isUserSeeking = true;
        PreviewPlayer.Position = position;
        if (DataContext is TrimmerViewModel vm)
        {
            vm.CurrentPositionSeconds = position.TotalSeconds;
        }
        _isUserSeeking = false;
    }

    private void OnRequestTogglePlay()
    {
        if (DataContext is not TrimmerViewModel vm || !IsVisible || _hostWindow?.WindowState == WindowState.Minimized) return;

        if (vm.IsPlaying)
        {
            PreviewPlayer.Pause();
            vm.IsPlaying = false;
            _positionTimer?.Stop();
        }
        else
        {
            PreviewPlayer.Play();
            vm.IsPlaying = true;
            _positionTimer?.Start();
        }
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isUserSeeking && DataContext is TrimmerViewModel vm && PreviewPlayer.NaturalDuration.HasTimeSpan)
        {
            vm.CurrentPositionSeconds = PreviewPlayer.Position.TotalSeconds;
            if (vm.CurrentPositionSeconds >= vm.EndSeconds && vm.EndSeconds > vm.StartSeconds)
            {
                PreviewPlayer.Pause();
                vm.IsPlaying = false;
                _positionTimer?.Stop();
            }
        }
    }

    private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        // FFmpeg analysis owns readiness and duration; preview events cannot make a failed load ready.
    }

    private void PreviewPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrimmerViewModel vm)
        {
            vm.IsPlaying = false;
            _positionTimer?.Stop();
            PreviewPlayer.Position = TimeSpan.FromSeconds(vm.StartSeconds);
        }
    }

    private void StartSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is TrimmerViewModel vm && vm.CanEdit)
        {
            _isUserSeeking = true;
            PreviewPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            _isUserSeeking = false;
        }
    }

    private void EndSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is TrimmerViewModel vm && vm.CanEdit)
        {
            _isUserSeeking = true;
            PreviewPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            _isUserSeeking = false;
        }
    }
}
