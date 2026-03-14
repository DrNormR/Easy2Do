using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Easy2Do.Models;

// Properties are explicit (not [ObservableProperty]) so that the System.Text.Json
// source generator can see them at compile time. [ObservableProperty]-generated
// properties are invisible to other source generators, causing {} on iOS AOT.
public class Note : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    private string _title = "New Note";
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _color = "#FFFFE680"; // Yellow
    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    private ObservableCollection<TodoItem> _items = new();
    public ObservableCollection<TodoItem> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    private DateTime _createdDate = DateTime.Now;
    public DateTime CreatedDate
    {
        get => _createdDate;
        set => SetProperty(ref _createdDate, value);
    }

    private DateTime _modifiedDate = DateTime.Now;
    public DateTime ModifiedDate
    {
        get => _modifiedDate;
        set => SetProperty(ref _modifiedDate, value);
    }

    [JsonIgnore]
    public DateTime? LastWriteTimeUtc { get; set; }

    [JsonIgnore]
    public bool NeedsItemMigration { get; set; }

    private double _windowX = double.NaN;
    public double WindowX
    {
        get => _windowX;
        set => SetProperty(ref _windowX, value);
    }

    private double _windowY = double.NaN;
    public double WindowY
    {
        get => _windowY;
        set => SetProperty(ref _windowY, value);
    }

    private double _windowWidth = double.NaN;
    public double WindowWidth
    {
        get => _windowWidth;
        set => SetProperty(ref _windowWidth, value);
    }

    private double _windowHeight = double.NaN;
    public double WindowHeight
    {
        get => _windowHeight;
        set => SetProperty(ref _windowHeight, value);
    }

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    private bool _isReloading;
    [JsonIgnore]
    public bool IsReloading
    {
        get => _isReloading;
        set => SetProperty(ref _isReloading, value);
    }
}
