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
    function connect(){ if(es) try{ es.close(); }catch(e){} es=new EventSource('/logs/stream?kind='+encodeURIComponent(kind)); es.onmessage=function(ev){ if(paused) return; try{ var entry=JSON.parse(ev.data); append(entry);}catch(e){ console.warn('LogsViewer: JSON parse failed', e);} }; es.onerror=function(){ try{ es.close(); }catch(e){} setTimeout(connect,1000); }; }
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
          e.preventDefault(); current.click();
        }
      });
    }

    document.querySelectorAll('[data-kind]').forEach(function(btn){
      btn.addEventListener('click', function(){
        // Update UI state
        document.querySelectorAll('[data-kind]').forEach(function(b){ b.classList.remove('active'); b.setAttribute('aria-pressed','false'); });
        btn.classList.add('active'); btn.setAttribute('aria-pressed','true');
        // Close existing stream and clear buffers to avoid race with pending flush
        if (es) { try{ es.close(); }catch{} }
        resetBuffers();
        kind=btn.getAttribute('data-kind')||'http';
        reloadHistory();
        connect();
      });
      btn.addEventListener('keydown', function(e){ if(e.key==='ArrowRight'||e.key==='ArrowLeft'){ e.preventDefault(); var arr=Array.prototype.slice.call(document.querySelectorAll('[data-kind]')); var idx=arr.indexOf(btn); var next=e.key==='ArrowRight'?(idx+1)%arr.length:(idx-1+arr.length)%arr.length; arr[next].focus(); } if(e.key==='Enter' || e.key===' '){ e.preventDefault(); btn.click(); } });
    });

    document.querySelectorAll('[data-view]').forEach(function(btn){ btn.addEventListener('click', function(){ document.querySelectorAll('[data-view]').forEach(function(b){ b.classList.remove('active'); b.setAttribute('aria-pressed','false'); }); btn.classList.add('active'); btn.setAttribute('aria-pressed','true'); view=btn.getAttribute('data-view')||'table'; syncViewVisibility(); }); });

    setupToolbarRoving('#logs-kind-toolbar');
    setupToolbarRoving('#logs-view-toolbar');

    filter && filter.addEventListener('input', function(){ filterTxt=(filter.value||'').trim().toLowerCase(); resetBuffers(); reloadHistory(); });
    level && level.addEventListener('change', function(){ filterLevel=(level.value||'').trim().toLowerCase(); resetBuffers(); reloadHistory(); });
    clearBtn && clearBtn.addEventListener('click', function(){ fetch('/logs/clear', { method: 'POST' }); cancelFlush(); tbody.innerHTML=''; cards.innerHTML=''; visible=[]; if(countEl) countEl.textContent='0'; if(statusEl) statusEl.textContent=''; });
    pauseBtn && pauseBtn.addEventListener('click', function(){ paused=!paused; pauseBtn.classList.toggle('active', paused); pauseBtn.textContent=paused?pauseTextResume:pauseTextPause; pauseBtn.setAttribute('aria-pressed', paused?'true':'false'); });
    downloadBtn && downloadBtn.addEventListener('click', function(){ try{ var blob=new Blob([JSON.stringify(visible,null,2)], { type:'application/json;charset=utf-8' }); var a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download='logs_'+kind+'_'+new Date().toISOString().replace(/[:.]/g,'-')+'.json'; document.body.appendChild(a); a.click(); URL.revokeObjectURL(a.href); a.remove(); }catch(e){ console.warn('LogsViewer: download failed', e); } });

    document.addEventListener('click', function(ev){ var b=ev.target && ev.target.closest && ev.target.closest('[data-action="copy"]'); if(!b) return; var host=b.closest('tr, .log-card'); if(!host) return; var entry=host.__entry; if(!entry) return; try{ navigator.clipboard.writeText(entry.message||''); b.textContent=SR.Copied||'Copied'; setTimeout(function(){ b.textContent=SR.Copy||'Copy'; }, 1200);}catch{} });

    backBtn && backBtn.addEventListener('click', function(e){ try{ var ref=document.referrer?new URL(document.referrer):null; if(ref && ref.origin===location.origin){ var p=(ref.pathname||'').toLowerCase(); if(p==='/'||p.endsWith('/index')){ e.preventDefault(); history.back(); return; } } }catch{} });

    reloadHistory();
    connect();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
