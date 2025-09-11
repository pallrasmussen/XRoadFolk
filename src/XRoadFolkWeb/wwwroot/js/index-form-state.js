(function(){
  'use strict';
  const dbg=(m,e)=>{ try{ if(e&&console&&console.debug) console.debug(m,e);}catch(_){} };
  const form = document.getElementById('gpip-search-form');
  if (!form) return;
  const KEY='index-form-state-v1';

  function save(){ try{ sessionStorage.setItem(KEY, JSON.stringify(Object.fromEntries(new FormData(form)))); }catch(e){ dbg('idx-state:save', e); } }
  function load(){ try{ const raw=sessionStorage.getItem(KEY); if(!raw) return; const data=JSON.parse(raw)||{}; Object.keys(data).forEach(k=>{ const el=form.querySelector('[name="'+k+'"]'); if(!el) return; if(el.type==='checkbox') el.checked=!!data[k]; else el.value=data[k]; }); }catch(e){ dbg('idx-state:load', e); } }
  load();

  form.addEventListener('input', save);
  form.addEventListener('change', save);
  form.addEventListener('submit', ev => {
    try{
      const sub = ev.submitter;
      const fa = sub && sub.getAttribute && sub.getAttribute('formaction');
      let isClear = false;
      if (fa){ const u=fa.toLowerCase(); isClear = u.indexOf('handler=clear')>=0 || u.indexOf('handler='+ encodeURIComponent('Clear').toLowerCase())>=0; }
      if (isClear) sessionStorage.removeItem(KEY); else save();
    }catch(e){ dbg('idx-state:submit', e); }
  });
})();
