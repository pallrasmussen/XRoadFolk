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
      title: document.querySelector('#person-details-section .card-title'),
      rawPre: document.getElementById('pd-raw-pre'),
      prettyPre: document.getElementById('pd-pretty-pre'),
      copyRaw: document.getElementById('pd-copy-raw'),
      copyPretty: document.getElementById('pd-copy-pretty'),
      dlRaw: document.getElementById('pd-dl-raw'),
      dlPretty: document.getElementById('pd-dl-pretty'),
      pdDetailsBtn: document.getElementById('pd-tab-details-btn'),
      pdRawBtn: document.getElementById('pd-tab-raw-btn'),
      pdPrettyBtn: document.getElementById('pd-tab-pretty-btn'),
      pdDetailsPane: document.getElementById('pd-tab-details'),
      pdRawPane: document.getElementById('pd-tab-raw'),
      pdPrettyPane: document.getElementById('pd-tab-pretty')
    };
  }

  function readI18n(){
    try{
      if (window.__gpivI18nCached) return window.__gpivI18nCached;
      var el = document.getElementById('gpiv-i18n-json');
      var txt = el ? (el.textContent || '') : '';
      var obj = {};
      try { obj = JSON.parse(txt || '{}') || {}; } catch {}
      window.__gpivI18nCached = obj;
      return obj;
    }catch{ return {}; }
  }

  function parseFirstPublicIdFromXmlText(xmlText){
    try{
      if (!xmlText || !xmlText.trim()) return '';
      var parser = new DOMParser();
      var doc = parser.parseFromString(xmlText, 'application/xml');
      var err = doc.querySelector('parsererror'); if (err) return '';
      var all = doc.getElementsByTagName('*');
      var firstPerson = null;
      for (var i=0;i<all.length;i++){ if (all[i].localName === 'PersonPublicInfo') { firstPerson = all[i]; break; } }
      if (!firstPerson){
        for (var j=0;j<all.length;j++){ if (all[j].localName === 'Person') { firstPerson = all[j]; break; } }
      }
      if (!firstPerson) return '';
      var kids = firstPerson.getElementsByTagName('*');
      var pid = '';
      for (var k=0;k<kids.length;k++){ var ln = kids[k].localName; if (ln === 'PublicId' || ln === 'PersonId'){ pid = (kids[k].textContent||'').trim(); if(pid){ break; } } }
      return pid || '';
    }catch{ return ''; }
  }

  function findFirstPersonIdFromEmbeddedXml(){
    try{
      var rawEl = document.getElementById('gpiv-raw-json');
      var prettyEl = document.getElementById('gpiv-pretty-json');
      var raw = rawEl ? JSON.parse(rawEl.textContent||'""') : '';
      var pretty = prettyEl ? JSON.parse(prettyEl.textContent||'""') : '';
      return parseFirstPublicIdFromXmlText(raw || pretty || '');
    }catch{ return ''; }
  }

  function prettify(name){
    var s = String(name || '');
    s = s.replace(/[_\-]+/g,' ');
    s = s.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
    s = s.trim().replace(/\s+/g, ' ');
    return s.split(' ').map(function(w){ return w ? (w[0].toUpperCase() + w.slice(1)) : w; }).join(' ');
  }

  function headerLabelForGroup(rawName){
    var i18n = readI18n();
    var t = String(rawName || '');
    var lower = t.toLowerCase();
    if (lower === 'summary') return i18n.Summary || 'Summary';
    if (lower === 'person' || lower === 'basics') return i18n.Basics || 'Basics';
    if (lower === 'names' || lower === 'name') return i18n.Names || 'Names';
    if (lower === 'addresses' || lower === 'address') return 'Addresses';
    if (lower === 'foreignssns' || lower === 'foreignssn' || (lower.includes('foreign') && lower.includes('ssn'))) return 'Foreign SSNs';
    if (lower === 'publicid') return i18n.PublicId || 'Public Id';
    if (lower === 'dob' || lower === 'dateofbirth') return i18n.DOB || 'Date of Birth';
    if (lower.includes('status')) return i18n.Status || 'Status';
    return prettify(t);
  }

  function pickGroupFromKey(key){
    var raw = String(key || '');
    if (!raw) return '';
    var parts = raw.split('.');
    var segs = [];
    for (var i = 0; i < parts.length; i++) {
      var p = parts[i] || '';
      var b = p.indexOf('[');
      if (b >= 0) p = p.slice(0, b);
      p = p.trim();
      if (p) segs.push(p);
    }
    if (segs.length === 0) return '';
    var idx = 0;
    if (/(?:response|result)$/i.test(segs[0])) idx = 1;
    if (idx >= segs.length) return '';

    var first = segs[idx];
    function norm(seg){
      if (/^address(?:es)?$/i.test(seg)) return 'Addresses';
      if (/^name(?:s)?$/i.test(seg)) return 'Names';
      if (/^foreignssn(?:s)?$/i.test(seg)) return 'ForeignSsns';
      return seg;
    }

    if (/^person$/i.test(first)) {
      if (segs.length >= idx + 3) {
        return norm(segs[idx + 1]);
      }
      return 'Person';
    }
    return norm(first);
  }

  function activateDetailsTab(){
    try{
      var btn = document.getElementById('gpiv-tab-details-btn');
      var pane = document.getElementById('gpiv-tab-details');
      if (!btn || !pane) return;
      document.querySelectorAll('#gpiv-tabs button[data-bs-target]').forEach(function(b){
        b.classList.remove('active');
        b.setAttribute('aria-selected','false');
      });
      document.querySelectorAll('#gpiv-tabs-content .tab-pane').forEach(function(p){
        p.classList.remove('show','active');
      });
      btn.classList.add('active');
      btn.setAttribute('aria-selected','true');
      pane.classList.add('show','active');
    }catch(e){ try{ console.debug('GPIV: activateDetailsTab failed', e); }catch{} }
  }

  function activatePdDetailsSubTab(){
    try{
      var els = getPanelEls();
      if (!els.pdDetailsBtn || !els.pdDetailsPane) return;
      [els.pdDetailsBtn, els.pdRawBtn, els.pdPrettyBtn].forEach(function(b){ if(!b) return; b.classList.remove('active'); b.setAttribute('aria-selected','false'); });
      [els.pdDetailsPane, els.pdRawPane, els.pdPrettyPane].forEach(function(p){ if(!p) return; p.classList.remove('show','active'); });
      els.pdDetailsBtn.classList.add('active');
      els.pdDetailsBtn.setAttribute('aria-selected','true');
      els.pdDetailsPane.classList.add('show','active');
    }catch(e){ try{ console.debug('GPIV: activatePdDetailsSubTab failed', e); }catch{} }
  }

  function showLoading(on){
    var e=getPanelEls(); if(!e.sec) return;
    activateDetailsTab();
    activatePdDetailsSubTab();
    e.sec.classList.remove('d-none');
    if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; }
    if(e.body && on){ e.body.innerHTML=''; }
    if(e.loading){ e.loading.classList.toggle('d-none', !on); }
  }
  function clearPanel(){
    var e=getPanelEls(); if(!e.sec) return;
    activateDetailsTab();
    activatePdDetailsSubTab();
    e.sec.classList.remove('d-none');
    if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; }
    if(e.body){ e.body.innerHTML=''; }
    if(e.loading){ e.loading.classList.add('d-none'); }
  }
  function showInfo(message){
    var e=getPanelEls(); if(!e.sec) return;
    activateDetailsTab();
    activatePdDetailsSubTab();
    e.sec.classList.remove('d-none');
    if(e.loading){ e.loading.classList.add('d-none'); }
    if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; }
    if(e.body){
      var div = document.createElement('div');
      div.className = 'alert alert-info mb-0';
      div.setAttribute('role','status');
      div.textContent = message || 'Select a person to view details.';
      e.body.innerHTML = '';
      e.body.appendChild(div);
    }
  }
  function ensureShownAndFocus(){
    var e=getPanelEls(); if(!e.sec) return;
    activateDetailsTab();
    activatePdDetailsSubTab();
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
      if (el) {
        return el.getAttribute('data-public-id') || el.getAttribute('data-publicid') || (el.dataset ? (el.dataset.publicId || el.dataset.publicid || '') : '');
      }
      return findFirstPersonIdFromEmbeddedXml();
    } catch {
      return '';
    }
  }

  function waitForSummaryThenLoadOnceOrInform(){
    try {
      var host = document.getElementById('gpiv-xml-summary');
      if (!host) {
        var pid0 = findFirstPersonIdFromEmbeddedXml();
        if (pid0) { loadPerson(pid0); }
        else { showInfo('Select a person to view details.'); }
        return;
      }

      // Try immediately if summary is already rendered
      var pidNow = findFirstPersonId();
      if (pidNow) { loadPerson(pidNow); return; }

      if (!window.MutationObserver) { showInfo('Select a person to view details.'); return; }

      var informed = false;
      var mo = new MutationObserver(function(){
        var pid = findFirstPersonId();
        if (pid) { try { mo.disconnect(); } catch {} loadPerson(pid); }
        else if (!informed) { informed = true; try { mo.disconnect(); } catch {} showInfo('Select a person to view details.'); }
      });
      mo.observe(host, { childList: true, subtree: true });
      // Safety timeout in case summary never changes
      setTimeout(function(){ if (!informed){ try{ mo.disconnect(); }catch{} showInfo('Select a person to view details.'); } }, 800);
    } catch { showInfo('Select a person to view details.'); }
  }

  function defaultRenderPairsGrouped(pairs){
    function iconClassForPd(name){
      var t = String(name || '').toLowerCase();
      if (t === 'summary') return 'bi-list-check';
      if (t === 'person' || t === 'basics') return 'bi-person-vcard';
      if (t === 'names' || t === 'name') return 'bi-person-lines-fill';
      if (t === 'addresses' || t === 'address') return 'bi-geo-alt';
      if (t === 'foreignssns' || t === 'foreignssn') return 'bi-passport';
      if (t === 'biologicalparents' || t === 'parents' || t.includes('parent') || t.includes('guardian') || t.includes('family')) return 'bi-people-fill';
      if (t.includes('basic') || t.includes('personal') || t === 'basics' || t.includes('overview') || t.includes('core')) return 'bi-person-vcard';
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

    function stripIndex(s){ var b = s.indexOf('['); return b>=0 ? s.slice(0,b) : s; }
    function parseAddressKey(k){
      var parts = String(k||'').split('.');
      var pos = -1; var idxVal = null;
      for (var i=0;i<parts.length;i++){
        var raw = parts[i]; var seg = stripIndex(raw);
        if (/^addresses?$/i.test(seg)) { pos = i; var m = raw.match(/\[(\d+)\]/); if (m) idxVal = (parseInt(m[1],10) || 0) + 1; break; }
      }
      if (pos < 0) return null;
      if (idxVal == null) idxVal = 1;
      var last = stripIndex(parts[parts.length-1] || '');
      return { index: idxVal, field: last };
    }

    function renderAddressesCardList(items){
      var groups = {};
      (items||[]).forEach(function(it){
        var parsed = parseAddressKey(it.k);
        if (!parsed) return;
        (groups[parsed.index] = groups[parsed.index] || []).push({ field: parsed.field, value: it.v });
      });
      var ids = Object.keys(groups).map(function(x){ return parseInt(x,10)||0; }).sort(function(a,b){ return a-b; });
      if (!ids.length) return null;
      var list = document.createElement('div'); list.className='d-flex flex-column gap-2';
      ids.forEach(function(idx){
        var card = document.createElement('div'); card.className='p-2 border rounded';
        var title = document.createElement('div'); title.className='small text-muted mb-1'; title.textContent = 'Address #' + idx;
        card.appendChild(title);
        var respWrap=document.createElement('div'); respWrap.className='table-responsive';
        var table=document.createElement('table'); table.className='table table-sm table-striped align-middle mb-0';
        var tb=document.createElement('tbody');
        var rows = groups[idx].slice().sort(function(a,b){ return a.field.localeCompare(b.field); });
        rows.forEach(function(r){
          var tr=document.createElement('tr');
          var th=document.createElement('th'); th.className='text-muted fw-normal'; th.style.width='36%'; th.textContent=r.field;
          var td=document.createElement('td'); td.textContent=r.value;
          tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr);
        });
        table.appendChild(tb); respWrap.appendChild(table); card.appendChild(respWrap); list.appendChild(card);
      });
      return list;
    }

    var groups = {};
    (pairs||[]).forEach(function(p){
      var k=p.key||'', v=p.value||'';
      var seg = pickGroupFromKey(k);
      if (/^address(?:es)?$/i.test(seg)) seg = 'Addresses';
      if (/^name(?:s)?$/i.test(seg)) seg = 'Names';
      if (/^foreignssn(?:s)?$/i.test(seg)) seg = 'ForeignSsns';
      (groups[seg]=groups[seg]||[]).push({k:k,v:v});
    });

    // Ensure ForeignSsns accordion is always present, even when empty
    if (!Object.prototype.hasOwnProperty.call(groups, 'ForeignSsns')) {
      groups['ForeignSsns'] = [];
    }

    var keys = Object.keys(groups).sort(function(a,b){
      var al=a.toLowerCase(), bl=b.toLowerCase();
      var ai=al==='summary'?0:(al==='person'?1:2);
      var bi=bl==='summary'?0:(bl==='person'?1:2);
      return (ai-bi)||a.localeCompare(b);
    });
    var accId='pd-acc-'+Date.now();
    var acc=document.createElement('div'); acc.className='accordion'; acc.id=accId;

    keys.forEach(function(name, gi){
      var items=groups[name].slice();
      var hid=accId+'-h-'+gi, cid=accId+'-c-'+gi;

      var item=document.createElement('div'); item.className='accordion-item'; item.setAttribute('data-group', name);

      var h2=document.createElement('h2'); h2.className='accordion-header'; h2.id=hid;
      var open = false; // always start collapsed
      var btn=document.createElement('button'); btn.className='accordion-button' + (open ? '' : ' collapsed');
      btn.type='button'; btn.setAttribute('data-bs-toggle','collapse'); btn.setAttribute('data-bs-target','#'+cid);
      btn.setAttribute('aria-expanded', open ? 'true' : 'false'); btn.setAttribute('aria-controls', cid);
      var ic = document.createElement('i'); ic.className='bi '+iconClassForPd(name)+' me-2'; ic.setAttribute('aria-hidden','true');
      btn.appendChild(ic); btn.appendChild(document.createTextNode(headerLabelForGroup(name)));
      h2.appendChild(btn);

      var col=document.createElement('div'); col.id=cid; col.className='accordion-collapse collapse' + (open ? ' show' : '');
      col.setAttribute('aria-labelledby', hid); col.setAttribute('data-bs-parent','#'+accId);

      var body=document.createElement('div'); body.className='accordion-body p-0';

      var lname = String(name||'').toLowerCase();
      if (lname==='addresses' || lname==='address'){
        var list = renderAddressesCardList(items);
        if (list) body.appendChild(list); else body.appendChild(document.createTextNode(''));
      } else if (lname==='foreignssns' || lname==='foreignssn') {
        if (!items || items.length === 0) {
          var ph = document.createElement('div');
          ph.className = 'text-muted small p-2';
          ph.textContent = (readI18n().NoForeignSsns || 'No foreign SSNs.');
          body.appendChild(ph);
        } else {
          var respWrapF=document.createElement('div'); respWrapF.className='table-responsive';
          var tableF=document.createElement('table'); tableF.className='table table-sm table-striped align-middle mb-0';
          var tbF=document.createElement('tbody');
          items.sort(function(x,y){ return x.k.localeCompare(y.k); }).forEach(function(it){
            var k=it.k, v=it.v;
            var lastDot=k.lastIndexOf('.'); var sub=lastDot>=0?k.slice(lastDot+1):k;
            var bpos=sub.indexOf('['); if(bpos>=0) sub=sub.slice(0,bpos);
            var tr=document.createElement('tr');
            var th=document.createElement('th'); th.className='text-muted fw-normal'; th.style.width='36%'; th.textContent=sub;
            var td=document.createElement('td'); td.textContent=v;
            tr.appendChild(th); tr.appendChild(td); tbF.appendChild(tr);
          });
          tableF.appendChild(tbF); respWrapF.appendChild(tableF); body.appendChild(respWrapF);
        }
      } else {
        var respWrap=document.createElement('div'); respWrap.className='table-responsive';
        var table=document.createElement('table'); table.className='table table-sm table-striped align-middle mb-0';
        var tb=document.createElement('tbody');
        items.sort(function(x,y){ return x.k.localeCompare(y.k); }).forEach(function(it){
          var k=it.k, v=it.v;
          var lastDot=k.lastIndexOf('.'); var sub=lastDot>=0?k.slice(lastDot+1):k;
          var bpos=sub.indexOf('['); if(bpos>=0) sub=sub.slice(0,bpos);
          var tr=document.createElement('tr');
          var th=document.createElement('th'); th.className='text-muted fw-normal'; th.style.width='36%'; th.textContent=sub;
          var td=document.createElement('td'); td.textContent=v;
          tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr);
        });
        table.appendChild(tb); respWrap.appendChild(table); body.appendChild(respWrap);
      }

      col.appendChild(body); item.appendChild(h2); item.appendChild(col); acc.appendChild(item);
    });
    return acc;
  }

  function setPdXml(raw, pretty){
    try{
      var els = getPanelEls();
      if (els.rawPre) els.rawPre.textContent = raw || '';
      if (els.prettyPre) els.prettyPre.textContent = pretty || raw || '';
      if (els.copyRaw) els.copyRaw.onclick = function(){ try{ navigator.clipboard.writeText(raw || ''); els.copyRaw.textContent = (readI18n().Copied||'Copied'); setTimeout(function(){ els.copyRaw.textContent = (readI18n().Copy||'Copy'); }, 1200);}catch{}};
      if (els.copyPretty) els.copyPretty.onclick = function(){ try{ navigator.clipboard.writeText(pretty || raw || ''); els.copyPretty.textContent = (readI18n().Copied||'Copied'); setTimeout(function(){ els.copyPretty.textContent = (readI18n().Copy||'Copy'); }, 1200);}catch{}};
      if (els.dlRaw) els.dlRaw.onclick = function(){ try{ var name='GetPerson_raw_'+new Date().toISOString().replace(/[:.]/g,'-')+'.xml'; var blob=new Blob([raw||''], {type:'text/xml;charset=utf-8'}); var a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download=name; document.body.appendChild(a); a.click(); URL.revokeObjectURL(a.href); a.remove(); }catch(e){ console.error('pd: download raw failed', e);} };
      if (els.dlPretty) els.dlPretty.onclick = function(){ try{ var name='GetPerson_pretty_'+new Date().toISOString().replace(/[:.]/g,'-')+'.xml'; var content=(pretty||raw||''); var blob=new Blob([content], {type:'text/xml;charset=utf-8'}); var a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download=name; document.body.appendChild(a); a.click(); URL.revokeObjectURL(a.href); a.remove(); }catch(e){ console.error('pd: download pretty failed', e);} };
    }catch(e){ try{ console.debug('GPIV: setPdXml failed', e); }catch{} }
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
    if(!publicId || !els.body) { showInfo('Select a person to view details.'); return; }

    var cached = personCache.get(publicId);
    if (cached && cached.details && cached.details.length) {
      clearPanel();
      var builder = (window._renderPairsGroupedForPerson || defaultRenderPairsGrouped);
      var accCached = builder(cached.details);
      if (accCached) els.body.appendChild(accCached);
      setPdXml(cached.raw || '', cached.pretty || '');
      ensureShownAndFocus();
    } else {
      showLoading(true);
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

      var details = Array.isArray(data.details) ? data.details : [];
      personCache.set(publicId, { details: details, raw: data.raw || '', pretty: data.pretty || '', ts: Date.now() });

      clearPanel();
      var builder2 = (window._renderPairsGroupedForPerson || defaultRenderPairsGrouped);
      var acc = builder2(details);
      if (acc) els.body.appendChild(acc); else showInfo('No details to display.');
      setPdXml(data.raw || '', data.pretty || '');
      ensureShownAndFocus();

      activatePdDetailsSubTab();

      if (sourceEl) markSelectedSummaryCard(sourceEl);

      try{
        var link = new URL(window.location.href);
        link.searchParams.set('gpivPublicId', publicId);
        history.pushState(null,'',link.toString());
      }catch(ex){ try{ console.debug('GPIV: history.pushState failed', ex); }catch{} }
    } catch (err) {
      showLoading(false);
      if (els.err) {
        els.err.innerHTML = 'Failed to load details. <button type=\"button\" id=\"pd-retry\" class=\"btn btn-sm btn-outline-secondary ms-2\">Retry</button>';
        els.err.classList.remove('d-none');
        var retry2 = document.getElementById('pd-retry');
        retry2 && retry2.addEventListener('click', function(){ loadPerson(publicId, sourceEl); });
        ensureShownAndFocus();
      }
      try { console.error('GetPerson fetch failed', err); } catch {}
    }
  }

  document.addEventListener('click', function(e){
    var header = e.target && e.target.closest && e.target.closest('.gpiv-person-header');
    if(!header) return;
    if(e.ctrlKey || e.metaKey) return;
    var wrap = header.parentElement; // .gpiv-person
    if(!wrap) return;
    var pid = wrap.getAttribute('data-public-id') || (wrap.dataset ? wrap.dataset.publicId : '') || '';
    if(pid) loadPerson(pid, header); else showInfo('Select a person with a valid ID to view details.');
  });

  document.addEventListener('keydown', function(e){
    if (e.key !== 'Enter' && e.key !== ' ') return;
    var header = e.target && e.target.closest && e.target.closest('.gpiv-person-header');
    if(!header) return;
    e.preventDefault();
    var wrap = header.parentElement;
    if(!wrap) return;
    var pid = wrap.getAttribute('data-public-id') || (wrap.dataset ? (wrap.dataset.PublicId || wrap.dataset.publicId) : '') || '';
    if(pid) loadPerson(pid, header); else showInfo('Select a person with a valid ID to view details.');
  });

  // Outer PersonDetails tab click -> load
  document.addEventListener('click', function(e){
    var tabBtn = e.target && e.target.closest && e.target.closest('#gpiv-tab-details-btn');
    if (!tabBtn) return;
    setTimeout(function(){
      var pid = window.lastPid || findFirstPersonId();
      if (pid) {
        loadPerson(pid);
        return;
      }
      // No PID yet: render an empty shell so the pane isn't blank
      clearPanel();
      var els = getPanelEls();
      try {
        var acc = (window._renderPairsGroupedForPerson || defaultRenderPairsGrouped)([]);
        if (els.body && acc) { els.body.appendChild(acc); }
        ensureShownAndFocus();
      } catch {}
      // Keep waiting to auto-load when a person becomes available
      waitForSummaryThenLoadOnceOrInform();
    }, 0);
  });

  // Inner pd-tab-details sub-tab click -> also load
  document.addEventListener('click', function(e){
    var innerBtn = e.target && e.target.closest && e.target.closest('#pd-tab-details-btn');
    if (!innerBtn) return;
    setTimeout(function(){
      var pid = window.lastPid || findFirstPersonId();
      if (pid) { loadPerson(pid); }
      else { clearPanel(); var els = getPanelEls(); try { var acc = (window._renderPairsGroupedForPerson || defaultRenderPairsGrouped)([]); if (els.body && acc) els.body.appendChild(acc); } catch {} ensureShownAndFocus(); waitForSummaryThenLoadOnceOrInform(); }
    }, 0);
  });

  // Also react when the tab is actually shown by Bootstrap
  document.addEventListener('shown.bs.tab', function(e){
    try{
      var t = e && e.target ? e.target : null;
      if (!t) return;
      var id = t.getAttribute('id') || '';
      if (id === 'gpiv-tab-details-btn' || id === 'pd-tab-details-btn') {
        var pid = window.lastPid || findFirstPersonId();
        if (pid) { loadPerson(pid); }
        else {
          clearPanel();
          var els = getPanelEls();
          try { var acc = (window._renderPairsGroupedForPerson || defaultRenderPairsGrouped)([]); if (els.body && acc) els.body.appendChild(acc); } catch {}
          ensureShownAndFocus();
          waitForSummaryThenLoadOnceOrInform();
        }
      }
    }catch{}
  });

  document.addEventListener('click', function(e){
    var fsBtn = e.target && e.target.closest && e.target.closest('#pd-fullscreen');
    if (!fsBtn) return;
    var sec = document.getElementById('person-details-section');
    if (!sec) return;
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
      sec.classList.toggle('pd-fullscreen');
    }
  });

  document.addEventListener('fullscreenchange', function(){
    var sec = document.getElementById('person-details-section');
    if (!sec) return;
    var isFs = document.fullscreenElement === sec;
    sec.classList.toggle('pd-fullscreen', isFs);
  });

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

  // Pre-render shell so the Details pane never appears blank
  function prerenderShell(){
    try{
      var els = getPanelEls();
      if (!els) return;
      if (els.sec) els.sec.classList.remove('d-none');
      if (els.body && !els.body.childElementCount) {
        var acc = (window._renderPairsGroupedForPerson || defaultRenderPairsGrouped)([]);
        if (acc) els.body.appendChild(acc);
      }
    }catch{}
  }
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', prerenderShell);
  } else {
    prerenderShell();
  }

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
