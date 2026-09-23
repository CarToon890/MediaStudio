using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;

namespace MediaStudio.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    public ObservableCollection<MediaAsset> RecentAssets { get; }
    public event Action<string>? NavigationRequested;
    public IRelayCommand<string> OpenToolCommand { get; }

    public HomeViewModel(MediaWorkspace workspace)
    {
        RecentAssets = workspace.Assets;
        OpenToolCommand = new RelayCommand<string>(tab =>
        {
            if (!string.IsNullOrWhiteSpace(tab)) NavigationRequested?.Invoke(tab);
        });
    }
}
