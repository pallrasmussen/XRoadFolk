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
      case 'fo-FO': return { pattern: 'dd-mm-yyyy', placeholder: 'dd-mm-áááá' };
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
    input.setAttribute('placeholder', input.getAttribute('placeholder') || fmt.placeholder);
    input.autocomplete = 'off';
    input.spellcheck = false;

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

    function onInput(ev){
      var before = input.value;
      var d = digitsOnly(before);
      d = clampDigits(d);
      var formatted = formatFromDigits(d);
      input.value = formatted;
      // Move caret to end after reformat to avoid jumps
      try { input.setSelectionRange(formatted.length, formatted.length); } catch {}
    }

    function onKeyDown(ev){
      var k = ev.key;
      // Allow control/navigation and submission keys
      if (k === 'Enter' || k === 'Backspace' || k === 'Delete' || k === 'Tab' || k === 'ArrowLeft' || k === 'ArrowRight' || k === 'Home' || k === 'End' || ev.ctrlKey || ev.metaKey){
        return; // do not block; Enter should submit the form
      }
      // Block non-digits
      if (!/\d/.test(k)){
        ev.preventDefault();
        return;
      }
      // If max length reached (8 digits), stop additional digits
      var d = input.value.replace(/\D+/g,'');
      if (d.length >= 8){ ev.preventDefault(); }
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
