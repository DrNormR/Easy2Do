using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Easy2Do.ViewModels;
using System;
using System.Collections.Generic;

namespace Easy2Do.Views;

public partial class NoteWindow : Window
{
    // Static registry for open note windows
    private static readonly Dictionary<Guid, List<NoteWindow>> OpenWindows = new();
    // Note ID for this window
    private Guid? _noteId;

    public NoteWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        // Subscribe to external note file changes
        Easy2Do.App.StorageService.NoteFileChanged += OnExternalNoteChanged;
    }

    // Automatic refresh on external note file change
    private void OnExternalNoteChanged(Guid id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_noteId.HasValue && _noteId.Value == id && DataContext is NoteViewModel vm)
            {
                vm.RefreshNoteCommand.Execute(null);
            }
        });
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (DataContext is NoteViewModel vm)
        {
            var note = vm.Note;
            _noteId = note.Id;
            // Register this window
            if (!OpenWindows.TryGetValue(note.Id, out var list))
                OpenWindows[note.Id] = list = new List<NoteWindow>();
            if (!list.Contains(this))
                list.Add(this);

            if (!double.IsNaN(note.WindowWidth) && note.WindowWidth > 0)
                Width = note.WindowWidth;
            if (!double.IsNaN(note.WindowHeight) && note.WindowHeight > 0)
                Height = note.WindowHeight;
            if (!double.IsNaN(note.WindowX) && !double.IsNaN(note.WindowY))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint((int)note.WindowX, (int)note.WindowY);
            }
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_noteId.HasValue && OpenWindows.TryGetValue(_noteId.Value, out var list))
        {
            list.Remove(this);
            if (list.Count == 0)
                OpenWindows.Remove(_noteId.Value);
        }
        if (DataContext is NoteViewModel vm)
        {
            var note = vm.Note;
            note.WindowX = Position.X;
            note.WindowY = Position.Y;
            note.WindowWidth = Width;
            note.WindowHeight = Height;
            note.ModifiedDate = DateTime.Now;
        }
    }
}
