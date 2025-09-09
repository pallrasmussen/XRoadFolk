// Restore PublicId click -> load PersonDetails panel behavior
(function(){
  if (window.__gpivPidHooked) return; window.__gpivPidHooked = true;

  // === Updated helper to extract OfficialName (with fallback to FirstName + LastName) ===
  function extractOfficialName(pairs){
    try{
      if(!Array.isArray(pairs)) return '';
      // 1. Direct key hit (attribute or element named OfficialName)
      for(var i=0;i<pairs.length;i++){
        var k=(pairs[i].key||'').toLowerCase();
        if(!k) continue;
        if(k==='officialname' || k.endsWith('.officialname') || k.includes('.officialname')){
          var v=(pairs[i].value||'').trim(); if(v) return v;
        }
      }
      // 2. Pattern: ...Name[x].Type == 'OfficialName' + sibling Value
      var candidateBases=new Set();
      for(var j=0;j<pairs.length;j++){
        var pj=pairs[j];
        var keyj=pj.key||''; var valj=(pj.value||'').trim();
        if(!keyj) continue;
        if(/\.Type$/i.test(keyj) && /^officialname$/i.test(valj)){
          candidateBases.add(keyj.slice(0,-5));
        }
      }
      if(candidateBases.size){
        for(var k2=0;k2<pairs.length;k2++){
          var pk=pairs[k2]; var kk=pk.key||''; if(!kk) continue;
          if(/\.Value$/i.test(kk)){
            var base=kk.slice(0,-6);
            if(candidateBases.has(base)){
              var vv=(pk.value||'').trim(); if(vv) return vv;
            }
          }
        }
      }
      // 3. Fallback: first + last name (first encountered)
      var first=null, last=null;
      for(var f=0; f<pairs.length && (first===null || last===null); f++){
        var pkf=pairs[f]; var kf=(pkf.key||'').toLowerCase(); if(!kf) continue;
        if(first===null && (kf==='firstname' || kf.endsWith('.firstname'))) first=(pkf.value||'').trim()||null;
        else if(last===null && (kf==='lastname' || kf.endsWith('.lastname'))) last=(pkf.value||'').trim()||null;
      }
      var joined=[]; if(first) joined.push(first); if(last) joined.push(last); return joined.join(' ');
    }catch{ return ''; }
  }
  function updateHeaderWithOfficialName(pairs){
    try{ var hdr=document.getElementById('pd-title'); if(!hdr) return; if(!hdr.dataset.baseTitle){ hdr.dataset.baseTitle = hdr.textContent || 'Person Details'; } var off=extractOfficialName(pairs); hdr.textContent = off ? (hdr.dataset.baseTitle + ' – ' + off) : hdr.dataset.baseTitle; }catch{}
  }

  // Specific icon mapping (Bootstrap Icons)
  var ICON_MAP = {
    person:'bi-person-badge', basics:'bi-person-badge',
    names:'bi-fonts', nameshistory:'bi-clock-history',
    addresses:'bi-geo-alt', addresseshistory:'bi-geo',
    foreignssns:'bi-passport',
    ssns:'bi-passport', ssnhistory:'bi-clock-history',
    civilstatuses:'bi-people', civilstatushistory:'bi-journal-check', civilstatus:'bi-people',
    spouse:'bi-heart', spousehistory:'bi-heart-pulse',
    notes:'bi-journal-text', noteshistory:'bi-journal-medical',
    incapacity:'bi-slash-circle', incapacityhistory:'bi-slash-circle',
    specialmarks:'bi-star', specialmarkshistory:'bi-star-half',
    churchmembership:'bi-building', churchmembershiphistory:'bi-building',
    citizenships:'bi-flag', citizenshipshistory:'bi-flag',
    juridicalparents:'bi-people', juridicalparentshistory:'bi-people',
    juridicalchildren:'bi-people-fill', juridicalchildrenhistory:'bi-people-fill',
    biologicalparents:'bi-people', postbox:'bi-mailbox'
  };
  function iconClassFor(group){ var k=(group||'').toString().toLowerCase(); return ICON_MAP[k] || 'bi-question-circle'; }

  var H = (typeof window !== 'undefined' && window.gpivHelpers) ? window.gpivHelpers : {};
  var prettify = H.prettify || (s=>String(s||''));
  var parseAddressKey = H.parseAddressKey || function(){ return null; };
  var nextUid = H.nextUid || (p=> (p||'id')+'-'+Date.now());
  var handleAccordionKeydown = H.handleAccordionKeydown || function(){};
  var copyToClipboard = H.copyToClipboard || (async function(){ return false; });
  var downloadBlob = H.downloadBlob || function(){};
  var toggleFullscreenWithCssFallback = H.toggleFullscreenWithCssFallback || function(el,cls){ if(el&&el.classList) el.classList.toggle(cls); };
  var clearChildren = H.clearChildren || function(n){ try{ while(n&&n.firstChild){ n.removeChild(n.firstChild);} }catch{} };
  var toggleAllAccordions = H.toggleAllAccordions || function(scope,open){ try{ var panes=(scope||document).querySelectorAll('.accordion-collapse')||[]; panes.forEach(p=>p&&p.classList.toggle('show',!!open)); var btns=(scope||document).querySelectorAll('.accordion-button')||[]; btns.forEach(b=>{ if(!b)return; b.classList.toggle('collapsed',!open); b.setAttribute('aria-expanded', open?'true':'false'); }); }catch{} };
  var focusFirstAccordionButton = H.focusFirstAccordionButton || function(scope){ try{ var btn=(scope||document).querySelector('.accordion .accordion-header .accordion-button'); if(btn&&btn.focus) btn.focus(); }catch{} };

  // --- local renderer for addresses using parseAddressKey helper ---
  function renderAddressesCardList(items){
    var groups={}; (items||[]).forEach(it=>{ var parsed=parseAddressKey(it.k||it.key); if(!parsed) return; (groups[parsed.index]=groups[parsed.index]||[]).push({ field:parsed.field, value:it.v||it.value }); });
    var ids=Object.keys(groups).map(x=>parseInt(x,10)||0).sort((a,b)=>a-b); if(!ids.length) return null;
    var list=document.createElement('div'); list.className='d-flex flex-column gap-2';
    ids.forEach(idx=>{ var card=document.createElement('div'); card.className='p-2 border rounded'; var title=document.createElement('div'); title.className='small text-muted mb-1'; title.textContent='Address #'+idx; card.appendChild(title); var resp=document.createElement('div'); resp.className='table-responsive'; var table=document.createElement('table'); table.className='table table-sm table-striped align-middle mb-0'; var tb=document.createElement('tbody'); groups[idx].slice().sort((a,b)=>a.field.localeCompare(b.field)).forEach(r=>{ var tr=document.createElement('tr'); var th=document.createElement('th'); th.className='text-muted fw-normal'; th.style.width='36%'; th.textContent=r.field; var td=document.createElement('td'); td.textContent=r.value; tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr); }); table.appendChild(tb); resp.appendChild(table); card.appendChild(resp); list.appendChild(card); });
    return list;
  }

  var personCache = (window.personCache instanceof Map) ? window.personCache : (window.personCache=new Map());
  var lastPid = window.lastPid || null; var loadSeq=0; var currentAbort=null;

  function getPanelEls(){ return { sec:document.getElementById('person-details-section'), body:document.getElementById('person-details-body'), err:document.getElementById('person-details-error'), loading:document.getElementById('person-details-loading'), title:document.querySelector('#person-details-section .card-title'), rawPre:document.getElementById('pd-raw-pre'), prettyPre:document.getElementById('pd-pretty-pre'), copyRaw:document.getElementById('pd-copy-raw'), copyPretty:document.getElementById('pd-copy-pretty'), dlRaw:document.getElementById('pd-dl-raw'), dlPretty:document.getElementById('pd-dl-pretty'), pdDetailsBtn:document.getElementById('pd-tab-details-btn'), pdRawBtn:document.getElementById('pd-tab-raw-btn'), pdPrettyBtn:document.getElementById('pd-tab-pretty-btn'), pdDetailsPane:document.getElementById('pd-tab-details'), pdRawPane:document.getElementById('pd-tab-raw'), pdPrettyPane:document.getElementById('pd-tab-pretty') }; }

  function readI18n(){ try{ if(window.__gpivI18nCached) return window.__gpivI18nCached; var el=document.getElementById('gpiv-i18n-json'); var txt=el?(el.textContent||''):''; var obj={}; try{ obj=JSON.parse(txt||'{}')||{}; }catch{} window.__gpivI18nCached=obj; return obj; }catch{return{};} }

  function parseFirstPublicIdFromXmlText(xmlText){ try{ if(!xmlText||!xmlText.trim()) return ''; var parser=new DOMParser(); var doc=parser.parseFromString(xmlText,'application/xml'); if(doc.querySelector('parsererror')) return ''; var all=doc.getElementsByTagName('*'); var first=null; for(var i=0;i<all.length;i++){ if(all[i].localName==='PersonPublicInfo'){ first=all[i]; break; } } if(!first){ for(var j=0;j<all.length;j++){ if(all[j].localName==='Person'){ first=all[j]; break; } } } if(!first) return ''; var kids=first.getElementsByTagName('*'); for(var k=0;k<kids.length;k++){ var ln=kids[k].localName; if(ln==='publicid'||ln==='personid'){ var pid=(kids[k].textContent||'').trim(); if(pid) return pid; } } return ''; }catch{return '';} }
  function findFirstPersonIdFromEmbeddedXml(){ try{ var rawEl=document.getElementById('gpiv-raw-json'); var prettyEl=document.getElementById('gpiv-pretty-json'); var raw=rawEl?JSON.parse(rawEl.textContent||'""'):''; var pretty=prettyEl?JSON.parse(prettyEl.textContent||'""'):''; if((!raw||!raw.length)&&(!pretty||!pretty.length)){ var host=document.getElementById('gpiv-data'); if(host){ var rb=host.getAttribute('data-raw-b64')||''; var pb=host.getAttribute('data-pretty-b64')||''; try{ var rawText=rb?decodeURIComponent(escape(window.atob(rb))):''; var prettyText=pb?decodeURIComponent(escape(window.atob(pb))):''; return parseFirstPublicIdFromXmlText(rawText||prettyText||''); }catch{} } } return parseFirstPublicIdFromXmlText(raw||pretty||''); }catch{return '';} }

  // Labels: separate Civil Statuses (current) and Civil Status History
  function headerLabelForGroup(rawName){ var i18n=readI18n(); var t=String(rawName||''); var lower=t.toLowerCase();
    if(lower==='person'||lower==='basics') return i18n.Basics||'Basics';
    if(lower==='names'||lower==='name') return i18n.Names||'Names';
    if(lower==='addresses'||lower==='address') return i18n.Addresses||'Addresses';
    if(lower==='addresseshistory') return 'Addresses History';
    if(lower==='foreignssns'||lower==='foreignssn') return 'Foreign SSNs';
    if(lower==='juridicalparents'||lower==='juridicalparent') return 'Juridical Parents';
    if(lower==='juridicalparentshistory') return 'Juridical Parents History';
    if(lower==='biologicalparents'||lower==='biologicalparent') return 'Biological Parents';
    if(lower==='civilstatuses') return 'Civil Statuses';
    if(lower==='civilstatushistory') return 'Civil Status History';
    if(lower==='ssns'||lower==='ssn') return 'SSNs';
    if(lower==='ssnhistory') return 'SSN History';
    if(lower==='spouse') return 'Spouse';
    if(lower==='spousehistory') return 'Spouse History';
    if(lower==='notes') return 'Notes';
    if(lower==='noteshistory') return 'Notes History';
    if(lower==='incapacity') return 'Incapacity';
    if(lower==='incapacityhistory') return 'Incapacity History';
    if(lower==='specialmarks') return 'Special Marks';
    if(lower==='specialmarkshistory') return 'Special Marks History';
    if(lower==='churchmembership') return 'Church Membership';
    if(lower==='churchmembershiphistory') return 'Church Membership History';
    if(lower==='citizenships') return 'Citizenships';
    if(lower==='citizenshipshistory') return 'Citizenships History';
    if(lower==='juridicalchildren') return 'Juridical Children';
    if(lower==='juridicalchildrenhistory') return 'Juridical Children History';
    if(lower==='postbox') return 'Postbox';
    if(lower==='publicid'||lower==='pid') return i18n.PublicId||'Public Id';
    if(lower==='dob'||lower==='dateofbirth') return i18n.DOB||'Date of Birth';
    if(lower==='status') return 'Status';
    return prettify(t);
  }

  function pickGroupFromKey(key){ var raw=String(key||''); if(!raw) return ''; var parts=raw.split('.'); var segs=[]; for(var i=0;i<parts.length;i++){ var p=parts[i]||''; var b=p.indexOf('['); if(b>=0) p=p.slice(0,b); p=p.trim(); if(p) segs.push(p);} if(!segs.length) return ''; var idx=0; if(/(?:response|result)$/i.test(segs[0])) idx=1; if(idx>=segs.length) return ''; var first=segs[idx]; function norm(seg){ if(/^addresseshistory$/i.test(seg)) return 'AddressesHistory'; if(/^address(?:es)?$/i.test(seg)) return 'Addresses'; if(/^name(?:s)?$/i.test(seg)) return 'Names'; if(/^foreignssn(?:s)?$/i.test(seg)) return 'ForeignSsns'; if(/^juridicalparent(?:s)?history$/i.test(seg)) return 'JuridicalParentsHistory'; if(/^juridicalparent(?:s)?$/i.test(seg)) return 'JuridicalParents'; if(/^ssn(?:s)?$/i.test(seg)) return 'Ssns'; if(/^civilstatushistory$/i.test(seg)) return 'CivilStatusHistory'; if(/^civilstatus$/i.test(seg)) return 'CivilStatuses'; if(/^noteshistory$/i.test(seg)) return 'NotesHistory'; if(/^notes$/i.test(seg)) return 'Notes'; if(/^status$/i.test(seg)) return ''; return seg; } if(/^person$/i.test(first)){ if(segs.length>=idx+3){ return norm(segs[idx+1]); } return 'Person'; } return norm(first); }

  function activateDetailsTab(){ try{ var btn=document.getElementById('gpiv-tab-details-btn'); var pane=document.getElementById('gpiv-tab-details'); if(!btn||!pane) return; document.querySelectorAll('#gpiv-tabs button[data-bs-target]').forEach(b=>{ b.classList.remove('active'); b.setAttribute('aria-selected','false'); }); document.querySelectorAll('#gpiv-tabs-content .tab-pane').forEach(p=>p.classList.remove('show','active')); btn.classList.add('active'); btn.setAttribute('aria-selected','true'); pane.classList.add('show','active'); }catch{} }
  function activatePdDetailsSubTab(){ try{ var els=getPanelEls(); if(!els.pdDetailsBtn||!els.pdDetailsPane) return; [els.pdDetailsBtn,els.pdRawBtn,els.pdPrettyBtn].forEach(b=>{ if(!b)return; b.classList.remove('active'); b.setAttribute('aria-selected','false'); }); [els.pdDetailsPane,els.pdRawPane,els.pdPrettyPane].forEach(p=>{ if(!p)return; p.classList.remove('show','active'); }); els.pdDetailsBtn.classList.add('active'); els.pdDetailsBtn.setAttribute('aria-selected','true'); els.pdDetailsPane.classList.add('show','active'); }catch{} }

  function showLoading(on){ var e=getPanelEls(); if(!e.sec) return; activateDetailsTab(); activatePdDetailsSubTab(); e.sec.classList.remove('d-none'); if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; } if(e.body && on){ try{ clearChildren(e.body);}catch{} } if(e.loading){ try{ e.loading.classList.toggle('d-none', !on);}catch{} } }
  function clearPanel(){ var e=getPanelEls(); if(!e.sec) return; activateDetailsTab(); activatePdDetailsSubTab(); e.sec.classList.remove('d-none'); if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; } if(e.body){ try{ clearChildren(e.body);}catch{} } if(e.loading){ try{ e.loading.classList.add('d-none'); }catch{} } }
  function showInfo(msg){ var e=getPanelEls(); if(!e.sec) return; activateDetailsTab(); activatePdDetailsSubTab(); e.sec.classList.remove('d-none'); if(e.loading) e.loading.classList.add('d-none'); if(e.err){ e.err.classList.add('d-none'); e.err.textContent=''; } if(e.body){ var div=document.createElement('div'); div.className='alert alert-info mb-0'; div.setAttribute('role','status'); div.textContent=msg||'Select a person to view details.'; try{ clearChildren(e.body);}catch{} try{ e.body.appendChild(div);}catch{} } }
  function ensureShownAndFocus(){ var e=getPanelEls(); if(!e.sec) return; activateDetailsTab(); activatePdDetailsSubTab(); e.sec.classList.remove('d-none'); if(e.title){ e.title.setAttribute('tabindex','-1'); try{ e.title.focus({preventScroll:true}); }catch{} try{ e.title.scrollIntoView({block:'start',behavior:'smooth'});}catch{} } }
  function markSelectedSummaryCard(fromEl){ try{ var host=document.getElementById('gpiv-xml-summary'); if(!host) return; var prev=host.querySelector('.gpiv-active-card'); if(prev) prev.classList.remove('gpiv-active-card'); var wrap=fromEl; while(wrap&&wrap!==host&&!(wrap.classList&&wrap.classList.contains('border')&&wrap.classList.contains('rounded'))) wrap=wrap.parentElement; if(wrap&&wrap!==host) wrap.classList.add('gpiv-active-card'); }catch{} }

  function findFirstPersonId(){ try{ var host=document.getElementById('gpiv-xml-summary'); if(!host) return ''; var el=host.querySelector('[data-public-id],[data-publicid]'); if(el) return el.getAttribute('data-public-id')||el.getAttribute('data-publicid')||(el.dataset? (el.dataset.publicId||el.dataset.publicid||''): ''); return findFirstPublicIdFromEmbeddedXml(); }catch{return '';} }
  function waitForSummaryThenLoadOnceOrInform(){ try{ var host=document.getElementById('gpiv-xml-summary'); if(!host){ var pid0=findFirstPublicIdFromEmbeddedXml(); if(pid0){ loadPerson(pid0); } else { showInfo('Select a person to view details.'); } return; } var pidNow=findFirstPersonId(); if(pidNow){ loadPerson(pidNow); return; } if(!window.MutationObserver){ showInfo('Select a person to view details.'); return; } var informed=false; var mo=new MutationObserver(function(){ var pid=findFirstPersonId(); if(pid){ try{ mo.disconnect(); }catch{} loadPerson(pid); } else if(!informed){ informed=true; try{ mo.disconnect(); }catch{} showInfo('Select a person to view details.'); } }); mo.observe(host,{childList:true,subtree:true}); setTimeout(function(){ if(!informed){ try{ mo.disconnect(); }catch{} showInfo('Select a person to view details.'); } },800); }catch{ showInfo('Select a person to view details.'); } }

  function defaultRenderPairsGrouped(pairs){
    function createPlaceholder(text){ var ph=document.createElement('div'); ph.className='text-muted small p-2'; ph.textContent=text; return ph; }
    function normIncludeName(n){ n=String(n||'').trim(); if(!n) return ''; var map={ nameshistory:'NamesHistory', names:'Names', addresseshistory:'AddressesHistory', addresses:'Addresses', foreignssns:'ForeignSsns', juridicalparents:'JuridicalParents', biologicalparents:'BiologicalParents', civilstatus:'CivilStatuses', civilstatushistory:'CivilStatusHistory', spouse:'Spouse', spousehistory:'SpouseHistory', notes:'Notes', noteshistory:'NotesHistory', incapacity:'Incapacity', incapacityhistory:'IncapacityHistory', specialmarks:'SpecialMarks', specialmarkshistory:'SpecialMarksHistory', postbox:'Postbox', juridicalchildren:'JuridicalChildren', juridicalchildrenhistory:'JuridicalChildrenHistory', ssn:'Ssns', ssns:'Ssns', ssnhistory:'SsnHistory', churchmembership:'ChurchMembership', churchmembershiphistory:'ChurchMembershipHistory', citizenships:'Citizenships', citizenshipshistory:'CitizenshipsHistory'}; var k=n.toLowerCase(); return map[k]||n; }

    var allowed=new Set(['Person']);
    try{ var incEl=document.getElementById('gpiv-includes-json'); if(incEl){ var inc=JSON.parse(incEl.textContent||'[]')||[]; if(Array.isArray(inc)){ inc.forEach(g=>{ var nn=normIncludeName(g); if(nn) allowed.add(nn); }); } } }catch{}

    var groups={}; (pairs||[]).forEach(function(p){ var k=p.key||'', v=p.value||''; var seg=pickGroupFromKey(k); if(!seg) return; if(/^addresseshistory$/i.test(seg)) seg='AddressesHistory'; if(/^juridicalparentshistory$/i.test(seg)) seg='JuridicalParentsHistory'; if(/^ssn(?:s)?$/i.test(seg)) seg='Ssns'; if(/^civilstatushistory$/i.test(seg)) seg='CivilStatusHistory'; if(/^civilstatuses$/i.test(seg)) seg='CivilStatuses'; if(/^noteshistory$/i.test(seg)) seg='NotesHistory'; if(/^notes$/i.test(seg)) seg='Notes'; if(!allowed.has(seg) && seg!=='Person') return; (groups[seg]=groups[seg]||[]).push({k:k,v:v}); });
    allowed.forEach(g=>{ if(g!=='Person' && !Object.prototype.hasOwnProperty.call(groups,g)) groups[g]=[]; });

    var keys=Object.keys(groups).sort(function(a,b){ var al=a.toLowerCase(), bl=b.toLowerCase(); var ai=al==='person'?0:1; var bi=bl==='person'?0:1; return (ai-bi)||a.localeCompare(b); });
    var accId=nextUid('pdacc'); var acc=document.createElement('div'); acc.className='accordion'; acc.id=accId;
    keys.forEach(function(name,gi){ var items=groups[name].slice(); var hid=accId+'-h-'+gi, cid=accId+'-c-'+gi; var item=document.createElement('div'); item.className='accordion-item'; item.setAttribute('data-group',name); var h2=document.createElement('h2'); h2.className='accordion-header'; h2.id=hid; var open=false; var btn=document.createElement('button'); btn.className='accordion-button'+(open?'':' collapsed'); btn.type='button'; btn.setAttribute('data-bs-toggle','collapse'); btn.setAttribute('data-bs-target','#'+cid); btn.setAttribute('aria-expanded',open?'true':'false'); btn.setAttribute('aria-controls',cid); var ic=document.createElement('i'); ic.className='bi '+iconClassFor(name)+' me-2'; ic.setAttribute('aria-hidden','true'); btn.appendChild(ic); btn.appendChild(document.createTextNode(headerLabelForGroup(name))); h2.appendChild(btn); var col=document.createElement('div'); col.id=cid; col.className='accordion-collapse collapse'+(open?' show':''); col.setAttribute('aria-labelledby',hid); col.setAttribute('data-bs-parent','#'+accId); var body=document.createElement('div'); body.className='accordion-body p-0'; var lname=String(name||'').toLowerCase(); var i18n=readI18n(); function simpleTable(sorted){ var respWrap=document.createElement('div'); respWrap.className='table-responsive'; var table=document.createElement('table'); table.className='table table-sm table-striped align-middle mb-0'; var tb=document.createElement('tbody'); sorted.forEach(it=>{ var k=it.k,v=it.v; var lastDot=k.lastIndexOf('.'); var sub=lastDot>=0?k.slice(lastDot+1):k; var bpos=sub.indexOf('['); if(bpos>=0) sub=sub.slice(0,bpos); var tr=document.createElement('tr'); var th=document.createElement('th'); th.className='text-muted fw-normal'; th.style.width='36%'; th.textContent=sub; var td=document.createElement('td'); td.textContent=v; tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr); }); table.appendChild(tb); respWrap.appendChild(table); return respWrap; }
      if(lname==='addresses'||lname==='address'){ if(!items.length){ body.appendChild(createPlaceholder(i18n.NoAddresses||'No addresses.')); } else { var list=renderAddressesCardList(items); if(list) body.appendChild(list); else body.appendChild(createPlaceholder(i18n.NoAddresses||'No addresses.')); } }
      else if(lname==='foreignssns'||lname==='foreignssn'){ if(!items.length){ body.appendChild(createPlaceholder(i18n.NoForeignSsns||'No foreign SSNs.')); } else { body.appendChild(simpleTable(items.sort((x,y)=>x.k.localeCompare(y.k)))); } }
      else if(lname==='person'||lname==='basics'){ if(!items.length){ body.appendChild(createPlaceholder(i18n.NoBasics||'No basic information.')); } else { body.appendChild(simpleTable(items.sort((x,y)=>x.k.localeCompare(y.k)))); } }
      else if(lname==='names'||lname==='name'){ if(!items.length){ body.appendChild(createPlaceholder(i18n.NoNames||'No names.')); } else { body.appendChild(simpleTable(items.sort((x,y)=>x.k.localeCompare(y.k)))); } }
      else if(lname==='juridicalparents'||lname==='juridicalparent'){ if(!items.length){ body.appendChild(createPlaceholder(i18n.NoJuridicalParents||'No juridical parents.')); } else { body.appendChild(simpleTable(items.sort((x,y)=>x.k.localeCompare(y.k)))); } }
      else { if(!items.length){ body.appendChild(createPlaceholder(i18n.NoData||'No data.')); } else { body.appendChild(simpleTable(items.sort((x,y)=>x.k.localeCompare(y.k)))); } }
      col.appendChild(body); item.appendChild(h2); item.appendChild(col); acc.appendChild(item); });
    return acc; }

  function focusFirstAccordionHeader(){ try{ var els=getPanelEls(); if(!els||!els.body) return; focusFirstAccordionButton(els.body); }catch{} }
  function setPdXml(raw,pretty){ try{ var els=getPanelEls(); if(els.rawPre) els.rawPre.textContent=raw||''; if(els.prettyPre) els.prettyPre.textContent=pretty||raw||''; if(els.copyRaw) els.copyRaw.onclick=function(){ copyToClipboard(raw||'').then(()=>{ els.copyRaw.textContent=(readI18n().Copied||'Copied'); setTimeout(()=>{ els.copyRaw.textContent=(readI18n().Copy||'Copy'); },1200); }); }; if(els.copyPretty) els.copyPretty.onclick=function(){ copyToClipboard(pretty||raw||'').then(()=>{ els.copyPretty.textContent=(readI18n().Copied||'Copied'); setTimeout(()=>{ els.copyPretty.textContent=(readI18n().Copy||'Copy'); },1200); }); }; if(els.dlRaw) els.dlRaw.onclick=function(){ downloadBlob('GetPerson_raw_'+new Date().toISOString().replace(/[:.]/g,'-')+'.xml', raw||'', 'text/xml;charset=utf-8'); }; if(els.dlPretty) els.dlPretty.onclick=function(){ var c=(pretty||raw||''); downloadBlob('GetPerson_pretty_'+new Date().toISOString().replace(/[:.]/g,'-')+'.xml', c, 'text/xml;charset=utf-8'); }; }catch{} }
  async function fetchDetails(publicId){ var url=new URL(window.location.href); url.searchParams.set('handler','PersonDetails'); url.searchParams.set('publicId',publicId); try{ if(currentAbort) currentAbort.abort(); }catch{} currentAbort=(window.AbortController?new AbortController():null); var init={ headers:{'Accept':'application/json; charset=utf-8'} }; if(currentAbort) init.signal=currentAbort.signal; var resp=await fetch(url.toString(),init); var ct=(resp.headers.get('content-type')||'').toLowerCase(); var data=null; if(ct.includes('application/json')) data=await resp.json(); else { var text=await resp.text(); data={ ok:false, error: text&&text.length<500?text:'Non-JSON response ('+resp.status+')'}; } if(!resp.ok) data.ok=false; return data; }
  function buildErrorWithRetry(message,onRetry){ var els=getPanelEls(); if(!els.err) return; clearChildren(els.err); els.err.classList.remove('d-none'); els.err.setAttribute('aria-live','assertive'); var span=document.createElement('span'); span.textContent=message||'Failed to load details.'; var btn=document.createElement('button'); btn.type='button'; btn.id='pd-retry'; btn.className='btn btn-sm btn-outline-secondary ms-2'; btn.textContent='Retry'; btn.addEventListener('click',()=>{ try{ onRetry&&onRetry(); }catch{} }); els.err.appendChild(span); els.err.appendChild(btn); try{ btn.focus(); }catch{} }
  async function loadPerson(publicId,sourceEl){
    window.lastPid=lastPid=publicId; var mySeq=++loadSeq; var els=getPanelEls();
    if(!publicId||!els.body){ showInfo('Select a person to view details.'); return; }
    var cached=personCache.get(publicId);
    if(cached&&cached.details&&cached.details.length){
      clearPanel(); var builder=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped); var accCached=builder(cached.details);
      if(mySeq!==loadSeq) return; if(accCached&&els.body) els.body.appendChild(accCached); setPdXml(cached.raw||'',cached.pretty||''); updateHeaderWithOfficialName(cached.details); ensureShownAndFocus(); focusFirstAccordionHeader();
    } else { showLoading(true); }
    try {
      var data=await fetchDetails(publicId); if(mySeq!==loadSeq) return; showLoading(false);
      if(!data||data.ok!==true){ buildErrorWithRetry('Failed to load details.',()=>loadPerson(publicId,sourceEl)); ensureShownAndFocus(); return; }
      var details=Array.isArray(data.details)?data.details:[]; personCache.set(publicId,{ details:details, raw:data.raw||'', pretty:data.pretty||'', ts:Date.now() });
      clearPanel(); var builder2=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped); var acc=builder2(details); if(acc&&els.body) els.body.appendChild(acc); else showInfo('No details to display.');
      setPdXml(data.raw||'', data.pretty||''); updateHeaderWithOfficialName(details); ensureShownAndFocus(); focusFirstAccordionHeader(); activatePdDetailsSubTab(); if(sourceEl) markSelectedSummaryCard(sourceEl);
      try {
        var link=new URL(window.location.href);
        var had=link.searchParams.has('publicId');
        // Drop legacy param if present
        link.searchParams.delete('gpivPublicId');
        link.searchParams.set('publicId', publicId);
        if(history){
          if(had && history.pushState){ history.pushState(null,'',link.toString()); }
          else if(history.replaceState){ history.replaceState(null,'',link.toString()); }
        }
      }catch{}
    }catch(err){ if(mySeq!==loadSeq) return; showLoading(false); if(err&&err.name==='AbortError') return; buildErrorWithRetry('Failed to load details.',()=>loadPerson(publicId,sourceEl)); ensureShownAndFocus(); try{ console.error('GetPerson fetch failed',err); }catch{} }
  }

  document.addEventListener('click', function(e){ var tabBtn=(e.target&&e.target.closest)? e.target.closest('#gpiv-tab-details-btn'):null; if(!tabBtn) return; setTimeout(function(){ var pid=window.lastPid||findFirstPersonId(); if(pid){ loadPerson(pid); return;} clearPanel(); try{ var els=getPanelEls(); var acc=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped)([]); if(els.body&&acc) els.body.appendChild(acc); ensureShownAndFocus(); }catch{} waitForSummaryThenLoadOnceOrInform(); },0); });
  document.addEventListener('click', function(e){ var innerBtn=(e.target&&e.target.closest)? e.target.closest('#pd-tab-details-btn'):null; if(!innerBtn) return; setTimeout(function(){ var pid=window.lastPid||findFirstPersonId(); if(pid){ loadPerson(pid);} else { clearPanel(); try{ var els=getPanelEls(); var acc=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped)([]); if(els.body&&acc) els.body.appendChild(acc); ensureShownAndFocus(); }catch{} waitForSummaryThenLoadOnceOrInform(); } },0); });
  document.addEventListener('shown.bs.tab', function(e){ try{ var t=e&&e.target?e.target:null; if(!t)return; var id=t.getAttribute('id')||''; if(id==='gpiv-tab-details-btn'||id==='pd-tab-details-btn'){ var pid=window.lastPid||findFirstPersonId(); if(pid){ loadPerson(pid);} else { clearPanel(); try{ var els=getPanelEls(); var acc=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped)([]); if(els.body&&acc) els.body.appendChild(acc); ensureShownAndFocus(); }catch{} waitForSummaryThenLoadOnceOrInform(); } } }catch{} });

  document.addEventListener('click', function(e){ var fsBtn=(e.target&&e.target.closest)? e.target.closest('#pd-fullscreen'):null; if(!fsBtn) return; var sec=document.getElementById('person-details-section'); if(!sec) return; try{ var viewer=document.querySelector('.gpiv-card'); if(viewer) viewer.classList.remove('gpiv-fullscreen'); }catch{} try{ toggleFullscreenWithCssFallback(sec,'pd-fullscreen'); }catch{ sec.classList.toggle('pd-fullscreen'); } });
  document.addEventListener('fullscreenchange', function(){ var sec=document.getElementById('person-details-section'); if(!sec) return; var isFs=document.fullscreenElement===sec; sec.classList.toggle('pd-fullscreen', isFs); });

  try{ var qs=new URLSearchParams(window.location.search); var pid=qs.get('publicId')||qs.get('gpivPublicId'); if(pid){ if(document.readyState==='loading'){ window.addEventListener('DOMContentLoaded',()=>loadPerson(pid)); } else { loadPerson(pid);} } }catch{}
  // Popstate handler to support back/forward navigation between persons
  window.addEventListener('popstate', function(){ try{ var q=new URLSearchParams(window.location.search); var pid=q.get('publicId')||q.get('gpivPublicId')||''; if(pid && pid!==window.lastPid){ loadPerson(pid); } }catch{} });

  function prerenderShell(){ try{ var els=getPanelEls(); if(!els) return; if(els.sec) els.sec.classList.remove('d-none'); if(els.body && !els.body.childElementCount){ var acc=(window._renderPairsGroupedForPerson||defaultRenderPairsGrouped)([]); if(acc) els.body.appendChild(acc); } }catch{} }
  function autoLoadInitialPerson(){ try{ if(document.getElementById('gpiv-xml-summary')) return; if(window.lastPid) return; var explicit=document.getElementById('gpiv-initial-publicid'); var hint=explicit? (explicit.getAttribute('data-initial-publicid')||''):''; if(hint){ loadPerson(hint); return; } var pid=findFirstPublicIdFromEmbeddedXml(); if(pid){ loadPerson(pid); } }catch{} }
  if(document.readyState==='loading'){
    document.addEventListener('DOMContentLoaded', function(){ prerenderShell(); setTimeout(autoLoadInitialPerson, 50); });
  } else {
    prerenderShell();
    setTimeout(autoLoadInitialPerson, 50);
  }

  function pdToggleAll(open){ try{ var host=document.getElementById('person-details-section'); if(!host) return; toggleAllAccordions(host, open); }catch{} }
  document.addEventListener('click', function(e){ var ex=(e.target&&e.target.closest)? e.target.closest('#pd-expand-all'):null; if(ex){ pdToggleAll(true); return;} var col=(e.target&&e.target.closest)? e.target.closest('#pd-collapse-all'):null; if(col){ pdToggleAll(false); return;} });
  document.addEventListener('keydown', function(e){ var scope=document.getElementById('person-details-section'); if(!scope) return; handleAccordionKeydown(e, scope); });
})();
