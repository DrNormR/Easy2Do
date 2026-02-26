using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Easy2Do.Models;
using Easy2Do.Views;

namespace Easy2Do.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly string[] NoteColors = new[]
    {
        "#FFFFE680", "#FFFF9999", "#FF99FF99", "#FF99CCFF", "#FFFFCC99", "#FFFF99FF", "#FFCCCCCC",
        "#FFFFF3E0", "#FFE8F5E9", "#FFE3F2FD", "#FFF3E5F5", "#FFFCE4EC", "#FFFFF8E1", "#FFE0F7FA"
    };
    private static readonly Random _random = new();
    [ObservableProperty]
    private ObservableCollection<Note> _notes = new();

    [ObservableProperty]
    private Note? _selectedNote;

    private bool _isLoading;
    private readonly Dictionary<Guid, CancellationTokenSource> _saveCtsMap = new();
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(1200);

    public MainViewModel()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadNotesAsync();

        // Start watching for external file changes (OneDrive / Dropbox)
        App.StorageService.NoteFileChanged += OnExternalNoteChanged;
        App.StorageService.NoteFileCreated += OnExternalNoteCreated;
        App.StorageService.NoteFileDeleted += OnExternalNoteDeleted;
        App.StorageService.StartWatching();
    }

    // ──────────────────────────── Load ────────────────────────────

    public async Task LoadNotesAsync()
    {
        _isLoading = true;
        try
        {
            foreach (var existing in Notes)
            {
                UnsubscribeNote(existing);
            }
            await App.StorageService.MigrateIfNeededAsync();
            var loadedNotes = await App.StorageService.LoadAllNotesAsync();
            System.Diagnostics.Debug.WriteLine($"LoadNotesAsync: loaded {loadedNotes.Count} notes.");
            Notes.Clear();
            foreach (var note in loadedNotes)
            {
                System.Diagnostics.Debug.WriteLine($"Loaded note: {note.Id} - {note.Title}");
                SubscribeNote(note);
                Notes.Add(note);
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ──────────────────────────── Subscriptions ────────────────────────────

    private void SubscribeNote(Note note)
    {
        note.PropertyChanged += OnNotePropertyChanged;
        note.Items.CollectionChanged += OnNoteItemsChanged;
        foreach (var item in note.Items)
            item.PropertyChanged += OnItemPropertyChanged;
    }

    private void UnsubscribeNote(Note note)
    {
        note.PropertyChanged -= OnNotePropertyChanged;
        note.Items.CollectionChanged -= OnNoteItemsChanged;
        foreach (var item in note.Items)
            item.PropertyChanged -= OnItemPropertyChanged;
    }

    // ──────────────────────────── Change handlers ────────────────────────────

    private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Note.ModifiedDate)) return;
        if (sender is Note note)
        {
            if (note.IsReloading) return;
            note.ModifiedDate = DateTime.Now;
            RequestSaveNote(note);
        }
    }

    private void OnNoteItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (sender is not ObservableCollection<TodoItem> items) return;

        var note = Notes.FirstOrDefault(n => n.Items == items);
        if (note is null) return;
        if (note.IsReloading) return;

        if (e.NewItems != null)
            foreach (TodoItem item in e.NewItems)
                item.PropertyChanged += OnItemPropertyChanged;

        if (e.OldItems != null)
            foreach (TodoItem item in e.OldItems)
                item.PropertyChanged -= OnItemPropertyChanged;

        note.ModifiedDate = DateTime.Now;
        RequestSaveNote(note);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TodoItem item) return;
        var note = Notes.FirstOrDefault(n => n.Items.Contains(item));
        if (note is null) return;
        if (note.IsReloading) return;

        note.ModifiedDate = DateTime.Now;
        RequestSaveNote(note);
    }

    // ──────────────────────────── Per-note debounced save ────────────────────────────

    private void RequestSaveNote(Note note)
    {
        if (_isLoading) return;

        // Cancel any pending debounce for this note
        if (_saveCtsMap.TryGetValue(note.Id, out var oldCts))
            oldCts.Cancel();

        var cts = new CancellationTokenSource();
        _saveCtsMap[note.Id] = cts;

        _ = DebouncedSaveNoteAsync(note, cts.Token);
    }

    private async Task DebouncedSaveNoteAsync(Note note, CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceDelay, token);
            await App.StorageService.SaveNoteAsync(note);
            await SaveManifestAsync();
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException ex)
        {
            // Version conflict detected
            await ShowConflictMessageAsync(note, ex.Message);
            await ReloadNoteFromDiskAsync(note.Id);
        }
    }

    /// <summary>
    /// Saves just the manifest (note ordering).
    /// </summary>
    private async Task SaveManifestAsync()
    {
        if (_isLoading) return;
        var ids = Notes.Select(n => n.Id).ToList();
        await App.StorageService.SaveManifestAsync(ids);
    }

    // ──────────────────────────── File watcher handlers ────────────────────────────

    private void OnExternalNoteChanged(Guid id)
    {
        Dispatcher.UIThread.Post(() => _ = ReloadNoteFromDiskAsync(id));
    }

    private void OnExternalNoteCreated(Guid id)
    {
        Dispatcher.UIThread.Post(() => _ = HandleExternalCreateAsync(id));
    }

    private void OnExternalNoteDeleted(Guid id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Notes.FirstOrDefault(n => n.Id == id);
            if (existing != null)
            {
                UnsubscribeNote(existing);
                Notes.Remove(existing);
            }
        });
    }

    private async Task ReloadNoteFromDiskAsync(Guid id)
    {
        CancelPendingSave(id);
        var freshNote = await App.StorageService.LoadNoteAsync(id);
        if (freshNote is null) return;

        var index = -1;
        for (var i = 0; i < Notes.Count; i++)
        {
            if (Notes[i].Id == id) { index = i; break; }
        }

        if (index < 0) return;

        _isLoading = true;
        try
        {
            var old = Notes[index];
            UnsubscribeNote(old);

            // Copy data into the existing Note so open NoteWindows stay connected
            old.Title = freshNote.Title;
            old.Color = freshNote.Color;
            old.CreatedDate = freshNote.CreatedDate;
            old.ModifiedDate = freshNote.ModifiedDate;
            old.LastWriteTimeUtc = freshNote.LastWriteTimeUtc;
            old.WindowX = freshNote.WindowX;
            old.WindowY = freshNote.WindowY;
            old.WindowWidth = freshNote.WindowWidth;
            old.WindowHeight = freshNote.WindowHeight;

            // Replace items
            foreach (var item in old.Items)
                item.PropertyChanged -= OnItemPropertyChanged;
            old.Items.Clear();
            foreach (var item in freshNote.Items)
                old.Items.Add(item);

            SubscribeNote(old);
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void CancelPendingSave(Guid noteId)
    {
        if (_saveCtsMap.TryGetValue(noteId, out var cts))
        {
            cts.Cancel();
            _saveCtsMap.Remove(noteId);
        }
    }

    private async Task HandleExternalCreateAsync(Guid id)
    {
        // Skip if we already have it
        if (Notes.Any(n => n.Id == id)) return;

        var note = await App.StorageService.LoadNoteAsync(id);
        if (note is null) return;

        _isLoading = true;
        try
        {
            SubscribeNote(note);
            Notes.Add(note);
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ──────────────────────────── Commands ────────────────────────────

    [RelayCommand]
    private async Task CreateNewNote()
    {
        var newNote = new Note
        {
            Title = "New Note",
            Color = NoteColors[_random.Next(NoteColors.Length)]
        };

        SubscribeNote(newNote);
        Notes.Add(newNote);
        newNote.ModifiedDate = DateTime.Now;
        await App.StorageService.SaveNoteAsync(newNote);
        await SaveManifestAsync();
        OpenNote(newNote);
    }

    [RelayCommand]
    private async Task DeleteNote(Note? note)
    {
        if (note != null && Notes.Contains(note))
        {
            UnsubscribeNote(note);
            Notes.Remove(note);
            await App.StorageService.DeleteNoteFileAsync(note.Id);
            await SaveManifestAsync();
        }
    }

    [RelayCommand]
    private async Task DuplicateNote(Note? note)
    {
        if (note is null) return;

        var dup = new Note
        {
            Title = note.Title + " (Copy)",
            Color = note.Color
        };

        foreach (var item in note.Items)
        {
            dup.Items.Add(new TodoItem
            {
                Text = item.Text,
                IsCompleted = item.IsCompleted,
                IsHeading = item.IsHeading,
                IsImportant = item.IsImportant,
                TextAttachment = item.TextAttachment
            });
        }

        SubscribeNote(dup);
        Notes.Add(dup);
        dup.ModifiedDate = DateTime.Now;
        await App.StorageService.SaveNoteAsync(dup);
        await SaveManifestAsync();
    }

    [RelayCommand]
    private void OpenNote(Note? note)
    {
        if (note == null) return;
        var noteViewModel = new NoteViewModel(note);
        var noteWindow = new NoteWindow
        {
            DataContext = noteViewModel
        };
        noteWindow.Show();
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

    [RelayCommand]
    private async Task RefreshMain()
    {
        await LoadNotesAsync();
    }
    private async Task ShowConflictMessageAsync(Note note, string message)
    {
        // This is a simple Avalonia dialog. You can replace it with a more advanced dialog if desired.
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.Windows.FirstOrDefault(w => w.DataContext is NoteViewModel vm && vm.Note.Id == note.Id)
                : null;
            if (window != null)
            {
                var dlg = new Avalonia.Controls.Window
                {
                    Title = "Sync Conflict",
                    Width = 350,
                    Height = 120,
                    Content = new Avalonia.Controls.StackPanel
                    {
                        Margin = new Avalonia.Thickness(20),
                        Children =
                        {
                            new Avalonia.Controls.TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                            new Avalonia.Controls.Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0,10,0,0), MinWidth = 60 }
                        }
                    }
                };
                var okButton = ((Avalonia.Controls.StackPanel)dlg.Content).Children.OfType<Avalonia.Controls.Button>().First();
                okButton.Click += (_, __) => dlg.Close();
                await dlg.ShowDialog(window);
            }
        });
    }

    public async Task FlushNoteAsync(Guid noteId)
    {
        var note = Notes.FirstOrDefault(n => n.Id == noteId);
        if (note == null) return;
        CancelPendingSave(noteId);
        await App.StorageService.SaveNoteAsync(note);
        await SaveManifestAsync();
    }
}

