using System.Windows;
using DiskScope.App.ViewModels;

namespace DiskScope.App.Views;

public partial class StorageView : System.Windows.Controls.UserControl
{
    public StorageView()
    {
        InitializeComponent();
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // Tree -> view model; SetProperty ignores equal values, so RevealNode does not loop back.
        if (e.NewValue is NodeViewModel { IsPlaceholder: false } node && DataContext is StorageViewModel vm)
        {
            vm.SelectedNodeId = node.Id;
        }
    }
}
