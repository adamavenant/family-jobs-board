import { useEffect, useState } from "react";

export const themeStorageKey = "family-jobs-board-theme";

type Theme = "light" | "dark";

export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(() =>
    document.documentElement.dataset.theme === "dark" ? "dark" : "light",
  );

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  useEffect(() => {
    if (hasSavedTheme() || typeof window.matchMedia !== "function") {
      return;
    }

    const preference = window.matchMedia("(prefers-color-scheme: dark)");
    const followSystemPreference = (event: MediaQueryListEvent) => {
      setTheme(event.matches ? "dark" : "light");
    };
    preference.addEventListener("change", followSystemPreference);
    return () =>
      preference.removeEventListener("change", followSystemPreference);
  }, []);

  const nextTheme = theme === "dark" ? "light" : "dark";

  return (
    <button
      type="button"
      className="theme-toggle"
      aria-pressed={theme === "dark"}
      onClick={() => {
        saveTheme(nextTheme);
        setTheme(nextTheme);
      }}
    >
      <span aria-hidden="true">{theme === "dark" ? "☀" : "☾"}</span>
      {nextTheme === "dark" ? "Dark mode" : "Light mode"}
    </button>
  );
}

function hasSavedTheme() {
  const saved = readSavedTheme();
  return saved === "light" || saved === "dark";
}

function readSavedTheme() {
  try {
    return window.localStorage?.getItem(themeStorageKey) ?? null;
  } catch {
    return null;
  }
}

function saveTheme(theme: Theme) {
  try {
    window.localStorage?.setItem(themeStorageKey, theme);
  } catch {
    // The selected theme still applies when browser storage is unavailable.
  }
}

function applyTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme;
  document
    .querySelector('meta[name="theme-color"]')
    ?.setAttribute("content", theme === "dark" ? "#111915" : "#fbf7ed");
}
