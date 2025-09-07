(function(){
  var form = document.getElementById('gpip-search-form');
  if (!form) return;
  var KEY='index:formState';
  var FLAG='restoreIndexOnBack';

  function qs(name){ return form.querySelector('[name="'+name+'"]'); }
  function getState(){
    return {
      Ssn: (qs('Ssn') && qs('Ssn').value) || '',
      FirstName: (qs('FirstName') && qs('FirstName').value) || '',
      LastName: (qs('LastName') && qs('LastName').value) || '',
      DateOfBirth: (qs('DateOfBirth') && qs('DateOfBirth').value) || ''
    };
  }
  function setState(s){ if(!s) return; try{
    if (qs('Ssn')) qs('Ssn').value = s.Ssn || '';
    if (qs('FirstName')) qs('FirstName').value = s.FirstName || '';
    if (qs('LastName')) qs('LastName').value = s.LastName || '';
    if (qs('DateOfBirth')) qs('DateOfBirth').value = s.DateOfBirth || '';
  }catch(e){ console.debug('Index: setState failed', e); } }

  function save(){ try{ sessionStorage.setItem(KEY, JSON.stringify(getState())); }catch(e){ console.debug('Index: sessionStorage.setItem failed', e); } }

  try{
    if (sessionStorage.getItem(FLAG) === '1'){
      var raw = sessionStorage.getItem(KEY) || '{}';
      var st = {};
      try{ st = JSON.parse(raw); }catch(e){}
      setState(st);
      sessionStorage.removeItem(FLAG);
    }
  }catch(e){ console.debug('Index: restore from sessionStorage failed', e); }

  form.addEventListener('input', save);
  form.addEventListener('change', save);
  form.addEventListener('submit', function(ev){
    try{
      var sub = ev.submitter;
      var fa = sub && sub.getAttribute && sub.getAttribute('formaction');
      var isClear = !!(fa && fa.toLowerCase().indexOf('handler=clear')>=0);
      if (isClear) sessionStorage.removeItem(KEY); else save();
    }catch(e){ console.debug('Index: submit handler failed', e); }
  });
})();
