using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;

namespace MediaStudio.ViewModels;

public sealed class WorkspaceViewModel
{
    private readonly MediaWorkspace _workspace;
    public ObservableCollection<MediaAsset> Assets => _workspace.Assets;
    public IRelayCommand<MediaAsset> OpenFileCommand { get; }
    public IRelayCommand<MediaAsset> OpenFolderCommand { get; }
    public IRelayCommand<MediaAsset> RemoveCommand { get; }
    public IRelayCommand<MediaAsset> SendToConverterCommand { get; }
    public IRelayCommand<MediaAsset> SendToVideoTrimmerCommand { get; }
    public IRelayCommand<MediaAsset> SendToAudioTrimmerCommand { get; }

    public WorkspaceViewModel(MediaWorkspace workspace)
    {
        _workspace = workspace;
        OpenFileCommand = new RelayCommand<MediaAsset>(MediaWorkspace.OpenFile);
        OpenFolderCommand = new RelayCommand<MediaAsset>(MediaWorkspace.OpenFolder);
        RemoveCommand = new RelayCommand<MediaAsset>(_workspace.Remove);
        SendToConverterCommand = new RelayCommand<MediaAsset>(x => _workspace.RequestTransfer(x, ToolDestination.Converter));
        SendToVideoTrimmerCommand = new RelayCommand<MediaAsset>(x => _workspace.RequestTransfer(x, ToolDestination.VideoTrimmer));
        SendToAudioTrimmerCommand = new RelayCommand<MediaAsset>(x => _workspace.RequestTransfer(x, ToolDestination.AudioTrimmer));
    }
}
