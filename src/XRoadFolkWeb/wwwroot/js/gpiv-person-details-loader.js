// Restore PublicId click -> load PersonDetails panel behavior
(function(){
  if (window.__gpivPidHooked) return;
  window.__gpivPidHooked = true;

  var personCache = (window.personCache instanceof Map) ? window.personCache : (window.personCache = new Map());
  var lastPid = window.lastPid || null;

  function getPanelEls() {
    return {
      sec: document.getElementById('person-details-section'),
      body: document.getElementById('person-details-body'),
      err: document.getElementById('person-details-error'),
      loading: document.getElementById('person-details-loading'),
      title: document.querySelector('#person-details-section .card-title')
    };
  }

  function activateDetailsTab(){
    try{
      var btn = document.getElementById('gpiv-tab-details-btn');
      var pane = document.getElementById('gpiv-tab-details');
      if (!btn || !pane) return;
      // Deactivate all
      document.querySelectorAll('#gpiv-tabs button[data-bs-target]').forEach(function(b){
        b.classList.remove('active');
        b.setAttribute('aria-selected','false');
      });
      document.querySelectorAll('#gpiv-tabs-content .tab-pane').forEach(function(p){
        p.classList.remove('show','active');
      });
      // Activate details
      btn.classList.add('active');
      btn.setAttribute('aria-selected','true');
      pane.classList.add('show','active');
    }catch(e){ try{ console.debug('GPIV: activateDetailsTab failed', e); }catch{} }
  }

  function showLoading(on){
    var e=getPanelEls(); if(!e.sec) return;
    activateDetailsTab();
    e.sec.classList.remove('d-none');
    if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; }
    if(e.loading){ e.loading.classList.toggle('d-none', !on); }
  }
  function clearPanel(){
    var e=getPanelEls(); if(!e.sec) return;
    activateDetailsTab();
    e.sec.classList.remove('d-none');
    if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; }
    if(e.body){ e.body.innerHTML=''; }
    if(e.loading){ e.loading.classList.add('d-none'); }
  }
  function ensureShownAndFocus(){
    var e=getPanelEls(); if(!e.sec) return;
    activateDetailsTab();
    e.sec.classList.remove('d-none');
    if(e.title){
      e.title.setAttribute('tabindex','-1');
      try{ e.title.focus({preventScroll:true}); }catch(ex){ try{ console.debug('GPIV: focus failed', ex); }catch{} }
      try{ e.title.scrollIntoView({block:'start', behavior:'smooth'});}catch(ex2){ try{ console.debug('GPIV: scrollIntoView failed', ex2); }catch{} }
    }
  }
  function markSelectedSummaryCard(fromEl){
    try {
      var host = document.getElementById('gpiv-xml-summary'); if (!host) return;
      var prev = host.querySelector('.gpiv-active-card'); if (prev) prev.classList.remove('gpiv-active-card');
      var wrap = fromEl;
      while (wrap && wrap !== host && !(wrap.classList && wrap.classList.contains('border') && wrap.classList.contains('rounded'))) wrap = wrap.parentElement;
      if (wrap && wrap !== host) wrap.classList.add('gpiv-active-card');
    } catch (e) { try{ console.debug('GPIV: markSelectedSummaryCard failed', e); }catch{} }
  }

  function findFirstPersonId(){
    try {
      var host = document.getElementById('gpiv-xml-summary');
      if (!host) return '';
      var el = host.querySelector('[data-public-id], [data-publicid]');
      if (!el) return '';
      return el.getAttribute('data-public-id') || el.getAttribute('data-publicid') || (el.dataset ? (el.dataset.publicId || el.dataset.publicid || '') : '');
    } catch { return ''; }
  }

  function waitForSummaryThenLoadOnce(){
    try {
      var host = document.getElementById('gpiv-xml-summary');
      if (!host || !window.MutationObserver) return;
      var mo = new MutationObserver(function(){
        var pid = findFirstPersonId();
        if (pid) { try { mo.disconnect(); } catch {} loadPerson(pid); }
      });
      mo.observe(host, { childList: true, subtree: true });
    } catch {}
  }

  function ensureDefaultPersonLoaded(){
    var els = getPanelEls(); if(!els.body) return;
    // If content already present, ensure visible and return
    if (els.body.children && els.body.children.length > 0) { ensureShownAndFocus(); return; }
    var pid = window.lastPid || findFirstPersonId();
    if (pid) { loadPerson(pid); }
    else { waitForSummaryThenLoadOnce(); }
  }

  function defaultRenderPairsGrouped(pairs){
    function iconClassForPd(name){
      var t = String(name || '').toLowerCase();
      if (t === 'summary') return 'bi-list-check';
      if (t === 'person' || t === 'names' || t === 'name') return 'bi-person-lines-fill';
      if (t === 'biologicalparents' || t === 'parents' || t.includes('parent') || t.includes('guardian') || t.includes('family')) return 'bi-people-fill';
      if (t.includes('basic') || t.includes('personal') || t === 'basics' || t.includes('overview') || t.includes('core')) return 'bi-person-vcard';
      if (t.includes('address') || t.includes('resid') || t.includes('domic') || t.includes('location') || t.includes('geo') || t.includes('place')) return 'bi-geo-alt';
      if (t.includes('status') || t.includes('civilstatus') || t.includes('marital')) return 'bi-patch-check';
      if (t.includes('employment') || t.includes('job') || t.includes('work') || t.includes('occupation') || t.includes('employer')) return 'bi-briefcase';
      if (t.includes('education') || t.includes('school') || t.includes('study') || t.includes('degree')) return 'bi-mortarboard';
      if (t.includes('document') || t.includes('certificate') || t.includes('doc') || t.includes('file') || t.includes('paper')) return 'bi-file-earmark-text';
      if (t.includes('bank') || t.includes('account') || t.includes('finance') || t.includes('iban') || t.includes('bic') || t.includes('payment')) return 'bi-wallet2';
      if (t.includes('email') || t.includes('mail')) return 'bi-envelope-at';
      if (t.includes('contact') || t.includes('phone') || t.includes('mobile') || t.includes('tel') || t.includes('fax')) return 'bi-telephone';
      if (t.includes('id') || t.includes('ident') || t.includes('passport') || t.includes('card') || t.includes('vcard')) return 'bi-person-vcard';
      if (t.includes('date') || t.includes('period') || t.includes('valid')) return 'bi-calendar-event';
      if (t.includes('nation') || t.includes('citizen') || t.includes('nationality') || t.includes('country')) return 'bi-flag';
      return 'bi-list-ul';
    }

    var groups = {};
    (pairs||[]).forEach(function(p){
      var k=p.key||'', v=p.value||'';
      var seg=k.includes('.') ? k.slice(0,k.indexOf('.')) : k;
      (groups[seg]=groups[seg]||[]).push({k:k,v:v});
    });
    var keys = Object.keys(groups).sort(function(a,b){
      var ai=a.toLowerCase()==='summary'?0:1, bi=b.toLowerCase()==='summary'?0:1;
      return (ai-bi)||a.localeCompare(b);
    });
    var accId='pd-acc-'+Date.now();
    var acc=document.createElement('div'); acc.className='accordion'; acc.id=accId;

    keys.forEach(function(name, gi){
      var items=groups[name].slice().sort(function(x,y){ return x.k.localeCompare(y.k); });
      var hid=accId+'-h-'+gi, cid=accId+'-c-'+gi;

      var item=document.createElement('div'); item.className='accordion-item'; item.setAttribute('data-group', name);

      var h2=document.createElement('h2'); h2.className='accordion-header'; h2.id=hid;
      var btn=document.createElement('button'); btn.className='accordion-button'+(name.toLowerCase()==='summary'?'':' collapsed');
      btn.type='button'; btn.setAttribute('data-bs-toggle','collapse'); btn.setAttribute('data-bs-target','#'+cid);
      btn.setAttribute('aria-expanded', name.toLowerCase()==='summary'?'true':'false'); btn.setAttribute('aria-controls', cid);
      var ic = document.createElement('i'); ic.className='bi '+iconClassForPd(name)+' me-2'; ic.setAttribute('aria-hidden','true');
      btn.appendChild(ic); btn.appendChild(document.createTextNode(name));
      h2.appendChild(btn);

      var col=document.createElement('div'); col.id=cid; col.className='accordion-collapse collapse'+(name.toLowerCase()==='summary'?' show':'').trim();
      col.setAttribute('aria-labelledby', hid); col.setAttribute('data-bs-parent','#'+accId);

      var body=document.createElement('div'); body.className='accordion-body p-0';
      var respWrap=document.createElement('div'); respWrap.className='table-responsive';
      var table=document.createElement('table'); table.className='table table-sm table-striped align-middle mb-0';
      var tb=document.createElement('tbody');

      items.forEach(function(it){
        var k=it.k, v=it.v;
        var lastDot=k.lastIndexOf('.'); var sub=lastDot>=0?k.slice(lastDot+1):k;
        var bpos=sub.indexOf('['); if(bpos>=0) sub=sub.slice(0,bpos);
        var tr=document.createElement('tr');
        var th=document.createElement('th'); th.className='text-muted fw-normal'; th.style.width='36%'; th.textContent=sub;
        var td=document.createElement('td'); td.textContent=v;
        tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr);
      });

      table.appendChild(tb); respWrap.appendChild(table); body.appendChild(respWrap); col.appendChild(body); item.appendChild(h2); item.appendChild(col); acc.appendChild(item);
    });
    return acc;
  }

  async function fetchDetails(publicId){
    var url = new URL(window.location.href);
    url.searchParams.set('handler','PersonDetails');
    url.searchParams.set('publicId', publicId);
    var resp = await fetch(url.toString(), { headers: { 'Accept':'application/json' } });
    var ct = (resp.headers.get('content-type') || '').toLowerCase();
    var data = null;
    if (ct.includes('application/json')) data = await resp.json();
    else { var text = await resp.text(); data = { ok: false, error: text && text.length < 500 ? text : 'Non-JSON response (' + resp.status + ')' }; }
    if (!resp.ok) data.ok = false;
    return data;
  }

  async function loadPerson(publicId, sourceEl){
    window.lastPid = lastPid = publicId;
    var els = getPanelEls();
    if(!publicId || !els.body) return;

    var cached = personCache.get(publicId);
    if (cached && cached.details && cached.details.length) {
      clearPanel();
      var builder = (window._renderPairsGroupedForPerson || defaultRenderPairsGrouped);
      var accCached = builder(cached.details);
      if (accCached) els.body.appendChild(accCached);
      ensureShownAndFocus();
    } else {
      clearPanel(); showLoading(true);
    }

    try {
      var data = await fetchDetails(publicId);
      showLoading(false);

      if (!data || data.ok !== true) {
        if (els.err) {
          els.err.innerHTML = 'Failed to load details. <button type="button" id="pd-retry" class="btn btn-sm btn-outline-secondary ms-2">Retry</button>';
          els.err.classList.remove('d-none');
          var retry = document.getElementById('pd-retry');
          retry && retry.addEventListener('click', function(){ loadPerson(publicId, sourceEl); });
          ensureShownAndFocus();
        }
        return;
      }

      if (!data.details || !data.details.length) {
        clearPanel();
        return;
      }

      personCache.set(publicId, { details: data.details || [], ts: Date.now() });

      clearPanel();
      var builder2 = (window._renderPairsGroupedForPerson || defaultRenderPairsGrouped);
      var acc = builder2(data.details || []);
      if (acc) els.body.appendChild(acc);
      ensureShownAndFocus();

      if (sourceEl) markSelectedSummaryCard(sourceEl);

      try{
        var link = new URL(window.location.href);
        link.searchParams.set('gpivPublicId', publicId);
        history.pushState(null,'',link.toString());
      }catch(ex){ try{ console.debug('GPIV: history.pushState failed', ex); }catch{} }
    } catch (err) {
      showLoading(false);
      if (els.err) {
        els.err.innerHTML = 'Failed to load details. <button type="button" id="pd-retry" class="btn btn-sm btn-outline-secondary ms-2">Retry</button>';
        els.err.classList.remove('d-none');
        var retry2 = document.getElementById('pd-retry');
        retry2 && retry2.addEventListener('click', function(){ loadPerson(publicId, sourceEl); });
        ensureShownAndFocus();
      }
      try { console.error('GetPerson fetch failed', err); } catch {}
    }
  }

  // New: click on person header/card to open details
  document.addEventListener('click', function(e){
    var header = e.target && e.target.closest && e.target.closest('.gpiv-person-header');
    if(!header) return;
    if(e.ctrlKey || e.metaKey) return;
    var wrap = header.parentElement; // .gpiv-person
    if(!wrap) return;
    var pid = wrap.getAttribute('data-public-id') || (wrap.dataset ? wrap.dataset.publicId : '') || '';
    if(pid) loadPerson(pid, header);
  });

  document.addEventListener('keydown', function(e){
    if (e.key !== 'Enter' && e.key !== ' ') return;
    var header = e.target && e.target.closest && e.target.closest('.gpiv-person-header');
    if(!header) return;
    e.preventDefault();
    var wrap = header.parentElement;
    if(!wrap) return;
    var pid = wrap.getAttribute('data-public-id') || (wrap.dataset ? wrap.dataset.publicId : '') || '';
    if(pid) loadPerson(pid, header);
  });

  // Populate details when Details tab is clicked, even without selecting a person
  document.addEventListener('click', function(e){
    var tabBtn = e.target && e.target.closest && e.target.closest('#gpiv-tab-details-btn');
    if (!tabBtn) return;
    // Defer to allow Bootstrap (if present) to toggle classes first
    setTimeout(ensureDefaultPersonLoaded, 0);
  });

  // Fullscreen toggle for Person Details panel
  document.addEventListener('click', function(e){
    var fsBtn = e.target && e.target.closest && e.target.closest('#pd-fullscreen');
    if (!fsBtn) return;
    var sec = document.getElementById('person-details-section');
    if (!sec) return;

    // Always drop viewer fullscreen before toggling PD fullscreen
    try { var viewer = document.querySelector('.gpiv-card'); if (viewer) viewer.classList.remove('gpiv-fullscreen'); } catch {}

    try {
      var isApiFull = document.fullscreenElement === sec;
      var hasCssFull = sec.classList.contains('pd-fullscreen');
      if (isApiFull) {
        if (document.exitFullscreen) {
          document.exitFullscreen().catch(function(){ sec.classList.remove('pd-fullscreen'); });
        } else {
          sec.classList.remove('pd-fullscreen');
        }
        return;
      }
      if (hasCssFull) {
        sec.classList.remove('pd-fullscreen');
        return;
      }

      function enterFs(){
        if (sec.requestFullscreen) {
          sec.requestFullscreen({ navigationUI: 'hide' }).catch(function(){ sec.classList.add('pd-fullscreen'); });
        } else {
          sec.classList.add('pd-fullscreen');
        }
      }

      if (document.fullscreenElement && document.fullscreenElement !== sec) {
        if (document.exitFullscreen) {
          document.exitFullscreen().then(function(){ enterFs(); }).catch(function(){ sec.classList.add('pd-fullscreen'); });
        } else {
          sec.classList.add('pd-fullscreen');
        }
      } else {
        enterFs();
      }
    } catch (err) {
      // Fallback toggle
      sec.classList.toggle('pd-fullscreen');
    }
  });

  // Sync CSS fallback with Fullscreen API state
  document.addEventListener('fullscreenchange', function(){
    var sec = document.getElementById('person-details-section');
    if (!sec) return;
    var isFs = document.fullscreenElement === sec;
    sec.classList.toggle('pd-fullscreen', isFs);
  });

  // Deep-link handler remains
  try{
    var qs=new URLSearchParams(window.location.search);
    var pid=qs.get('gpivPublicId');
    if(pid){
      if(document.readyState==='loading') {
        window.addEventListener('DOMContentLoaded', function(){ loadPerson(pid); });
      } else {
        loadPerson(pid);
      }
    }
  }catch(e){ try{ console.debug('GPIV: initial gpivPublicId load failed', e); }catch{} }

  function pdToggleAll(open){
    try{
      var host = document.getElementById('person-details-section');
      if (!host) return;
      var panes = host.querySelectorAll('.accordion-collapse');
      panes.forEach(function(p){ p.classList.toggle('show', !!open); });
      var btns = host.querySelectorAll('.accordion-button');
      btns.forEach(function(b){ b.classList.toggle('collapsed', !open); });
    }catch(e){ try{ console.debug('GPIV: pdToggleAll failed', e); }catch{} }
  }
  document.addEventListener('click', function(e){
    var t = e.target && e.target.id || '';
    if (t === 'pd-expand-all') pdToggleAll(true);
    if (t === 'pd-collapse-all') pdToggleAll(false);
  });
})();
