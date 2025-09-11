(function(){
  'use strict';
  const dbg=(m,e)=>{ try{ if(e&&console&&console.debug) console.debug(m,e);}catch(_){} };
  function readCookie(name){
    try{
      const m = document.cookie.match(new RegExp('(?:^|; )' + name.replace(/[.$?*|{}()\[\]\\\/\+^]/g, '\\$&') + '=([^;]*)'));
      return m ? decodeURIComponent(m[1]) : null;
    }catch(e){ dbg('af:readCookie', e); return null; }
  }
  function readMetaToken(){
    try{
      const m = document.querySelector('meta[name="request-verification-token"]');
      if (m && m.content) return m.content;
    }catch(e){ dbg('af:readMetaToken', e); }
    return null;
  }
  function getRequestVerificationToken(){
    const v = readMetaToken();
    if (v) return v;
    try{
      const f = document.querySelector('input[name="__RequestVerificationToken"]');
      if (f && f.value) return f.value;
    }catch(e){ dbg('af:getToken-hiddenInput', e); }
    return readCookie('RequestVerificationToken') || readCookie('.AspNetCore.Antiforgery');
  }
  const _fetch = window.fetch;
  window.fetch = function(input, init){
    init = init || {};
    const m = (init.method || (typeof input === 'object' && input.method) || 'GET').toUpperCase();
    const unsafe = (m === 'POST' || m === 'PUT' || m === 'PATCH' || m === 'DELETE');
    let sameOrigin = true;
    try {
      const url = (typeof input === 'string') ? new URL(input, location.href) : new URL(input.url || '', location.href);
      sameOrigin = (url.origin === location.origin);
    } catch(e){ dbg('af:url-parse', e); }
    if (unsafe && sameOrigin){
      const token = getRequestVerificationToken();
      if (token){
        init.headers = init.headers || {};
        if (init.headers.append){ try{ init.headers.append('RequestVerificationToken', token); }catch(e){ dbg('af:append-header', e); } }
        else { init.headers['RequestVerificationToken'] = token; }
      }
    }
    return _fetch(input, init);
  };
})();
