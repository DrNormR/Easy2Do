import "./style.css";
import { h, formatDate, formatDue, toLocalInput, autoGrow } from "./dom";
import { Store, REFRESH_INTERVAL_MS } from "./store";
import { loadSettings, saveSettings, isConfigured, type Settings } from "./settings";
import { Api } from "./api";
import { NOTE_COLORS, cssColor, type Note, type TodoItem } from "./types";

const app = document.getElementById("app")!;
let settings = loadSettings();
let store: Store | null = null;
let refreshTimer = 0;

// ---------- routing ----------

function currentNoteId(): string | null {
  const m = location.hash.match(/^#\/note\/([\w-]+)/);
  return m ? m[1] : null;
}

let renderedRoute = "";

/** Renders the new route immediately, so focus can be set right after. */
function openNote(id: string | null) {
  location.hash = id ? `#/note/${id}` : "#/";
  render();
}

window.addEventListener("hashchange", () => {
  if (location.hash !== renderedRoute) render();
});

// ---------- shell ----------

const statusEl = h("span", { class: "status" });

function renderStatus() {
  if (!store) {
    statusEl.textContent = "";
    return;
  }
  const s = store.syncState;
  statusEl.className = `status ${s}`;
  statusEl.title = store.lastError || "";
  statusEl.textContent =
    s === "loading"
      ? "Loading…"
      : s === "saving"
        ? "Saving…"
        : s === "error"
          ? "Sync error"
          : store.lastSynced
            ? `Synced ${formatDate(store.lastSynced)}`
            : "";
}

const header = h(
  "header",
  { class: "topbar" },
  h("a", { class: "brand", href: "#/" }, h("img", { src: "/icon.png", alt: "" }), "Easy2Do"),
  statusEl,
  h("span", { class: "spacer" }),
  h("button", { class: "ghost", title: "Refresh notes", onclick: () => void manualRefresh() }, "⟳"),
  h("button", { class: "ghost", title: "Settings", onclick: () => openSettings() }, "⚙"),
);

const body = h("main", { class: "layout" });
const trayEl = h("div", { class: "alarm-tray" });
app.append(header, body, trayEl);

// ---------- render ----------

function render() {
  renderStatus();
  renderedRoute = location.hash;
  updateFooter = null;
  body.replaceChildren();
  if (!isConfigured(settings) || !store) {
    body.append(welcome());
    return;
  }
  const noteId = currentNoteId();
  const note = noteId ? store.getNote(noteId) : undefined;
  body.classList.toggle("has-note", !!note);
  sidebarEl = renderList(note?.id ?? null);
  body.append(sidebarEl, note ? renderNote(note) : emptyDetail());
}

let sidebarEl: HTMLElement | null = null;
let updateFooter: (() => void) | null = null;

/** Cheap refresh after every edit: status, sidebar cards and note footer — never the field being typed in. */
function refreshChrome() {
  renderStatus();
  if (!store || !sidebarEl?.isConnected) return;
  const scroll = sidebarEl.scrollTop;
  const fresh = renderList(currentNoteId());
  sidebarEl.replaceWith(fresh);
  fresh.scrollTop = scroll;
  sidebarEl = fresh;
  updateFooter?.();
}

function welcome() {
  return h(
    "section",
    { class: "welcome" },
    h("h1", {}, "Welcome to Easy2Do"),
    h(
      "p",
      {},
      "Connect to the same Supabase project your desktop app syncs with, and your notes will show up here.",
    ),
    h("button", { class: "primary", onclick: () => openSettings() }, "Connect"),
  );
}

function emptyDetail() {
  return h(
    "section",
    { class: "detail empty" },
    h("p", {}, store && store.notes.length ? "Pick a note, or create a new one." : "No notes yet."),
  );
}

// ---------- notes list ----------

let dragNoteIndex = -1;

function renderList(activeId: string | null) {
  const s = store!;
  const list = h("ul", { class: "note-list" });
  s.notes.forEach((note, index) => {
    const open = note.items.filter((i) => !i.isCompleted && !i.isHeading).length;
    const total = note.items.filter((i) => !i.isHeading).length;
    const li = h(
      "li",
      {
        class: `note-card${note.id === activeId ? " active" : ""}`,
        draggable: "true",
        tabindex: "0",
        onclick: () => openNote(note.id),
        onkeydown: (e: KeyboardEvent) => {
          if (e.key === "Enter") openNote(note.id);
        },
        ondragstart: (e: DragEvent) => {
          dragNoteIndex = index;
          e.dataTransfer?.setData("text/plain", note.id);
        },
        ondragover: (e: DragEvent) => {
          if (dragNoteIndex >= 0) e.preventDefault();
        },
        ondrop: (e: DragEvent) => {
          e.preventDefault();
          if (dragNoteIndex >= 0) void s.moveNote(dragNoteIndex, index).then(render);
          dragNoteIndex = -1;
        },
      },
      h("div", { class: "note-card-title" }, note.isPinned ? "📌 " : "", note.title || "Untitled"),
      h(
        "div",
        { class: "note-card-meta" },
        total ? `${open} of ${total} left` : "Empty",
        " · ",
        formatDate(note.modifiedDate),
      ),
    );
    li.style.setProperty("--note", cssColor(note.color));
    list.append(li);
  });

  return h(
    "section",
    { class: "sidebar" },
    h(
      "div",
      { class: "sidebar-actions" },
      h(
        "button",
        {
          class: "primary",
          onclick: () => {
            const n = s.createNote();
            openNote(n.id);
            const t = document.querySelector<HTMLInputElement>(".note-title");
            t?.focus();
            t?.select();
          },
        },
        "+ New Note",
      ),
    ),
    s.notes.length ? list : h("p", { class: "muted pad" }, "No notes yet."),
  );
}

// ---------- note detail ----------

let dragItemIndex = -1;

function renderNote(note: Note) {
  const s = store!;
  const itemsEl = h("ul", { class: "items" });

  const rerenderItems = (focusId?: string) => {
    itemsEl.replaceChildren(...note.items.map((it, i) => renderItem(note, it, i, rerenderItems)));
    // Measure after the panel is attached to the page, or scrollHeight is 0.
    requestAnimationFrame(() => {
      itemsEl.querySelectorAll("textarea").forEach((t) => autoGrow(t));
      if (focusId) itemsEl.querySelector<HTMLTextAreaElement>(`[data-id="${focusId}"] textarea`)?.focus();
    });
  };

  const title = h("input", {
    class: "note-title",
    value: note.title,
    placeholder: "Note title…",
    oninput: (e: Event) => {
      note.title = (e.target as HTMLInputElement).value;
      s.touch(note);
    },
  });

  const colorMenu = h(
    "div",
    { class: "color-menu", hidden: true },
    ...NOTE_COLORS.map((c) =>
      h("button", {
        class: `swatch${c.value === note.color ? " selected" : ""}`,
        title: c.name,
        style: { background: cssColor(c.value) },
        onclick: () => {
          note.color = c.value;
          s.touch(note);
          render();
        },
      }),
    ),
  );

  const newItem = h("input", {
    class: "new-item",
    placeholder: "Add new item…",
    enterkeyhint: "enter",
    onkeydown: (e: KeyboardEvent) => {
      if (e.key !== "Enter") return;
      const text = newItem.value.trim();
      if (!text) return;
      s.addItem(note, text);
      newItem.value = "";
      rerenderItems();
    },
  });

  const footerText = h("span");
  const clearBtn = h(
    "button",
    {
      class: "link",
      onclick: async () => {
        const completed = note.items.filter((i) => i.isCompleted);
        if (!confirm(`Remove ${completed.length} completed item(s)?`)) return;
        for (const it of completed) await s.deleteItem(note, it);
        rerenderItems();
      },
    },
    "Clear completed",
  );
  updateFooter = () => {
    const done = note.items.filter((i) => i.isCompleted).length;
    footerText.textContent = `${done}/${note.items.length} done · edited ${formatDate(note.modifiedDate)}`;
    clearBtn.hidden = done === 0;
  };
  updateFooter();

  const panel = h(
    "section",
    { class: "detail" },
    h(
      "div",
      { class: "note", style: { background: cssColor(note.color) } },
      h(
        "div",
        { class: "note-toolbar" },
        h("button", { class: "ghost back", title: "Back to notes", onclick: () => openNote(null) }, "←"),
        title,
        h(
          "button",
          {
            class: `ghost${note.isPinned ? " on" : ""}`,
            title: note.isPinned ? "Unpin" : "Pin to top of desktop",
            onclick: () => {
              note.isPinned = !note.isPinned;
              s.touch(note);
              render();
            },
          },
          "📌",
        ),
        h(
          "button",
          { class: "ghost", title: "Color", onclick: () => (colorMenu.hidden = !colorMenu.hidden) },
          "🎨",
        ),
        h(
          "button",
          {
            class: "ghost",
            title: "Duplicate note",
            onclick: async () => openNote((await s.duplicateNote(note)).id),
          },
          "⧉",
        ),
        h(
          "button",
          {
            class: "ghost danger",
            title: "Delete note",
            onclick: async () => {
              if (!confirm(`Delete "${note.title}" and all its items? This also removes it from your other devices.`))
                return;
              if (await s.deleteNote(note)) openNote(null);
              else alert(`Couldn't delete the note: ${s.lastError}`);
            },
          },
          "🗑",
        ),
      ),
      colorMenu,
      itemsEl,
      h(
        "div",
        { class: "add-row" },
        newItem,
        h(
          "button",
          {
            class: "add",
            title: "Add item",
            onclick: () => newItem.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter" })),
          },
          "+",
        ),
      ),
      h("div", { class: "note-footer" }, footerText, clearBtn),
    ),
  );
  rerenderItems();
  return panel;
}

function renderItem(note: Note, item: TodoItem, index: number, rerender: (focusId?: string) => void) {
  const s = store!;

  const text = h("textarea", {
    rows: 1,
    value: item.text,
    oninput: (e: Event) => {
      const t = e.target as HTMLTextAreaElement;
      autoGrow(t);
      s.updateItem(note, item, { text: t.value });
    },
    onkeydown: (e: KeyboardEvent) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        const added = s.addItem(note, "", index + 1);
        rerender(added.id);
      }
    },
  });

  const overdue =
    item.dueDate && !item.isCompleted && item.dueDate.getTime() <= Date.now() && !item.isAlarmDismissed;

  const row = h(
    "li",
    {
      class: [
        "item",
        item.isCompleted && "done",
        item.isHeading && "heading",
        item.isImportant && "important",
      ]
        .filter(Boolean)
        .join(" "),
      "data-id": item.id,
      ondragover: (e: DragEvent) => {
        if (dragItemIndex >= 0) e.preventDefault();
      },
      ondrop: (e: DragEvent) => {
        e.preventDefault();
        if (dragItemIndex >= 0) {
          s.moveItem(note, dragItemIndex, index);
          rerender();
        }
        dragItemIndex = -1;
      },
    },
    h("span", {
      class: "handle",
      title: "Drag to reorder",
      draggable: "true",
      ondragstart: (e: DragEvent) => {
        dragItemIndex = index;
        e.dataTransfer?.setData("text/plain", item.id);
        e.dataTransfer?.setDragImage(row, 10, 10);
      },
    }, "⋮⋮"),
    item.isHeading
      ? h("span", { class: "check-spacer" })
      : h("input", {
          type: "checkbox",
          checked: item.isCompleted,
          onchange: (e: Event) => {
            s.updateItem(note, item, { isCompleted: (e.target as HTMLInputElement).checked });
            row.classList.toggle("done", item.isCompleted);
          },
        }),
    h(
      "div",
      { class: "item-body" },
      text,
      item.dueDate || item.textAttachment
        ? h(
            "div",
            { class: "item-meta" },
            item.dueDate
              ? h("span", { class: `due${overdue ? " overdue" : ""}` }, `📅 ${formatDue(item.dueDate)}`)
              : null,
            item.textAttachment
              ? h(
                  "button",
                  { class: "link", onclick: () => editAttachment(note, item, rerender) },
                  "📎 note",
                )
              : null,
          )
        : null,
    ),
    h(
      "div",
      { class: "item-actions" },
      h(
        "button",
        {
          class: `ghost${item.isImportant ? " on" : ""}`,
          title: "Important",
          onclick: () => {
            s.updateItem(note, item, { isImportant: !item.isImportant });
            rerender();
          },
        },
        "🚨",
      ),
      h(
        "button",
        {
          class: `ghost${item.isHeading ? " on" : ""}`,
          title: "Heading",
          onclick: () => {
            s.updateItem(note, item, { isHeading: !item.isHeading });
            rerender();
          },
        },
        "H",
      ),
      h(
        "button",
        { class: `ghost${item.dueDate ? " on" : ""}`, title: "Due date / alarm", onclick: () => editDue(note, item, rerender) },
        "📅",
      ),
      h(
        "button",
        {
          class: `ghost${item.textAttachment ? " on" : ""}`,
          title: "Text attachment",
          onclick: () => editAttachment(note, item, rerender),
        },
        "📎",
      ),
      h(
        "button",
        {
          class: "ghost danger",
          title: "Delete item",
          onclick: async () => {
            row.classList.add("removing");
            if (await s.deleteItem(note, item)) rerender();
            else {
              row.classList.remove("removing");
              alert(`Couldn't delete the item: ${s.lastError}`);
            }
          },
        },
        "✕",
      ),
    ),
    h(
      "button",
      { class: "ghost more", title: "More", onclick: () => row.classList.toggle("open") },
      "⋯",
    ),
  );
  return row;
}

