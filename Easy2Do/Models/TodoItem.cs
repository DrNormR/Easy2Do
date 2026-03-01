using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Easy2Do.Models;

public partial class TodoItem : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private DateTime _createdAtUtc = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime _updatedAtUtc = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime? _deletedAtUtc;

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

    partial void OnTextChanged(string value) => Touch();

    partial void OnIsCompletedChanged(bool value) => Touch();

    partial void OnIsHeadingChanged(bool value) => Touch();

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
