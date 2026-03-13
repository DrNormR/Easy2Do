using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Easy2Do.ViewModels;
using Easy2Do.Models;
using Avalonia.VisualTree;
using System;

namespace Easy2Do.Views;

public partial class NoteView : UserControl
{
    public NoteView()
    {
        InitializeComponent();
    }

    private void OnMobileCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        // On mobile, closing the note means returning to the main list
        App.MainViewModel?.CloseDetailCommand.Execute(null);
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is NoteViewModel viewModel)
        {
            viewModel.AddItemCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control)
        {
            control.Focus();
        }
    }

    private void OnItemTextBoxGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            Dispatcher.UIThread.Post(() => textBox.CaretIndex = textBox.Text?.Length ?? 0);
        }
    }

    private void OnItemTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            e.Handled = true;
            textBox.IsEnabled = false;
            Dispatcher.UIThread.Post(() => textBox.IsEnabled = true);
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

    private async void OnCustomColorMenuItemClick(object? sender, RoutedEventArgs e)
    {
        // Color picker dialogs require a Window owner — not supported on mobile
        if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid()) return;

        if (DataContext is NoteViewModel vm)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) return;

            var picker = new ColorPickerWindow(vm.Note.Color);
            await picker.ShowDialog(owner);

            if (picker.IsSaved)
            {
                vm.ChangeColorCommand.Execute(picker.SelectedColorHex);
            }
        }
    }

    private const string DragItemFormat = "todo-item";

    private async void OnMoveHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is TodoItem item
            && e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            var data = new DataObject();
            data.Set(DragItemFormat, item);
            e.Handled = true;
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
    }

    private static Border? FindChildBorder(Grid grid, string name)
    {
        foreach (var child in grid.GetVisualChildren())
        {
            if (child is Border b && b.Name == name)
                return b;
        }
        return null;
    }

    private void OnItemRowDragOver(object? sender, DragEventArgs e)
    {
        if (sender is Grid grid && DataContext is NoteViewModel vm)
        {
            var dragItem = e.Data.Get(DragItemFormat) as TodoItem;
            if (dragItem is null)
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var targetItem = grid.DataContext as TodoItem;
            if (targetItem is null || ReferenceEquals(targetItem, dragItem))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            HideAllDropIndicators();

            var beforeIndicator = FindChildBorder(grid, "DropIndicatorBefore");
            var afterIndicator = FindChildBorder(grid, "DropIndicatorAfter");

            var pos = e.GetPosition(grid);
            var insertBefore = pos.Y < grid.Bounds.Height / 2;

            if (beforeIndicator != null && afterIndicator != null)
            {
                beforeIndicator.IsVisible = insertBefore;
                afterIndicator.IsVisible = !insertBefore;
            }

            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnItemRowDragEnter(object? sender, DragEventArgs e)
    {
        OnItemRowDragOver(sender, e);
    }

    private void OnItemRowDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Grid grid)
        {
            var before = FindChildBorder(grid, "DropIndicatorBefore");
            var after = FindChildBorder(grid, "DropIndicatorAfter");
            if (before != null) before.IsVisible = false;
            if (after != null) after.IsVisible = false;
        }
    }

    private void OnItemRowDrop(object? sender, DragEventArgs e)
    {
        if (sender is Grid grid && DataContext is NoteViewModel vm)
        {
            var dragItem = e.Data.Get(DragItemFormat) as TodoItem;
            if (dragItem is null)
            {
                return;
            }

            var targetItem = grid.DataContext as TodoItem;
            if (targetItem is null)
            {
                HideAllDropIndicators();
                return;
            }

            var items = vm.Note.Items;
            var sourceIndex = items.IndexOf(dragItem);
            var targetIndex = items.IndexOf(targetItem);
            if (sourceIndex < 0 || targetIndex < 0)
            {
                HideAllDropIndicators();
                return;
            }

            var beforeIndicator = FindChildBorder(grid, "DropIndicatorBefore");
            var insertBefore = beforeIndicator?.IsVisible == true;
            var insertIndex = insertBefore ? targetIndex : targetIndex + 1;

            if (insertIndex > items.Count)
            {
                insertIndex = items.Count;
            }

            if (sourceIndex < insertIndex)
            {
                insertIndex -= 1;
            }

            if (insertIndex != sourceIndex)
            {
                items.Move(sourceIndex, insertIndex);
                vm.Note.ModifiedDate = DateTime.Now;
            }

            HideAllDropIndicators();
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private async void OnAttachmentButtonClick(object? sender, RoutedEventArgs e)
    {
        // Attachment dialogs require a Window owner — not supported on mobile
        if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid()) return;

        if (sender is Button button && button.DataContext is TodoItem item)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) return;

            var window = new TextAttachmentWindow(item.TextAttachment);
            await window.ShowDialog(owner);

            if (window.IsSaved)
            {
                item.TextAttachment = window.AttachmentText;
                if (DataContext is NoteViewModel vm)
                {
                    vm.Note.ModifiedDate = DateTime.Now;
                }
            }
        }
    }

    private async void OnDueDateButtonClick(object? sender, RoutedEventArgs e)
    {
        // Due date dialogs require a Window owner — not supported on mobile
        if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid()) return;

        if (sender is Button button && button.DataContext is TodoItem item)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) return;

            var window = new DueDatePickerWindow(item.DueDate);
            await window.ShowDialog(owner);

            if (window.IsSet)
            {
                item.DueDate = window.SelectedDueDate;
                if (DataContext is NoteViewModel vm)
                {
                    vm.Note.ModifiedDate = DateTime.Now;
                }
            }
        }
    }

    private void HideAllDropIndicators()
    {
        foreach (var desc in this.GetVisualDescendants())
        {
            if (desc is Border border && (border.Name == "DropIndicatorBefore" || border.Name == "DropIndicatorAfter"))
            {
                border.IsVisible = false;
            }
        }
    }
}
