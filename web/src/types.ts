// Mirrors Easy2Do/Models/Note.cs and TodoItem.cs, and the Supabase tables
// `notes`, `note_items` and `note_order` that the desktop app syncs through.

export interface TodoItem {
  id: string;
  text: string;
  isCompleted: boolean;
  isHeading: boolean;
  isImportant: boolean;
  textAttachment: string;
  dueDate: Date | null;
  isAlarmDismissed: boolean;
  snoozeUntil: Date | null;
  createdAtUtc: Date;
  updatedAtUtc: Date;
}

export interface Note {
  id: string;
  title: string;
  /** Avalonia-style #AARRGGBB, kept as-is so desktop clients read it unchanged. */
  color: string;
  createdDate: Date;
  modifiedDate: Date;
  isPinned: boolean;
  /** Desktop window geometry; the web app never changes it but must round-trip it. */
  windowX: number;
  windowY: number;
  windowWidth: number;
  windowHeight: number;
  items: TodoItem[];
}

export const NOTE_COLORS: { name: string; value: string }[] = [
  { name: "Yellow", value: "#FFFFE680" },
  { name: "Red", value: "#FFFF9999" },
  { name: "Green", value: "#FF99FF99" },
  { name: "Blue", value: "#FF99CCFF" },
  { name: "Orange", value: "#FFFFCC99" },
  { name: "Pink", value: "#FFFF99FF" },
  { name: "Gray", value: "#FFCCCCCC" },
  { name: "White", value: "#FFFFFFFF" },
  { name: "Cream", value: "#FFFFF3E0" },
  { name: "Mint", value: "#FFE8F5E9" },
  { name: "Sky", value: "#FFE3F2FD" },
  { name: "Lavender", value: "#FFF3E5F5" },
  { name: "Blush", value: "#FFFCE4EC" },
  { name: "Butter", value: "#FFFFF8E1" },
  { name: "Aqua", value: "#FFE0F7FA" },
];

export const DEFAULT_COLOR = "#FFFFE680";

/** #AARRGGBB (Avalonia) → #RRGGBBAA (CSS). Also accepts #RRGGBB. */
export function cssColor(argb: string): string {
  const hex = (argb || "").trim().replace(/^#/, "");
  if (/^[0-9a-f]{8}$/i.test(hex)) return `#${hex.slice(2)}${hex.slice(0, 2)}`;
  if (/^[0-9a-f]{6}$/i.test(hex)) return `#${hex}`;
  return "#FFE680";
}
