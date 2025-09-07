(function(){
  var form = document.getElementById('gpip-search-form');
  if (!form) return;
  var KEY='index-form-state-v1';

  function save(){ try{ sessionStorage.setItem(KEY, JSON.stringify(Object.fromEntries(new FormData(form)))); }catch(e){} }
  function load(){ try{ var raw=sessionStorage.getItem(KEY); if(!raw) return; var data=JSON.parse(raw)||{}; Object.keys(data).forEach(function(k){ var el=form.querySelector('[name="'+k+'"]'); if(!el) return; if(el.type==='checkbox') el.checked=!!data[k]; else el.value=data[k]; }); }catch(e){} }
  load();

  form.addEventListener('input', save);
  form.addEventListener('change', save);
  form.addEventListener('submit', function(ev){
    try{
      var sub = ev.submitter;
      var fa = sub && sub.getAttribute && sub.getAttribute('formaction');
      var isClear = false;
      if (fa){ var u=fa.toLowerCase(); isClear = u.indexOf('handler=clear')>=0 || u.indexOf('handler='+ encodeURIComponent('Clear').toLowerCase())>=0; }
      if (isClear) sessionStorage.removeItem(KEY); else save();
    }catch(e){ console.debug('Index: submit handler failed', e); }
  });
})();
