import { iconClassFor, prettify, nextUid } from './gpiv-helpers.js';

(function () {
  'use strict';

  // Utilities
  function byId(id) { return document.getElementById(id); }
  function qs(sel, root) { return (root || document).querySelector(sel); }
  function qsa(sel, root) { return (root || document).querySelectorAll(sel); }
  function on(el, ev, fn) { el && el.addEventListener(ev, fn); }
  function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
  function clearChildren(node){ try{ while(node && node.firstChild){ node.removeChild(node.firstChild); } }catch{} }

  var dropOverlay = null;
  var summaryHost = null;
  var emptyMsg = null;
  var card = null;
  var I18N = {};

  var sourcePretty = '';
  var sourceRaw = '';

  function parseXml(text) {
    var parser = new DOMParser();
    var doc = parser.parseFromString(text, 'application/xml');
    var err = doc.querySelector('parsererror');
    if (err) throw new Error(err.textContent || 'Invalid XML');
    return doc;
  }

  // Height sync with People Search card
  function syncViewerHeight() {
    try {
      var search = byId('people-search-card');
      if (!search || !card) return;
      if (card.classList.contains('gpiv-fullscreen')) {
        card.style.height = '';
        return;
      }
      var h = Math.round(search.getBoundingClientRect().height);
      if (h > 0) card.style.height = h + 'px';
    } catch (e) { console.error('gpiv: syncViewerHeight failed', e); }
  }

  function buildHeaderBtnContent(btn, titleText) {
    btn.textContent = '';
    var ic = document.createElement('i'); ic.className = 'bi ' + iconClassFor(titleText) + ' me-2'; ic.setAttribute('aria-hidden', 'true');
    btn.appendChild(ic);
    btn.appendChild(document.createTextNode(titleText || ''));
  }

  function isNil(node) {
    if (!node || node.nodeType !== 1 || !node.attributes) return false;
    for (var i = 0; i < node.attributes.length; i++) {
      var a = node.attributes[i];
      var ln = a.localName || a.name;
      if (ln === 'nil' && /^(true|1)$/i.test((a.value || '').trim())) return true;
    }
    return false;
  }
  function hasElementChildren(node) {
    if (!node || !node.children) return false;
    for (var i = 0; i < node.children.length; i++) if (!isNil(node.children[i])) return true;
    return false;
  }
  // Hide exact field names: Id, PersonAddressId (case-insensitive)
  function isNoiseKey(name) {
    if (!name) return false;
    var n = String(name).toLowerCase();
    return n === 'id' || n === 'personaddressid';
  }

  var atSign = String.fromCharCode(64);
  function buildLeavesTable(owner) {
    var rows = [];
    if (owner && owner.attributes && owner.attributes.length) {
      for (var i = 0; i < owner.attributes.length; i++) {
        var a = owner.attributes[i];
        var attrName = a.localName || a.name || '';
        if (isNoiseKey(attrName)) continue;
        rows.push({ key: atSign + (a.name || ''), val: (a.value || '') });
      }
    }
    var kids = owner && owner.children ? owner.children : [];
    for (var k = 0; k < kids.length; k++) {
      var ch = kids[k];
      var nil = isNil(ch);
      var treatAsLeaf = nil || !hasElementChildren(ch);
      if (treatAsLeaf) {
        if (isNoiseKey(ch.localName)) continue;
        var v = nil ? '' : (ch.textContent || '').trim();
        rows.push({ key: ch.localName, val: v });
        if (!nil && ch.attributes && ch.attributes.length) {
          for (var a2 = 0; a2 < ch.attributes.length; a2++) {
            var at = ch.attributes[a2];
            var atName = at.localName || at.name || '';
            if (isNoiseKey(atName)) continue;
            rows.push({ key: ch.localName + ' ' + atSign + (at.name || ''), val: (at.value || '') });
          }
        }
      }
    }
    if (!rows.length) return null;
    var wrap = document.createElement('div'); wrap.className = 'table-responsive';
    var t = document.createElement('table'); t.className = 'table table-sm table-striped align-middle mb-0';
    var tb = document.createElement('tbody');
    for (var r = 0; r < rows.length; r++) {
      var tr = document.createElement('tr');
      var th = document.createElement('th'); th.className = 'text-muted fw-normal'; th.style.width = '36%'; th.textContent = rows[r].key;
      var td = document.createElement('td'); td.textContent = rows[r].val;
      tr.appendChild(th); tr.appendChild(td); tb.appendChild(tr);
    }
    t.appendChild(tb); wrap.appendChild(t);
    return wrap;
  }

  function getTextLocal(elx, name) {
    if (!elx || !elx.children) return '';
    for (var i = 0; i < elx.children.length; i++) {
      var c = elx.children[i];
      if (c.localName === name) return (c.textContent || '').trim();
    }
    return '';
  }
  function badge(text) { var b = document.createElement('span'); b.className = 'badge text-bg-light'; b.textContent = text; return b; }

  function accItem(accId, idx, title, bodyContent, open) {
    var item = document.createElement('div'); item.className = 'accordion-item';
    var hid = accId + '-h-' + idx; var cid = accId + '-c-' + idx;
    var h2 = document.createElement('h2'); h2.className = 'accordion-header'; h2.id = hid;
    var btn = document.createElement('button'); btn.type = 'button'; btn.className = 'accordion-button' + (open ? '' : ' collapsed');
    btn.setAttribute('data-bs-toggle', 'collapse'); btn.setAttribute('data-bs-target', '#' + cid);
    btn.setAttribute('aria-expanded', open ? 'true' : 'false'); btn.setAttribute('aria-controls', cid);
    buildHeaderBtnContent(btn, title || '');
    h2.appendChild(btn);
    var col = document.createElement('div'); col.id = cid; col.className = 'accordion-collapse collapse' + (open ? ' show' : '');
    col.setAttribute('aria-labelledby', hid);
    var body = document.createElement('div'); body.className = 'accordion-body p-0';
    if (bodyContent) body.appendChild(bodyContent);
    col.appendChild(body);
    item.appendChild(h2); item.appendChild(col);
    return item;
  }

  function expandSummaryAll(open) {
    if (!summaryHost) return;
    qsa('.accordion-collapse', summaryHost).forEach(function (p) { p.classList.toggle('show', !!open); });
    qsa('.accordion-button', summaryHost).forEach(function (b) { b.classList.toggle('collapsed', !open); b.setAttribute('aria-expanded', open ? 'true':'false'); });
  }

  function groupChildrenByName(node) {
    var map = Object.create(null);
    if (!node || !node.children) return map;
    for (var i = 0; i < node.children.length; i++) {
      var c = node.children[i];
      if (!c || c.nodeType !== 1) continue;
      if (c.localName === 'Names') continue; // handled separately at top-level
      if (isNil(c)) continue; // nil nodes rendered as empty leaf rows in table
      if (!hasElementChildren(c)) continue; // leaves handled in table
      var nm = c.localName;
      (map[nm] || (map[nm] = [])).push(c);
    }
    return map;
  }

  function buildNodeContent(node) {
    var container = document.createElement('div'); container.className = 'd-flex flex-column gap-2';

    var leaves = buildLeavesTable(node);
    if (leaves) container.appendChild(leaves);

    var groups = groupChildrenByName(node);
    for (var key in groups) {
      if (!Object.prototype.hasOwnProperty.call(groups, key)) continue;
      var arr = groups[key];
      if (arr.length > 1) {
        var listWrap = document.createElement('div'); listWrap.className = 'd-flex flex-column gap-2';
        for (var i = 0; i < arr.length; i++) {
          var card3 = el('div', 'p-2 border rounded', null);
          var title3 = el('div', 'small text-muted mb-1', key + ' #' + (i + 1));
          card3.appendChild(title3);
          var inner = buildNodeContent(arr[i]);
          if (inner) card3.appendChild(inner);
          listWrap.appendChild(card3);
        }
        container.appendChild(listWrap);
      } else {
        // Single child: render inline
        var inner2 = buildNodeContent(arr[0]);
        var headline = el('div', 'small text-muted mb-1', key);
        var wrap = document.createElement('div'); wrap.className = 'p-2 border rounded';
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
      var xmlText = (sourceRaw && sourceRaw.trim()) ? sourceRaw : (sourcePretty || '');
      if (!xmlText.trim()) { summaryHost.textContent = I18N.NoXmlToDisplay || 'No XML to display.'; syncViewerHeight(); return; }
      var xml = parseXml(xmlText);
      var people = []; var all = xml.getElementsByTagName('*');
      for (var i = 0; i < all.length; i++) if (all[i].localName === 'PersonPublicInfo') people.push(all[i]);
      if (people.length === 0) { summaryHost.appendChild(el('div', 'text-muted', I18N.NoPeopleFound || 'No PersonPublicInfo elements found.')); syncViewerHeight(); return; }

      var hdr = el('div', 'mb-2 d-flex align-items-center gap-2', null);
      var cnt = el('strong', null, (I18N.PeopleLabel || 'People') + ': ' + people.length); cnt.id = 'gpiv-people-count';
      hdr.appendChild(cnt);

      var grp = el('div', 'd-flex flex-wrap gap-2 ms-auto', null);
      var bExpand = el('button', 'btn btn-sm btn-outline-secondary', null); bExpand.id = 'gpiv-summary-expand';
      bExpand.appendChild(el('i', 'bi bi-chevron-double-down me-1', ''));
      bExpand.appendChild(document.createTextNode(I18N.Expand || 'Expand'));
      var bFs = el('button', 'btn btn-sm btn-outline-secondary', null); bFs.id = 'gpiv-fullscreen'; bFs.title = I18N.ToggleFullscreen || 'Toggle fullscreen';
      bFs.appendChild(el('i', 'bi bi-arrows-fullscreen me-1', ''));
      bFs.appendChild(document.createTextNode(I18N.Fullscreen || 'Fullscreen'));
      var bCollapse = el('button', 'btn btn-sm btn-outline-secondary', null); bCollapse.id = 'gpiv-summary-collapse';
      bCollapse.appendChild(el('i', 'bi bi-chevron-double-up me-1', ''));
      bCollapse.appendChild(document.createTextNode(I18N.Collapse || 'Collapse'));
      grp.appendChild(bExpand); grp.appendChild(bCollapse); grp.appendChild(bFs);
      hdr.appendChild(grp);
      summaryHost.appendChild(hdr);

      on(bExpand, 'click', function () { expandSummaryAll(true); });
      on(bCollapse, 'click', function () { expandSummaryAll(false); });

      for (var p = 0; p < people.length; p++) {
        var person = people[p];
        var wrap = document.createElement('div'); wrap.className = 'mb-3 p-2 border rounded gpiv-person';
        wrap.setAttribute('data-idx', String(p));

        var header = el('div', 'gpiv-person-header d-flex flex-wrap gap-2 align-items-baseline mb-2', null);
        header.appendChild(el('span', 'badge text-bg-secondary', '#' + (p + 1)));

        var nameRoot = null;
        for (var nc = 0; nc < person.children.length; nc++) { if (person.children[nc].localName === 'Names') { nameRoot = person.children[nc]; break; } }
        var fullName = '';
        if (nameRoot) {
          var firsts = []; var last = '';
          for (var nn = 0; nn < nameRoot.children.length; nn++) {
            var nm = nameRoot.children[nn]; if (nm.localName !== 'Name') continue;
            var tp = getTextLocal(nm, 'Type'); if (tp === 'FirstName') {
              var order = parseInt(getTextLocal(nm, 'Order') || '9999', 10);
              firsts.push({ o: isNaN(order) ? 9999 : order, v: getTextLocal(nm, 'Value') });
            } else if (tp === 'LastName') last = getTextLocal(nm, 'Value') || last;
          }
          firsts.sort(function (a, b) { return a.o - b.o; });
          var joined = []; for (var fj = 0; fj < firsts.length; fj++) joined.push(firsts[fj].v);
          fullName = (joined.join(' ') + ' ' + last).trim();
        }
        var nameEl = document.createElement('strong'); nameEl.textContent = fullName || '-'; header.appendChild(nameEl);

        var publicId = getTextLocal(person, 'PublicId') || getTextLocal(person, 'PersonId');
        var dob = (getTextLocal(person, 'DateOfBirth') || '').slice(0, 10);

        wrap.dataset.name = fullName || '';
        wrap.dataset.publicId = publicId || '';
        wrap.dataset.dob = dob || '';
        if (publicId) { try { wrap.setAttribute('data-public-id', publicId); } catch(e) {} }

        if (dob) header.appendChild(badge((I18N.DOB || 'DOB') + ': ' + dob));

        try { header.setAttribute('role','button'); header.setAttribute('tabindex','0'); } catch(e) {}

        wrap.appendChild(header);

        var accId = nextUid('acc');
        var acc = document.createElement('div'); acc.className = 'accordion'; acc.id = accId;

        if (nameRoot) {
          var list = document.createElement('div'); list.className = 'd-flex flex-column gap-2';
          var nameItems = [];
          for (var n2 = 0; n2 < nameRoot.children.length; n2++) {
            var nItem = nameRoot.children[n2];
            if (nItem.localName !== 'Name') continue;
            var ord = parseInt(getTextLocal(nItem, 'Order') || '9999', 10); if (isNaN(ord)) ord = 9999;
            nameItems.push({ node: nItem, order: ord });
          }
          nameItems.sort(function (a, b) { return a.order - b.order; });
          for (var ix = 0; ix < nameItems.length; ix++) {
            var nNode = nameItems[ix].node;
            var card2 = el('div', 'p-2 border rounded', null);
            var title2 = el('div', 'small text-muted mb-1', (I18N.Name || 'Name') + ' #' + (ix + 1));
            card2.appendChild(title2);
            var t2 = buildLeavesTable(nNode); if (t2) card2.appendChild(t2);
            var nestedUnderName = buildNodeContent(nNode);
            if (nestedUnderName && nestedUnderName.childElementCount > (t2 ? 1 : 0)) {
              card2.appendChild(nestedUnderName);
            }
            list.appendChild(card2);
          }
          acc.appendChild(accItem(accId, acc.childElementCount, I18N.Names || 'Names', list, false));
        }

        var basicsTable = buildLeavesTable(person);
        if (basicsTable) acc.appendChild(accItem(accId, acc.childElementCount, I18N.Basics || 'Basics', basicsTable, false));

        var groups = groupChildrenByName(person);
        for (var gName in groups) {
          if (!Object.prototype.hasOwnProperty.call(groups, gName)) continue;
          var content = document.createElement('div'); content.className = 'd-flex flex-column gap-2';
          var arr2 = groups[gName];
          if (arr2.length > 1) {
            var listWrap = document.createElement('div'); listWrap.className = 'd-flex flex-column gap-2';
            for (var it = 0; it < arr2.length; it++) {
              var card3 = el('div', 'p-2 border rounded', null);
              var title3 = el('div', 'small text-muted mb-1', gName + ' #' + (it + 1));
              card3.appendChild(title3);
              var inner3 = buildNodeContent(arr2[it]);
              if (inner3) card3.appendChild(inner3);
              listWrap.appendChild(card3);
            }
            content.appendChild(listWrap);
          } else {
            var inner4 = buildNodeContent(arr2[0]);
            if (inner4) content.appendChild(inner4);
          }
          acc.appendChild(accItem(accId, acc.childElementCount, gName, content, false));
        }

        wrap.appendChild(acc);
        summaryHost.appendChild(wrap);
      }

      // Focus first accordion header for keyboard users
      try {
        var firstAccBtn = summaryHost.querySelector('.accordion .accordion-header .accordion-button');
        if (firstAccBtn) firstAccBtn.focus();
      } catch {}

      syncViewerHeight();
    } catch (e) {
      console.error('gpiv: failed to build summary', e);
      summaryHost.textContent = (I18N.FailedToBuildSummaryPrefix || 'Failed to build summary:') + ' ' + (e && e.message ? e.message : e);
      syncViewerHeight();
    }
  }

  function updateView() {
    var combined = (sourceRaw && sourceRaw.trim()) || (sourcePretty && sourcePretty.trim()) || '';
    var hasAny = combined.length > 0;

    if (emptyMsg) emptyMsg.style.display = hasAny ? 'none' : 'block';
    if (hasAny) { renderSummary(); } else { clearChildren(summaryHost); syncViewerHeight(); }
  }

  function setXmlFromText(text) {
    if (!text || !text.trim()) return;
    sourceRaw = text; sourcePretty = text;
    updateView();
  }
  function showDrop(v) { if (dropOverlay) dropOverlay.style.display = v ? 'flex' : 'none'; }

  function initDragDrop() {
    if (!card) return;
    on(card, 'dragenter', function (e) { e.preventDefault(); showDrop(true); });
    on(card, 'dragover', function (e) { e.preventDefault(); showDrop(true); });
    on(card, 'dragleave', function (e) { e.preventDefault(); showDrop(false); });
    on(card, 'drop', function (e) {
      e.preventDefault(); showDrop(false);
      try {
        var dt = e.dataTransfer; if (!dt) return;
        if (dt.files && dt.files.length) {
          var f = dt.files[0]; var reader = new FileReader();
          reader.onload = function () { setXmlFromText(String(reader.result || '')); };
          reader.readAsText(f);
        } else {
          var txt = dt.getData('text') || '';
          setXmlFromText(txt);
        }
      } catch (err) {
        console.error('gpiv: drop failed', err);
      }
    });
  }

  function initFullscreen() {
    // Align People Public fullscreen behavior with Person detail
    document.addEventListener('click', function (e) {
      var t = e && e.target ? e.target : null;
      if (!t) return;
      var trigger = (t.id === 'gpiv-fullscreen') || (t.closest && t.closest('#gpiv-fullscreen'));
      if (!trigger) return;
      if (!card) return;
      try { var pdSec = byId('person-details-section'); if (pdSec) pdSec.classList.remove('pd-fullscreen'); } catch (_) {}
      try {
        var isApiFull = document.fullscreenElement === card;
        var hasCssFull = card.classList.contains('gpiv-fullscreen');
        if (isApiFull) {
          if (document.exitFullscreen) {
            document.exitFullscreen().catch(function(){ card.classList.remove('gpiv-fullscreen'); });
          } else {
            card.classList.remove('gpiv-fullscreen');
          }
          return;
        }
        if (hasCssFull) {
          card.classList.remove('gpiv-fullscreen');
          syncViewerHeight();
          return;
        }
        function enterFs(){
          if (card.requestFullscreen) {
            card.requestFullscreen({ navigationUI: 'hide' })
              .then(function(){ card.style.height=''; })
              .catch(function(){ card.classList.add('gpiv-fullscreen'); card.style.height=''; });
          } else {
            card.classList.add('gpiv-fullscreen');
            card.style.height='';
          }
        }
        if (document.fullscreenElement && document.fullscreenElement !== card) {
          if (document.exitFullscreen) {
            document.exitFullscreen().then(function(){ enterFs(); }).catch(function(){ card.classList.add('gpiv-fullscreen'); card.style.height=''; });
          } else {
            card.classList.add('gpiv-fullscreen');
            card.style.height='';
          }
        } else {
          enterFs();
        }
      } catch (err) {
        card.classList.toggle('gpiv-fullscreen');
        card.style.height = '';
        if (!card.classList.contains('gpiv-fullscreen')) syncViewerHeight();
      }
    });

    document.addEventListener('fullscreenchange', function(){
      if (!card) return;
      var isFs = document.fullscreenElement === card;
      card.classList.toggle('gpiv-fullscreen', isFs);
      if (!isFs) syncViewerHeight();
    });
  }

  function loadInitialDataFromJsonTags() {
    try {
      var i18nEl = byId('gpiv-i18n-json');
      if (i18nEl) I18N = JSON.parse(i18nEl.textContent || '{}');
    } catch (e) { console.error('gpiv: failed to parse i18n json', e); }

    try {
      var rawEl = byId('gpiv-raw-json');
      var prettyEl = byId('gpiv-pretty-json');
      var rawInit = rawEl ? JSON.parse(rawEl.textContent || '""') : "";
      var prettyInit = prettyEl ? JSON.parse(prettyEl.textContent || '""') : "";
      if (rawInit || prettyInit) window.gpiv.setXml(rawInit, prettyInit);
    } catch (e) { console.error('gpiv: failed to parse initial xml json', e); }
  }

  function initPublicIdLink() {
    try {
      var qs = new URLSearchParams(window.location.search);
      var linkPid = qs.get('gpivPublicId');
      if (linkPid) { setTimeout(function () { window.gpiv.focusPerson(linkPid); }, 50); }
    } catch (e) { console.error('gpiv: focusPerson failed', e); }
  }

  // Accordion keyboard navigation (ArrowUp/Down/Left/Right, Home/End)
  function handleAccordionKeydown(e, scopeRoot){
    try{
      var t = e.target;
      if (!t || !t.classList || !t.classList.contains('accordion-button')) return;
      if (!scopeRoot || !scopeRoot.contains(t)) return;
      var key = e.key;
      if (key!=='ArrowDown' && key!=='ArrowUp' && key!=='ArrowLeft' && key!=='ArrowRight' && key!=='Home' && key!=='End') return;
      var acc = t.closest('.accordion'); if (!acc) return;
      var btns = Array.prototype.slice.call(acc.querySelectorAll('.accordion-header .accordion-button'));
      var idx = btns.indexOf(t); if (idx < 0) return;
      e.preventDefault();
      var nextIdx = idx;
      if (key==='ArrowDown' || key==='ArrowRight') nextIdx = (idx+1) % btns.length;
      else if (key==='ArrowUp' || key==='ArrowLeft') nextIdx = (idx-1+btns.length) % btns.length;
      else if (key==='Home') nextIdx = 0;
      else if (key==='End') nextIdx = btns.length-1;
      var next = btns[nextIdx]; if (next && next.focus) next.focus();
    }catch{}
  }

  // Public API (exposed for other modules/pages that may want to interact)
  window.gpiv = {
    setXml: function (raw, pretty) {
      try {
        if (typeof raw === 'string') sourceRaw = raw;
        if (typeof pretty === 'string') sourcePretty = pretty; else if (typeof raw === 'string') sourcePretty = raw;
        updateView();
      } catch (e) { console.error('gpiv: setXml failed', e); }
    },
    focusPerson: function (publicId) {
      try {
        if (!publicId) return;
        updateView();
        // Prefer modern cards rendered with data-public-id
        var cards = qsa('.gpiv-person[data-public-id]', summaryHost);
        for (var i = 0; i < cards.length; i++) {
          var pidAttr = cards[i].getAttribute('data-public-id') || (cards[i].dataset ? cards[i].dataset.publicId : '');
          if (pidAttr === publicId) {
            if (cards[i].scrollIntoView) { cards[i].scrollIntoView({ block: 'center' }); cards[i].classList.add('gpiv-highlight'); setTimeout(function (el) { return function(){ el.classList.remove('gpiv-highlight'); }; }(cards[i]), 1500); }
            break;
          }
        }
      } catch (e) { console.error('gpiv: focusPerson failed', e); }
    }
  };

  // Init
  document.addEventListener('DOMContentLoaded', function () {
    try {
      dropOverlay = byId('gpiv-drop');
      summaryHost = byId('gpiv-xml-summary');
      emptyMsg = byId('gpiv-empty');
      card = qs('.gpiv-card');

      // Resize observers
      try {
        var sc = byId('people-search-card');
        if (window.ResizeObserver && sc) {
          var ro = new ResizeObserver(function () { syncViewerHeight(); });
          ro.observe(sc);
        }
      } catch (e) { console.error('gpiv: ResizeObserver failed', e); }
      window.addEventListener('resize', syncViewerHeight);

      initDragDrop();
      initFullscreen();

      // ARIA keyboard within summary accordions
      document.addEventListener('keydown', function(e){ handleAccordionKeydown(e, summaryHost); });

      loadInitialDataFromJsonTags();
      initPublicIdLink();

      // Initial height sync
      syncViewerHeight();
    } catch (e) {
      console.error('gpiv: init failed', e);
      if (summaryHost) {
        summaryHost.textContent = (I18N.FailedToBuildSummaryPrefix || 'Failed to initialize:') + ' ' + (e && e.message ? e.message : e);
      }
    }
  });
})();
