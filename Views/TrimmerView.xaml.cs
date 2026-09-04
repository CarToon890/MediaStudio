using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MediaStudio.ViewModels;

namespace MediaStudio.Views;

public partial class TrimmerView : UserControl
{
    private DispatcherTimer? _positionTimer;
    private bool _isUserSeeking;

    public TrimmerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrimmerViewModel vm)
        {
            vm.RequestSeek -= OnRequestSeek;
            vm.RequestSeek += OnRequestSeek;

            vm.RequestTogglePlay -= OnRequestTogglePlay;
            vm.RequestTogglePlay += OnRequestTogglePlay;
        }

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _positionTimer.Tick += PositionTimer_Tick;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _positionTimer?.Stop();
        if (DataContext is TrimmerViewModel vm)
        {
            vm.RequestSeek -= OnRequestSeek;
            vm.RequestTogglePlay -= OnRequestTogglePlay;
        }
        PreviewPlayer.Stop();
        PreviewPlayer.Close();
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
        if (DataContext is not TrimmerViewModel vm) return;

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
        if (PreviewPlayer.NaturalDuration.HasTimeSpan && DataContext is TrimmerViewModel vm)
        {
            var dur = PreviewPlayer.NaturalDuration.TimeSpan;
            if (dur.TotalSeconds > 0)
            {
                vm.TotalDuration = dur;
                vm.TotalSeconds = dur.TotalSeconds;
                if (vm.EndSeconds > dur.TotalSeconds || vm.EndSeconds <= 0)
                {
                    vm.EndSeconds = Math.Min(dur.TotalSeconds, 10);
                }
            }
        }
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
        if (DataContext is TrimmerViewModel vm && vm.HasLoadedFile)
        {
            _isUserSeeking = true;
            PreviewPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            _isUserSeeking = false;
        }
    }

    private void EndSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is TrimmerViewModel vm && vm.HasLoadedFile)
        {
            _isUserSeeking = true;
            PreviewPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            _isUserSeeking = false;
        }
    }
}
