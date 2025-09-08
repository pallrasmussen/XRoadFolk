// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Global site scripts
// - Culture switcher: auto-submit on change (progressive; noscript fallback exists)

(function(){
  try{
    document.addEventListener('DOMContentLoaded', function(){
      try{
        var sel = document.querySelector('form[action="/set-culture"] select[name="culture"]');
        if (sel) {
          sel.addEventListener('change', function(){ if (sel.form) sel.form.submit(); });
        }
      } catch(e){ /* no-op */ }
    });
  }catch{ /* ignore */ }
})();
