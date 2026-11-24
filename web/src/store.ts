import { Api } from "./api";
import type { Note, TodoItem } from "./types";
import { DEFAULT_COLOR } from "./types";
import type { Settings } from "./settings";

const SAVE_DELAY_MS = 700;
export const REFRESH_INTERVAL_MS = 30_000;

export type SyncState = "idle" | "loading" | "saving" | "error";

/**
 * Holds the notes in memory and writes changes through to Supabase.
 * Edits are applied locally right away; saves are debounced per note.
 * Every save bumps the note's modified_date, because the desktop app only
 * pulls a note when the remote modified_date is newer than its local copy.
 */
export class Store {
  notes: Note[] = [];
  syncState: SyncState = "idle";
  lastError = "";
  lastSynced: Date | null = null;

  private api: Api;
  private pending = new Map<string, number>(); // noteId → timeout handle
  private inFlight = 0;
  private listeners = new Set<() => void>();

  constructor(settings: Settings) {
    this.api = new Api(settings);
  }

  subscribe(fn: () => void): () => void {
    this.listeners.add(fn);
    return () => this.listeners.delete(fn);
  }

  private emit() {
    for (const fn of this.listeners) fn();
  }

  private fail(err: unknown) {
    this.syncState = "error";
    this.lastError = err instanceof Error ? err.message : String(err);
    console.error("[Easy2Do]", err);
    this.emit();
  }

  get hasPendingWork() {
    return this.pending.size > 0 || this.inFlight > 0;
  }

  getNote(id: string) {
    return this.notes.find((n) => n.id === id);
  }

  /** Initial load: replaces everything. */
  async load(): Promise<void> {
    this.syncState = "loading";
    this.emit();
    try {
      this.notes = await this.api.fetchAll();
      this.syncState = "idle";
      this.lastError = "";
      this.lastSynced = new Date();
    } catch (e) {
      this.fail(e);
      throw e;
    }
    this.emit();
  }

  /**
   * Periodic pull. Like the desktop merge: take a remote note if it is new or its
   * modified_date is newer; drop local notes the server no longer has. Notes with
   * unsaved local edits are left alone so a pull never overwrites typing.
   */
  async refresh(): Promise<boolean> {
    if (this.hasPendingWork) return false;
    let remote: Note[];
    try {
      remote = await this.api.fetchAll();
    } catch (e) {
      this.fail(e);
      return false;
    }
    if (this.hasPendingWork) return false; // user edited while we were fetching

    const localById = new Map(this.notes.map((n) => [n.id, n]));
    let changed = remote.length !== this.notes.length;
    const merged = remote.map((r) => {
      const local = localById.get(r.id);
      if (!local) {
        changed = true;
        return r;
      }
      if (r.modifiedDate.getTime() > local.modifiedDate.getTime()) {
        changed = true;
        return r;
      }
      return local;
    });
    if (!changed) changed = merged.some((n, i) => this.notes[i]?.id !== n.id);

    this.lastSynced = new Date();
    if (this.syncState === "error") {
      this.syncState = "idle";
      this.lastError = "";
      changed = true;
    }
    if (changed) {
      this.notes = merged;
      this.emit();
    }
    return changed;
  }

  // ---- note-level operations ----

  /** Call after mutating a note in place. Re-renders now, saves shortly. */
  touch(note: Note) {
    note.modifiedDate = new Date();
    this.emit();
    this.scheduleSave(note.id);
  }

  private scheduleSave(noteId: string) {
    const existing = this.pending.get(noteId);
    if (existing) clearTimeout(existing);
    this.pending.set(
      noteId,
      window.setTimeout(() => void this.flush(noteId), SAVE_DELAY_MS),
    );
  }

  async flush(noteId: string): Promise<void> {
    const handle = this.pending.get(noteId);
    if (handle) clearTimeout(handle);
    this.pending.delete(noteId);
    const note = this.getNote(noteId);
    if (!note) return;
    await this.serial(noteId, () => this.run(() => this.api.saveNote(note)));
  }

  /** Runs network work for one note strictly in order, so a save can't land after a delete. */
  private chains = new Map<string, Promise<unknown>>();
  private serial<T>(noteId: string, fn: () => Promise<T>): Promise<T> {
    const prev = this.chains.get(noteId) ?? Promise.resolve();
    const next = prev.then(fn, fn);
    this.chains.set(noteId, next.catch(() => undefined));
    return next;
  }

