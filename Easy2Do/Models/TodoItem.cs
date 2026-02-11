using System;
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

    private string _textAttachment = string.Empty;

    public string TextAttachment
    {
        get => _textAttachment;
        set
        {
            if (SetProperty(ref _textAttachment, value))
            {
                OnPropertyChanged(nameof(HasTextAttachment));
            }
        }
    }

    public bool HasTextAttachment => !string.IsNullOrEmpty(_textAttachment);

    private DateTime? _dueDate;

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (SetProperty(ref _dueDate, value))
            {
                OnPropertyChanged(nameof(HasDueDate));
            }
        }
    }

    public bool HasDueDate => _dueDate.HasValue;
}
