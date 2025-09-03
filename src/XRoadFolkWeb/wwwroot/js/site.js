// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Minimal bootstrap: force light theme
(() => {
  const log = (...args) => {
    if (typeof console !== 'undefined' && typeof console.debug === 'function') {
      console.debug(...args);
    }
  };

  try {
    localStorage.removeItem('theme');
  } catch (e) {
    log('site.js: localStorage.removeItem failed', e);
  }

  try {
    document.documentElement.setAttribute('data-bs-theme', 'brand');
  } catch (e) {
    log('site.js: setAttribute failed', e);
  }
})();
