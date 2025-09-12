(function(){
  'use strict';
  const key='prefers-dark-mode';
  const cls='dark-mode';
  const html=document.documentElement;
  const btn=document.getElementById('dark-mode-toggle');
  const icon=document.getElementById('dark-mode-icon');
  function setIcon(isDark){
    if(!icon||!btn) return;
    icon.classList.remove('bi-moon-stars','bi-sun');
    if(isDark){ icon.classList.add('bi-sun'); btn.setAttribute('title','Switch to light mode'); }
    else { icon.classList.add('bi-moon-stars'); btn.setAttribute('title','Switch to dark mode'); }
    btn.setAttribute('aria-pressed', isDark?'true':'false');
  }
  function apply(v){ const isDark=(v===true||v==='true'); if(isDark){ html.classList.add(cls);} else { html.classList.remove(cls);} setIcon(isDark); }
  try{ apply(localStorage.getItem(key)); }catch{ setIcon(html.classList.contains(cls)); }
  if(btn){ btn.addEventListener('click', function(){ const enabled=html.classList.toggle(cls); try{ localStorage.setItem(key, enabled?'true':'false'); }catch{} setIcon(enabled); }); }
})();
