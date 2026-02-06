# Easy2Do - Sticky Note To-Do List Application

A simple and elegant to-do list application built with Avalonia UI for .NET 8.

## Features

### Main Window
- **Create New Note**: Create a new sticky note with a single click
- **Delete Note**: Remove notes you no longer need
- **Duplicate Note**: Copy an existing note with all its items
- **Settings Menu**: Access application settings and information
- **Notes List**: View all your notes with item counts and modification dates
- **Persistent Storage**: All notes are automatically saved and loaded between sessions

### Sticky Notes (Note Window)
- **Custom Titles**: Give each note a descriptive title
- **Color Customization**: Choose from 7 different colors:
  - Yellow (default)
  - Red
  - Green
  - Blue
  - Orange
  - Pink
  - Gray
- **To-Do Items**: 
  - Add items by typing and pressing Enter
  - Check items off to mark as complete (strikethrough)
  - Delete individual items
  - Completed items remain visible but are struck through
- **Auto-Save**: Changes are automatically saved as you work

### Settings
- **Storage Location**: Choose where your notes are saved on your computer
  - Browse to select a custom folder
  - Open the storage folder directly from settings
- **About Easy2Do**: View application information including version and description

## Data Storage

- Notes are stored in **JSON format** for easy backup and portability
- Default storage location: `Documents/Easy2Do/notes.json`
- Application settings: `%LocalAppData%/Easy2Do/settings.json`
- **Auto-Save**: All changes are saved automatically - no need to manually save!
- Notes persist between application sessions

## How to Use

1. **Launch the application** - You'll see the main window with a sample note
2. **Create a new note** - Click "+ New Note" button
3. **Add items to your note**:
   - Type in the text box at the bottom
   - Press Enter or click the + button to add the item
4. **Complete items** - Click the checkbox next to an item to mark it complete
5. **Change note color** - Click the "Color" button and select a color
6. **Organize your notes** - Use Duplicate to copy notes, Delete to remove them
7. **Access settings** - Click the "Settings" button
8. **Change storage location** (optional):
   - Go to Settings
   - Click "Browse..." to select a new folder
   - Click "Open Folder" to view your notes JSON file

## Technical Details

- **Framework**: .NET 8
- **UI Framework**: Avalonia UI 11.x
- **MVVM Pattern**: Uses CommunityToolkit.Mvvm for ViewModels and Commands
- **Cross-Platform**: Desktop (Windows, macOS, Linux) with mobile support structure

## Project Structure

```
Easy2Do/
??? Models/
?   ??? Note.cs              # Note data model
?   ??? TodoItem.cs          # Todo item data model
??? ViewModels/
?   ??? MainViewModel.cs     # Main window logic
?   ??? NoteViewModel.cs     # Sticky note logic
?   ??? SettingsViewModel.cs # Settings logic
?   ??? AboutViewModel.cs    # About page logic
??? Views/
?   ??? MainView.axaml       # Main window UI
?   ??? NoteWindow.axaml     # Sticky note UI
?   ??? SettingsWindow.axaml # Settings UI
?   ??? AboutView.axaml      # About page UI
??? Services/
    ??? StorageService.cs    # JSON persistence
    ??? SettingsService.cs   # App settings management
```

## Future Enhancement Ideas

- Note search and filtering
- Categories/tags for notes
- Export notes to various formats
- Reminders and due dates
- Note sharing
- Cloud sync
- Dark mode theme

## License

© 2025 Easy2Do. All rights reserved.
