using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Easy2Do.ViewModels;
using Easy2Do.Views;

namespace Easy2Do;

/// <summary>
/// Given a view model, returns the corresponding view. Uses explicit mapping
/// rather than reflection so it is safe for AOT/trimming on iOS.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => param switch
    {
        NoteViewModel     => new NoteView(),
        SettingsViewModel => new SettingsView(),
        MainViewModel     => new MainView(),
        null              => null,
        _                 => new TextBlock { Text = "View not found for: " + param.GetType().Name }
    };

    public bool Match(object? data) => data is ViewModelBase;
}