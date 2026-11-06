using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Threading;
using MediaStudio.ViewModels;

namespace MediaStudio.Views;

public partial class DownloaderView : UserControl
{
    private DownloaderViewModel? _subscribedVm;
    public DownloaderView()
    {
        InitializeComponent();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => { if (_subscribedVm != null) { _subscribedVm.RequestUrlFocus -= FocusUrl; _subscribedVm.PropertyChanged -= MetadataChanged; } _subscribedVm = null; };
        DataContextChanged += (_, _) => { if (IsLoaded) Subscribe(); };
    }
    private void Subscribe()
    {
        if (_subscribedVm != null) { _subscribedVm.RequestUrlFocus -= FocusUrl; _subscribedVm.PropertyChanged -= MetadataChanged; }
        _subscribedVm = DataContext as DownloaderViewModel;
        if (_subscribedVm != null) { _subscribedVm.RequestUrlFocus += FocusUrl; _subscribedVm.PropertyChanged += MetadataChanged; }
    }
    private void MetadataChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DownloaderViewModel.HasMediaInfo) || _subscribedVm?.HasMediaInfo != true || !IsVisible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!IsVisible || _subscribedVm?.HasMediaInfo != true) return;
            FileNameTextBox.Focus();
            ReviewPanel.BringIntoView();
        }));
    }
    private void FocusUrl() => UrlTextBox.Focus();
}
