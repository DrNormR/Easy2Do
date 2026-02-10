using CommunityToolkit.Mvvm.ComponentModel;

namespace Easy2Do.Models;

public partial class TodoItem : ObservableObject
{
    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isHeading;

    private bool _isImportant;

    public bool IsImportant
    {
        get => _isImportant;
        set => SetProperty(ref _isImportant, value);
    }
}
