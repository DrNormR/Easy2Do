using System;
using System.Collections.ObjectModel;
using System.Linq;
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

    public MainViewModel()
    {
        // Add a sample note for demonstration
        var sampleNote = new Note
        {
            Title = "Sample Note",
            Color = "#FFFFE680"
        };
        sampleNote.Items.Add(new TodoItem { Text = "Click to mark as complete" });
        sampleNote.Items.Add(new TodoItem { Text = "Click 'Create New Note' to add more notes", IsCompleted = true });
        Notes.Add(sampleNote);
    }

    [RelayCommand]
    private void CreateNewNote()
    {
        var newNote = new Note
        {
            Title = "New Note",
            Color = "#FFFFE680"
        };
        Notes.Add(newNote);
        OpenNote(newNote);
    }

    [RelayCommand]
    private void DeleteNote(Note? note)
    {
        if (note != null && Notes.Contains(note))
        {
            Notes.Remove(note);
        }
    }

    [RelayCommand]
    private void DuplicateNote(Note? note)
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

            Notes.Add(duplicatedNote);
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
