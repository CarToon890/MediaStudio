using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Services;
using Microsoft.Win32;

namespace MediaStudio.ViewModels;

public sealed class OutputLocationViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    public string Path => _settings.Settings.DownloadDirectory;
    private string _error = string.Empty;
    public string Error { get => _error; private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public IRelayCommand BrowseCommand { get; }
    public IRelayCommand OpenCommand { get; }
    public OutputLocationViewModel(SettingsService settings)
    {
        _settings = settings;
        settings.OutputDirectoryChanged += () => { OnPropertyChanged(nameof(Path)); Error = string.Empty; };
        BrowseCommand = new RelayCommand(Browse);
        OpenCommand = new RelayCommand(Open);
    }
    public bool ChangeDirectory(string path)
    {
        var success = _settings.TryChangeOutputDirectory(path, out var error);
        Error = error;
        return success;
    }
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "เลือกโฟลเดอร์กลางสำหรับงานใหม่", InitialDirectory = Directory.Exists(Path) ? Path : string.Empty };
        if (dialog.ShowDialog() == true) ChangeDirectory(dialog.FolderName);
    }
    private void Open()
    {
        try
        {
            if (!Directory.Exists(Path)) { Error = "ไม่พบโฟลเดอร์ปลายทาง กรุณาเลือกโฟลเดอร์ใหม่"; return; }
            Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true });
            Error = string.Empty;
        }
        catch { Error = "เปิดโฟลเดอร์ไม่ได้ กรุณาตรวจสอบสิทธิ์และลองใหม่"; }
    }
}
