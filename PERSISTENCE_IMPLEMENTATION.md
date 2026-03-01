# Persistent Storage Implementation (SQLite + PowerSync)

## Overview
Added automatic SQLite-based persistence for notes with configurable storage location.
Stage 3 adds PowerSync scaffolding to sync the same SQLite file with Supabase/Postgres.

## New Components

### 1. Services Layer
Created `Easy2Do/Services/` folder with two services:

#### **StorageService.cs**
- Handles saving/loading notes in SQLite
- Default file: `easy2do.db` in configured storage location
- Legacy JSON import supported for one-time migration
- Features:
  - Async database operations
  - Error handling with console logging
  - Auto-creates directories if they don't exist
  - Migration to add `note_order.id` for sync compatibility

#### **SettingsService.cs**
- Manages application settings persistence
- Stores settings in: `%LocalAppData%/Easy2Do/settings.json`
- Default storage location: `Documents/Easy2Do/`
- Features:
  - Get/Set storage location
  - Persistent settings across sessions
  - Auto-creates app data folder
  - PowerSync settings for sync configuration

#### **PowerSyncService.cs**
- Initializes PowerSync against the same `easy2do.db` SQLite file
- Connects to PowerSync using a backend connector
- Uses a schema that mirrors the local tables

#### **PowerSyncConnector.cs**
- Fetches PowerSync credentials (dev token or backend-provided token)
- Uploads CRUD batches to a backend endpoint
- Dev shortcut: can upload directly to Supabase REST using an API key (no backend)

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
- PowerSync config fields and toggle for sync enablement

### 3. Updated UI

#### **SettingsWindow.axaml**
- Increased window size to 550x550
- Added "Storage Location" section with:
  - Display of current storage path
  - "Browse..." button to change location
  - "Open Folder" button to view files
  - Warning about location changes not moving existing files
- Added "Sync (PowerSync)" section with:
  - Toggle to enable sync
  - Fields for PowerSync URL, dev token, backend URL
  - Reconnect button

### 4. Application Lifecycle

#### **App.axaml.cs**
- Initializes services on startup
- Creates static instances for app-wide access
- Hooks into application exit event to save notes
- Services available via `App.StorageService` and `App.SettingsService`

## How It Works

### On Application Start:
1. `SettingsService` loads settings (or creates defaults)
2. `StorageService` initializes the SQLite database
3. `MainViewModel` loads existing notes from SQLite
4. If database is empty, StorageService attempts to import legacy JSON notes

### During Use:
1. User makes changes to notes (add/edit/delete)
2. Changes trigger property/collection change events
3. `SaveNoteAsync()` is called automatically
4. Notes and items are persisted to SQLite

### Sync Behavior (Stage 3):
1. If sync is enabled and PowerSync is configured, PowerSync connects on app start
2. Local SQLite changes are queued for upload via PowerSync triggers
3. PowerSync pulls remote changes into the same SQLite file
4. The app continues to read/write via SQLite as before

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

- **SQLite DB**: `{StorageLocation}/easy2do.db`
- **Settings**: `%LocalAppData%/Easy2Do/settings.json`
- **Default Storage**: `Documents/Easy2Do/`

## Data Format

### SQLite Schema (high-level):
- `notes`: note metadata
- `note_items`: list items per note
- `note_order`: note ordering

### PowerSync Schema (mirrors SQLite):
- `notes`, `note_items`, `note_order`
- Each table includes a primary key column `id` (required by PowerSync)

### settings.json Example:
```json
{
  "StorageLocation": "C:\\Users\\YourName\\Documents\\Easy2Do",
  "SyncEnabled": false,
  "PowerSyncUrl": "https://your-powersync-url",
  "PowerSyncDevToken": "",
  "SyncBackendUrl": "https://your-backend",
  "SupabaseUrl": "https://your-project.supabase.co",
  "SupabaseApiKey": "your-supabase-api-key"
}
```

## Benefits

1. **No Manual Saving**: Everything is automatic
2. **Data Persistence**: Notes survive app restarts
3. **User Control**: Choose where data is stored
4. **Fast Local Queries**: SQLite is optimized for local data access
5. **Backup Friendly**: JSON backups are still produced on save
6. **Cross-Platform**: Works on Windows, macOS, Linux

## Future Enhancements

- Migration wizard for moving notes between locations
- Import/Export functionality
- Multiple note files support
- Cloud sync integration
- Backup/restore features

## Supabase Setup Notes (Stage 3)

### Tables (Postgres)
Create tables matching the local schema. Example:

```sql
create table if not exists notes (
  id uuid primary key,
  title text not null,
  color text not null,
  created_date timestamptz not null,
  modified_date timestamptz not null,
  window_x double precision not null,
  window_y double precision not null,
  window_width double precision not null,
  window_height double precision not null,
  is_pinned boolean not null
);

create table if not exists note_items (
  id uuid primary key,
  note_id uuid not null references notes(id) on delete cascade,
  text text not null,
  is_completed boolean not null,
  is_heading boolean not null,
  is_important boolean not null,
  text_attachment text not null,
  due_date timestamptz null,
  is_alarm_dismissed boolean not null,
  snooze_until timestamptz null,
  created_at_utc timestamptz not null,
  updated_at_utc timestamptz not null,
  deleted_at_utc timestamptz null,
  position integer not null
);

create table if not exists note_order (
  id uuid primary key,
  note_id uuid not null references notes(id) on delete cascade,
  sort_order integer not null
);
```

### PowerSync Sync Rules
Configure sync rules to mirror the tables:

```yaml
bucket_definitions:
  all_data:
    data:
      - SELECT * FROM notes
      - SELECT * FROM note_items
      - SELECT * FROM note_order
```

### Backend Endpoints
The client expects the following endpoints on your backend:
- `GET /sync/token` returns `{ "token": "..." }`
- `POST /sync/upload` accepts a PowerSync upload batch

### Dev Upload Shortcut (No Backend)
For single-user dev, the client can upload directly to Supabase REST:
- Set `SupabaseUrl` and `SupabaseApiKey` in Settings
- This bypasses the backend and uses PostgREST directly
- Use an anon key if RLS is disabled, or a service role key for full access
