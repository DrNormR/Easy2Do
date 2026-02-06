using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Easy2Do.Models;
using Easy2Do.Views;
using Avalonia.Controls;

namespace Easy2Do.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<Note> _notes = new();

    [ObservableProperty]
    private Note? _selectedNote;

    private bool _isSaving = false;

    public MainViewModel()
    {
        // Load notes asynchronously
        _ = LoadNotesAsync();
        
        // Subscribe to collection changes to auto-save
        Notes.CollectionChanged += (s, e) => _ = SaveNotesAsync();
    }

    private async Task LoadNotesAsync()
    {
        var loadedNotes = await App.StorageService.LoadNotesAsync();
        
        if (loadedNotes.Count > 0)
        {
            Notes.Clear();
            foreach (var note in loadedNotes)
            {
                Notes.Add(note);
                
                // Subscribe to property changes for auto-save
                note.PropertyChanged += OnNotePropertyChanged;
                note.Items.CollectionChanged += OnNoteItemsChanged;
            }
        }
        else
        {
            // Add a sample note for demonstration if no notes exist
            var sampleNote = new Note
            {
                Title = "Sample Note",
                Color = "#FFFFE680"
            };
            sampleNote.Items.Add(new TodoItem { Text = "Click to mark as complete" });
            sampleNote.Items.Add(new TodoItem { Text = "Click 'Create New Note' to add more notes", IsCompleted = true });
            Notes.Add(sampleNote);
            await SaveNotesAsync();
        }
    }

    private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ignore ModifiedDate changes to prevent infinite recursion
        if (e.PropertyName != nameof(Note.ModifiedDate) && sender is Note note)
        {
            note.ModifiedDate = DateTime.Now;
            _ = SaveNotesAsync();
        }
    }

    private void OnNoteItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (sender is ObservableCollection<TodoItem> items)
        {
            // Find the note that owns this items collection
            var note = Notes.FirstOrDefault(n => n.Items == items);
            if (note != null)
            {
                note.ModifiedDate = DateTime.Now;
                _ = SaveNotesAsync();
            }
        }
    }

    private async Task SaveNotesAsync()
    {
        if (_isSaving) return;
        
        try
        {
            _isSaving = true;
            
            // Just save - don't update ModifiedDate here!
            await App.StorageService.SaveNotesAsync(Notes);
        }
        finally
        {
            _isSaving = false;
        }
    }

    [RelayCommand]
    private async Task CreateNewNote()
    {
        var newNote = new Note
        {
            Title = "New Note",
            Color = "#FFFFE680"
        };
        
        // Subscribe to property changes for auto-save
        newNote.PropertyChanged += OnNotePropertyChanged;
        newNote.Items.CollectionChanged += OnNoteItemsChanged;
        
        Notes.Add(newNote);
        newNote.ModifiedDate = DateTime.Now;
        await SaveNotesAsync();
        OpenNote(newNote);
    }

    [RelayCommand]
    private async Task DeleteNote(Note? note)
    {
        if (note != null && Notes.Contains(note))
        {
            Notes.Remove(note);
            await SaveNotesAsync();
        }
    }

    [RelayCommand]
    private async Task DuplicateNote(Note? note)
    {
        if (note != null)
        {
            var duplicatedNote = new Note
            {
                Title = note.Title + " (Copy)",
                Color = note.Color
            };

            foreach (var item in note.Items)
            {
                duplicatedNote.Items.Add(new TodoItem
                {
                    Text = item.Text,
                    IsCompleted = item.IsCompleted
                });
            }

            // Subscribe to property changes for auto-save
            duplicatedNote.PropertyChanged += OnNotePropertyChanged;
            duplicatedNote.Items.CollectionChanged += OnNoteItemsChanged;

            Notes.Add(duplicatedNote);
            duplicatedNote.ModifiedDate = DateTime.Now;
            await SaveNotesAsync();
        }
    }

    [RelayCommand]
    private void OpenNote(Note? note)
    {
        if (note != null)
        {
            var noteViewModel = new NoteViewModel(note);
            var noteWindow = new NoteWindow
            {
                DataContext = noteViewModel
            };
            noteWindow.Show();
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsViewModel = new SettingsViewModel();
        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsViewModel
        };
        if (App.MainWindow != null)
        {
            settingsWindow.ShowDialog(App.MainWindow);
        }
        else
        {
            settingsWindow.Show();
        }
    }
}

