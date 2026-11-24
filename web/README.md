# Easy2Do Web

A browser version of Easy2Do. It reads and writes the same Supabase tables
(`notes`, `note_items`, `note_order`) as the desktop and mobile apps, so your
notes stay in sync across all of them.

No framework, no backend: plain TypeScript built with Vite into static files.

## Features

- Notes list (drag to reorder) and note editor side by side; one at a time on phones
- Title, color (same palette as desktop), pin, duplicate, delete
- Items: check off, edit inline, Enter for a new item below, drag to reorder
- Heading, important, due date, and text attachment per item
- Due-date alarms in the page, with Snooze 10m / Dismiss (same rules as the desktop AlarmService),
  plus optional browser notifications
- Pulls changes from other devices every 30 seconds and when the tab regains focus
- Installable to a phone's home screen

## Run locally

```sh
npm install
npm run dev
```

Open the page, click **Connect**, and enter the same Supabase URL and API key as
in the desktop app's Settings. They're stored only in that browser's
localStorage and are never built into the bundle.

## Deploy with Docker

```sh
docker compose up -d --build
```

Serves on port 8088. Or run `npm run build` and serve `dist/` from any static host.

## Security

The Supabase tables are currently accessed with the API key alone, with no user login.
Anyone who can open the page **and** has the key can read and change notes.
The key is not in the bundle, but don't expose this page to the internet unless
it's behind something that requires login (for example Cloudflare Access), or
the Supabase project gets Auth + row-level security.

## How it stays compatible with the desktop app

- Every change bumps the note's `modified_date`: the desktop app only pulls a note
  when the remote `modified_date` is newer than its local copy.
- Item deletes are a hard `DELETE`, falling back to setting `deleted_at_utc`, then the
  note is re-saved — the same order as the desktop fix for deleted items coming back.
- Saves and deletes for the same note run one at a time, so a slow save can't
  re-insert an item that was just deleted.
- Colors stay in Avalonia's `#AARRGGBB` form; desktop window geometry is round-tripped untouched.
