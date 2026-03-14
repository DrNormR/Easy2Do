using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Easy2Do.Models;

// Properties are explicit (not [ObservableProperty]) so that the System.Text.Json
// source generator can see them at compile time. [ObservableProperty]-generated
// properties are invisible to other source generators, causing {} on iOS AOT.
public class TodoItem : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    private DateTime _createdAtUtc = DateTime.UtcNow;
    public DateTime CreatedAtUtc
    {
        get => _createdAtUtc;
        set => SetProperty(ref _createdAtUtc, value);
    }

    private DateTime _updatedAtUtc = DateTime.UtcNow;
    public DateTime UpdatedAtUtc
    {
        get => _updatedAtUtc;
        set => SetProperty(ref _updatedAtUtc, value);
    }

    private DateTime? _deletedAtUtc;
    public DateTime? DeletedAtUtc
    {
        get => _deletedAtUtc;
        set => SetProperty(ref _deletedAtUtc, value);
    }

    private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
                Touch();
        }
    }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (SetProperty(ref _isCompleted, value))
                Touch();
        }
    }

    private bool _isHeading;
    public bool IsHeading
    {
        get => _isHeading;
        set
        {
            if (SetProperty(ref _isHeading, value))
                Touch();
        }
    }

    private bool _isImportant;
    public bool IsImportant
    {
        get => _isImportant;
        set
        {
            if (SetProperty(ref _isImportant, value))
                Touch();
        }
    }

    private string _textAttachment = string.Empty;
    public string TextAttachment
    {
        get => _textAttachment;
        set
        {
            if (SetProperty(ref _textAttachment, value))
            {
                Touch();
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
                Touch();
                OnPropertyChanged(nameof(HasDueDate));
            }
        }
    }

    public bool HasDueDate => _dueDate.HasValue;

    private bool _isAlarmDismissed;
    public bool IsAlarmDismissed
    {
        get => _isAlarmDismissed;
        set
        {
            if (SetProperty(ref _isAlarmDismissed, value))
                Touch();
        }
    }

    private DateTime? _snoozeUntil;
    public DateTime? SnoozeUntil
    {
        get => _snoozeUntil;
        set
        {
            if (SetProperty(ref _snoozeUntil, value))
                Touch();
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
