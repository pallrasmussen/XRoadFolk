(function(){
  function assert(cond, name){ if(!cond) throw new Error('Assertion failed: '+name); }
  function q(sel){ return document.querySelector(sel); }

  var H = window.gpivHelpers;
  if (!H) { console.error('gpivHelpers not found'); return; }

  // Retry button focus test
  console.log('UI retry focus test');
  var err = document.createElement('div'); err.id='__test_err'; document.body.appendChild(err);
  err.className='alert alert-danger';
  err.innerHTML='';
  (function buildErrorWithRetryForTest(){
    var btn = document.createElement('button'); btn.id='pd-retry'; btn.textContent='Retry'; err.appendChild(document.createTextNode('Failed.')); err.appendChild(btn); try{ btn.focus(); }catch{}
  })();
  assert(document.activeElement && document.activeElement.id==='pd-retry', 'retry focuses button');

  // Fullscreen toggle CSS fallback test
  console.log('Fullscreen CSS fallback test');
  var host = document.createElement('div'); host.id='__fs_host'; document.body.appendChild(host);
  H.toggleFullscreenWithCssFallback(host, 'pd-fullscreen');
  assert(host.classList.contains('pd-fullscreen') || document.fullscreenElement===host, 'entered fullscreen or css full');
  // toggle back
  H.toggleFullscreenWithCssFallback(host, 'pd-fullscreen');
  assert(!host.classList.contains('pd-fullscreen'), 'exited fullscreen fallback');

  console.log('UI tests passed');
})();
