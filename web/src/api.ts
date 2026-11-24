// Supabase REST (PostgREST) client. Request shapes match the desktop app's
// StorageService / PowerSyncService so both clients read each other's writes.
import type { Note, TodoItem } from "./types";
import { DEFAULT_COLOR } from "./types";
import type { Settings } from "./settings";

type Row = Record<string, unknown>;

/** Parses .NET "O" timestamps (7 fractional digits) and PostgREST timestamps. */
export function parseDate(value: unknown): Date | null {
  if (typeof value !== "string" || !value.trim()) return null;
  const normalized = value.trim().replace(/(\.\d{3})\d+/, "$1");
  const d = new Date(normalized);
  return isNaN(d.getTime()) ? null : d;
}

const num = (v: unknown, fallback: number) => (typeof v === "number" && isFinite(v) ? v : fallback);
const str = (v: unknown, fallback = "") => (typeof v === "string" ? v : v == null ? fallback : String(v));

export class Api {
  constructor(private settings: Settings) {}

  private url(path: string) {
    return `${this.settings.supabaseUrl.replace(/\/+$/, "")}/rest/v1/${path}`;
  }

  private async request(method: string, path: string, body?: unknown, prefer?: string): Promise<Response> {
    const headers: Record<string, string> = {
      apikey: this.settings.supabaseApiKey,
      Authorization: `Bearer ${this.settings.supabaseApiKey}`,
    };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (prefer) headers["Prefer"] = prefer;
    const json = body === undefined ? undefined : JSON.stringify(body);
    const res = await fetch(this.url(path), {
      method,
      headers,
      body: json,
      // Lets a write finish even if the tab closes or navigates mid-request.
      // Browsers cap keepalive bodies at 64 KB, so very large notes go without it.
      keepalive: method !== "GET" && (json?.length ?? 0) < 60_000,
    });
    if (!res.ok) {
      const text = await res.text().catch(() => "");
      throw new Error(`${method} ${path.split("?")[0]} failed: ${res.status} ${text}`.trim());
    }
    return res;
  }

  private async getJson(path: string): Promise<Row[]> {
    const res = await this.request("GET", path);
    const data = await res.json();
    return Array.isArray(data) ? data : [];
  }

  /** Same three queries as PowerSyncService.RefreshFromSupabaseAsync. Returns notes in list order. */
  async fetchAll(): Promise<Note[]> {
    const [noteRows, itemRows, orderRows] = await Promise.all([
      this.getJson("notes?select=*"),
      this.getJson("note_items?select=*&deleted_at_utc=is.null&order=position.asc"),
      this.getJson("note_order?select=note_id,sort_order&order=sort_order.asc"),
    ]);

    const byId = new Map<string, Note>();
    for (const r of noteRows) {
      const id = str(r.id);
      if (!id || byId.has(id)) continue;
      byId.set(id, {
        id,
        title: str(r.title, "New Note"),
        color: str(r.color, DEFAULT_COLOR) || DEFAULT_COLOR,
        createdDate: parseDate(r.created_date) ?? new Date(),
        modifiedDate: parseDate(r.modified_date) ?? new Date(),
        isPinned: r.is_pinned === true,
        windowX: num(r.window_x, 0),
        windowY: num(r.window_y, 0),
        windowWidth: num(r.window_width, 320),
        windowHeight: num(r.window_height, 420),
        items: [],
      });
    }

    for (const r of itemRows) {
      if (parseDate(r.deleted_at_utc)) continue;
      const note = byId.get(str(r.note_id));
      const id = str(r.id);
      if (!note || !id) continue;
      note.items.push({
        id,
        text: str(r.text),
        isCompleted: r.is_completed === true,
        isHeading: r.is_heading === true,
        isImportant: r.is_important === true,
        textAttachment: str(r.text_attachment),
        dueDate: parseDate(r.due_date),
        isAlarmDismissed: r.is_alarm_dismissed === true,
        snoozeUntil: parseDate(r.snooze_until),
        createdAtUtc: parseDate(r.created_at_utc) ?? new Date(),
        updatedAtUtc: parseDate(r.updated_at_utc) ?? new Date(),
      });
    }

    const ordered: Note[] = [];
    for (const r of orderRows) {
      const n = byId.get(str(r.note_id));
      if (n && !ordered.includes(n)) ordered.push(n);
    }
    // Notes with no order row go last, newest first.
    const rest = [...byId.values()]
      .filter((n) => !ordered.includes(n))
      .sort((a, b) => b.modifiedDate.getTime() - a.modifiedDate.getTime());
    return [...ordered, ...rest];
  }

  /** Mirrors TryUpsertNoteToSupabaseAsync + TryUpsertNoteItemsToSupabaseAsync. */
  async saveNote(note: Note): Promise<void> {
    await this.request(
      "POST",
      "notes",
      [
        {
          id: note.id,
          title: note.title ?? "",
          color: note.color || DEFAULT_COLOR,
          created_date: note.createdDate.toISOString(),
          modified_date: note.modifiedDate.toISOString(),
          window_x: note.windowX,
          window_y: note.windowY,
          window_width: note.windowWidth,
          window_height: note.windowHeight,
          is_pinned: note.isPinned,
        },
      ],
      "resolution=merge-duplicates",
    );

    if (note.items.length === 0) return;
    await this.request(
      "POST",
      "note_items",
      note.items.map((item: TodoItem, i: number) => ({
        id: item.id,
        note_id: note.id,
        text: item.text ?? "",
        is_completed: item.isCompleted,
        is_heading: item.isHeading,
        is_important: item.isImportant,
        text_attachment: item.textAttachment ?? "",
        due_date: item.dueDate?.toISOString() ?? null,
        is_alarm_dismissed: item.isAlarmDismissed,
        snooze_until: item.snoozeUntil?.toISOString() ?? null,
        created_at_utc: item.createdAtUtc.toISOString(),
        updated_at_utc: item.updatedAtUtc.toISOString(),
        deleted_at_utc: null,
        position: i,
      })),
      "resolution=merge-duplicates",
    );
  }

  /**
   * Mirrors TryDeleteNoteItemFromSupabaseAsync: hard delete, and if that fails
   * (or hits no row) fall back to a soft delete so other clients drop it on pull.
   */
  async deleteItem(itemId: string): Promise<void> {
    const q = `note_items?id=eq.${encodeURIComponent(itemId)}`;
    try {
      await this.request("DELETE", q);
    } catch (primary) {
      try {
        await this.request("PATCH", q, { deleted_at_utc: new Date().toISOString() }, "return=representation");
      } catch (soft) {
        throw new Error(`Item delete failed: ${(soft as Error).message} (primary: ${(primary as Error).message})`);
      }
    }
  }

  async deleteNote(noteId: string): Promise<void> {
    const id = encodeURIComponent(noteId);
    await this.request("DELETE", `note_items?note_id=eq.${id}`);
    await this.request("DELETE", `note_order?note_id=eq.${id}`);
    const res = await this.request("DELETE", `notes?id=eq.${id}`, undefined, "return=representation");
    const body = (await res.text()).trim();
    if (!body || body === "[]") throw new Error("Note delete affected 0 rows.");
  }

  /** Mirrors TryUpsertNoteOrderToSupabaseAsync (row id == note id). */
  async saveOrder(noteIds: string[]): Promise<void> {
    if (noteIds.length === 0) return;
    await this.request(
      "POST",
      "note_order",
      noteIds.map((id, i) => ({ id, note_id: id, sort_order: i })),
      "resolution=merge-duplicates",
    );
  }
}
