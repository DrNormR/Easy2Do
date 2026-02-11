using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Easy2Do.Models;

namespace Easy2Do.Views;

public partial class AlarmWindow : Window
{
    private readonly TodoItem _item;

    public AlarmWindow()
    {
        InitializeComponent();
        _item = null!;
    }

    public AlarmWindow(string noteTitle, TodoItem item) : this()
    {
        _item = item;
        NoteTitle.Text = $"from: {noteTitle}";
        ItemText.Text = item.Text;
    }

    private void OnSnooze5Click(object? sender, RoutedEventArgs e) => Snooze(5);
    private void OnSnooze15Click(object? sender, RoutedEventArgs e) => Snooze(15);
    private void OnSnooze60Click(object? sender, RoutedEventArgs e) => Snooze(60);

    private void Snooze(int minutes)
    {
        _item.SnoozeUntil = DateTime.Now.AddMinutes(minutes);
        Close();
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        _item.IsAlarmDismissed = true;
        Close();
    }
}
