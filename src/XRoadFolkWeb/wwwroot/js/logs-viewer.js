// ES module: Logs viewer
(function(){
  'use strict';

  function getI18n(){
    try { var el=document.getElementById('logs-i18n-json'); return el? JSON.parse(el.textContent||'{}') : {}; } catch { return {}; }
  }

  var initialized = false;
  function init(){
    if (initialized) return; initialized = true;
    var SR = getI18n();
    var root = document.getElementById('logs-view');
    var kind = (root && root.getAttribute('data-default-kind')) || 'http';
    var view = 'table';

    var paused = false;
    var filterTxt = '';
    var filterLevel = '';
    var es = null;
    var reconnectDelayMs = 500; // start small

    var tbody = document.querySelector('#logs-table tbody');
    var tableContainer = document.querySelector('.logs-table-container');
    var cards = document.getElementById('logs-cards');
    var filter = document.getElementById('logs-filter');
    var level = document.getElementById('logs-level');
    var clearBtn = document.getElementById('logs-clear');
    var pauseBtn = document.getElementById('logs-pause');
    var downloadBtn = document.getElementById('logs-download');
    var backBtn = document.getElementById('logs-back');
    var statusEl = document.getElementById('logs-status');
    var countEl = document.getElementById('logs-count');

    var pauseTextPause = (pauseBtn && pauseBtn.getAttribute('data-i18n-pause')) || 'Pause';
    var pauseTextResume = (pauseBtn && pauseBtn.getAttribute('data-i18n-resume')) || 'Resume';

    function escapeHtml(s){ return String(s||'').replace(/[&<>]/g, function(ch){ return ({'&':'&amp;','<':'&lt;','>':'&gt;'}[ch]); }); }
    function lvlBadge(l){ var t=String(l||''); return '<span class="badge lvl-badge badge-'+escapeHtml(t)+'">'+escapeHtml(t)+'</span>'; }
    function pad(n,l){ n=String(n); l=l||2; return n.length>=l?n:('0'.repeat(l-n.length)+n); }
    function fmtLocal(ts){ try{ var d=new Date(ts); if(isNaN(d)) return ts; var yyyy=d.getFullYear(), mm=pad(d.getMonth()+1), dd=pad(d.getDate()), HH=pad(d.getHours()), MM=pad(d.getMinutes()), SS=pad(d.getSeconds()), mmm=pad(d.getMilliseconds(),3); var off=-d.getTimezoneOffset(); var sign=off>=0?'+':'-'; var abs=Math.abs(off); var oh=pad(Math.floor(abs/60)); var om=pad(abs%60); return yyyy+'-'+mm+'-'+dd+' '+HH+':'+MM+':'+SS+'.'+mmm+' '+sign+oh+':'+om; }catch{return ts;} }

    function rowFor(entry){
      var tr=document.createElement('tr');
      tr.innerHTML='<td>'+fmtLocal(entry.timestamp)+'</td>'+
                   '<td>'+lvlBadge(entry.level)+'</td>'+
                   '<td>'+escapeHtml(entry.category||'')+'</td>'+
                   '<td>'+escapeHtml(entry.message||'')+'</td>'+
                   '<td class="text-end">\
                      <button class="btn btn-sm btn-outline-dark" data-action="copy" aria-label="Copy">'+escapeHtml(SR.Copy||'Copy')+'</button>\
                   </td>';
      tr.__entry = entry;
      return tr;
    }

    function cardFor(entry){
      var col=document.createElement('div'); col.className='col';
      col.setAttribute('role','article');
      col.innerHTML='<div class="card log-card" data-level="'+escapeHtml(entry.level)+'">\
        <div class="card-body">\
          <div class="d-flex justify-content-between align-items-start">\
            <div>\
              <div class="small text-muted">'+escapeHtml(entry.category||'')+'</div>\
              <div class="fw-semibold">'+fmtLocal(entry.timestamp)+'</div>\
            </div>\
            <span class="badge lvl-badge badge-'+escapeHtml(String(entry.level||''))+'">'+escapeHtml(String(entry.level||''))+'</span>\
          </div>\
          <div class="log-message mt-2">'+escapeHtml(entry.message||'')+'</div>\
          '+(entry.exception?'<details class="mt-2"><summary>Exception</summary><pre class="mb-0">'+escapeHtml(entry.exception)+'</pre></details>':'')+'\
          <div class="d-flex justify-content-end mt-2">\
            <button class="btn btn-sm btn-outline-dark" data-action="copy" aria-label="Copy">'+escapeHtml(SR.Copy||'Copy')+'</button>\
          </div>\
        </div>\
      </div>';
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

    var MAX_ROWS=3000, TRIM_TO_ROWS=2000, BATCH_FLUSH_MS=50;
    var pending=[], pendingCards=[], flushTimer=0;
    var visible=[];

    function cancelFlush(){ if (flushTimer){ try{ clearTimeout(flushTimer);}catch{} flushTimer=0; } }
    function resetBuffers(){ cancelFlush(); pending=[]; pendingCards=[]; visible=[]; }

    function scheduleFlush(){ if (flushTimer) return; flushTimer=setTimeout(flush,BATCH_FLUSH_MS); }
    function flush(){
      flushTimer=0; if(!pending.length && !pendingCards.length) return;
      var autoscroll=false; try{ if(view==='table' && tableContainer){ var near=(tableContainer.scrollTop+tableContainer.clientHeight)>=(tableContainer.scrollHeight-8); autoscroll=near; } }catch{}
      if(pending.length){ var frag=document.createDocumentFragment(); for(var i=0;i<pending.length;i++) frag.appendChild(pending[i]); pending=[]; tbody.appendChild(frag); trimIfNeeded(); }
      if(pendingCards.length){ var frag2=document.createDocumentFragment(); for(var j=0;j<pendingCards.length;j++) frag2.appendChild(pendingCards[j]); pendingCards=[]; cards.appendChild(frag2); trimCardsIfNeeded(); }
      if(autoscroll && tableContainer){ try{ tableContainer.scrollTop=tableContainer.scrollHeight; }catch{} }
      if(view==='cards'){ visible=Array.prototype.map.call(cards.children, function(el){ return el.__entry; }).filter(Boolean);} else { visible=Array.prototype.map.call(tbody.children, function(tr){ return tr.__entry; }).filter(Boolean);} 
      if(countEl) countEl.textContent=String(visible.length||0);
      if(statusEl) statusEl.textContent=(visible.length||0)+' '+(SR.Entries||'entries');
    }

    function trimIfNeeded(){ try{ var extra=tbody.children.length-MAX_ROWS; if(extra>0){ var toRemove=Math.max(extra, tbody.children.length-TRIM_TO_ROWS); for(var i=0;i<toRemove;i++){ if(!tbody.firstChild) break; tbody.removeChild(tbody.firstChild);} } }catch(e){ console.debug('LogsViewer: trim failed', e); } }
    function trimCardsIfNeeded(){ try{ var extra=cards.children.length-MAX_ROWS; if(extra>0){ var toRemove=Math.max(extra, cards.children.length-TRIM_TO_ROWS); for(var i=0;i<toRemove;i++){ if(!cards.firstChild) break; cards.removeChild(cards.firstChild);} } }catch(e){ console.debug('LogsViewer: trim cards failed', e); } }

    function match(entry){ if(filterLevel && String(entry.level).toLowerCase()!==filterLevel) return false; if(filterTxt){ var t=(entry.message||'')+' '+(entry.category||''); if(t.toLowerCase().indexOf(filterTxt)<0) return false; } return true; }
    function append(entry){ if(!match(entry)) return; var tr=rowFor(entry); pending.push(tr); var col=cardFor(entry); pendingCards.push(col); scheduleFlush(); }
    function connect(){ if(es) try{ es.close(); }catch(e){} es=new EventSource('/logs/stream?kind='+encodeURIComponent(kind)); es.onmessage=function(ev){ reconnectDelayMs=500; if(paused) return; try{ var entry=JSON.parse(ev.data); append(entry);}catch(e){ console.warn('LogsViewer: JSON parse failed', e);} }; es.onerror=function(){ try{ es.close(); }catch(e){} var delay=reconnectDelayMs*(1+Math.random()*0.25); reconnectDelayMs=Math.min(reconnectDelayMs*2, 15000); setTimeout(connect, delay); }; }
    function reloadHistory(){ tbody.innerHTML=''; cards.innerHTML=''; visible=[]; if(countEl) countEl.textContent='0'; if(statusEl) statusEl.textContent=''; fetch('/logs?kind='+encodeURIComponent(kind)).then(function(r){return r.json();}).then(function(d){ if(!d||d.ok!==true) return; var items=d.items||[]; if(items.length>MAX_ROWS) items=items.slice(items.length-MAX_ROWS); for(var i=0;i<items.length;i++) append(items[i]); flush(); }); }

    function setupToolbarRoving(containerSelector){
      var container = document.querySelector(containerSelector);
      if (!container) return;
      var items = Array.prototype.slice.call(container.querySelectorAll('[data-kind], [data-view]'));
      items.forEach(function(btn, i){ btn.setAttribute('tabindex', i===0 ? '0' : '-1'); btn.setAttribute('role','button'); });
      container.addEventListener('keydown', function(e){
        var current = e.target && e.target.closest && e.target.closest('[data-kind], [data-view]');
        if (!current) return;
        var idx = items.indexOf(current);
        if (idx < 0) return;
        var key = e.key;
        if (key==='ArrowRight' || key==='ArrowDown'){
          e.preventDefault(); var n=(idx+1)%items.length; items.forEach(function(b){ b.setAttribute('tabindex','-1'); }); items[n].setAttribute('tabindex','0'); items[n].focus();
        } else if (key==='ArrowLeft' || key==='ArrowUp'){
          e.preventDefault(); var p=(idx-1+items.length)%items.length; items.forEach(function(b){ b.setAttribute('tabindex','-1'); }); items[p].setAttribute('tabindex','0'); items[p].focus();
        } else if (key==='Home'){
          e.preventDefault(); items.forEach(function(b){ b.setAttribute('tabindex','-1'); }); items[0].setAttribute('tabindex','0'); items[0].focus();
        } else if (key==='End'){
          e.preventDefault(); items.forEach(function(b){ b.setAttribute('tabindex','-1'); }); items[items.length-1].setAttribute('tabindex','0'); items[items.length-1].focus();
        } else if (key===' ' || key==='Enter'){
          // handled by click
        }
      });
    }

    // Toolbar: filter and level
    if (filter) filter.addEventListener('input', function(){ filterTxt = String(filter.value||'').toLowerCase(); reloadHistory(); });
    if (level) level.addEventListener('change', function(){ var v=String(level.value||'').toLowerCase(); filterLevel=v; reloadHistory(); });

    // Toolbar: clear
    if (clearBtn) clearBtn.addEventListener('click', function(){ fetch('/logs/clear', { method: 'POST' }).then(function(){ reloadHistory(); }); });

    // Toolbar: pause/resume
    if (pauseBtn) pauseBtn.addEventListener('click', function(){ paused = !paused; pauseBtn.textContent = paused ? pauseTextResume : pauseTextPause; });

    // Toolbar: download visible as JSON
    if (downloadBtn) downloadBtn.addEventListener('click', function(){ try{ var blob=new Blob([JSON.stringify(visible||[], null, 2)], {type:'application/json'}); var a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download='logs_'+(kind||'all')+'_'+new Date().toISOString().replace(/[:.]/g,'-')+'.json'; document.body.appendChild(a); a.click(); setTimeout(function(){ try{ URL.revokeObjectURL(a.href);}catch{}; try{ document.body.removeChild(a);}catch{}; }, 0);}catch(e){ console.warn('LogsViewer: download failed', e); } });

    // Toolbar: kind/view buttons (event delegation)
    document.addEventListener('click', function(e){
      var btnKV = e.target && e.target.closest ? e.target.closest('[data-kind]') : null;
      if (btnKV && btnKV.hasAttribute('data-kind')){
        var newKind = btnKV.getAttribute('data-kind')||'';
        if (newKind && newKind !== kind){
          kind = newKind;
          // update active states
          var kinds = document.querySelectorAll('[data-kind]');
          for (var i=0;i<kinds.length;i++){ var b=kinds[i]; var is=b.getAttribute('data-kind')===kind; b.classList.toggle('active', is); b.setAttribute('aria-pressed', is ? 'true' : 'false'); }
          reloadHistory();
          connect();
        }
        return;
      }
      var btnV = e.target && e.target.closest ? e.target.closest('[data-view]') : null;
      if (btnV && btnV.hasAttribute('data-view')){
        var newView = btnV.getAttribute('data-view')||'table';
        if (newView !== view){
          view = newView;
          var views = document.querySelectorAll('[data-view]');
          for (var j=0;j<views.length;j++){ var v=views[j]; var is=v.getAttribute('data-view')===view; v.classList.toggle('active', is); v.setAttribute('aria-pressed', is ? 'true' : 'false'); }
          syncViewVisibility();
        }
        return;
      }
      var copyBtn = e.target && e.target.closest ? e.target.closest('[data-action="copy"]') : null;
      if (copyBtn){
        try{
          var host = copyBtn.closest('tr') || copyBtn.closest('.col');
          var entry = host && host.__entry ? host.__entry : null;
          var text = entry ? (entry.message || JSON.stringify(entry)) : '';
          navigator.clipboard && navigator.clipboard.writeText ? navigator.clipboard.writeText(text) : null;
          copyBtn.textContent = (SR.Copied||'Copied');
          setTimeout(function(){ copyBtn.textContent = (SR.Copy||'Copy'); }, 1200);
        }catch{}
      }
    });

    // Set initial view visibility
    syncViewVisibility();

    // Initial load
    reloadHistory();
    connect();

    // Accessibility roving for the two btn groups
    setupToolbarRoving('#logs-section .btn-group[role="group"]');
  }

  if (document.readyState === 'loading'){
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