// ---------- dialogs ----------

function dialog(titleText: string, content: Node, actions: { label: string; cls?: string; run: () => boolean | void | Promise<boolean | void> }[]) {
  const dlg = h("dialog", { class: "dlg" });
  const close = () => {
    dlg.close();
    dlg.remove();
  };
  dlg.append(
    h("h2", {}, titleText),
    content,
    h(
      "div",
      { class: "dlg-actions" },
      ...actions.map((a) =>
        h(
          "button",
          {
            class: a.cls ?? "",
            onclick: async () => {
              if ((await a.run()) !== false) close();
            },
          },
          a.label,
        ),
      ),
    ),
  );
  dlg.addEventListener("cancel", close);
  document.body.append(dlg);
  dlg.showModal();
  return dlg;
}

function editDue(note: Note, item: TodoItem, rerender: () => void) {
  const input = h("input", {
    type: "datetime-local",
    value: item.dueDate ? toLocalInput(item.dueDate) : toLocalInput(new Date(Date.now() + 3600_000)),
  });
  dialog("Due date", h("label", { class: "field" }, "Remind me at", input), [
    {
      label: "Clear",
      run: () => {
        store!.updateItem(note, item, { dueDate: null, isAlarmDismissed: false, snoozeUntil: null });
        rerender();
      },
    },
    { label: "Cancel", run: () => {} },
    {
      label: "Save",
      cls: "primary",
      run: () => {
        if (!input.value) return false;
        store!.updateItem(note, item, {
          dueDate: new Date(input.value),
          isAlarmDismissed: false,
          snoozeUntil: null,
        });
        rerender();
      },
    },
  ]);
}

