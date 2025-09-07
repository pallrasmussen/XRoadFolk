(function(){
  var form = document.getElementById('gpip-search-form');
  if (!form) return;
  form.addEventListener('submit', function(){
    try{
      var sec = document.getElementById('person-details-section');
      var body = document.getElementById('person-details-body');
      var err = document.getElementById('person-details-error');
      var loading = document.getElementById('person-details-loading');
      if (sec) sec.classList.add('d-none');
      if (body) body.innerHTML='';
      if (err) { err.classList.add('d-none'); err.textContent=''; }
      if (loading) loading.classList.add('d-none');
      window.lastPid = null;
      if (window.personCache && window.personCache.clear) window.personCache.clear();
      try{
        var url = new URL(window.location.href);
        url.searchParams.delete('gpivPublicId');
        history.replaceState(null,'',url.toString());
      }catch{}
    }catch{}
  });
})();
