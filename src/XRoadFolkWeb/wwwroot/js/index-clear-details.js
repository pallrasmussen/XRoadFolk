(function(){
  'use strict';
  const dbg=(m,e)=>{ try{ if(e&&console&&console.debug) console.debug(m,e);}catch(_){} };
  const form = document.getElementById('gpip-search-form');
  if (!form) return;
  form.addEventListener('submit', () => {
    try{
      const sec = document.getElementById('person-details-section');
      const body = document.getElementById('person-details-body');
      const err = document.getElementById('person-details-error');
      const loading = document.getElementById('person-details-loading');
      if (err) { err.classList.add('d-none'); err.textContent=''; }
      if (loading) loading.classList.remove('d-none');
      if (body) { body.innerHTML=''; }
      if (sec) { sec.classList.remove('d-none'); }
      window.lastPid = null;
      if (window.personCache && window.personCache.clear) window.personCache.clear();
      try{ const url = new URL(window.location.href); url.searchParams.delete('publicId'); url.searchParams.delete('gpivPublicId'); history.replaceState(null,'',url.toString()); }catch(e){ dbg('index-clear:history', e); }
    }catch(e){ dbg('index-clear:submit', e); }
  });
})();
