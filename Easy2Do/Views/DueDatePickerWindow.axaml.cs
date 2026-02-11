using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Easy2Do.Views;

public partial class DueDatePickerWindow : Window
{
    public DateTime? SelectedDate { get; private set; }
    public bool IsSaved { get; private set; }
    public bool IsCleared { get; private set; }

    public DueDatePickerWindow()
    {
        InitializeComponent();
    }

    public DueDatePickerWindow(DateTime? currentDate) : this()
    {
        SelectedDate = currentDate;
        DatePicker.SelectedDate = currentDate;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        SelectedDate = DatePicker.SelectedDate;
        IsSaved = true;
        Close();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        SelectedDate = null;
        IsCleared = true;
        IsSaved = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
