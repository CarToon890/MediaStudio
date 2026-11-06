using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace MediaStudio.Views;
public partial class ResultActionsView : UserControl
{
    private static readonly DependencyPropertyKey HasTransferTargetsKey = DependencyProperty.RegisterReadOnly(nameof(HasTransferTargets), typeof(bool), typeof(ResultActionsView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasTransferTargetsProperty = HasTransferTargetsKey.DependencyProperty;
    public bool HasTransferTargets => (bool)GetValue(HasTransferTargetsProperty);
    private static void TransferTargetsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (ResultActionsView)sender;
        view.SetValue(HasTransferTargetsKey, view.SendToConverterCommand != null || (view.CanSendVideo && view.SendToVideoCommand != null) || (view.CanSendAudio && view.SendToAudioCommand != null));
    }
    public ResultActionsView() => InitializeComponent();
    private void ShowTransferMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    public static readonly DependencyProperty OpenFileCommandProperty = DependencyProperty.Register(nameof(OpenFileCommand), typeof(ICommand), typeof(ResultActionsView), new PropertyMetadata(null));
    public ICommand? OpenFileCommand { get => (ICommand?)GetValue(OpenFileCommandProperty); set => SetValue(OpenFileCommandProperty, value); }
    public static readonly DependencyProperty OpenFolderCommandProperty = DependencyProperty.Register(nameof(OpenFolderCommand), typeof(ICommand), typeof(ResultActionsView), new PropertyMetadata(null));
    public ICommand? OpenFolderCommand { get => (ICommand?)GetValue(OpenFolderCommandProperty); set => SetValue(OpenFolderCommandProperty, value); }
    public static readonly DependencyProperty SendToConverterCommandProperty = DependencyProperty.Register(nameof(SendToConverterCommand), typeof(ICommand), typeof(ResultActionsView), new PropertyMetadata(null, TransferTargetsChanged));
    public ICommand? SendToConverterCommand { get => (ICommand?)GetValue(SendToConverterCommandProperty); set => SetValue(SendToConverterCommandProperty, value); }
    public static readonly DependencyProperty SendToVideoCommandProperty = DependencyProperty.Register(nameof(SendToVideoCommand), typeof(ICommand), typeof(ResultActionsView), new PropertyMetadata(null, TransferTargetsChanged));
    public ICommand? SendToVideoCommand { get => (ICommand?)GetValue(SendToVideoCommandProperty); set => SetValue(SendToVideoCommandProperty, value); }
    public static readonly DependencyProperty SendToAudioCommandProperty = DependencyProperty.Register(nameof(SendToAudioCommand), typeof(ICommand), typeof(ResultActionsView), new PropertyMetadata(null, TransferTargetsChanged));
    public ICommand? SendToAudioCommand { get => (ICommand?)GetValue(SendToAudioCommandProperty); set => SetValue(SendToAudioCommandProperty, value); }
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(ResultActionsView), new PropertyMetadata(null));
    public object? CommandParameter { get => (object?)GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public static readonly DependencyProperty CanSendVideoProperty = DependencyProperty.Register(nameof(CanSendVideo), typeof(bool), typeof(ResultActionsView), new PropertyMetadata(true, TransferTargetsChanged));
    public bool CanSendVideo { get => (bool)GetValue(CanSendVideoProperty); set => SetValue(CanSendVideoProperty, value); }
    public static readonly DependencyProperty CanSendAudioProperty = DependencyProperty.Register(nameof(CanSendAudio), typeof(bool), typeof(ResultActionsView), new PropertyMetadata(true, TransferTargetsChanged));
    public bool CanSendAudio { get => (bool)GetValue(CanSendAudioProperty); set => SetValue(CanSendAudioProperty, value); }
}
