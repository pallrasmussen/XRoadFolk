(function(){
  // Fallback accordion toggling (works even without Bootstrap JS)
  document.addEventListener('click', function (e) {
    var btn = e.target && e.target.closest && e.target.closest('#gpip-accordion .accordion-button');
    if (!btn) return;

    // If Bootstrap is present, let it handle the toggle
    if (window.bootstrap && bootstrap.Collapse) return;

    e.preventDefault();

    // Resolve the collapse pane
    var targetSel = btn.getAttribute('data-bs-target');
    var pane = targetSel ? document.querySelector(targetSel)
                         : btn.closest('.accordion-item')?.querySelector('.accordion-collapse');
    if (!pane) return;

    var isOpen = pane.classList.contains('show');

    // Respect data-bs-parent: close siblings when opening one
    var parentSel = pane.getAttribute('data-bs-parent');
    var container = parentSel ? document.querySelector(parentSel) : document.getElementById('gpip-accordion');

    if (!isOpen && container) {
      container.querySelectorAll('.accordion-collapse.show').forEach(function (c) {
        if (c === pane) return;
        c.classList.remove('show');
        var hdrBtn = container.querySelector('[data-bs-target="#' + c.id + '"]')
                  || c.closest('.accordion-item')?.querySelector('.accordion-button');
        if (hdrBtn) {
          hdrBtn.classList.add('collapsed');
          hdrBtn.setAttribute('aria-expanded', 'false');
        }
      });
    }

    // Toggle current
    pane.classList.toggle('show', !isOpen);
    btn.classList.toggle('collapsed', isOpen);
    btn.setAttribute('aria-expanded', (!isOpen).toString());
  });

  // Expand/Collapse all
  document.addEventListener('click', function (e) {
    if (!e.target) return;
    var id = e.target.id;
    if (id !== 'gpip-expand-all' && id !== 'gpip-collapse-all') return;
    var open = id === 'gpip-expand-all';
    document.querySelectorAll('#gpip-accordion .accordion-collapse').forEach(function (c) {
      try {
        var inst = bootstrap.Collapse.getOrCreateInstance(c, { toggle: false });
        open ? inst.show() : inst.hide();
      } catch {
        // Fallback if bootstrap JS not present
        c.classList.toggle('show', open);
        var hdrBtn = document.querySelector('[data-bs-target="#' + c.id + '"]')
                  || c.closest('.accordion-item')?.querySelector('.accordion-button');
        if (hdrBtn) {
          hdrBtn.classList.toggle('collapsed', !open);
          hdrBtn.setAttribute('aria-expanded', open.toString());
        }
      }
    });
  });

  // Summary filter
  var q = document.getElementById('gpip-summary-q');
  var qClear = document.getElementById('gpip-summary-q-clear');
  function applyFilter() {
    var term = ((q && q.value) || '').trim().toLowerCase();
    document.querySelectorAll('#gpip-summary-table tbody tr').forEach(function(row){
      var txt = row.textContent.toLowerCase();
      row.classList.toggle('d-none', !!term && !txt.includes(term));
    });
  }
  if (q) {
    q.addEventListener('input', applyFilter);
  }
  if (qClear) {
    qClear.addEventListener('click', function(){
      if (!q) return;
      q.value = '';
      applyFilter();
      q.focus();
    });
  }

  // Simple sortable headers
  var heads = document.querySelectorAll('#gpip-summary-table thead th');
  var dirState = {}; // idx -> 'asc' | 'desc'
  function parseCell(val, kind) {
    if (kind === 'num') return parseFloat(val) || 0;
    if (kind === 'date') {
      var t = Date.parse(val); return isNaN(t) ? Number.MAX_SAFE_INTEGER : t;
    }
    return val.toLowerCase();
  }
  heads.forEach(function(th, idx){
    th.addEventListener('click', function(){
      var kind = th.getAttribute('data-sort') || 'text';
      var tbody = th.closest('table').querySelector('tbody');
      var rows = Array.from(tbody.querySelectorAll('tr')).filter(function(r){ return !r.classList.contains('d-none'); });
      var dir = dirState[idx] = (dirState[idx] === 'asc' ? 'desc' : 'asc');
      rows.sort(function(a,b){
        var ta = a.children[idx].textContent.trim();
        var tb = b.children[idx].textContent.trim();
        var va = parseCell(ta, kind), vb = parseCell(tb, kind);
        if (va < vb) return dir === 'asc' ? -1 : 1;
        if (va > vb) return dir === 'asc' ? 1 : -1;
        return 0;
      });
      rows.forEach(function(r){ tbody.appendChild(r); });
      // visual cue (optional): toggle a class
      heads.forEach(function(h){ h.classList.remove('sorted-asc','sorted-desc'); });
      th.classList.add(dir === 'asc' ? 'sorted-asc' : 'sorted-desc');
    });
  });

  // Optional deep link: #gpip-raw / #gpip-pretty / #gpip-summary
  if (location.hash) {
    var pane = document.querySelector(location.hash);
    if (pane && pane.classList.contains('accordion-collapse')) {
      try { bootstrap.Collapse.getOrCreateInstance(pane, { toggle:false }).show(); } catch { pane.classList.add('show'); }
      pane.scrollIntoView({ block: 'start' });
    }
  }
})();