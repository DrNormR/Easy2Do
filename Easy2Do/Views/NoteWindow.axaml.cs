using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Easy2Do.ViewModels;

namespace Easy2Do.Views;

public partial class NoteWindow : Window
{
    public NoteWindow()
    {
        InitializeComponent();
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is NoteViewModel viewModel)
        {
            viewModel.AddItemCommand.Execute(null);
            e.Handled = true;
        }
    }
}