function editAttachment(note: Note, item: TodoItem, rerender: () => void) {
  const ta = h("textarea", { class: "attachment", rows: 10, value: item.textAttachment });
  dialog(`Attachment — ${item.text || "item"}`, ta, [
    {
      label: "Remove",
      cls: "danger",
      run: () => {
        store!.updateItem(note, item, { textAttachment: "" });
        rerender();
      },
    },
    { label: "Cancel", run: () => {} },
    {
      label: "Save",
      cls: "primary",
      run: () => {
        store!.updateItem(note, item, { textAttachment: ta.value });
        rerender();
      },
    },
  ]);
  requestAnimationFrame(() => ta.focus());
}

function openSettings() {
  const url = h("input", { type: "url", value: settings.supabaseUrl, placeholder: "https://xxxx.supabase.co" });
  const key = h("input", { type: "password", value: settings.supabaseApiKey, autocomplete: "off" });
  const msg = h("p", { class: "muted small" });
  const notifyBtn =
    "Notification" in window && Notification.permission !== "granted"
      ? h(
          "button",
          {
            onclick: async () => {
              const p = await Notification.requestPermission();
              notifyBtn!.textContent = p === "granted" ? "Notifications on" : "Notifications blocked";
            },
          },
          "Enable due-date notifications",
        )
      : null;

  const content = h(
    "div",
    { class: "form" },
    h("label", { class: "field" }, "Supabase URL", url),
    h("label", { class: "field" }, "Supabase API key", key),
    h(
      "p",
      { class: "muted small" },
      "Use the same URL and key as in the desktop app's Settings. They're stored only in this browser.",
    ),
    notifyBtn,
    msg,
  );

  dialog("Settings", content, [
    { label: "Cancel", run: () => {} },
    {
      label: "Save & connect",
      cls: "primary",
      run: async () => {
        const next: Settings = { supabaseUrl: url.value.trim(), supabaseApiKey: key.value.trim() };
        if (!next.supabaseUrl || !next.supabaseApiKey) {
          msg.textContent = "Both fields are required.";
          return false;
        }
        msg.textContent = "Connecting…";
        try {
          await new Api(next).fetchAll();
        } catch (e) {
          msg.textContent = `Couldn't connect: ${(e as Error).message}`;
          return false;
        }
        saveSettings(next);
        settings = next;
        await connect();
      },
    },
  ]);
}

