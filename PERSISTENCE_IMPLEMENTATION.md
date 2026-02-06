# Persistent Storage Implementation

## Overview
Added automatic JSON-based persistence for notes with configurable storage location.

## New Components

### 1. Services Layer
Created `Easy2Do/Services/` folder with two services:

#### **StorageService.cs**
- Handles saving/loading notes in JSON format
- Uses `System.Text.Json` for serialization
- Default file: `notes.json` in configured storage location
- Features:
  - Async file I/O operations
  - Error handling with console logging
  - Auto-creates directories if they don't exist

#### **SettingsService.cs**
- Manages application settings persistence
- Stores settings in: `%LocalAppData%/Easy2Do/settings.json`
- Default storage location: `Documents/Easy2Do/`
- Features:
  - Get/Set storage location
  - Persistent settings across sessions
  - Auto-creates app data folder

### 2. Updated ViewModels

#### **MainViewModel.cs**
- **LoadNotesAsync()**: Loads notes on startup
- **SaveNotesAsync()**: Saves notes automatically
- Auto-save triggers:
  - When notes collection changes (add/remove)
  - When note properties change
  - When note items change
- Subscribes to property/collection changes for real-time saves
- Updates `ModifiedDate` on all changes

#### **SettingsViewModel.cs**
Added new functionality:
- **StorageLocation** property (displays current location)
- **BrowseStorageLocationCommand**: Opens folder picker dialog
- **OpenStorageFolderCommand**: Opens storage folder in file explorer
- Uses Avalonia's `IStorageProvider` for cross-platform folder selection

### 3. Updated UI

#### **SettingsWindow.axaml**
- Increased window size to 550x550
- Added "Storage Location" section with:
  - Display of current storage path
  - "Browse..." button to change location
  - "Open Folder" button to view files
  - Warning about location changes not moving existing files

### 4. Application Lifecycle

#### **App.axaml.cs**
- Initializes services on startup
- Creates static instances for app-wide access
- Hooks into application exit event to save notes
- Services available via `App.StorageService` and `App.SettingsService`

## How It Works

### On Application Start:
1. `SettingsService` loads settings (or creates defaults)
2. `StorageService` is initialized with settings
3. `MainViewModel` loads existing notes from JSON
4. If no notes exist, creates a sample note

### During Use:
1. User makes changes to notes (add/edit/delete)
2. Changes trigger property/collection change events
3. `SaveNotesAsync()` is called automatically
4. Notes are serialized to JSON and saved

### On Application Close:
1. Exit event handler is triggered
2. Final save of all notes
3. Application closes cleanly

### Changing Storage Location:
1. User clicks "Browse..." in Settings
2. Selects new folder location
3. Setting is saved immediately
4. Future saves go to new location
5. Old notes remain in previous location (manual migration needed)

## File Locations

- **Notes**: `{StorageLocation}/notes.json`
- **Settings**: `%LocalAppData%/Easy2Do/settings.json`
- **Default Storage**: `Documents/Easy2Do/`

## Data Format

### notes.json Example:
```json
[
  {
    "Id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "Title": "My Note",
    "Color": "#FFFFE680",
    "Items": [
      {
        "Text": "Task 1",
        "IsCompleted": false
      },
      {
        "Text": "Task 2",
        "IsCompleted": true
      }
    ],
    "CreatedDate": "2025-01-15T10:30:00",
    "ModifiedDate": "2025-01-15T14:25:00"
  }
]
```

### settings.json Example:
```json
{
  "StorageLocation": "C:\\Users\\YourName\\Documents\\Easy2Do"
}
```

## Benefits

1. **No Manual Saving**: Everything is automatic
2. **Data Persistence**: Notes survive app restarts
3. **User Control**: Choose where data is stored
4. **Portable Format**: JSON is human-readable and editable
5. **Backup Friendly**: Easy to backup/restore/share notes
6. **Cross-Platform**: Works on Windows, macOS, Linux

## Future Enhancements

- Migration wizard for moving notes between locations
- Import/Export functionality
- Multiple note files support
- Cloud sync integration
- Backup/restore features
