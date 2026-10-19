using System.Windows;
using DiskScope.App.ViewModels;

namespace DiskScope.App;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // Tree -> view model; SetProperty ignores equal values, so RevealNode does not loop back.
        if (e.NewValue is NodeViewModel { IsPlaceholder: false } node && DataContext is MainViewModel vm)
        {
            vm.SelectedNodeId = node.Id;
        }
    }
}
