// ES module: shared helpers for GPIV components

export function prettify(name){
  let s = String(name || '');
  s = s.replace(/[_\-]+/g,' ');
  s = s.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
  s = s.trim().replace(/\s+/g, ' ');
  return s.split(' ').map(w => w ? (w[0].toUpperCase() + w.slice(1)) : w).join(' ');
}

// Parses an address-like key and returns { index: <1-based>, field: <last segment> } or null
export function parseAddressKey(k){
  const parts = String(k||'').split('.');
  let pos = -1; let idxVal = null;
  for (let i=0;i<parts.length;i++){
    const raw = parts[i] || ''; let seg = raw; const b = seg.indexOf('['); if (b>=0) seg = seg.slice(0,b);
    if (/^addresses?$/i.test(seg)) { pos = i; const m = raw.match(/\[(\d+)\]/); if (m) idxVal = (parseInt(m[1],10) || 0) + 1; break; }
  }
  if (pos < 0) return null;
  if (idxVal == null) idxVal = 1;
  let lastRaw = parts[parts.length-1] || ''; const lb = lastRaw.indexOf('['); if(lb>=0) lastRaw = lastRaw.slice(0,lb);
  return { index: idxVal, field: lastRaw };
}

// Maps a group/name to a Bootstrap icon class
export function iconClassFor(name){
  const t = String(name || '').toLowerCase();
  if (t === 'summary') return 'bi-list-check';
  if (t === 'person' || t === 'basics') return 'bi-person-vcard';
  if (t === 'names' || t === 'name') return 'bi-person-lines-fill';
  if (t === 'addresses' || t === 'address') return 'bi-geo-alt';
  if (t === 'foreignssns' || t === 'foreignssn' || t === 'ssns' || t === 'ssn') return 'bi-passport';
  if (t === 'juridicalparents' || t === 'juridicalparent') return 'bi-people-fill';
  if (t === 'biologicalparents' || t === 'parents' || t.includes('parent') || t.includes('guardian') || t.includes('family')) return 'bi-people-fill';
  if (t.includes('basic') || t.includes('personal') || t.includes('overview') || t.includes('core')) return 'bi-person-vcard';
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

// Unique id generator for dynamic accordions/widgets
let __uidCounter = 0;
export function nextUid(prefix){
  __uidCounter = (__uidCounter + 1) | 0;
  const p = prefix ? String(prefix).replace(/[^a-z0-9_-]/gi,'').toLowerCase() : 'id';
  return 'gpiv-' + p + '-' + Date.now().toString(36) + '-' + __uidCounter.toString(36);
}

// Shared UI utilities
export function clearChildren(node){ try { while (node && node.firstChild) { node.removeChild(node.firstChild); } } catch { /* noop */ } }

export function handleAccordionKeydown(e, scopeRoot){
  try{
    const t = e.target;
    if (!t || !t.classList || !t.classList.contains('accordion-button')) return;
    if (!scopeRoot || !scopeRoot.contains(t)) return;
    const key = e.key;
    if (key!== 'ArrowDown' && key!== 'ArrowUp' && key!== 'ArrowLeft' && key!== 'ArrowRight' && key!== 'Home' && key!== 'End') return;
    const acc = t.closest('.accordion'); if (!acc) return;
    const btns = Array.prototype.slice.call(acc.querySelectorAll('.accordion-header .accordion-button'));
    const idx = btns.indexOf(t); if (idx < 0) return;
    e.preventDefault();
    let nextIdx = idx;
    if (key==='ArrowDown' || key==='ArrowRight') nextIdx = (idx+1) % btns.length;
    else if (key==='ArrowUp' || key==='ArrowLeft') nextIdx = (idx-1+btns.length) % btns.length;
    else if (key==='Home') nextIdx = 0;
    else if (key==='End') nextIdx = btns.length-1;
    const next = btns[nextIdx]; if (next && next.focus) next.focus();
  } catch { /* noop */ }
}

export async function copyToClipboard(text){
  try { if (navigator && navigator.clipboard && navigator.clipboard.writeText) { await navigator.clipboard.writeText(String(text||'')); return true; } } catch { }
  return false;
}

export function downloadBlob(filename, content, mime){
  try{
    const blob = new Blob([content || ''], { type: mime || 'application/octet-stream' });
    const a = document.createElement('a');
    if (URL && URL.createObjectURL) { a.href = URL.createObjectURL(blob); a.download = filename || 'download'; document.body.appendChild(a); a.click(); URL.revokeObjectURL(a.href); a.remove(); }
  } catch(e){ try{ console.error('gpiv: download failed', e); }catch{} }
}

export function toggleFullscreenWithCssFallback(el, cssClass){
  try{
    if (!el) return;
    const isApiFull = document.fullscreenElement === el;
    const hasCssFull = el.classList.contains(cssClass);
    if (isApiFull) {
      if (document.exitFullscreen) { document.exitFullscreen().catch(() => { el.classList.remove(cssClass); }); } else { el.classList.remove(cssClass); }
      return;
    }
    if (hasCssFull) { el.classList.remove(cssClass); return; }
    function enter(){ if (el.requestFullscreen) { el.requestFullscreen({ navigationUI:'hide' }).catch(() => { el.classList.add(cssClass); }); } else { el.classList.add(cssClass); } }
    if (document.fullscreenElement && document.fullscreenElement !== el) {
      if (document.exitFullscreen) { document.exitFullscreen().then(() => enter()).catch(() => { el.classList.add(cssClass); }); } else { el.classList.add(cssClass); }
    } else { enter(); }
  } catch { el && el.classList && el.classList.toggle(cssClass); }
}

export function isFullscreenFor(el){ try{ return document.fullscreenElement === el; } catch { return false; } }

// UMD-style bridge for non-module tests and simple pages
try {
  if (typeof window !== 'undefined') {
    window.gpivHelpers = window.gpivHelpers || { };
    window.gpivHelpers.prettify = prettify;
    window.gpivHelpers.parseAddressKey = parseAddressKey;
    window.gpivHelpers.iconClassFor = iconClassFor;
    window.gpivHelpers.nextUid = nextUid;
    window.gpivHelpers.clearChildren = clearChildren;
    window.gpivHelpers.handleAccordionKeydown = handleAccordionKeydown;
    window.gpivHelpers.copyToClipboard = copyToClipboard;
    window.gpivHelpers.downloadBlob = downloadBlob;
    window.gpivHelpers.toggleFullscreenWithCssFallback = toggleFullscreenWithCssFallback;
    window.gpivHelpers.isFullscreenFor = isFullscreenFor;
  }
} catch {}
