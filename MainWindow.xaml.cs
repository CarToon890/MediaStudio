using MediaStudio.ViewModels;
using Wpf.Ui.Controls;

namespace MediaStudio;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.InitializeOnStartupAsync();
    }
}
