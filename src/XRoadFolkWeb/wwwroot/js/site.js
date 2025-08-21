// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

(() => {
  const key = 'theme';
  const light = 'brand';
  const dark = 'brand-dark';

  const setTheme = (theme) => {
    document.documentElement.setAttribute('data-bs-theme', theme);
    localStorage.setItem(key, theme);
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
        setTheme(current === dark ? light : dark);
      });
      updateToggle(document.documentElement.getAttribute('data-bs-theme') || light);
    }
  });

  // If no saved preference, follow system changes live
  if (!localStorage.getItem(key) && window.matchMedia) {
    const mm = window.matchMedia('(prefers-color-scheme: dark)');
    mm.addEventListener('change', e => setTheme(e.matches ? dark : light));
  }
})();
