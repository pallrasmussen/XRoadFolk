(function(){
  'use strict';
  function ready(fn){ if (document.readyState !== 'loading') fn(); else document.addEventListener('DOMContentLoaded', fn); }

  function detectCulture(){
    var lang = (document.documentElement && document.documentElement.lang || 'en-US').toLowerCase();
    // Normalize
    if (lang.startsWith('fo')) return 'fo-FO';
    if (lang.startsWith('da')) return 'da-DK';
    if (lang.startsWith('en')) return 'en-US';
    return lang;
  }

  function getFormatFor(culture){
    switch (culture){
      case 'fo-FO': return { pattern: 'dd-mm-yyyy', placeholder: 'dd-mm-åååå' };
      case 'da-DK': return { pattern: 'dd-mm-yyyy', placeholder: 'dd-mm-åååå' };
      default:      return { pattern: 'yyyy-mm-dd', placeholder: 'yyyy-mm-dd' };
    }
  }

  function applyMask(input){
    if (!input) return;
    var culture = detectCulture();
    var fmt = getFormatFor(culture);

    // Disable native picker hinting
    input.setAttribute('type','text');
    input.setAttribute('inputmode','numeric');
    if (!input.getAttribute('placeholder')) input.setAttribute('placeholder', fmt.placeholder);
    input.autocomplete = 'off';
    input.spellcheck = false;

    function isDigit(ch){ return ch >= '0' && ch <= '9'; }
    function digitsOnly(s){ return (s||'').replace(/\D+/g,''); }

    function formatFromDigits(d){
      var p = fmt.pattern;
      if (p === 'dd-mm-yyyy'){
        var dd = d.slice(0,2);
        var mm = d.slice(2,4);
        var yyyy = d.slice(4,8);
        var out = dd; if (mm.length) out += '-' + mm; if (yyyy.length) out += '-' + yyyy; return out;
      } else { // yyyy-mm-dd
        var y = d.slice(0,4);
        var m = d.slice(4,6);
        var day = d.slice(6,8);
        var out2 = y; if (m.length) out2 += '-' + m; if (day.length) out2 += '-' + day; return out2;
      }
    }

    function clampDigits(d){ return d.slice(0,8); }

    function caretIndexForDigits(formatted, count){
      if (count <= 0) return 0;
      var seen = 0;
      for (var i=0;i<formatted.length;i++){
        if (isDigit(formatted.charAt(i))){
          seen++;
          if (seen >= count) return i+1; // caret just after that digit
        }
      }
      return formatted.length;
    }

    function onInput(){
      // Preserve caret relative to digits
      var before = input.value;
      var caret = input.selectionStart || 0;
      var digitsBeforeCaret = digitsOnly(before.slice(0, caret)).length;

      var d = digitsOnly(before);
      d = clampDigits(d);
      var formatted = formatFromDigits(d);
      input.value = formatted;

      // Restore caret at equivalent digit position
      try {
        var newCaret = caretIndexForDigits(formatted, digitsBeforeCaret);
        input.setSelectionRange(newCaret, newCaret);
      } catch {}
    }

    function onKeyDown(ev){
      var k = ev.key;
      // Allow control/navigation and submission keys
      if (k === 'Enter' || k === 'Backspace' || k === 'Delete' || k === 'Tab' || k === 'ArrowLeft' || k === 'ArrowRight' || k === 'Home' || k === 'End' || ev.ctrlKey || ev.metaKey){
        return; // let browser handle
      }
      // Block non-digits
      if (!/\d/.test(k)){
        ev.preventDefault();
        return;
      }
      // Respect max 8 digits, but allow replacement when selection contains at least one digit
      var raw = input.value;
      var totalDigits = digitsOnly(raw).length;
      var selStart = input.selectionStart || 0;
      var selEnd = input.selectionEnd || selStart;
      var selectedDigits = digitsOnly(raw.slice(selStart, selEnd)).length;
      var replacingSome = selectedDigits > 0;
      if (totalDigits >= 8 && !replacingSome){
        ev.preventDefault();
      }
    }

    function onBlur(){
      var d = digitsOnly(input.value);
      if (!d){ input.value = ''; return; }
      d = clampDigits(d);
      input.value = formatFromDigits(d);
    }

    input.addEventListener('input', onInput);
    input.addEventListener('keydown', onKeyDown);
    input.addEventListener('blur', onBlur);
  }

  ready(function(){
    var el = document.querySelector('input[name="DateOfBirth"]');
    if (el){ applyMask(el); }
  });
})();