  async flushAll(): Promise<void> {
    await Promise.all([...this.pending.keys()].map((id) => this.flush(id)));
  }

  private async run(op: () => Promise<void>): Promise<boolean> {
    this.inFlight++;
    this.syncState = "saving";
    this.emit();
    let ok = true;
    try {
      await op();
    } catch (e) {
      ok = false;
      this.syncState = "error";
      this.lastError = e instanceof Error ? e.message : String(e);
      console.error("[Easy2Do]", e);
    }
    this.inFlight--;
    if (this.inFlight === 0 && this.syncState !== "error") {
      this.syncState = "idle";
      this.lastError = "";
      this.lastSynced = new Date();
    }
    this.emit();
    return ok;
  }

  /** Adds the note locally right away and saves it in the background. */
  createNote(): Note {
    const now = new Date();
    const note: Note = {
      id: crypto.randomUUID(),
      title: "New Note",
      color: DEFAULT_COLOR,
      createdDate: now,
      modifiedDate: now,
      isPinned: false,
      windowX: 0,
      windowY: 0,
      windowWidth: 320,
      windowHeight: 420,
      items: [],
    };
    this.notes.unshift(note);
    this.emit();
    void this.serial(note.id, () =>
      this.run(async () => {
        await this.api.saveNote(note);
        await this.api.saveOrder(this.notes.map((n) => n.id));
      }),
    );
    return note;
  }

  async duplicateNote(source: Note): Promise<Note> {
    const now = new Date();
    const copy: Note = {
      ...source,
      id: crypto.randomUUID(),
      title: `${source.title} (Copy)`,
      createdDate: now,
      modifiedDate: now,
      items: source.items.map((i) => ({ ...i, id: crypto.randomUUID(), createdAtUtc: now, updatedAtUtc: now })),
    };
    const idx = this.notes.indexOf(source);
    this.notes.splice(idx + 1, 0, copy);
    this.emit();
    await this.run(async () => {
      await this.api.saveNote(copy);
      await this.api.saveOrder(this.notes.map((n) => n.id));
    });
    return copy;
  }

  async deleteNote(note: Note): Promise<boolean> {
    const handle = this.pending.get(note.id);
    if (handle) clearTimeout(handle);
    this.pending.delete(note.id);
    const ok = await this.serial(note.id, () => this.run(() => this.api.deleteNote(note.id)));
    if (ok) {
      this.notes = this.notes.filter((n) => n.id !== note.id);
      this.emit();
    }
    return ok;
  }

  async moveNote(fromIndex: number, toIndex: number) {
    if (fromIndex === toIndex) return;
    const [n] = this.notes.splice(fromIndex, 1);
    this.notes.splice(toIndex, 0, n);
    this.emit();
    await this.run(() => this.api.saveOrder(this.notes.map((x) => x.id)));
  }

  // ---- item-level operations ----

  addItem(note: Note, text: string, index = note.items.length): TodoItem {
    const now = new Date();
    const item: TodoItem = {
      id: crypto.randomUUID(),
      text,
      isCompleted: false,
      isHeading: false,
      isImportant: false,
      textAttachment: "",
      dueDate: null,
      isAlarmDismissed: false,
      snoozeUntil: null,
      createdAtUtc: now,
      updatedAtUtc: now,
    };
    note.items.splice(index, 0, item);
    this.touch(note);
    return item;
  }

  updateItem(note: Note, item: TodoItem, patch: Partial<TodoItem>) {
    Object.assign(item, patch, { updatedAtUtc: new Date() });
    this.touch(note);
  }

  /**
   * Same order as the desktop fix for the "deleted items come back" bug:
   * delete remotely first, remove locally only once that succeeded, then save
   * the note so its modified_date moves forward for other clients.
   */
  async deleteItem(note: Note, item: TodoItem): Promise<boolean> {
    // Drop it from any queued save first, then delete remotely after in-flight saves finish.
    const at = note.items.indexOf(item);
    note.items = note.items.filter((i) => i.id !== item.id);
    const ok = await this.serial(note.id, () => this.run(() => this.api.deleteItem(item.id)));
    if (!ok) {
      note.items.splice(Math.max(0, at), 0, item);
      this.emit();
      return false;
    }
    this.touch(note);
    return true;
  }

  moveItem(note: Note, fromIndex: number, toIndex: number) {
    if (fromIndex === toIndex) return;
    const [it] = note.items.splice(fromIndex, 1);
    note.items.splice(toIndex, 0, it);
    this.touch(note);
  }
}
