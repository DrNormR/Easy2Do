using CommunityToolkit.Mvvm.ComponentModel;

namespace Easy2Do.Models;

public partial class TodoItem : ObservableObject
{
    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;
}
