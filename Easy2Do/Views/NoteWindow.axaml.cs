using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using Easy2Do.ViewModels;
using Easy2Do.Models;

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

    private void OnTitleTextBoxGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Defer to ensure selection happens after focus is applied
            Dispatcher.UIThread.Post(() => textBox.SelectAll());
        }
    }

    private void OnTitleTextBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Always handle the press to prevent caret placement before select-all
            e.Handled = true;
            textBox.Focus();
            Dispatcher.UIThread.Post(() => textBox.SelectAll());
        }
    }

    private void OnTitleTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            e.Handled = true;
            // Title binding uses UpdateSourceTrigger=PropertyChanged, so value is already committed.
            // Clear focus so caret disappears and SaveNotes triggers via PropertyChanged.
            textBox.IsEnabled = false;
            Dispatcher.UIThread.Post(() => textBox.IsEnabled = true);
        }
    }

    private void OnImportantButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is object ctx)
        {
            var prop = ctx.GetType().GetProperty("IsImportant");
            if (prop?.CanRead == true && prop.CanWrite)
            {
                var current = prop.GetValue(ctx) as bool? ?? false;
                prop.SetValue(ctx, !current);
            }
        }
    }

}
