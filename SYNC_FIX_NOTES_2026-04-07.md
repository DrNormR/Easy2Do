# Easy2Do Sync Debug Notes (2026-04-07)

## Issue Reported

- Deleting certain todo items in a note caused them to reappear after a few seconds.
- Adding items worked; deleting specific items did not.
- User later observed a pattern: first delete worked, second delete often failed.
- Error seen in app: "The note has been modified elsewhere, please reload before saving..."
- UI clue: refresh/reload button appeared to pulse/trigger repeatedly.

## Root Causes Identified

- Remote sync could reintroduce items when row deletion/tombstoning was not consistently reflected.
- Auto-refresh from note file change events in desktop note windows could race with local edits/saves.
- This race produced stale-write conflicts (`LastWriteTimeUtc`) on subsequent deletes.

## Code Changes Made

### 1) Item deletion propagation and diagnostics

**File:** `Easy2Do/Services/StorageService.cs`

- Added `TryDeleteNoteItemFromSupabaseAsync(Guid itemId)`.
- Added status-code checks and richer HTTP error context for failed deletes.
- Delete request now uses `Prefer: return=representation`.
- If DELETE returns no affected row, fallback to soft delete (`PATCH deleted_at_utc`).
- If soft delete also affects no rows, throws explicit error to reveal policy/row mismatch.
- Existing note delete requests were hardened with success checks.

### 2) Ignore deleted items during sync pull

**File:** `Easy2Do/Services/PowerSyncService.cs`

- Updated items fetch query to include `deleted_at_utc=is.null`.
- Added defensive skip when `deleted_at_utc` is present while parsing pulled rows.

### 3) Remove-item workflow updates to reduce sync conflicts

**File:** `Easy2Do/ViewModels/NoteViewModel.cs`

- Updated remove-item command to perform remote delete/tombstone first.
- Only removes locally after remote operation succeeds.
- After local removal:
  - cancels pending debounced save for that note,
  - reloads latest `LastWriteTimeUtc`,
  - saves with fresh write timestamp to avoid stale-save conflict.

### 4) Removed auto-refresh behavior that caused race conditions

**File:** `Easy2Do/Views/NoteWindow.axaml.cs`

- Removed subscription to `StorageService.NoteFileChanged`.
- Removed automatic `RefreshNoteCommand` execution on external file changes.

### 5) Removed pulsing refresh button in note UI

**Files:**

- `Easy2Do/Views/NoteWindow.axaml`
- `Easy2Do/Views/NoteView.axaml`

- Removed refresh button from note header layouts.
- Updated header grid column definitions/indices accordingly.

## Validation Performed

- Desktop project builds succeeded during intermediate checks.
- Some subsequent build attempts failed only due to running process file lock (`Easy2Do.Desktop` holding `Easy2Do.dll`), not compile errors.

## Current Status

- User reported: "i think that did it--it seems to be working ok now."

## Git / Release Guidance Provided

- Commit/push commands were provided for GitHub.
- Explained that uninstall is usually not required for upgrades (depends on install method).
