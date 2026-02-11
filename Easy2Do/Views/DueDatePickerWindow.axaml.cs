using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Easy2Do.Views;

public partial class DueDatePickerWindow : Window
{
    public DateTime? SelectedDueDate { get; private set; }
    public bool IsSet { get; private set; }
    public bool IsCleared { get; private set; }

    public DueDatePickerWindow()
    {
        InitializeComponent();
    }

    public DueDatePickerWindow(DateTime? existingDueDate) : this()
    {
        SelectedDueDate = existingDueDate;

        if (existingDueDate.HasValue)
        {
            DateSelector.SelectedDate = existingDueDate.Value.Date;
            TimeSelector.SelectedTime = existingDueDate.Value.TimeOfDay;
            CurrentLabel.Text = $"Current: {existingDueDate.Value:g}";
        }
        else
        {
            DateSelector.SelectedDate = DateTime.Today;
            TimeSelector.SelectedTime = DateTime.Now.TimeOfDay;
            CurrentLabel.Text = "No due date set";
        }
    }

    private void OnSetClick(object? sender, RoutedEventArgs e)
    {
        var date = DateSelector.SelectedDate?.Date ?? DateTime.Today;
        var time = TimeSelector.SelectedTime ?? TimeSpan.Zero;
        SelectedDueDate = date + time;
        IsSet = true;
        Close();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        SelectedDueDate = null;
        IsCleared = true;
        IsSet = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
