// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Global site scripts
// - Culture switcher: auto-submit on change (progressive; noscript fallback exists)
(function(){
  'use strict';
  const dbg=(m,e)=>{ try{ if(e&&console&&console.debug) console.debug(m,e);}catch(_){} };
  try{
    document.addEventListener('DOMContentLoaded', () => {
      try{
        const sel = document.querySelector('form[action="/set-culture"] select[name="culture"]');
        if (sel) {
          sel.addEventListener('change', () => { if (sel.form) sel.form.submit(); });
        }
      } catch(e){ dbg('site:culture-init', e); }
    });
  }catch(e){ dbg('site:init-wrapper', e); }
})();
