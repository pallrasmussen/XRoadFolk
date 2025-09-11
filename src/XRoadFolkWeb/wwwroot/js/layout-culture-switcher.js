(function(){
  document.addEventListener('DOMContentLoaded', function(){
    try{
      const sel = document.querySelector('form[action="/set-culture"] select[name="culture"]');
      if (sel) sel.addEventListener('change', function(){ if (sel.form) sel.form.submit(); });
    }catch(e){ console.error('Culture switch init failed', e); }
  });
})();
