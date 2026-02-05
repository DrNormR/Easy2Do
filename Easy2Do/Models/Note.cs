using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Easy2Do.Models;

public partial class Note : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _title = "New Note";

    [ObservableProperty]
    private string _color = "#FFFFE680"; // Yellow

    [ObservableProperty]
    private ObservableCollection<TodoItem> _items = new();

    [ObservableProperty]
    private DateTime _createdDate = DateTime.Now;

    [ObservableProperty]
    private DateTime _modifiedDate = DateTime.Now;
}
