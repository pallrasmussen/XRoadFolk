(function () {
  'use strict';

  // Utilities
  function byId(id) { return document.getElementById(id); }
  function qs(sel, root) { return (root || document).querySelector(sel); }
  function qsa(sel, root) { return (root || document).querySelectorAll(sel); }
  function on(el, ev, fn) { el && el.addEventListener(ev, fn); }
  function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }

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

  function iconClassFor(title) {
    var t = String(title || '').toLowerCase();
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
  function isNoiseKey(name) {
    if (!name) return false;
    var n = String(name).toLowerCase();
    return n === 'id' || n === 'fixed' || n === 'authoritycode' || n === 'personaddressid';
  }
  var atSign = String.fromCharCode(64);
  function buildKvpTableIncludingAttributes(owner) {
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
      if (isNil(ch)) continue;
      if (!hasElementChildren(ch)) {
        if (isNoiseKey(ch.localName)) continue;
        rows.push({ key: ch.localName, val: (ch.textContent || '').trim() });
        if (ch.attributes && ch.attributes.length) {
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
    qsa('.accordion-button', summaryHost).forEach(function (b) { b.classList.toggle('collapsed', !open); });
  }

  function renderSummary() {
    summaryHost.innerHTML = '';
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
            var nm = nameRoot.children[nn]; if (nm.localName !== 'Name' || isNil(nm)) continue;
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

        // Make header accessible and clickable to open details
        try { header.setAttribute('role','button'); header.setAttribute('tabindex','0'); } catch(e) {}

        wrap.appendChild(header);

        var accId = 'gpiv-acc-' + p + '-' + Date.now();
        var acc = document.createElement('div'); acc.className = 'accordion'; acc.id = accId;

        if (nameRoot) {
          var list = document.createElement('div'); list.className = 'd-flex flex-column gap-2';
          var nameItems = [];
          for (var n2 = 0; n2 < nameRoot.children.length; n2++) {
            var nItem = nameRoot.children[n2];
            if (nItem.localName !== 'Name' || isNil(nItem)) continue;
            var ord = parseInt(getTextLocal(nItem, 'Order') || '9999', 10); if (isNaN(ord)) ord = 9999;
            nameItems.push({ node: nItem, order: ord });
          }
          nameItems.sort(function (a, b) { return a.order - b.order; });
          for (var ix = 0; ix < nameItems.length; ix++) {
            var nNode = nameItems[ix].node;
            var card2 = el('div', 'p-2 border rounded', null);
            var title2 = el('div', 'small text-muted mb-1', (I18N.Name || 'Name') + ' #' + (ix + 1));
            card2.appendChild(title2);
            var t2 = buildKvpTableIncludingAttributes(nNode); if (t2) card2.appendChild(t2);
            list.appendChild(card2);
          }
          acc.appendChild(accItem(accId, acc.childElementCount, I18N.Names || 'Names', list, false));
        }

        var basicsTable = buildKvpTableIncludingAttributes(person);
        if (basicsTable) acc.appendChild(accItem(accId, acc.childElementCount, I18N.Basics || 'Basics', basicsTable, false));

        var groups = [];
        for (var g = 0; g < person.children.length; g++) {
          var ch = person.children[g]; if (isNil(ch)) continue; if (ch.localName === 'Names' || ch.localName === 'CivilStatus') continue; if (hasElementChildren(ch)) groups.push(ch);
        }
        for (var gi = 0; gi < groups.length; gi++) {
          var group = groups[gi];
          var counts = {}; for (var kk = 0; kk < group.children.length; kk++) { var lk = group.children[kk]; if (isNil(lk)) continue; var nm2 = lk.localName; counts[nm2] = (counts[nm2] || 0) + 1; }
          var repeated = null; for (var key in counts) { if (counts[key] > 1) { repeated = key; break; } }

          if (repeated) {
            var items = []; for (var itx = 0; itx < group.children.length; itx++) { var it = group.children[itx]; if (!isNil(it) && it.localName === repeated) items.push(it); }
            var listWrap = document.createElement('div'); listWrap.className = 'd-flex flex-column gap-2';
            for (var itx2 = 0; itx2 < items.length; itx2++) {
              var card3 = el('div', 'p-2 border rounded', null);
              var title3 = el('div', 'small text-muted mb-1', repeated + ' #' + (itx2 + 1));
              card3.appendChild(title3);
              var leaves2 = buildKvpTableIncludingAttributes(items[itx2]); if (leaves2) card3.appendChild(leaves2);
              var nested = []; for (var nc = 0; nc < items[itx2].children.length; nc++) { var kid = items[itx2].children[nc]; if (!isNil(kid) && hasElementChildren(kid)) nested.push(kid); }
              for (var nx = 0; nx < nested.length; nx++) { var sub = buildKvpTableIncludingAttributes(nested[nx]); if (sub) card3.appendChild(sub); }
              listWrap.appendChild(card3);
            }
            acc.appendChild(accItem(accId, acc.childElementCount, group.localName, listWrap, false));
          } else {
            var leaves = buildKvpTableIncludingAttributes(group);
            acc.appendChild(accItem(accId, acc.childElementCount, group.localName, leaves, false));
          }
        }

        wrap.appendChild(acc);
        summaryHost.appendChild(wrap);
      }

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
    if (hasAny) { renderSummary(); } else { summaryHost.innerHTML = ''; syncViewerHeight(); }
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
    document.addEventListener('click', function (e) {
      var t = e.target;
      if (t && (t.id === 'gpiv-fullscreen' || (t.closest && t.closest('#gpiv-fullscreen')))) {
        try {
          if (!card) return;
          var entering = !card.classList.contains('gpiv-fullscreen');
          card.classList.toggle('gpiv-fullscreen');
          if (entering) { card.style.height = ''; }
          else { syncViewerHeight(); }
        } catch (err) {
          console.error('gpiv: fullscreen toggle failed', err);
        }
      }
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

  // Public API
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
        var badges = qsa('.gpiv-pid', summaryHost);
        for (var i = 0; i < badges.length; i++) {
          var pidAttr = badges[i].getAttribute('data-public-id') || '';
          if (pidAttr === publicId || (badges[i].textContent || '').indexOf(publicId) >= 0) {
            var wrap = badges[i];
            while (wrap && wrap !== summaryHost && !wrap.classList.contains('border')) wrap = wrap.parentElement;
            if (wrap && wrap.scrollIntoView) { wrap.scrollIntoView({ block: 'center' }); wrap.classList.add('gpiv-highlight'); setTimeout(function () { wrap.classList.remove('gpiv-highlight'); }, 1500); }
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
