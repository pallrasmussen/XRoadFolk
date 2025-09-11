(function(){
  'use strict';
  const dbg=(m,e)=>{ try{ if(e&&console&&console.debug) console.debug(m,e);}catch(_){} };
  function ready(fn){ if (document.readyState !== 'loading') fn(); else document.addEventListener('DOMContentLoaded', fn); }

  function detectCulture(){
    const langRaw = (document.documentElement && document.documentElement.lang || 'en-US').toLowerCase();
    if (langRaw.startsWith('fo')) return 'fo-FO';
    if (langRaw.startsWith('da')) return 'da-DK';
    if (langRaw.startsWith('en')) return 'en-US';
    return langRaw;
  }

  function getFormatFor(culture){
    switch (culture){
      case 'fo-FO':
      case 'da-DK': return { pattern: 'dd-mm-yyyy', placeholder: 'dd-mm-åååå' };
      default: return { pattern: 'yyyy-mm-dd', placeholder: 'yyyy-mm-dd' };
    }
  }

  function applyMask(input){
    if (!input) return;
    const culture = detectCulture();
    const fmt = getFormatFor(culture);

    input.setAttribute('type','text');
    input.setAttribute('inputmode','numeric');
    if (!input.getAttribute('placeholder')) input.setAttribute('placeholder', fmt.placeholder);
    input.autocomplete = 'off';
    input.spellcheck = false;

    const isDigit = ch => ch >= '0' && ch <= '9';
    const digitsOnly = s => (s||'').replace(/\D+/g,'');

    function formatFromDigits(d){
      const p = fmt.pattern;
      if (p === 'dd-mm-yyyy'){
        const dd = d.slice(0,2);
        const mm = d.slice(2,4);
        const yyyy = d.slice(4,8);
        let out = dd; if (mm.length) out += '-' + mm; if (yyyy.length) out += '-' + yyyy; return out;
      } else {
        const y = d.slice(0,4);
        const m = d.slice(4,6);
        const day = d.slice(6,8);
        let out2 = y; if (m.length) out2 += '-' + m; if (day.length) out2 += '-' + day; return out2;
      }
    }

    const clampDigits = d => d.slice(0,8);

    function caretIndexForDigits(formatted, count){
      if (count <= 0) return 0;
      let seen = 0;
      for (let i=0;i<formatted.length;i++){
        if (isDigit(formatted.charAt(i))){
          seen++;
          if (seen >= count) return i+1;
        }
      }
      return formatted.length;
    }

    function onInput(){
      const before = input.value;
      const caret = input.selectionStart || 0;
      const digitsBeforeCaret = digitsOnly(before.slice(0, caret)).length;

      let d = digitsOnly(before);
      d = clampDigits(d);
      const formatted = formatFromDigits(d);
      input.value = formatted;

      try {
        const newCaret = caretIndexForDigits(formatted, digitsBeforeCaret);
        input.setSelectionRange(newCaret, newCaret);
      } catch(e){ dbg('date-mask:setSelection', e); }
    }

    function onKeyDown(ev){
      const k = ev.key;
      if (k === 'Enter' || k === 'Backspace' || k === 'Delete' || k === 'Tab' || k === 'ArrowLeft' || k === 'ArrowRight' || k === 'Home' || k === 'End' || ev.ctrlKey || ev.metaKey){
        return;
      }
      if (!/\d/.test(k)){
        ev.preventDefault();
        return;
      }
      const raw = input.value;
      const totalDigits = digitsOnly(raw).length;
      const selStart = input.selectionStart || 0;
      const selEnd = input.selectionEnd || selStart;
      const selectedDigits = digitsOnly(raw.slice(selStart, selEnd)).length;
      const replacingSome = selectedDigits > 0;
      if (totalDigits >= 8 && !replacingSome){
        ev.preventDefault();
      }
    }

    function onBlur(){
      let d = digitsOnly(input.value);
      if (!d){ input.value = ''; return; }
      d = clampDigits(d);
      input.value = formatFromDigits(d);
    }

    input.addEventListener('input', onInput);
    input.addEventListener('keydown', onKeyDown);
    input.addEventListener('blur', onBlur);
  }

  ready(function(){
    try{
      const el = document.querySelector('input[name="DateOfBirth"]');
      if (el){ applyMask(el); }
    }catch(e){ dbg('date-mask:init', e); }
  });
})();
