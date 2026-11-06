using System.Windows.Controls;
using MediaStudio.ViewModels;
namespace MediaStudio.Views;
public partial class DownloaderView : UserControl
{
    private DownloaderViewModel? _subscribedVm;
    public DownloaderView()
    {
        InitializeComponent();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => { if (_subscribedVm != null) _subscribedVm.RequestUrlFocus -= FocusUrl; _subscribedVm = null; };
        DataContextChanged += (_, _) => { if (IsLoaded) Subscribe(); };
    }
    private void Subscribe()
    {
        if (_subscribedVm != null) _subscribedVm.RequestUrlFocus -= FocusUrl;
        _subscribedVm = DataContext as DownloaderViewModel;
        if (_subscribedVm != null) _subscribedVm.RequestUrlFocus += FocusUrl;
    }
    private void FocusUrl() => UrlTextBox.Focus();
}
