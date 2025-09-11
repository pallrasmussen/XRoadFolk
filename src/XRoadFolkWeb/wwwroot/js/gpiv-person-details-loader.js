// Restore PublicId click -> load PersonDetails panel behavior
(function(){
  if (window.__gpivPidHooked) return; window.__gpivPidHooked = true;

  // Cache for flattened PersonPublicInfo entries keyed by PublicId
  const _publicInfoCache = new Map();

  function getSearchXml(){
    try{
      // Raw search response stored in gpiv-raw-json or base64 data attr
      const rawEl=document.getElementById('gpiv-raw-json');
      if(rawEl){ const txtRaw=rawEl.textContent||''; if(txtRaw.length>0){ return JSON.parse(txtRaw); } }
      const host=document.getElementById('gpiv-data');
      if(host){ const rb=host.getAttribute('data-raw-b64')||''; if(rb){ try{ return decodeURIComponent(escape(window.atob(rb))); }catch{} } }
    }catch{}
    return '';
  }

  function flattenPersonPublicInfoElement(el, rootName){
    const pairs=[]; if(!el) return pairs;
    const maxDepth=64;
    function walk(node,path,depth){
      if(depth>maxDepth) return;
      if(node.nodeType===1){ // element
        const elem=node;
        // attributes
        if(elem.attributes){ for(let i=0;i<elem.attributes.length;i++){ const a=elem.attributes[i]; if(a&&a.value && a.value.trim()){ const base= path || elem.localName; pairs.push([ (path?path:elem.localName)+'.'+a.localName, a.value.trim() ]); } } }
        // children elements?
        let childElCount=0; for(let c=node.firstElementChild;c;c=c.nextElementSibling){ childElCount++; }
        if(childElCount===0){ const val=(node.textContent||'').trim(); if(val){ const key= path?path:elem.localName; pairs.push([key,val]); } return; }
        // count names for indexing duplicates
        const counts={}; for(let c=node.firstElementChild;c;c=c.nextElementSibling){ const ln=c.localName; counts[ln]=(counts[ln]||0)+1; }
        const idxTrack={};
        for(let c=node.firstElementChild;c;c=c.nextElementSibling){ const ln=c.localName; const count=counts[ln]; let nextPath; if(count>1){ const cur=idxTrack[ln]||0; idxTrack[ln]=cur+1; nextPath = (path?path+'.':'')+ln+'['+cur+']'; } else { nextPath=(path?path+'.':'')+ln; } walk(c,nextPath,depth+1); }
      }
    }
    walk(el, rootName,0);
    return pairs.map(p=>({ key:p[0], value:p[1] }));
  }

  function getPersonPublicInfoPairs(publicId){
    if(!publicId) return [];
    if(_publicInfoCache.has(publicId)) return _publicInfoCache.get(publicId);
    const xmlText=getSearchXml(); if(!xmlText) { _publicInfoCache.set(publicId,[]); return []; }
    let doc; try{ doc=new DOMParser().parseFromString(xmlText,'application/xml'); if(doc.querySelector('parsererror')){ _publicInfoCache.set(publicId,[]); return []; } }catch{ _publicInfoCache.set(publicId,[]); return []; }
    // Find PersonPublicInfo with matching PublicId
    let target=null; const candidates=doc.getElementsByTagName('*');
    for(let i=0;i<candidates.length;i++){
      const n=candidates[i]; if(n.localName==='PersonPublicInfo'){ const pub=n.getElementsByTagName('*'); for(let j=0;j<pub.length;j++){ const pn=pub[j]; if(pn.localName==='PublicId'){ if((pn.textContent||'').trim()===publicId){ target=n; break; } } } if(target) break; }
    }
    if(!target){ _publicInfoCache.set(publicId,[]); return []; }
    const list=flattenPersonPublicInfoElement(target,'PersonPublicInfo');
    _publicInfoCache.set(publicId,list); return list;
  }

  // === Updated helper to extract OfficialName (with fallback to FirstName + LastName) ===
  function extractOfficialName(pairs){
    try{
      if(!Array.isArray(pairs)) return '';
      for(let i=0;i<pairs.length;i++){
        const k=(pairs[i].key||'').toLowerCase();
        if(!k) continue;
        if(k==='officialname' || k.endsWith('.officialname') || k.includes('.officialname')){
          const v=(pairs[i].value||'').trim(); if(v) return v;
        }
      }
      const candidateBases=new Set();
      for(let j=0;j<pairs.length;j++){
        const pj=pairs[j];
        const keyj=pj.key||''; const valj=(pj.value||'').trim();
        if(!keyj) continue;
        if(/\.Type$/i.test(keyj) && /^officialname$/i.test(valj)){
          candidateBases.add(keyj.slice(0,-5));
        }
      }
      if(candidateBases.size){
        for(let k2=0;k2<pairs.length;k2++){
          const pk=pairs[k2]; const kk=pk.key||''; if(!kk) continue;
          if(/\.Value$/i.test(kk)){
            const base=kk.slice(0,-6);
            if(candidateBases.has(base)){
              const vv=(pk.value||'').trim(); if(vv) return vv;
            }
          }
        }
      }
      let first=null, last=null;
      for(let f=0; f<pairs.length && (first===null || last===null); f++){
        const pkf=pairs[f]; const kf=(pkf.key||'').toLowerCase(); if(!kf) continue;
        if(first===null && (kf==='firstname' || kf.endsWith('.firstname'))) first=(pkf.value||'').trim()||null;
        else if(last===null && (kf==='lastname' || kf.endsWith('.lastname'))) last=(pkf.value||'').trim()||null;
      }
      const joined=[]; if(first) joined.push(first); if(last) joined.push(last); return joined.join(' ');
    }catch{ return ''; }
  }
  function escapeHtml(s){
    try {
      return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;','\'':'&#39;' }[c] || c));
    } catch { return String(s||''); }
  }
  function updateHeaderWithOfficialName(pairs){
    try{
      const hdr=document.getElementById('pd-title');
      if(hdr && !hdr.dataset.baseTitle){ hdr.dataset.baseTitle = hdr.textContent || 'Person Details'; }
      if(hdr){ hdr.textContent = hdr.dataset.baseTitle || 'Person Details'; }
      const tabBtn=document.getElementById('pd-tab-details-btn');
      if(!tabBtn) return;
      if(!tabBtn.dataset.baseLabel){ tabBtn.dataset.baseLabel = tabBtn.textContent || 'Person Details'; }
      const off=extractOfficialName(pairs);
      if(off){
        tabBtn.innerHTML = '<span class="pd-official-name" title="'+escapeHtml(off)+'">'+escapeHtml(off)+'</span>';
        tabBtn.setAttribute('aria-label', off);
      } else {
        tabBtn.textContent = tabBtn.dataset.baseLabel;
        tabBtn.removeAttribute('aria-label');
      }
    }catch{}
  }

  // Specific icon mapping (Bootstrap Icons)
  const ICON_MAP = { person:'bi-person-badge', basics:'bi-person-badge', personpublicinfo:'bi-card-text', names:'bi-fonts', nameshistory:'bi-clock-history', addresses:'bi-geo-alt', addresseshistory:'bi-geo', foreignssns:'bi-passport', ssns:'bi-passport', ssnhistory:'bi-clock-history', civilstatuses:'bi-people', civilstatushistory:'bi-journal-check', civilstatus:'bi-people', spouse:'bi-heart', spousehistory:'bi-heart-pulse', notes:'bi-journal-text', noteshistory:'bi-journal-medical', incapacity:'bi-slash-circle', incapacityhistory:'bi-slash-circle', specialmarks:'bi-star', specialmarkshistory:'bi-star-half', churchmembership:'bi-building', churchmembershiphistory:'bi-building', citizenships:'bi-flag', citizenshipshistory:'bi-flag', juridicalparents:'bi-people', juridicalparentshistory:'bi-people', juridicalchildren:'bi-people-fill', juridicalchildrenhistory:'bi-people-fill', biologicalparents:'bi-people', postbox:'bi-mailbox' };
  function iconClassFor(group){ const k=(group||'').toString().toLowerCase(); return ICON_MAP[k] || 'bi-question-circle'; }

  const H = (typeof window !== 'undefined' && window.gpivHelpers) ? window.gpivHelpers : {};
  const prettify = H.prettify || (s=>String(s||''));
  const parseAddressKey = H.parseAddressKey || function(){ return null; };
  const nextUid = H.nextUid || (p=> (p||'id')+'-'+Date.now());
  const handleAccordionKeydown = H.handleAccordionKeydown || function(){};
  const copyToClipboard = H.copyToClipboard || (async function(){ return false; });
  const downloadBlob = H.downloadBlob || function(){};
  const toggleFullscreenWithCssFallback = H.toggleFullscreenWithCssFallback || function(el,cls){ if(el&&el.classList) el.classList.toggle(cls); };
  const clearChildren = H.clearChildren || function(n){ try{ while(n&&n.firstChild){ n.removeChild(n.firstChild);} }catch{} };
  const toggleAllAccordions = H.toggleAllAccordions || function(scope,open){ try{ const panes=(scope||document).querySelectorAll('.accordion-collapse')||[]; panes.forEach(p=>p&&p.classList.toggle('show',!!open)); const btns=(scope||document).querySelectorAll('.accordion-button')||[]; btns.forEach(b=>{ if(!b)return; b.classList.toggle('collapsed',!open); b.setAttribute('aria-expanded', open?'true':'false'); }); }catch{} };
  const focusFirstAccordionButton = H.focusFirstAccordionButton || function(scope){ try{ const btn=(scope||document).querySelector('.accordion .accordion-header .accordion-button'); if(btn&&btn.focus) btn.focus(); }catch{} };

  // --- local renderer for addresses using parseAddressKey helper ---
  function renderAddressesCardList(items){
    const groups={}; (items||[]).forEach(it=>{ const parsed=parseAddressKey(it.k||it.key); if(!parsed) return; (groups[parsed.index]=groups[parsed.index]||[]).push({ field:parsed.field, value:it.v||it.value }); });
    const ids=Object.keys(groups).map(x=>parseInt(x,10)||0).sort((a,b)=>a-b); if(!ids.length) return null;
    const list=document.createElement('div'); list.className='d-flex flex-column gap-2';
    ids.forEach(idx=>{ const card=document.createElement('div'); card.className='p-2 border rounded'; const title=document.createElement('div'); title.className='small text-muted mb-1'; title.textContent='Address #'+idx; card.appendChild(title); const resp=document.createElement('div'); resp.className='table-responsive'; const table=document.createElement('table'); table.className='table table-sm table-striped align-middle mb-0'; const tb=document.createElement('tbody'); groups[idx].slice().sort((a,b)=>a.field.localeCompare(b.field)).forEach(r=>{ const tr=document.createElement('tr'); const th=document.createElement('th'); th.className='text-muted fw-normal'; th.style.width='36%'; th.textContent=r.field; const td=document.createElement('td'); td.textContent=r.value; tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr); }); table.appendChild(tb); resp.appendChild(table); card.appendChild(resp); list.appendChild(card); });
    return list;
  }

  const personCache = (window.personCache instanceof Map) ? window.personCache : (window.personCache=new Map());
  let lastPid = window.lastPid || null; let loadSeq=0; let currentAbort=null;

  function getPanelEls(){ return { sec:document.getElementById('person-details-section'), body:document.getElementById('person-details-body'), err:document.getElementById('person-details-error'), loading:document.getElementById('person-details-loading'), title:document.querySelector('#person-details-section .card-title'), rawPre:document.getElementById('pd-raw-pre'), prettyPre:document.getElementById('pd-pretty-pre'), copyRaw:document.getElementById('pd-copy-raw'), copyPretty:document.getElementById('pd-copy-pretty'), dlRaw:document.getElementById('pd-dl-raw'), dlPretty:document.getElementById('pd-dl-pretty'), pdDetailsBtn:document.getElementById('pd-tab-details-btn'), pdRawBtn:document.getElementById('pd-tab-raw-btn'), pdPrettyBtn:document.getElementById('pd-tab-pretty-btn'), pdDetailsPane:document.getElementById('pd-tab-details'), pdRawPane:document.getElementById('pd-tab-raw'), pdPrettyPane:document.getElementById('pd-tab-pretty') }; }

  function readI18n(){ try{ if(window.__gpivI18nCached) return window.__gpivI18nCached; const el=document.getElementById('gpiv-i18n-json'); const txt=el?(el.textContent||''):''; let obj={}; try{ obj=JSON.parse(txt||'{}')||{}; }catch{} window.__gpivI18nCached=obj; return obj; }catch{return{};} }

  function parseFirstPublicIdFromXmlText(xmlText){ try{ if(!xmlText||!xmlText.trim()) return ''; const parser=new DOMParser(); const doc=parser.parseFromString(xmlText,'application/xml'); if(doc.querySelector('parsererror')) return ''; const all=doc.getElementsByTagName('*'); let first=null; for(let i=0;i<all.length;i++){ if(all[i].localName==='PersonPublicInfo'){ first=all[i]; break; } } if(!first){ for(let j=0;j<all.length;j++){ if(all[j].localName==='Person'){ first=all[j]; break; } } } if(!first) return ''; const kids=first.getElementsByTagName('*'); for(let k=0;k<kids.length;k++){ const ln=kids[k].localName; if(ln==='publicid'||ln==='personid'){ const pid=(kids[k].textContent||'').trim(); if(pid) return pid; } } return ''; }catch{return '';} }
  function findFirstPersonIdFromEmbeddedXml(){ try{ const rawEl=document.getElementById('gpiv-raw-json'); const prettyEl=document.getElementById('gpiv-pretty-json'); const raw=rawEl?JSON.parse(rawEl.textContent||'""'):''; const pretty=prettyEl?JSON.parse(prettyEl.textContent||'""'):''; if((!raw||!raw.length)&&(!pretty||!pretty.length)){ const host=document.getElementById('gpiv-data'); if(host){ const rb=host.getAttribute('data-raw-b64')||''; const pb=host.getAttribute('data-pretty-b64')||''; try{ const rawText=rb?decodeURIComponent(escape(window.atob(rb))):''; const prettyText=pb?decodeURIComponent(escape(window.atob(pb))):''; return parseFirstPublicIdFromXmlText(rawText||prettyText||''); }catch{} } } return parseFirstPublicIdFromXmlText(raw||pretty||''); }catch{return '';} }

  // Labels: separate Civil Statuses (current) and Civil Status History
  function headerLabelForGroup(rawName){ const i18n=readI18n(); const t=String(rawName||''); const l=t.toLowerCase(); if(l==='personpublicinfo') return 'Person Public Info'; if(l==='person') return 'Person'; if(l==='basics') return i18n.Basics||'Basics'; if(l==='names'||l==='name') return i18n.Names||'Names'; if(l==='addresses'||l==='address') return i18n.Addresses||'Addresses'; if(l==='addresseshistory') return 'Addresses History'; if(l==='foreignssns'||l==='foreignssn') return 'Foreign SSNs'; if(l==='juridicalparents'||l==='juridicalparent') return 'Juridical Parents'; if(l==='juridicalparentshistory') return 'Juridical Parents History'; if(l==='biologicalparents'||l==='biologicalparent') return 'Biological Parents'; if(l==='civilstatuses') return 'Civil Statuses'; if(l==='civilstatushistory') return 'Civil Status History'; if(l==='ssns'||l==='ssn') return 'SSNs'; if(l==='ssnhistory') return 'SSN History'; if(l==='spouse') return 'Spouse'; if(l==='spousehistory') return 'Spouse History'; if(l==='notes') return 'Notes'; if(l==='noteshistory') return 'Notes History'; if(l==='incapacity') return 'Incapacity'; if(l==='incapacityhistory') return 'Incapacity History'; if(l==='specialmarks') return 'Special Marks'; if(l==='specialmarkshistory') return 'Special Marks History'; if(l==='churchmembership') return 'Church Membership'; if(l==='churchmembershiphistory') return 'Church Membership History'; if(l==='citizenships') return 'Citizenships'; if(l==='citizenshipshistory') return 'Citizenships History'; if(l==='juridicalchildren') return 'Juridical Children'; if(l==='juridicalchildrenhistory') return 'Juridical Children History'; if(l==='postbox') return 'Postbox'; if(l==='publicid'||l==='pid') return i18n.PublicId||'Public Id'; if(l==='dob'||l==='dateofbirth') return i18n.DOB||'Date of Birth'; if(l==='status') return 'Status'; return prettify(t); }
  function pickGroupFromKey(key){ const raw=String(key||''); if(!raw) return ''; const parts=raw.split('.'); const segs=[]; for(let i=0;i<parts.length;i++){ let p=parts[i]||''; const b=p.indexOf('['); if(b>=0) p=p.slice(0,b); p=p.trim(); if(p) segs.push(p);} if(!segs.length) return ''; let idx=0; if(/(?:response|result)$/i.test(segs[0])) idx=1; if(idx>=segs.length) return ''; const first=segs[idx]; function norm(seg){ if(/^addresseshistory$/i.test(seg)) return 'AddressesHistory'; if(/^address(?:es)?$/i.test(seg)) return 'Addresses'; if(/^name(?:s)?$/i.test(seg)) return 'Names'; if(/^foreignssn(?:s)?$/i.test(seg)) return 'ForeignSsns'; if(/^juridicalparent(?:s)?history$/i.test(seg)) return 'JuridicalParentsHistory'; if(/^juridicalparent(?:s)?$/i.test(seg)) return 'JuridicalParents'; if(/^ssn(?:s)?$/i.test(seg)) return 'Ssns'; if(/^civilstatushistory$/i.test(seg)) return 'CivilStatusHistory'; if(/^civilstatus$/i.test(seg)) return 'CivilStatuses'; if(/^noteshistory$/i.test(seg)) return 'NotesHistory'; if(/^notes$/i.test(seg)) return 'Notes'; if(/^spouses?history$/i.test(seg)) return 'SpouseHistory'; if(/^spouses?$/i.test(seg)) return 'Spouse'; if(/^status$/i.test(seg)) return ''; return seg; } if(/^person$/i.test(first)){ if(segs.length>=idx+3){ return norm(segs[idx+1]); } return 'Person'; } return norm(first); }
  function activateDetailsTab(){ try{ const btn=document.getElementById('gpiv-tab-details-btn'); const pane=document.getElementById('gpiv-tab-details'); if(!btn||!pane) return; document.querySelectorAll('#gpiv-tabs button[data-bs-target]').forEach(b=>{ b.classList.remove('active'); b.setAttribute('aria-selected','false'); }); document.querySelectorAll('#gpiv-tabs-content .tab-pane').forEach(p=>p.classList.remove('show','active')); btn.classList.add('active'); btn.setAttribute('aria-selected','true'); pane.classList.add('show','active'); }catch{} }
  function activatePdDetailsSubTab(){ try{ const els=getPanelEls(); if(!els.pdDetailsBtn||!els.pdDetailsPane) return; [els.pdDetailsBtn,els.pdRawBtn,els.pdPrettyBtn].forEach(b=>{ if(!b)return; b.classList.remove('active'); b.setAttribute('aria-selected','false'); }); [els.pdDetailsPane,els.pdRawPane,els.pdPrettyPane].forEach(p=>{ if(!p)return; p.classList.remove('show','active'); }); els.pdDetailsBtn.classList.add('active'); els.pdDetailsBtn.setAttribute('aria-selected','true'); els.pdDetailsPane.classList.add('show','active'); }catch{} }

  function showLoading(on){ const e=getPanelEls(); if(!e.sec) return; activateDetailsTab(); activatePdDetailsSubTab(); e.sec.classList.remove('d-none'); if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; } if(e.body && on){ try{ clearChildren(e.body);}catch{} } if(e.loading){ try{ e.loading.classList.toggle('d-none', !on);}catch{} } }
  function clearPanel(){ const e=getPanelEls(); if(!e.sec) return; activateDetailsTab(); activatePdDetailsSubTab(); e.sec.classList.remove('d-none'); if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; } if(e.body){ try{ clearChildren(e.body);}catch{} } if(e.loading){ try{ e.loading.classList.add('d-none'); }catch{} } }
  function showInfo(msg){ const e=getPanelEls(); if(!e.sec) return; activateDetailsTab(); activatePdDetailsSubTab(); e.sec.classList.remove('d-none'); if(e.loading) e.loading.classList.add('d-none'); if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; } if(e.body){ const div=document.createElement('div'); div.className='alert alert-info mb-0'; div.setAttribute('role','status'); div.textContent=msg||'Select a person to view details.'; try{ clearChildren(e.body);}catch{} try{ e.body.appendChild(div);}catch{} } }
  function ensureShownAndFocus(){ const e=getPanelEls(); if(!e.sec) return; activateDetailsTab(); activatePdDetailsSubTab(); e.sec.classList.remove('d-none'); if(e.title){ e.title.setAttribute('tabindex','-1'); try{ e.title.focus({preventScroll:true}); }catch{} try{ e.title.scrollIntoView({block:'start',behavior:'smooth'});}catch{} } }
  function markSelectedSummaryCard(fromEl){ try{ const host=document.getElementById('gpiv-xml-summary'); if(!host) return; const prev=host.querySelector('.gpiv-active-card'); if(prev) prev.classList.remove('gpiv-active-card'); let wrap=fromEl; while(wrap&&wrap!==host&&!(wrap.classList&&wrap.classList.contains('border')&&wrap.classList.contains('rounded'))) wrap=wrap.parentElement; if(wrap&&wrap!==host) wrap.classList.add('gpiv-active-card'); }catch{} }
  function findFirstPersonId(){ try{ const host=document.getElementById('gpiv-xml-summary'); if(!host) return ''; const el=host.querySelector('[data-public-id],[data-publicid]'); if(el) return el.getAttribute('data-public-id')||el.getAttribute('data-publicid')||(el.dataset? (el.dataset.publicId||el.dataset.publicid||''): ''); return findFirstPublicIdFromEmbeddedXml(); }catch{return '';} }
  function waitForSummaryThenLoadOnceOrInform(){ try{ const host=document.getElementById('gpiv-xml-summary'); if(!host){ const pid0=findFirstPublicIdFromEmbeddedXml(); if(pid0){ loadPerson(pid0); } else { showInfo('Select a person to view details.'); } return; } const pidNow=findFirstPersonId(); if(pidNow){ loadPerson(pidNow); return; } if(!window.MutationObserver){ showInfo('Select a person to view details.'); return; } let informed=false; var mo=new MutationObserver(function(){ const pid=findFirstPersonId(); if(pid){ try{ mo.disconnect(); }catch{} loadPerson(pid); } else if(!informed){ informed=true; try{ mo.disconnect(); }catch{} showInfo('Select a person to view details.'); } }); mo.observe(host,{childList:true,subtree:true}); setTimeout(function(){ if(!informed){ try{ mo.disconnect(); }catch{} showInfo('Select a person to view details.'); } },800); }catch{ showInfo('Select a person to view details.'); } }
  function defaultRenderPairsGrouped(pairs){
    function createPlaceholder(text){ const ph=document.createElement('div'); ph.className='text-muted small p-2'; ph.textContent=text; return ph; }
    function normIncludeName(n){ n=String(n||'').trim(); if(!n) return ''; const map={ personpublicinfo:'PersonPublicInfo', nameshistory:'NamesHistory', names:'Names', addresseshistory:'AddressesHistory', addresses:'Addresses', foreignssns:'ForeignSsns', juridicalparents:'JuridicalParents', biologicalparents:'BiologicalParents', civilstatus:'CivilStatuses', civilstatushistory:'CivilStatusHistory', spouse:'Spouse', spouses:'Spouse', spousehistory:'SpouseHistory', spouseshistory:'SpouseHistory', notes:'Notes', noteshistory:'NotesHistory', incapacity:'Incapacity', incapacityhistory:'IncapacityHistory', specialmarks:'SpecialMarks', specialmarkshistory:'SpecialMarksHistory', postbox:'Postbox', juridicalchildren:'JuridicalChildren', juridicalchildrenhistory:'JuridicalChildrenHistory', ssn:'Ssns', ssns:'Ssns', ssnhistory:'SsnHistory', churchmembership:'ChurchMembership', churchmembershiphistory:'ChurchMembershipHistory', citizenships:'Citizenships', citizenshipshistory:'CitizenshipsHistory'}; const k=n.toLowerCase(); return map[k]||n; }
    // Do not include PersonPublicInfo in the details accordion
    const allowed=new Set(['Person']);
    try{ const incEl=document.getElementById('gpiv-includes-json'); if(incEl){ const inc=JSON.parse(incEl.textContent||'[]')||[]; if(Array.isArray(inc)){ inc.forEach(g=>{ const nn=normIncludeName(g); if(nn) allowed.add(nn); }); } } }catch{}
    const groups={}; (pairs||[]).forEach(p=>{ const k=p.key||'', v=p.value||''; const seg=pickGroupFromKey(k); if(!seg) return; let g=seg; if(/^addresseshistory$/i.test(g)) g='AddressesHistory'; if(/^juridicalparentshistory$/i.test(g)) g='JuridicalParentsHistory'; if(/^ssn(?:s)?$/i.test(g)) g='Ssns'; if(/^civilstatushistory$/i.test(g)) g='CivilStatusHistory'; if(/^civilstatuses$/i.test(g)) g='CivilStatuses'; if(/^noteshistory$/i.test(g)) g='NotesHistory'; if(/^notes$/i.test(g)) g='Notes'; if(/^spouses?$/i.test(g)) g='Spouse'; if(/^spouses?history$/i.test(g)) g='SpouseHistory';
      // Only allow configured groups and core 'Person' (exclude PersonPublicInfo)
      if(!allowed.has(g) && g!=='Person') return; (groups[g]=groups[g]||[]).push({k:k,v:v}); });
    allowed.forEach(g=>{ if(g!=='Person'&&!Object.prototype.hasOwnProperty.call(groups,g)) groups[g]=[]; });
    const keys=Object.keys(groups).sort((a,b)=>{ const al=a.toLowerCase(), bl=b.toLowerCase(); const order=x=> x==='Person'?0:1; return (order(al)-order(bl))||a.localeCompare(b); });
    const accId=nextUid('pdacc'); const acc=document.createElement('div'); acc.className='accordion'; acc.id=accId;
    // Lazy materializer for collapse events (Bootstrap)
    acc.addEventListener('show.bs.collapse', function(e){ try{ const col=e && e.target ? e.target : null; if(!col) return; const body=col.querySelector('.accordion-body'); if(body && body.dataset.rendered!=='1' && typeof body._render==='function'){ body._render(); } }catch{} });

    keys.forEach((name,gi)=>{ const items=groups[name].slice(); const hid=accId+'-h-'+gi, cid=accId+'-c-'+gi; const item=document.createElement('div'); item.className='accordion-item'; item.setAttribute('data-group',name); const h2=document.createElement('h2'); h2.className='accordion-header'; h2.id=hid; const open=false; const btn=document.createElement('button'); btn.className='accordion-button'+(open?'':' collapsed'); btn.type='button'; btn.setAttribute('data-bs-toggle','collapse'); btn.setAttribute('data-bs-target','#'+cid); btn.setAttribute('aria-expanded',open?'true':'false'); btn.setAttribute('aria-controls',cid); const ic=document.createElement('i'); ic.className='bi '+iconClassFor(name)+' me-2'; ic.setAttribute('aria-hidden','true'); btn.appendChild(ic); btn.appendChild(document.createTextNode(headerLabelForGroup(name))); h2.appendChild(btn); const col=document.createElement('div'); col.id=cid; col.className='accordion-collapse collapse'+(open?' show':''); col.setAttribute('aria-labelledby',hid); col.setAttribute('data-bs-parent','#'+accId); const body=document.createElement('div'); body.className='accordion-body p-0'; body.dataset.group=name; body.dataset.rendered='0'; const lname=name.toLowerCase(); const i18n=readI18n(); function simpleTable(sorted){ const respWrap=document.createElement('div'); respWrap.className='table-responsive'; const table=document.createElement('table'); table.className='table table-sm table-striped align-middle mb-0'; const tb=document.createElement('tbody'); sorted.forEach(it=>{ const k=it.k, v=it.v; const lastDot=k.lastIndexOf('.'); let sub=lastDot>=0? k.slice(lastDot+1) : k; const bpos=sub.indexOf('['); if(bpos>=0) sub=sub.slice(0,bpos); const tr=document.createElement('tr'); const th=document.createElement('th'); th.className='text-muted fw-normal'; th.style.width='36%'; th.textContent=sub; const td=document.createElement('td'); td.textContent=v; tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr); }); table.appendChild(tb); respWrap.appendChild(table); return respWrap; }
      // Attach lazy render function
      body._render = function(){ if(body.dataset.rendered==='1') return; clearChildren(body); if(lname==='addresses'||lname==='address'){ if(!items.length){ body.appendChild(createPlaceholder(i18n.NoAddresses||'No addresses.')); } else { const list=renderAddressesCardList(items); if(list) body.appendChild(list); else body.appendChild(createPlaceholder(i18n.NoAddresses||'No addresses.')); } } else { if(!items.length){ let msg='No data.'; if(lname==='person'||lname==='basics') msg=i18n.NoBasics||'No basic information.'; if(lname==='names') msg=i18n.NoNames||'No names.'; if(lname==='foreignssns') msg=i18n.NoForeignSsns||'No foreign SSNs.'; if(lname==='juridicalparents') msg=i18n.NoJuridicalParents||'No juridical parents.'; body.appendChild(createPlaceholder(msg)); } else { body.appendChild(simpleTable(items.sort((x,y)=>x.k.localeCompare(y.k)))); } } body.dataset.rendered='1'; };
      // Lightweight placeholder until expanded
      body.appendChild(createPlaceholder(''));
      col.appendChild(body); item.appendChild(h2); item.appendChild(col); acc.appendChild(item); });
    return acc;
  }

  function focusFirstAccordionHeader(){ try{ const els=getPanelEls(); if(!els||!els.body) return; focusFirstAccordionButton(els.body); }catch{} }
  function setPdXml(raw,pretty){ try{ const els=getPanelEls(); if(els.rawPre) els.rawPre.textContent=raw||''; if(els.prettyPre) els.prettyPre.textContent=pretty||raw||''; if(els.copyRaw) els.copyRaw.onclick=function(){ copyToClipboard(raw||'').then(()=>{ els.copyRaw.textContent=(readI18n().Copied||'Copied'); setTimeout(()=>{ els.copyRaw.textContent=(readI18n().Copy||'Copy'); },1200); }); }; if(els.copyPretty) els.copyPretty.onclick=function(){ copyToClipboard(pretty||raw||'').then(()=>{ els.copyPretty.textContent=(readI18n().Copied||'Copied'); setTimeout(()=>{ els.copyPretty.textContent=(readI18n().Copy||'Copy'); },1200); }); }; if(els.dlRaw) els.dlRaw.onclick=function(){ downloadBlob('GetPerson_raw_'+new Date().toISOString().replace(/[:.]/g,'-')+'.xml', raw||'', 'text/xml;charset=utf-8'); }; if(els.dlPretty) els.dlPretty.onclick=function(){ const c=(pretty||raw||''); downloadBlob('GetPerson_pretty_'+new Date().toISOString().replace(/[:.]/g,'-')+'.xml', c, 'text/xml;charset=utf-8'); }; }catch{} }
  async function fetchDetails(publicId){ const url=new URL(window.location.href); url.searchParams.set('handler','PersonDetails'); url.searchParams.set('publicId',publicId); try{ if(currentAbort) currentAbort.abort(); }catch{} currentAbort=(window.AbortController?new AbortController():null); const init={ headers:{'Accept':'application/json; charset=utf-8'} }; if(currentAbort) init.signal=currentAbort.signal; const resp=await fetch(url.toString(),init); const ct=(resp.headers.get('content-type')||'').toLowerCase(); let data=null; if(ct.includes('application/json')) data=await resp.json(); else { const text=await resp.text(); data={ ok:false, error: text&&text.length<500?text:'Non-JSON response ('+resp.status+')'}; } if(!resp.ok) data.ok=false; return data; }
  function buildErrorWithRetry(message,onRetry){ const els=getPanelEls(); if(!els.err) return; clearChildren(els.err); els.err.classList.remove('d-none'); els.err.setAttribute('aria-live','assertive'); const span=document.createElement('span'); span.textContent=message||'Failed to load details.'; const btn=document.createElement('button'); btn.type='button'; btn.id='pd-retry'; btn.className='btn btn-sm btn-outline-secondary ms-2'; btn.textContent='Retry'; btn.addEventListener('click',()=>{ try{ onRetry&&onRetry(); }catch{} }); els.err.appendChild(span); els.err.appendChild(btn); try{ btn.focus(); }catch{} }
  function addFadeIn(node){ try{ if(node) node.classList.add('pd-fade-in'); }catch{} }
  async function loadPerson(publicId,sourceEl){ window.lastPid=lastPid=publicId; const mySeq=++loadSeq; const els=getPanelEls(); if(!publicId||!els.body){ showInfo('Select a person to view details.'); return; } const cached=personCache.get(publicId); if(cached&&cached.details&&cached.details.length){ clearPanel(); const builder=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped); const combined=[...getPersonPublicInfoPairs(publicId), ...cached.details]; const accCached=builder(combined); if(mySeq!==loadSeq) return; if(accCached&&els.body) els.body.appendChild(accCached); setPdXml(cached.raw||'',cached.pretty||''); updateHeaderWithOfficialName(combined); ensureShownAndFocus(); focusFirstAccordionHeader(); } else { showLoading(true); } try { const data=await fetchDetails(publicId); if(mySeq!==loadSeq) return; showLoading(false); if(!data||data.ok!==true){ buildErrorWithRetry('Failed to load details.',()=>loadPerson(publicId,sourceEl)); ensureShownAndFocus(); return; }
      // Hide any previous error on success
      try{ const els2=getPanelEls(); if(els2.err){ els2.err.classList.add('d-none'); els2.err.textContent=''; } }catch{}
      const details=Array.isArray(data.details)?data.details:[];
      personCache.set(publicId,{ details:details, raw:data.raw||'', pretty:data.pretty||'', ts:Date.now() }); clearPanel(); const builder2=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped); const combined2=[...getPersonPublicInfoPairs(publicId), ...details]; const acc=builder2(combined2); if(acc&&els.body) els.body.appendChild(acc); else showInfo('No details to display.');
      setPdXml(data.raw||'', data.pretty||''); updateHeaderWithOfficialName(combined2); ensureShownAndFocus(); focusFirstAccordionHeader(); activatePdDetailsSubTab(); if(sourceEl) markSelectedSummaryCard(sourceEl); try { const link=new URL(window.location.href); const had=link.searchParams.has('publicId'); link.searchParams.delete('gpivPublicId'); link.searchParams.set('publicId', publicId); if(history){ if(had && history.pushState){ history.pushState(null,'',link.toString()); } else if(history.replaceState){ history.replaceState(null,'',link.toString()); } } }catch{} }catch(err){ if(mySeq!==loadSeq) return; showLoading(false); if(err&&err.name==='AbortError') return; buildErrorWithRetry('Failed to load details.',()=>loadPerson(publicId,sourceEl)); ensureShownAndFocus(); try{ console.error('GetPerson fetch failed',err); }catch{} }
  }

  document.addEventListener('click', function(e){ const tabBtn=(e.target&&e.target.closest)? e.target.closest('#gpiv-tab-details-btn'):null; if(!tabBtn) return; setTimeout(function(){ const pid=window.lastPid||findFirstPersonId(); if(pid){ loadPerson(pid); return;} clearPanel(); try{ const els=getPanelEls(); const acc=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped)([]); if(els.body&&acc) els.body.appendChild(acc); ensureShownAndFocus(); }catch{} waitForSummaryThenLoadOnceOrInform(); },0); });
  document.addEventListener('click', function(e){ const innerBtn=(e.target&&e.target.closest)? e.target.closest('#pd-tab-details-btn'):null; if(!innerBtn) return; setTimeout(function(){ const pid=window.lastPid||findFirstPersonId(); if(pid){ loadPerson(pid);} else { clearPanel(); try{ const els=getPanelEls(); const acc=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped)([]); if(els.body&&acc) els.body.appendChild(acc); ensureShownAndFocus(); }catch{} waitForSummaryThenLoadOnceOrInform(); } },0); });
  document.addEventListener('shown.bs.tab', function(e){ try{ const t=e&&e.target?e.target:null; if(!t)return; const id=t.getAttribute('id')||''; if(id==='gpiv-tab-details-btn'||id==='pd-tab-details-btn'){ const pid=window.lastPid||findFirstPersonId(); if(pid){ loadPerson(pid);} else { clearPanel(); try{ const els=getPanelEls(); const acc=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped)([]); if(els.body&&acc) els.body.appendChild(acc); ensureShownAndFocus(); }catch{} waitForSummaryThenLoadOnceOrInform(); } } }catch{} });
  document.addEventListener('click', function(e){ const fsBtn=(e.target&&e.target.closest)? e.target.closest('#pd-fullscreen'):null; if(!fsBtn) return; const sec=document.getElementById('person-details-section'); if(!sec) return; try{ const viewer=document.querySelector('.gpiv-card'); if(viewer) viewer.classList.remove('gpiv-fullscreen'); }catch{} try{ toggleFullscreenWithCssFallback(sec,'pd-fullscreen'); }catch{ sec.classList.toggle('pd-fullscreen'); };
});
document.addEventListener('fullscreenchange', function(){ const sec=document.getElementById('person-details-section'); if(!sec) return; const isFs=document.fullscreenElement===sec; sec.classList.toggle('pd-fullscreen', isFs); });
try{ const qs=new URLSearchParams(window.location.search); const pid=qs.get('publicId')||qs.get('gpivPublicId'); if(pid){ if(document.readyState==='loading'){ window.addEventListener('DOMContentLoaded',()=>loadPerson(pid)); } else { loadPerson(pid);} } }catch{}
window.addEventListener('popstate', function(){ try{ const q=new URLSearchParams(window.location.search); const pid=q.get('publicId')||q.get('gpivPublicId')||''; if(pid && pid!==window.lastPid){ loadPerson(pid); } }catch{} });
function prerenderShell(){ try{ const els=getPanelEls(); if(!els) return; if(els.sec) els.sec.classList.remove('d-none'); if(els.body && !els.body.childElementCount){ const acc=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped)([]); if(acc) els.body.appendChild(acc); } }catch{} }
function autoLoadInitialPerson(){ try{ if(document.getElementById('gpiv-xml-summary')) return; if(window.lastPid) return; const explicit=document.getElementById('gpiv-initial-publicid'); const hint=explicit? (explicit.getAttribute('data-initial-publicid')||''):''; if(hint){ loadPerson(hint); return; } const pid=findFirstPublicIdFromEmbeddedXml(); if(pid){ loadPerson(pid); } }catch{} }
if(document.readyState==='loading'){ document.addEventListener('DOMContentLoaded', function(){ prerenderShell(); setTimeout(autoLoadInitialPerson,50); }); } else { prerenderShell(); setTimeout(autoLoadInitialPerson,50); }
function pdToggleAll(open){ try{ const host=document.getElementById('person-details-section'); if(!host) return; toggleAllAccordions(host, open); // materialize any shown bodies lazily
    if(open){ try{ (host.querySelectorAll('.accordion-collapse.show .accordion-body')||[]).forEach(function(b){ if(b && b.dataset && b.dataset.rendered!=='1' && typeof b._render==='function'){ b._render(); } }); }catch{} } }catch{} }
document.addEventListener('click', function(e){ const ex=(e.target&&e.target.closest)? e.target.closest('#pd-expand-all'):null; if(ex){ pdToggleAll(true); return;} const col=(e.target&&e.target.closest)? e.target.closest('#pd-collapse-all'):null; if(col){ pdToggleAll(false); return;} });
document.addEventListener('keydown', function(e){ const scope=document.getElementById('person-details-section'); if(!scope) return; handleAccordionKeydown(e, scope); });
})();
