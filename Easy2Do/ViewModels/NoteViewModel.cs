using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Easy2Do.Models;
using Easy2Do.Views;

namespace Easy2Do.ViewModels;

public partial class NoteViewModel : ViewModelBase
{
    [ObservableProperty]
    private Note _note;

    [ObservableProperty]
    private string _newItemText = string.Empty;

    [ObservableProperty]
    private bool _newItemIsHeading;

    public ObservableCollection<string> AvailableColors { get; } = new()
    {
        "#FFFFE680", // Yellow
        "#FFFF9999", // Light Red
        "#FF99FF99", // Light Green
        "#FF99CCFF", // Light Blue
        "#FFFFCC99", // Light Orange
        "#FFFF99FF", // Light Pink
        "#FFCCCCCC"  // Light Gray
    };

    public NoteViewModel(Note note)
    {
        _note = note;
    }

    [RelayCommand]
    private void AddItem()
    {
        if (!string.IsNullOrWhiteSpace(NewItemText))
        {
            var item = new TodoItem { Text = NewItemText, IsHeading = NewItemIsHeading };
            Note.Items.Add(item);
            NewItemText = string.Empty;
            NewItemIsHeading = false;
            Note.ModifiedDate = DateTime.Now;
        }
    }

    [RelayCommand]
    private void RemoveItem(TodoItem item)
    {
        Note.Items.Remove(item);
        Note.ModifiedDate = DateTime.Now;
    }

    [RelayCommand]
    private void ChangeColor(string color)
    {
        Note.Color = color;
        Note.ModifiedDate = DateTime.Now;
    }

    [RelayCommand]
    private void TogglePin()
    {
        Note.IsPinned = !Note.IsPinned;
        Note.ModifiedDate = DateTime.Now;
    }

    [RelayCommand]
    private async Task RefreshNote()
    {
        if (Easy2Do.App.MainWindow?.DataContext is MainViewModel mainVm)
        {
            mainVm.CancelPendingSave(Note.Id);
        }

        // Reload note from disk
        var reloaded = await Easy2Do.App.StorageService.LoadNoteAsync(Note.Id);
        if (reloaded != null)
        {
            Note.IsReloading = true;
            try
            {
                // Update all properties
                Note.Title = reloaded.Title;
            Note.Color = reloaded.Color;
            Note.CreatedDate = reloaded.CreatedDate;
            Note.ModifiedDate = reloaded.ModifiedDate;
            Note.LastWriteTimeUtc = reloaded.LastWriteTimeUtc;
            Note.IsPinned = reloaded.IsPinned;
                Note.WindowX = reloaded.WindowX;
                Note.WindowY = reloaded.WindowY;
                Note.WindowWidth = reloaded.WindowWidth;
                Note.WindowHeight = reloaded.WindowHeight;
                Note.Items.Clear();
                foreach (var item in reloaded.Items)
                    Note.Items.Add(item);
            }
            finally
            {
                Note.IsReloading = false;
            }
        }
    }
}
