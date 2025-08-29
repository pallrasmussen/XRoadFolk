// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// App bootstrap (with theme toggle)
(() => {
  const key = 'theme';
  const light = 'brand';
  const dark = 'brand-dark';

  const setTheme = (theme, persist = true) => {
    document.documentElement.setAttribute('data-bs-theme', theme);
    if (persist) {
      try { localStorage.setItem(key, theme); } catch {}
    }
    updateToggle(theme);
  };

  const updateToggle = (theme) => {
    const btn = document.getElementById('theme-toggle');
    if (!btn) return;
    const icon = btn.querySelector('.theme-icon');
    const isDark = theme === dark;
    btn.setAttribute('aria-pressed', String(isDark));
    if (icon) icon.textContent = isDark ? '🌙' : '☀️';
  };

  document.addEventListener('DOMContentLoaded', () => {
    const btn = document.getElementById('theme-toggle');
    if (btn) {
      btn.addEventListener('click', () => {
        const current = document.documentElement.getAttribute('data-bs-theme') || light;
        const next = current === dark ? light : dark;
        setTheme(next, true); // user action persists
      });
      updateToggle(document.documentElement.getAttribute('data-bs-theme') || light);
    }
  });

  // Initialize theme based on saved preference or OS, but don't persist OS-derived value
  (function init() {
    const saved = (() => { try { return localStorage.getItem(key); } catch { return null; } })();
    if (saved) {
      setTheme(saved, true);
    } else {
      const preferDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
      setTheme(preferDark ? dark : light, false);
    }
  })();

  // Keep in sync with OS if no explicit saved preference
  (function bindOsSync() {
    const hasSaved = (() => { try { return !!localStorage.getItem(key); } catch { return false; } })();
    if (!hasSaved && window.matchMedia) {
      const mm = window.matchMedia('(prefers-color-scheme: dark)');
      const handler = (e) => setTheme(e.matches ? dark : light, false);
      try { mm.addEventListener('change', handler); } catch { try { mm.addListener(handler); } catch {} }
    }
  })();
})();
