using Avalonia.Controls;

namespace Easy2Do.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        DataContext = new ViewModels.AboutViewModel();
    }
}
