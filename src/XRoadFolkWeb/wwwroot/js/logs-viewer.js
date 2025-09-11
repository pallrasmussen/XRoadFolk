// ES module: Logs viewer
(function(){
  'use strict';

  const dbg=(m,e)=>{ try{ if(e&&console&&console.debug) console.debug(m,e);}catch(_){} };

  function getI18n(){
    try { const el=document.getElementById('logs-i18n-json'); return el? JSON.parse(el.textContent||'{}') : {}; } catch (e) { dbg('logs:i18n', e); return {}; }
  }

  let initialized = false;
  function init(){
    if (initialized) return; initialized = true;
    const SR = getI18n();
    const root = document.getElementById('logs-view');
    let kind = (root && root.getAttribute('data-default-kind')) || 'http';
    let view = 'table';

    let paused = false;
    let filterTxt = '';
    let filterLevel = '';
    let es = null;
    let reconnectDelayMs = 500; // start small

    const tbody = document.querySelector('#logs-table tbody');
    const tableContainer = document.querySelector('.logs-table-container');
    const cards = document.getElementById('logs-cards');
    const filter = document.getElementById('logs-filter');
    const level = document.getElementById('logs-level');
    const clearBtn = document.getElementById('logs-clear');
    const pauseBtn = document.getElementById('logs-pause');
    const downloadBtn = document.getElementById('logs-download');
    const statusEl = document.getElementById('logs-status');
    const countEl = document.getElementById('logs-count');

    const pauseTextPause = (pauseBtn && pauseBtn.getAttribute('data-i18n-pause')) || 'Pause';
    const pauseTextResume = (pauseBtn && pauseBtn.getAttribute('data-i18n-resume')) || 'Resume';

    const el=(tag, cls, text)=>{ const e=document.createElement(tag); if(cls) e.className=cls; if(text!=null) e.textContent=String(text); return e; };
    const lvlBadgeEl=l=>{ const span=el('span','badge lvl-badge badge-'+String(l||''), String(l||'')); return span; };
    const pad=(n,l=2)=>{ n=String(n); return n.length>=l?n:('0'.repeat(l-n.length)+n); };
    const fmtLocal=ts=>{ try{ const d=new Date(ts); if(isNaN(d)) return ts; const yyyy=d.getFullYear(), mm=pad(d.getMonth()+1), dd=pad(d.getDate()), HH=pad(d.getHours()), MM=pad(d.getMinutes()), SS=pad(d.getSeconds()), mmm=pad(d.getMilliseconds(),3); const off=-d.getTimezoneOffset(); const sign=off>=0?'+':'-'; const abs=Math.abs(off); const oh=pad(Math.floor(abs/60)); const om=pad(abs%60); return `${yyyy}-${mm}-${dd} ${HH}:${MM}:${SS}.${mmm} ${sign}${oh}:${om}`; }catch{return ts;} };

    function rowFor(entry){
      const tr=document.createElement('tr');
      const tdTs=el('td', null, fmtLocal(entry.timestamp)); tr.appendChild(tdTs);
      const tdLvl=el('td'); tdLvl.appendChild(lvlBadgeEl(entry.level)); tr.appendChild(tdLvl);
      const tdCat=el('td', null, entry.category||''); tr.appendChild(tdCat);
      const tdMsg=el('td', null, entry.message||''); tr.appendChild(tdMsg);
      const tdAct=el('td','text-end');
      const copyBtn=el('button','btn btn-sm btn-outline-dark', SR.Copy||'Copy');
      copyBtn.setAttribute('data-action','copy');
      copyBtn.setAttribute('aria-label','Copy');
      tdAct.appendChild(copyBtn);
      tr.appendChild(tdAct);
      tr.__entry = entry;
      return tr;
    }

    function cardFor(entry){
      const col=el('div','col'); col.setAttribute('role','article');
      const card=el('div','card log-card'); card.setAttribute('data-level', String(entry.level||''));
      const body=el('div','card-body');

      const top=el('div','d-flex justify-content-between align-items-start');
      const left=el('div');
      left.appendChild(el('div','small text-muted', entry.category||''));
      left.appendChild(el('div','fw-semibold', fmtLocal(entry.timestamp)));
      top.appendChild(left);
      top.appendChild(lvlBadgeEl(entry.level));
      body.appendChild(top);

      body.appendChild(el('div','log-message mt-2', entry.message||''));

      if (entry.exception){
        const details=el('details','mt-2');
        details.appendChild(el('summary', null, 'Exception'));
        const pre=el('pre','mb-0'); pre.textContent = entry.exception;
        details.appendChild(pre);
        body.appendChild(details);
      }

      const actions=el('div','d-flex justify-content-end mt-2');
      const copyBtn2=el('button','btn btn-sm btn-outline-dark', SR.Copy||'Copy');
      copyBtn2.setAttribute('data-action','copy');
      copyBtn2.setAttribute('aria-label','Copy');
      actions.appendChild(copyBtn2);
      body.appendChild(actions);

      card.appendChild(body);
      col.appendChild(card);
      col.__entry = entry;
      return col;
    }

    function syncViewVisibility(){
      if (view === 'cards'){
        cards.classList.remove('d-none');
        cards.setAttribute('aria-hidden','false');
        tableContainer.classList.add('d-none');
        tableContainer.setAttribute('aria-hidden','true');
      } else {
        tableContainer.classList.remove('d-none');
        tableContainer.setAttribute('aria-hidden','false');
        cards.classList.add('d-none');
        cards.setAttribute('aria-hidden','true');
      }
      // Ensure any queued entries render into the now-visible pane
      flush();
    }

    const MAX_ROWS=3000, TRIM_TO_ROWS=2000, BATCH_FLUSH_MS=50;
    let pending=[], pendingCards=[], flushTimer=0;
    let visible=[];

    const cancelFlush=()=>{ if (flushTimer){ try{ clearTimeout(flushTimer);}catch(e){ dbg('logs:cancelFlush', e);} flushTimer=0; } };
    function scheduleFlush(){ if (flushTimer) return; flushTimer=setTimeout(flush,BATCH_FLUSH_MS); }
    function flush(){
      flushTimer=0; if(!pending.length && !pendingCards.length) return;
      let autoscroll=false; try{ if(view==='table' && tableContainer){ const near=(tableContainer.scrollTop+tableContainer.clientHeight)>=(tableContainer.scrollHeight-8); autoscroll=near; } }catch(e){ dbg('logs:autoscroll', e); }
      if(pending.length){ const frag=document.createDocumentFragment(); for(let i=0;i<pending.length;i++) frag.appendChild(pending[i]); pending=[]; tbody.appendChild(frag); trimIfNeeded(); }
      if(pendingCards.length){ const frag2=document.createDocumentFragment(); for(let j=0;j<pendingCards.length;j++) frag2.appendChild(pendingCards[j]); pendingCards=[]; cards.appendChild(frag2); trimCardsIfNeeded(); }
      if(autoscroll && tableContainer){ try{ tableContainer.scrollTop=tableContainer.scrollHeight; }catch(e){ dbg('logs:scroll', e); } }
      if(view==='cards'){ visible=Array.prototype.map.call(cards.children, el => el.__entry).filter(Boolean);} else { visible=Array.prototype.map.call(tbody.children, tr => tr.__entry).filter(Boolean);} 
      if(countEl) countEl.textContent=String(visible.length||0);
      if(statusEl) statusEl.textContent=(visible.length||0)+' '+(SR.Entries||'entries');
    }

    function trimIfNeeded(){ try{ const extra=tbody.children.length-MAX_ROWS; if(extra>0){ const toRemove=Math.max(extra, tbody.children.length-TRIM_TO_ROWS); for(let i=0;i<toRemove;i++){ if(!tbody.firstChild) break; tbody.removeChild(tbody.firstChild);} } }catch(e){ dbg('logs:trimRows', e); } }
    function trimCardsIfNeeded(){ try{ const extra=cards.children.length-MAX_ROWS; if(extra>0){ const toRemove=Math.max(extra, cards.children.length-TRIM_TO_ROWS); for(let i=0;i<toRemove;i++){ if(!cards.firstChild) break; cards.removeChild(cards.firstChild);} } }catch(e){ dbg('logs:trimCards', e); } }

    const match=entry=>{ if(filterLevel && String(entry.level).toLowerCase()!==filterLevel) return false; if(filterTxt){ const t=(entry.message||'')+' '+(entry.category||''); if(t.toLowerCase().indexOf(filterTxt)<0) return false; } return true; };
    function append(entry){ if(!match(entry)) return; const tr=rowFor(entry); pending.push(tr); const col=cardFor(entry); pendingCards.push(col); scheduleFlush(); }
    function connect(){ if(es) try{ es.close(); }catch(e){ dbg('logs:es-close', e);} es=new EventSource('/logs/stream?kind='+encodeURIComponent(kind)); es.onmessage=function(ev){ reconnectDelayMs=500; if(paused) return; try{ const entry=JSON.parse(ev.data); append(entry);}catch(e){ console.warn('LogsViewer: JSON parse failed', e);} }; es.onerror=function(){ try{ es.close(); }catch(e){ dbg('logs:es-close-err', e);} const delay=reconnectDelayMs*(1+Math.random()*0.25); reconnectDelayMs=Math.min(reconnectDelayMs*2, 15000); setTimeout(connect, delay); }; }
    function reloadHistory(){ tbody.innerHTML=''; cards.innerHTML=''; visible=[]; if(countEl) countEl.textContent='0'; if(statusEl) statusEl.textContent=''; fetch('/logs?kind='+encodeURIComponent(kind)).then(r=>r.json()).then(d=>{ if(!d||d.ok!==true) return; let items=d.items||[]; if(items.length>MAX_ROWS) items=items.slice(items.length-MAX_ROWS); for(let i=0;i<items.length;i++) append(items[i]); flush(); }).catch(e=>dbg('logs:reloadHistory', e)); }

    function setupToolbarRoving(containerSelector){
      const container = document.querySelector(containerSelector);
      if (!container) return;
      const items = Array.prototype.slice.call(container.querySelectorAll('[data-kind], [data-view]'));
      items.forEach((btn, i)=>{ btn.setAttribute('tabindex', i===0 ? '0' : '-1'); btn.setAttribute('role','button'); });
      container.addEventListener('keydown', e => {
        const current = e.target && e.target.closest && e.target.closest('[data-kind], [data-view]');
        if (!current) return;
        const idx = items.indexOf(current);
        if (idx < 0) return;
        const key = e.key;
        if (key==='ArrowRight' || key==='ArrowDown'){
          e.preventDefault(); const n=(idx+1)%items.length; items.forEach(b=>b.setAttribute('tabindex','-1')); items[n].setAttribute('tabindex','0'); items[n].focus();
        } else if (key==='ArrowLeft' || key==='ArrowUp'){
          e.preventDefault(); const p=(idx-1+items.length)%items.length; items.forEach(b=>b.setAttribute('tabindex','-1')); items[p].setAttribute('tabindex','0'); items[p].focus();
        } else if (key==='Home'){
          e.preventDefault(); items.forEach(b=>b.setAttribute('tabindex','-1')); items[0].setAttribute('tabindex','0'); items[0].focus();
        } else if (key==='End'){
          e.preventDefault(); items.forEach(b=>b.setAttribute('tabindex','-1')); items[items.length-1].setAttribute('tabindex','0'); items[items.length-1].focus();
        }
      });
    }

    // Debounced filter input (200ms)
    let filterDebounceTimer = 0;
    const FILTER_DEBOUNCE_MS = 200;

    if (filter) filter.addEventListener('input', () => {
      filterTxt = String(filter.value||'').toLowerCase();
      if (filterDebounceTimer){ try{ clearTimeout(filterDebounceTimer);}catch(e){ dbg('logs:clearDebounce', e); } }
      filterDebounceTimer = setTimeout(() => { reloadHistory(); }, FILTER_DEBOUNCE_MS);
    });
    if (level) level.addEventListener('change', () => { const v=String(level.value||'').toLowerCase(); filterLevel=v; reloadHistory(); });

    if (filter) filter.addEventListener('keydown', e => { if(e.key==='Enter'){ if(filterDebounceTimer){ try{ clearTimeout(filterDebounceTimer);}catch(er){ dbg('logs:enterClearDebounce', er); } } reloadHistory(); } });

    if (clearBtn) clearBtn.addEventListener('click', () => { fetch('/logs/clear', { method: 'POST' }).then(()=> reloadHistory()); });

    if (pauseBtn) pauseBtn.addEventListener('click', () => { paused = !paused; pauseBtn.textContent = paused ? pauseTextResume : pauseTextPause; });

    if (downloadBtn) downloadBtn.addEventListener('click', () => { try{ const blob=new Blob([JSON.stringify(visible||[], null, 2)], {type:'application/json'}); const a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download='logs_'+(kind||'all')+'_'+new Date().toISOString().replace(/[:.]/g,'-')+'.json'; document.body.appendChild(a); a.click(); setTimeout(()=>{ try{ URL.revokeObjectURL(a.href);}catch(e){ dbg('logs:revokeUrl', e);} try{ document.body.removeChild(a);}catch(e){ dbg('logs:removeLink', e);} }, 0);}catch(e){ console.warn('LogsViewer: download failed', e); } });

    document.addEventListener('click', e => {
      const btnKV = e.target && e.target.closest ? e.target.closest('[data-kind]') : null;
      if (btnKV && btnKV.hasAttribute('data-kind')){
        const newKind = btnKV.getAttribute('data-kind')||'';
        if (newKind && newKind !== kind){
          kind = newKind;
          const kinds = document.querySelectorAll('[data-kind]');
          for (let i=0;i<kinds.length;i++){ const b=kinds[i]; const is=b.getAttribute('data-kind')===kind; b.classList.toggle('active', is); b.setAttribute('aria-pressed', is ? 'true' : 'false'); }
          reloadHistory();
          connect();
        }
        return;
      }
      const btnV = e.target && e.target.closest ? e.target.closest('[data-view]') : null;
      if (btnV && btnV.hasAttribute('data-view')){
        const newView = btnV.getAttribute('data-view')||'table';
        if (newView !== view){
          view = newView;
          const views = document.querySelectorAll('[data-view]');
            for (let j=0;j<views.length;j++){ const v=views[j]; const is=v.getAttribute('data-view')===view; v.classList.toggle('active', is); v.setAttribute('aria-pressed', is ? 'true' : 'false'); }
          syncViewVisibility();
        }
        return;
      }
      const copyBtn = e.target && e.target.closest ? e.target.closest('[data-action="copy"]') : null;
      if (copyBtn){
        try{
          const host = copyBtn.closest('tr') || copyBtn.closest('.col');
          const entry = host && host.__entry ? host.__entry : null;
          const text = entry ? (entry.message || JSON.stringify(entry)) : '';
          if(navigator.clipboard && navigator.clipboard.writeText){ navigator.clipboard.writeText(text); }
          copyBtn.textContent = (SR.Copied||'Copied');
          setTimeout(()=>{ copyBtn.textContent = (SR.Copy||'Copy'); }, 1200);
        }catch(err){ dbg('logs:copy', err); }
      }
    });

    syncViewVisibility();
    reloadHistory();
    connect();
    setupToolbarRoving('#logs-section .btn-group[role="group"]');
  }

  if (document.readyState === 'loading'){
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
