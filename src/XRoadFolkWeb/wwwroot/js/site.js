// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Minimal bootstrap: force light theme
(() => {
  try { localStorage.removeItem('theme'); } catch (e) { try { console.debug('site.js: localStorage.removeItem failed', e); } catch {} }
  try { document.documentElement.setAttribute('data-bs-theme', 'brand'); } catch (e) { try { console.debug('site.js: setAttribute failed', e); } catch {} }
})();
