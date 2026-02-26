using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
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

    [JsonIgnore]
    public DateTime? LastWriteTimeUtc { get; set; }

    [ObservableProperty]
    private double _windowX = double.NaN;

    [ObservableProperty]
    private double _windowY = double.NaN;

    [ObservableProperty]
    private double _windowWidth = double.NaN;

    [ObservableProperty]
    private double _windowHeight = double.NaN;

    [ObservableProperty]
    private bool _isPinned;

    private bool _isReloading;

    [JsonIgnore]
    public bool IsReloading
    {
        get => _isReloading;
        set => SetProperty(ref _isReloading, value);
    }
}
