(function(){
  document.addEventListener('DOMContentLoaded', function(){
    document.querySelectorAll('[data-egg]').forEach(function(el){
      var timer=null;
      el.addEventListener('mouseenter', function(){
        timer=setTimeout(function(){ alert('хули ты на меня курсор навел'); },5000);
      });
      el.addEventListener('mouseleave', function(){ clearTimeout(timer); });
    });
  });
})();