// ---------- alarms (mirrors AlarmService rules) ----------

const notified = new Set<string>();

function dueAlarms(): { note: Note; item: TodoItem }[] {
  if (!store) return [];
  const now = Date.now();
  const out: { note: Note; item: TodoItem }[] = [];
  for (const note of store.notes)
    for (const item of note.items) {
      if (!item.dueDate || item.isCompleted || item.isAlarmDismissed) continue;
      if (item.dueDate.getTime() > now) continue;
      if (item.snoozeUntil && item.snoozeUntil.getTime() > now) continue;
      out.push({ note, item });
    }
  return out;
}

function checkAlarms() {
  const alarms = dueAlarms();
  trayEl.replaceChildren(
    ...alarms.map(({ note, item }) =>
      h(
        "div",
        { class: "alarm" },
        h("div", {}, h("strong", {}, item.text || "(untitled)"), h("div", { class: "muted small" }, `${note.title} · due ${formatDue(item.dueDate!)}`)),
        h("button", { onclick: () => openNote(note.id) }, "Open"),
        h(
          "button",
          {
            onclick: () => {
              store!.updateItem(note, item, { snoozeUntil: new Date(Date.now() + 10 * 60_000) });
              checkAlarms();
            },
          },
          "Snooze 10m",
        ),
        h(
          "button",
          {
            class: "primary",
            onclick: () => {
              store!.updateItem(note, item, { isAlarmDismissed: true });
              checkAlarms();
              render();
            },
          },
          "Dismiss",
        ),
      ),
    ),
  );
  if ("Notification" in window && Notification.permission === "granted") {
    for (const { note, item } of alarms) {
      const key = `${item.id}:${item.dueDate!.getTime()}:${item.snoozeUntil?.getTime() ?? 0}`;
      if (notified.has(key)) continue;
      notified.add(key);
      new Notification(item.text || "Easy2Do reminder", { body: note.title, tag: item.id, icon: "/icon.png" });
    }
  }
}

// ---------- sync loop ----------

function editingNow() {
  const a = document.activeElement;
  return !!a && (a.tagName === "TEXTAREA" || a.tagName === "INPUT") || !!document.querySelector("dialog[open]");
}

async function manualRefresh() {
  if (!store) return;
  await store.flushAll();
  if (await store.refresh()) render();
  renderStatus();
}

async function connect() {
  clearInterval(refreshTimer);
  store = new Store(settings);
  store.subscribe(refreshChrome);
  render();
  try {
    await store.load();
  } catch {
    /* status shows the error */
  }
  render();
  checkAlarms();
  refreshTimer = window.setInterval(async () => {
    if (!store || document.hidden) return;
    if (!editingNow() && (await store.refresh())) render();
    checkAlarms();
  }, REFRESH_INTERVAL_MS);
}

document.addEventListener("visibilitychange", () => {
  if (!store) return;
  if (document.hidden) void store.flushAll();
  else if (!editingNow()) void store.refresh().then((c) => c && render());
});
window.addEventListener("pagehide", () => void store?.flushAll());
window.addEventListener("beforeunload", (e) => {
  if (store?.hasPendingWork) {
    void store.flushAll();
    e.preventDefault();
  }
});

if (isConfigured(settings)) void connect();
else render();
