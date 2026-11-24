// Connection settings live only in this browser's localStorage.
// The API key is never built into the bundle.

const KEY = "easy2do.settings";

export interface Settings {
  supabaseUrl: string;
  supabaseApiKey: string;
}

const DEFAULTS: Settings = {
  supabaseUrl: import.meta.env.VITE_SUPABASE_URL || "https://sitbwplgthilakgspfab.supabase.co",
  supabaseApiKey: "",
};

export function loadSettings(): Settings {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return { ...DEFAULTS, ...JSON.parse(raw) };
  } catch {
    /* fall through to defaults */
  }
  return { ...DEFAULTS };
}

export function saveSettings(s: Settings): void {
  localStorage.setItem(
    KEY,
    JSON.stringify({ supabaseUrl: s.supabaseUrl.trim(), supabaseApiKey: s.supabaseApiKey.trim() }),
  );
}

export function isConfigured(s: Settings): boolean {
  return !!s.supabaseUrl.trim() && !!s.supabaseApiKey.trim();
}
