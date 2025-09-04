(function(){
  'use strict';
  function readCookie(name){
    var m = document.cookie.match(new RegExp('(?:^|; )' + name.replace(/[.$?*|{}()\[\]\\\/\+^]/g, '\\$&') + '=([^;]*)'));
    return m ? decodeURIComponent(m[1]) : null;
  }
  function readMetaToken(){
    try{
      var m = document.querySelector('meta[name="request-verification-token"]');
      if (m && m.content) return m.content;
    }catch{}
    return null;
  }
  // Discover the antiforgery token value from meta, hidden input, or cookie
  function getRequestVerificationToken(){
    var v = readMetaToken();
    if (v) return v;
    try{
      var f = document.querySelector('input[name="__RequestVerificationToken"]');
      if (f && f.value) return f.value;
    }catch{}
    // Fallback to cookie (name may vary depending on configuration)
    return readCookie('RequestVerificationToken') || readCookie('.AspNetCore.Antiforgery');
  }
  // Wrap fetch to automatically include the header for same-origin unsafe methods
  var _fetch = window.fetch;
  window.fetch = function(input, init){
    init = init || {};
    var m = (init.method || (typeof input === 'object' && input.method) || 'GET').toUpperCase();
    var unsafe = (m === 'POST' || m === 'PUT' || m === 'PATCH' || m === 'DELETE');
    var sameOrigin = true;
    try {
      var url = (typeof input === 'string') ? new URL(input, location.href) : new URL(input.url || '', location.href);
      sameOrigin = (url.origin === location.origin);
    } catch {}
    if (unsafe && sameOrigin){
      var token = getRequestVerificationToken();
      if (token){
        init.headers = init.headers || {};
        if (init.headers.append){ init.headers.append('RequestVerificationToken', token); }
        else { init.headers['RequestVerificationToken'] = token; }
      }
    }
    return _fetch(input, init);
  };
})();
