(function(){
  function assert(cond, name){ if(!cond) throw new Error('Assertion failed: '+name); }
  function eq(a,b){ return JSON.stringify(a)===JSON.stringify(b); }

  var H = window.gpivHelpers;
  if (!H) { console.error('gpivHelpers not found'); return; }

  // parseAddressKey tests
  console.log('parseAddressKey tests');
  assert(eq(H.parseAddressKey('Person.Addresses[0].Street'), {index:1, field:'Street'}), 'addr idx 0 street');
  assert(eq(H.parseAddressKey('Person.Addresses[2].Zip'), {index:3, field:'Zip'}), 'addr idx 2 zip');
  assert(H.parseAddressKey('Person.Name')===null, 'no address path returns null');

  // iconClassFor tests
  console.log('iconClassFor tests');
  assert(H.iconClassFor('Summary').indexOf('bi-')===0, 'summary icon');
  assert(H.iconClassFor('Names')==='bi-person-lines-fill', 'names icon');
  assert(H.iconClassFor('Addresses')==='bi-geo-alt', 'addresses icon');
  assert(H.iconClassFor('Unknown')==='bi-list-ul', 'fallback icon');

  console.log('All gpiv-helpers tests passed');
})();
