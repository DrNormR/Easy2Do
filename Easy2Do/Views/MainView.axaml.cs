
using System;
using Avalonia.Controls;

namespace Easy2Do.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        // Subscribe to external note file changes for auto-refresh
        Easy2Do.App.StorageService.NoteFileChanged += OnExternalNoteChanged;
        Easy2Do.App.StorageService.NoteFileCreated += OnExternalNoteChanged;
        Easy2Do.App.StorageService.NoteFileDeleted += OnExternalNoteChanged;
    }

    private void OnExternalNoteChanged(Guid id)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is Easy2Do.ViewModels.MainViewModel vm && vm.RefreshMainCommand.CanExecute(null))
            {
                // Always refresh the main list on any note file change
                vm.RefreshMainCommand.Execute(null);
            }
        });
    }
}