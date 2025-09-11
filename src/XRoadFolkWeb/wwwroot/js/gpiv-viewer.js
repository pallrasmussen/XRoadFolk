/* eslint-disable no-var */
(function () {
  'use strict';

  // Minimal debug helper to surface previously swallowed errors (toggle flag if too noisy)
  function dbg(tag, err){ try{ if(!err) return; if (window && window.console && console.debug){ console.debug(tag, err); } }catch(_){} }

  // UTF-8 safe base64 decode (replaces legacy decodeURIComponent/escape pattern)
  function b64ToUtf8(b64){
    try {
      if(!b64) return '';
      const bin = atob(b64);
      // Fast path ASCII
      if(!/[\x80-\xFF]/.test(bin)) return bin;
      const bytes = Uint8Array.from(bin, c => c.charCodeAt(0));
      return new TextDecoder('utf-8',{fatal:false}).decode(bytes);
    } catch(e){ dbg('gpiv:b64ToUtf8', e); return ''; }
  }

  // Pull helpers from global UMD bridge to avoid import resolution issues
  const H = (typeof window !== 'undefined' && window.gpivHelpers) ? window.gpivHelpers : {};
  const iconClassFor = H.iconClassFor || function(){ return 'bi-list-ul'; };
  const prettify = H.prettify || (s => String(s||''));
  const nextUid = H.nextUid || (p => (p||'id')+'-'+Date.now());
  const clearChildren = H.clearChildren || function(n){ try{ while(n && n.firstChild){ n.removeChild(n.firstChild);} }catch(e){ dbg('gpiv:clearChildren', e); } };
  const handleAccordionKeydown = H.handleAccordionKeydown || function(){};
  const copyToClipboard = H.copyToClipboard || (async function(){ return false; });
  const downloadBlob = H.downloadBlob || function(){};
  const toggleFullscreenWithCssFallback = H.toggleFullscreenWithCssFallback || function(el, cls){ if (el && el.classList) el.classList.toggle(cls); };
  const focusFirstAccordionButton = H.focusFirstAccordionButton || function(scope){ try{ const btn=(scope||document).querySelector('.accordion .accordion-header .accordion-button'); if(btn&&btn.focus) btn.focus(); }catch(e){ dbg('gpiv:focusFirstAccordionButton', e); } };
  const toggleAllAccordions = H.toggleAllAccordions || function(scope, open){ try{ const panes=(scope||document).querySelectorAll('.accordion-collapse')||[]; for(let i=0;i<panes.length;i++){ const p=panes[i]; if(p) p.classList.toggle('show', !!open);} const btns=(scope||document).querySelectorAll('.accordion-button')||[]; for(let j=0;j<btns.length;j++){ const b=btns[j]; if(b){ b.classList.toggle('collapsed', !open); b.setAttribute('aria-expanded', open?'true':'false'); } } }catch(e){ dbg('gpiv:toggleAllAccordions', e); } };

  // Utilities
  function byId(id) { return document.getElementById(id); }
  function qs(sel, root) { return (root || document).querySelector(sel); }
  function qsa(sel, root) { return (root || document).querySelectorAll(sel); }
  function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }

  let summaryHost = null;
  let emptyMsg = null;
  let card = null;
  let I18N = {};

  let sourcePretty = '';
  let sourceRaw = '';

  function parseXml(text) {
    const parser = new DOMParser();
    const doc = parser.parseFromString(text, 'application/xml');
    const err = doc.querySelector('parsererror');
    if (err) throw new Error(err.textContent || 'Invalid XML');
    return doc;
  }

  // Height sync with People Search card (original)
  function syncViewerHeight() {
    try {
      const search = byId('people-search-card');
      if (!search || !card) return;
      if (card.classList.contains('gpiv-fullscreen')) {
        card.style.height = '';
        return;
      }
      const h = Math.round(search.getBoundingClientRect().height);
      if (h > 0) card.style.height = h + 'px';
    } catch (e) { console.error('gpiv: syncViewerHeight failed', e); }
  }

  // Throttled / debounced height sync (75ms debounce + rAF)
  let heightSyncTimer = 0;
  const HEIGHT_SYNC_DELAY = 75; // within requested 50–100ms window
  function scheduleHeightSync(){
    try{ if(heightSyncTimer){ clearTimeout(heightSyncTimer); } }catch(e){ dbg('gpiv:scheduleHeightSync-clearTimeout', e); }
    heightSyncTimer = setTimeout(function(){
      heightSyncTimer = 0;
      try{ (window.requestAnimationFrame||function(fn){ return setTimeout(fn,16); })(function(){ syncViewerHeight(); }); }catch(e){ dbg('gpiv:scheduleHeightSync-rAF', e); syncViewerHeight(); }
    }, HEIGHT_SYNC_DELAY);
  }

  function buildHeaderBtnContent(btn, titleText) {
    btn.textContent = '';
    const ic = document.createElement('i'); ic.className = 'bi ' + iconClassFor(titleText) + ' me-2'; ic.setAttribute('aria-hidden', 'true');
    btn.appendChild(ic);
    btn.appendChild(document.createTextNode(titleText || ''));
  }

  function isNil(node) {
    if (!node || node.nodeType !== 1 || !node.attributes) return false;
    for (let i = 0; i < node.attributes.length; i++) {
      const a = node.attributes[i];
      const ln = a.localName || a.name;
      if (ln === 'nil' && /^(true|1)$/i.test((a.value || '').trim())) return true;
    }
    return false;
  }
  function hasElementChildren(node) {
    if (!node || !node.children) return false;
    for (let i = 0; i < node.children.length; i++) if (!isNil(node.children[i])) return true;
    return false;
  }
  function isNoiseKey(name) {
    if (!name) return false;
    const n = String(name).toLowerCase();
    return n === 'id' || n === 'personaddressid';
  }

  const atSign = String.fromCharCode(64);
  function buildLeavesTable(owner) {
    const rows = [];
    if (owner && owner.attributes && owner.attributes.length) {
      for (let i = 0; i < owner.attributes.length; i++) {
        const a = owner.attributes[i];
        const attrName = a.localName || a.name || '';
        if (isNoiseKey(attrName)) continue;
        rows.push({ key: atSign + (a.name || ''), val: (a.value || '') });
      }
    }
    const kids = owner && owner.children ? owner.children : [];
    for (let k = 0; k < kids.length; k++) {
      const ch = kids[k];
      const nil = isNil(ch);
      const treatAsLeaf = nil || !hasElementChildren(ch);
      if (treatAsLeaf) {
        if (isNoiseKey(ch.localName)) continue;
        const v = nil ? '' : (ch.textContent || '').trim();
        rows.push({ key: ch.localName, val: v });
        if (!nil && ch.attributes && ch.attributes.length) {
          for (let a2 = 0; a2 < ch.attributes.length; a2++) {
            const at = ch.attributes[a2];
            const atName = at.localName || at.name || '';
            if (isNoiseKey(atName)) continue;
            rows.push({ key: ch.localName + ' ' + atSign + (at.name || ''), val: (at.value || '') });
          }
        }
      }
    }
    if (!rows.length) return null;
    const wrap = document.createElement('div'); wrap.className = 'table-responsive';
    const t = document.createElement('table'); t.className = 'table table-sm table-striped align-middle mb-0';
    const tb = document.createElement('tbody');
    for (let r = 0; r < rows.length; r++) {
      const tr = document.createElement('tr');
      const th = document.createElement('th'); th.className = 'text-muted fw-normal'; th.style.width = '36%'; th.textContent = rows[r].key;
      const td = document.createElement('td'); td.textContent = rows[r].val;
      tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr);
    }
    t.appendChild(tb); wrap.appendChild(t);
    return wrap;
  }

  function getTextLocal(elx, name) {
    if (!elx || !elx.children) return '';
    for (let i = 0; i < elx.children.length; i++) {
      const c = elx.children[i];
      if (c.localName === name) return (c.textContent || '').trim();
    }
    return '';
  }
  function badge(text) { const b = document.createElement('span'); b.className = 'badge text-bg-light'; b.textContent = text; return b; }

  function makeAccItem(accId, idx, title, open){
    const item = document.createElement('div'); item.className = 'accordion-item';
    const hid = accId + '-h-' + idx; const cid = accId + '-c-' + idx;
    const h2 = document.createElement('h2'); h2.className = 'accordion-header'; h2.id = hid;
    const btn = document.createElement('button'); btn.type = 'button'; btn.className = 'accordion-button' + (open ? '' : ' collapsed');
    btn.setAttribute('data-bs-toggle', 'collapse'); btn.setAttribute('data-bs-target', '#' + cid);
    btn.setAttribute('aria-expanded', open ? 'true' : 'false'); btn.setAttribute('aria-controls', cid);
    buildHeaderBtnContent(btn, title || '');
    h2.appendChild(btn);
    const col = document.createElement('div'); col.id = cid; col.className = 'accordion-collapse collapse' + (open ? ' show' : '');
    col.setAttribute('aria-labelledby', hid);
    const body = document.createElement('div'); body.className = 'accordion-body p-0'; body.dataset.rendered='0';
    col.appendChild(body);
    item.appendChild(h2); item.appendChild(col);
    return { item, body };
  }

  function expandSummaryAll(open) { if (!summaryHost) return; toggleAllAccordions(summaryHost, open); }

  function groupChildrenByName(node) {
    const map = Object.create(null);
    if (!node || !node.children) return map;
    for (let i = 0; i < node.children.length; i++) {
      const c = node.children[i];
      if (!c || c.nodeType !== 1) continue;
      if (c.localName === 'Names') continue;
      if (isNil(c)) continue;
      if (!hasElementChildren(c)) continue;
      const nm = c.localName;
      (map[nm] || (map[nm] = [])).push(c);
    }
    return map;
  }

  function findPeopleNodes(xml) {
    const nodes = [];
    const all = xml.getElementsByTagName('*');
    for (let i = 0; i < all.length; i++) if (all[i].localName === 'PersonPublicInfo') nodes.push(all[i]);
    if (nodes.length > 0) return nodes;
    for (let j = 0; j < all.length; j++) if (all[j].localName === 'Person') nodes.push(all[j]);
    if (nodes.length > 0, nodes) return nodes;
    for (let k = 0; k < all.length; k++) {
      const e = all[k];
      const ln = e.localName;
      if (!e.children || ln === 'Names' || ln === 'Name' || ln === 'ListOfPersonPublicInfo' || ln === 'ListOfPersonPublicInfoCriteria') continue;
      let hasId = false, hasNames = false;
      for (let m = 0; m < e.children.length; m++) {
        const ch = e.children[m];
        const cl = ch.localName;
        if (cl === 'PublicId' || cl === 'PersonId') hasId = true;
        if (cl === 'Names') hasNames = true;
      }
      if (hasId || hasNames) nodes.push(e);
    }
    return nodes;
  }

  function createRawPrettyButtons(inlineHost){
    const wrap=document.createElement('div');
    wrap.className='btn-group btn-group-sm align-self-center ms-2';
    function showInline(mode){
      if(!inlineHost) return;
      const openMode = inlineHost.getAttribute('data-mode') || '';
      const visible = !inlineHost.classList.contains('d-none');
      if(visible && openMode===mode){ inlineHost.classList.add('d-none'); return; }
      inlineHost.setAttribute('data-mode', mode);
      inlineHost.classList.remove('d-none');
      clearChildren(inlineHost);

      const content=(mode==='raw') ? (sourceRaw||'') : ((sourcePretty||sourceRaw||''));

      const card=document.createElement('div'); card.className='p-2 border rounded bg-body-tertiary';
      const actions=document.createElement('div'); actions.className='d-flex justify-content-end gap-2 mb-2';
      const copyBtn=document.createElement('button'); copyBtn.type='button'; copyBtn.className='btn btn-sm btn-outline-secondary'; copyBtn.textContent=(I18N.Copy||'Copy');
      copyBtn.addEventListener('click', function(){ copyToClipboard(content).then(function(){ const prev=copyBtn.textContent; copyBtn.textContent=(I18N.Copied||'Copied'); setTimeout(function(){ copyBtn.textContent=prev; }, 1200); }).catch(function(e){ dbg('gpiv:copy-inline', e); }); });
      const dlBtn=document.createElement('button'); dlBtn.type='button'; dlBtn.className='btn btn-sm btn-outline-secondary'; dlBtn.textContent=(I18N.Download||'Download');
      dlBtn.addEventListener('click', function(){ try{ const ts=new Date().toISOString().replace(/[:.]/g,'-'); const fname=(mode==='raw'?'GetPeoplePublicInfo_raw_':'GetPeoplePublicInfo_pretty_')+ts+'.xml'; downloadBlob(fname, content, 'text/xml;charset=utf-8'); }catch(e){ dbg('gpiv:download-inline', e); } });
      actions.appendChild(copyBtn); actions.appendChild(dlBtn);

      const pre=document.createElement('pre'); pre.className='gpiv-pre mb-0'; pre.style.maxHeight='16rem'; pre.style.overflow='auto';
      pre.textContent = content;

      card.appendChild(actions);
      card.appendChild(pre);
      inlineHost.appendChild(card);
    }
    const rawBtn=document.createElement('button'); rawBtn.type='button'; rawBtn.className='btn btn-outline-secondary'; rawBtn.innerHTML='<i class="bi bi-filetype-xml me-1" aria-hidden="true"></i>'+(I18N.RawXml||'Raw XML'); rawBtn.addEventListener('click',function(){ showInline('raw'); }); wrap.appendChild(rawBtn);
    const prettyBtn=document.createElement('button'); prettyBtn.type='button'; prettyBtn.className='btn btn-outline-secondary'; prettyBtn.innerHTML='<i class="bi bi-braces-asterisk me-1" aria-hidden="true"></i>'+(I18N.PrettyXml||'Pretty XML'); prettyBtn.addEventListener('click',function(){ showInline('pretty'); }); wrap.appendChild(prettyBtn);
    return wrap;
  }

  function buildNodeContent(node) {
    const container = document.createElement('div'); container.className = 'd-flex flex-column gap-2';
    const leaves = buildLeavesTable(node);
    if (leaves) container.appendChild(leaves);
    const groups = groupChildrenByName(node);
    for (const key in groups) {
      if (!Object.prototype.hasOwnProperty.call(groups, key)) continue;
      const arr = groups[key];
      if (arr.length > 1) {
        const listWrap = document.createElement('div'); listWrap.className = 'd-flex flex-column gap-2';
        for (let i = 0; i < arr.length; i++) {
          const card3 = el('div', 'p-2 border rounded', null);
            const title3 = el('div', 'small text-muted mb-1', key + ' #' + (i + 1));
            card3.appendChild(title3);
            const inner = buildNodeContent(arr[i]);
            if (inner) card3.appendChild(inner);
            listWrap.appendChild(card3);
        }
        container.appendChild(listWrap);
      } else {
        const inner2 = buildNodeContent(arr[0]);
        const headline = el('div', 'small text-muted mb-1', key);
        const wrap = document.createElement('div'); wrap.className = 'p-2 border rounded';
        wrap.appendChild(headline);
        if (inner2) wrap.appendChild(inner2);
        container.appendChild(wrap);
      }
    }
    return container;
  }

  function renderSummary() {
    clearChildren(summaryHost);
    try {
      const xmlText = (sourceRaw && sourceRaw.trim()) ? sourceRaw : (sourcePretty || '');
      if (!xmlText.trim()) { summaryHost.textContent = I18N.NoXmlToDisplay || 'No XML to display.'; scheduleHeightSync(); return; }
      const xml = parseXml(xmlText);
      const people = findPeopleNodes(xml);
      if (!people || people.length === 0) { summaryHost.appendChild(el('div', 'text-muted', I18N.NoPeopleFound || 'No people found.')); scheduleHeightSync(); return; }
      const frag=document.createDocumentFragment();
      for (let p = 0; p < people.length; p++) {
        const person = people[p];
        const wrap = document.createElement('div'); wrap.className = 'mb-3 p-2 border rounded gpiv-person';
        wrap.setAttribute('data-idx', String(p));
        const header = el('div', 'gpiv-person-header d-flex flex-wrap gap-2 align-items-baseline mb-2', null);
        header.appendChild(el('span', 'badge text-bg-secondary', '#' + (p + 1)));
        let nameRoot = null;
        for (let nc = 0; nc < person.children.length; nc++) { if (person.children[nc].localName === 'Names') { nameRoot = person.children[nc]; break; } }
        let fullName = '';
        if (nameRoot) {
          const firsts = []; let last = '';
          for (let nn = 0; nn < nameRoot.children.length; nn++) {
            const nm = nameRoot.children[nn]; if (nm.localName !== 'Name') continue;
            const tp = getTextLocal(nm, 'Type'); if (tp === 'FirstName') {
              let order = parseInt(getTextLocal(nm, 'Order') || '9999', 10);
              if (isNaN(order)) order = 9999;
              firsts.push({ o: order, v: getTextLocal(nm, 'Value') });
            } else if (tp === 'LastName') last = getTextLocal(nm, 'Value') || last;
          }
          firsts.sort(function (a, b) { return a.o - b.o; });
          const joined = []; for (let fj = 0; fj < firsts.length; fj++) joined.push(firsts[fj].v);
          fullName = (joined.join(' ') + ' ' + last).trim();
        }
        const nameEl = document.createElement('strong'); nameEl.textContent = fullName || '-'; header.appendChild(nameEl);
        const inlineHost=document.createElement('div'); inlineHost.className='gpiv-inline-xml mt-2 d-none';
        const publicId = getTextLocal(person, 'PublicId') || getTextLocal(person, 'PersonId');
        const dob = (getTextLocal(person, 'DateOfBirth') || '').slice(0, 10);
        wrap.dataset.name = fullName || '';
        wrap.dataset.publicId = publicId || '';
        wrap.dataset.dob = dob || '';
        if (publicId) { try { wrap.setAttribute('data-public-id', publicId); } catch(e){ dbg('gpiv:setAttribute-publicId', e); } }
        if (dob) header.appendChild(badge((I18N.DOB || 'DOB') + ': ' + dob));
        try{ const rp=createRawPrettyButtons(inlineHost); if(rp) header.appendChild(rp); }catch(e){ dbg('gpiv:createRawPrettyButtons', e); }
        try{ header.appendChild((function(){ const label=(I18N&&(I18N.Details||I18N.PersonDetails))?(I18N.Details||I18N.PersonDetails):'Details'; const btn=document.createElement('button'); btn.type='button'; btn.className='btn btn-sm btn-outline-primary ms-auto gpiv-more-info-btn'; btn.setAttribute('title',(I18N.PersonDetails||label)); btn.setAttribute('aria-label',(I18N.PersonDetails||label)); btn.innerHTML='<i class="bi bi-info-circle me-1" aria-hidden="true"></i>'+label; btn.addEventListener('click', function(){ const panel=document.getElementById('pd-details-panel'); const isOpen=panel && panel.classList.contains('show'); const currentPid=(typeof window!=='undefined' && window.lastPid)?window.lastPid:''; if(isOpen && currentPid && publicId && currentPid===publicId){ try{ if(window.bootstrap&&window.bootstrap.Collapse){ const instHide=window.bootstrap.Collapse.getOrCreateInstance(panel,{toggle:false}); instHide.hide(); } else { panel.classList.remove('show'); } }catch(e){ dbg('gpiv:toggleDetailsHide', e); } return; } try{ window.lastPid=publicId || window.lastPid; }catch(e){ dbg('gpiv:setLastPid', e); } try{ if(panel){ if(window.bootstrap&&window.bootstrap.Collapse){ const instShow=window.bootstrap.Collapse.getOrCreateInstance(panel,{toggle:false}); instShow.show(); } else { panel.classList.add('show'); } } }catch(e){ dbg('gpiv:toggleDetailsShow', e); } try{ const trigger=document.getElementById('gpiv-tab-details-btn'); if(trigger){ trigger.click(); } }catch(e){ dbg('gpiv:triggerDetailsTab', e); } }); return btn; })()); }catch(e){ dbg('gpiv:addDetailsButton', e); }
        wrap.appendChild(header); wrap.appendChild(inlineHost);

        const accId = nextUid('acc'); const acc = document.createElement('div'); acc.className = 'accordion'; acc.id = accId;
        // Lazy materialize content on expansion
        acc.addEventListener('show.bs.collapse', function(e){ try{ const col=e && e.target ? e.target : null; if(!col) return; const body=col.querySelector('.accordion-body'); if(body && body.dataset.rendered!=='1' && typeof body._render==='function'){ body._render(); body.dataset.rendered='1'; } }catch(ex){ dbg('gpiv:lazyRender', ex); } });

        if (nameRoot) {
          const namesItem = makeAccItem(accId, acc.childElementCount, I18N.Names || 'Names', false);
          namesItem.body._render = (function(nRoot, host){ return function(){ try{ clearChildren(host); const list=document.createElement('div'); list.className='d-flex flex-column gap-2'; const nameItems=[]; for (let n2 = 0; n2 < nRoot.children.length; n2++){ const nItem=nRoot.children[n2]; if(nItem.localName!== 'Name') continue; let ord=parseInt(getTextLocal(nItem,'Order')||'9999',10); if(isNaN(ord)) ord=9999; nameItems.push({node:nItem,order:ord}); } nameItems.sort(function(a,b){return a.order-b.order;}); for(let ix=0; ix<nameItems.length; ix++){ const nNode=nameItems[ix].node; const card2=el('div','p-2 border rounded',null); const title2=el('div','small text-muted mb-1',(I18N.Name||'Name')+' #'+(ix+1)); card2.appendChild(title2); const t2=buildLeavesTable(nNode); if(t2) card2.appendChild(t2); const nestedUnderName=buildNodeContent(nNode); if(nestedUnderName && nestedUnderName.childElementCount > (t2?1:0)){ card2.appendChild(nestedUnderName);} list.appendChild(card2);} host.appendChild(list); }catch(er){ dbg('gpiv:renderNames', er); } }; })(nameRoot, namesItem.body);
          acc.appendChild(namesItem.item);
        }

        const basicsTable = buildLeavesTable(person);
        if (basicsTable) {
          const basicsItem = makeAccItem(accId, acc.childElementCount, I18N.Basics || 'Basics', false);
          basicsItem.body._render = (function(tbl, host){ return function(){ try{ clearChildren(host); host.appendChild(tbl); }catch(er){ dbg('gpiv:renderBasics', er); } }; })(basicsTable, basicsItem.body);
          acc.appendChild(basicsItem.item);
        }

        const groups = groupChildrenByName(person);
        for (const gName in groups) {
          if (!Object.prototype.hasOwnProperty.call(groups, gName)) continue;
          (function(g, arr){
            const gi = makeAccItem(accId, acc.childElementCount, g, false);
            gi.body._render = function(){ try{ clearChildren(gi.body); const content=document.createElement('div'); content.className='d-flex flex-column gap-2'; if(arr.length>1){ const listWrap=document.createElement('div'); listWrap.className='d-flex flex-column gap-2'; for(let it=0; it<arr.length; it++){ const card3=el('div','p-2 border rounded',null); const title3=el('div','small text-muted mb-1', g + ' #' + (it+1)); card3.appendChild(title3); const inner3=buildNodeContent(arr[it]); if(inner3) card3.appendChild(inner3); listWrap.appendChild(card3);} content.appendChild(listWrap);} else { const inner4=buildNodeContent(arr[0]); if(inner4) content.appendChild(inner4);} gi.body.appendChild(content); }catch(er){ dbg('gpiv:renderGroup:'+g, er); } };
            acc.appendChild(gi.item);
          })(gName, groups[gName]);
        }

        wrap.appendChild(acc); frag.appendChild(wrap);
      }
      summaryHost.appendChild(frag);
      try { focusFirstAccordionButton(summaryHost); } catch(e){ dbg('gpiv:focusFirstAccordionButton-final', e); }
      // Hide old navbar Details button if present
      try{ const navBtn=byId('pd-toggle-details'); if(navBtn){ navBtn.classList.add('d-none'); navBtn.setAttribute('aria-hidden','true'); } }catch(e){ dbg('gpiv:hideOldDetailsBtn', e); }
      scheduleHeightSync();
    } catch (e) {
      console.error('gpiv: failed to build summary', e);
      summaryHost.textContent = (I18N.FailedToBuildSummaryPrefix || 'Failed to build summary:') + ' ' + (e && e.message ? e.message : e);
      scheduleHeightSync();
    }
  }

  function updateView() {
    const combined = (sourceRaw && sourceRaw.trim()) || (sourcePretty && sourcePretty.trim()) || '';
    const hasAny = combined.length > 0;
    if (emptyMsg) emptyMsg.style.display = hasAny ? 'none' : 'block';
    if (hasAny) { renderSummary(); } else { clearChildren(summaryHost); scheduleHeightSync(); }
  }

  function setRawPrettyUi(raw, pretty){
    try{
      const rawPre = byId('gpiv-raw-pre');
      const prettyPre = byId('gpiv-pretty-pre');
      if (rawPre) rawPre.textContent = raw || '';
      if (prettyPre) prettyPre.textContent = (pretty || raw || '');
      const copyRaw = byId('gpiv-copy-raw');
      const copyPretty = byId('gpiv-copy-pretty');
      const dlRaw = byId('gpiv-dl-raw');
      const dlPretty = byId('gpiv-dl-pretty');
      if (copyRaw) copyRaw.onclick = function(){ copyToClipboard(raw || '').then(function(){ copyRaw.textContent = (I18N.Copied||'Copied'); setTimeout(function(){ copyRaw.textContent = (I18N.Copy||'Copy'); }, 1200); }).catch(function(e){ dbg('gpiv:copyRaw', e); }); };
      if (copyPretty) copyPretty.onclick = function(){ copyToClipboard((pretty||raw||'')).then(function(){ copyPretty.textContent = (I18N.Copied||'Copied'); setTimeout(function(){ copyPretty.textContent = (I18N.Copy||'Copy'); }, 1200); }).catch(function(e){ dbg('gpiv:copyPretty', e); }); };
      if (dlRaw) dlRaw.onclick = function(){ try{ downloadBlob('GetPeoplePublicInfo_raw_'+new Date().toISOString().replace(/[:.]/g,'-')+'.xml', raw||'', 'text/xml;charset=utf-8'); }catch(e){ dbg('gpiv:downloadRaw', e); } };
      if (dlPretty) dlPretty.onclick = function(){ try{ const content=(pretty||raw||''); downloadBlob('GetPeoplePublicInfo_pretty_'+new Date().toISOString().replace(/[:.]/g,'-')+'.xml', content, 'text/xml;charset=utf-8'); }catch(e){ dbg('gpiv:downloadPretty', e); } };
    }catch(e){ dbg('gpiv:setRawPrettyUi', e); }
  }

  function setXmlFromText(text) { if (!text || !text.trim()) return; sourceRaw = text; sourcePretty = text; setRawPrettyUi(sourceRaw, sourcePretty); updateView(); }

  function initFullscreen() {
    document.addEventListener('click', function (e) {
      const t = e && e.target ? e.target : null;
      if (!t) return;
      const trigger = (t.id === 'gpiv-fullscreen') || (t.closest && t.closest('#gpiv-fullscreen'));
      if (!trigger) return;
      if (!card) return;
      try { const pdSec = byId('person-details-section'); if (pdSec) pdSec.classList.remove('pd-fullscreen'); } catch (e2) { dbg('gpiv:exitPersonDetailsFs', e2); }
      try {
        toggleFullscreenWithCssFallback(card, 'gpiv-fullscreen');
        if (!document.fullscreenElement && !card.classList.contains('gpiv-fullscreen')) scheduleHeightSync();
      } catch (err) {
        card.classList.toggle('gpiv-fullscreen');
        card.style.height = '';
        if (!card.classList.contains('gpiv-fullscreen')) scheduleHeightSync();
      }
    });

    document.addEventListener('fullscreenchange', function(){
      if (!card) return;
      const isFs = document.fullscreenElement === card;
      card.classList.toggle('gpiv-fullscreen', isFs);
      if (!isFs) scheduleHeightSync();
    });
  }

  function loadInitialDataFromJsonTags() {
    try { const i18nEl = byId('gpiv-i18n-json'); if (i18nEl) I18N = JSON.parse(i18nEl.textContent || '{}'); } catch (e) { console.error('gpiv: failed to parse i18n json', e); }
    try {
      const rawEl = byId('gpiv-raw-json');
      const prettyEl = byId('gpiv-pretty-json');
      const rawInit = rawEl ? JSON.parse(rawEl.textContent || '""') : "";
      const prettyInit = prettyEl ? JSON.parse(prettyEl.textContent || '""') : "";
      if (typeof rawInit === 'string' || typeof prettyInit === 'string') { window.gpiv.setXml(String(rawInit || ''), String(prettyInit || '')); }
      if ((!rawInit || rawInit.length === 0) && (!prettyInit || prettyInit.length === 0)){
        const dataHost = byId('gpiv-data');
        if (dataHost){
          const rb = dataHost.getAttribute('data-raw-b64') || '';
          const pb = dataHost.getAttribute('data-pretty-b64') || '';
          try{
            const rawFromB64 = rb ? b64ToUtf8(rb) : '';
            const prettyFromB64 = pb ? b64ToUtf8(pb) : '';
            if (rawFromB64 || prettyFromB64){ window.gpiv.setXml(rawFromB64, prettyFromB64); }
          }catch(e){ console.error('gpiv: base64 decode failed', e); }
        }
      }
    } catch (e) { console.error('gpiv: failed to parse initial xml json', e); }
  }

  function initPublicIdLink() {
    try {
      const qs = new URLSearchParams(window.location.search);
      const linkPid = qs.get('gpivPublicId');
      if (linkPid) { setTimeout(function () { window.gpiv.focusPerson(linkPid); }, 50); }
    } catch (e) { console.error('gpiv: focusPerson failed', e); }
  }

  window.gpiv = {
    setXml: function (raw, pretty) {
      try {
        if (typeof raw === 'string') sourceRaw = raw;
        if (typeof pretty === 'string') sourcePretty = pretty; else if (typeof raw === 'string') sourcePretty = raw;
        setRawPrettyUi(sourceRaw, sourcePretty);
        updateView();
      } catch (e) { console.error('gpiv: setXml failed', e); }
    },
    focusPerson: function (publicId) {
      try {
        if (!publicId) return;
        updateView();
        const cards = qsa('.gpiv-person[data-public-id]', summaryHost);
        for (let i = 0; i < cards.length; i++) {
          const pidAttr = cards[i].getAttribute('data-public-id') || (cards[i].dataset ? cards[i].dataset.publicId : '');
          if (pidAttr === publicId) {
            if (cards[i].scrollIntoView) { cards[i].scrollIntoView({ block: 'center' }); cards[i].classList.add('gpiv-highlight'); setTimeout(function (el) { return function(){ el.classList.remove('gpiv-highlight'); }; }(cards[i]), 1500); }
            break;
          }
        }
      } catch (e) { console.error('gpiv: focusPerson failed', e); }
    }
  };

  document.addEventListener('DOMContentLoaded', function () {
    try {
      summaryHost = byId('gpiv-xml-summary');
      emptyMsg = byId('gpiv-empty');
      card = qs('.gpiv-card');
      try {
        const sc = byId('people-search-card');
        if (window.ResizeObserver && sc) {
          const ro = new ResizeObserver(function () { scheduleHeightSync(); });
            ro.observe(sc);
        }
      } catch (e) { console.error('gpiv: ResizeObserver failed', e); }
      window.addEventListener('resize', scheduleHeightSync);
      initFullscreen();
      document.addEventListener('keydown', function(e){ try{ handleAccordionKeydown(e, summaryHost); }catch(er){ dbg('gpiv:handleAccordionKeydown', er); } });
      document.addEventListener('click', function(e){
        try{ const ex = e.target && e.target.closest ? e.target.closest('#gpiv-expand-all') : null; if (ex) { expandSummaryAll(true); } }catch(er){ dbg('gpiv:expandAll', er); }
        try{ const col = e.target && e.target.closest ? e.target.closest('#gpiv-collapse-all') : null; if (col) { expandSummaryAll(false); } }catch(er2){ dbg('gpiv:collapseAll', er2); }
      });
      loadInitialDataFromJsonTags();
      initPublicIdLink();
      scheduleHeightSync();
    } catch (e) {
      console.error('gpiv: init failed', e);
      if (summaryHost) { summaryHost.textContent = (I18N.FailedToBuildSummaryPrefix || 'Failed to initialize:') + ' ' + (e && e.message ? e.message : e); }
    }
  });
})();
