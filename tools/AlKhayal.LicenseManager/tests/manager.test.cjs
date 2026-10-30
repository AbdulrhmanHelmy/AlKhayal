'use strict';
const {test}=require('node:test');const assert=require('node:assert/strict');const crypto=require('node:crypto');const fs=require('node:fs');const os=require('node:os');const path=require('node:path');
const protocol=require('../protocol.cjs');const {Registry}=require('../registry.cjs');const {startServer}=require('../server.cjs');
const password=' Disposable test key password ';
function request(seed='fake'){const claims=['uuid','board','bios'].map(n=>n+':'+protocol.hash(seed+n)).join(';');const body='ALKREQ|1|AlKhayalPOS|1|'+claims;return body+'|'+protocol.hash(body);}
function temp(){return fs.mkdtempSync(path.join(os.tmpdir(),'alk-owner-test-'));}
function remove(root){const full=path.resolve(root);assert.equal(path.dirname(full),path.resolve(os.tmpdir()));assert(path.basename(full).startsWith('alk-owner-test-'));assert(!fs.lstatSync(full).isSymbolicLink());fs.rmSync(full,{recursive:true,force:true});}
const {privateKey}=crypto.generateKeyPairSync('rsa',{modulusLength:3072});
const encrypted=privateKey.export({type:'pkcs8',format:'pem',cipher:'aes-256-cbc',passphrase:password});
function setup(root){const r=new Registry(root);r.installKey(encrypted,password);return r;}
test('strict request grammar matches canonical v1 and rejects wrong product, checksum, duplicates and oversize',()=>{
  assert.equal(protocol.parseRequest(request()).claims.split(';').length,3);
  for(const value of [request()+'a',request().replace('AlKhayalPOS','Other'),request()+'\n','a'.repeat(9000),'{}'])assert.throws(()=>protocol.parseRequest(value));
  const base='ALKREQ|1|AlKhayalPOS|1|uuid:'+protocol.hash('a')+';uuid:'+protocol.hash('b');assert.throws(()=>protocol.parseRequest(base+'|'+protocol.hash(base)));
});
test('RSA-3072 SHA256 PKCS1 exact bytes verify and tampering or PSS fail',()=>{
  const text=protocol.issue(request(),privateKey).signedLicenseText;assert(protocol.verify(text,privateKey));assert(!protocol.verify(text.replace('Perpetual','Trial'),privateKey));
  const p=text.split('|');p[3]=crypto.randomUUID();assert(!protocol.verify(p.join('|'),privateKey));
  const payload=text.split('|').slice(0,10).join('|');const pss=crypto.sign('sha256',Buffer.from(payload),{key:privateKey,padding:crypto.constants.RSA_PKCS1_PSS_PADDING});assert(!protocol.verify(payload+'|'+pss.toString('base64'),privateKey));
});
test('SQLite customers search devices idempotent issuance and append-only restart history',()=>{
  const root=temp();let r=setup(root);try{
    const customer=r.addCustomer({customerName:'عميل أحمد',phone:'010',notes:'ملاحظة'});const input={customerId:customer.CustomerId,customerName:customer.CustomerName,phone:'010',requestCode:request(),password,deviceLabel:'المحل'};
    const first=r.issue(input);assert(!first.reused);const again=r.issue(input);assert(again.reused);assert.equal(again.LicenseId,first.LicenseId);
    assert.equal(r.customers('أحمد').length,1);assert.equal(r.customers('010').length,1);assert.equal(r.devices('المحل').length,1);assert.equal(r.history(customer.CustomerId).length,1);
    assert.throws(()=>r.issue({...input,customerName:'عميل آخر'}));assert.throws(()=>r.db.exec('DELETE FROM Licenses'));assert.throws(()=>r.db.exec("UPDATE Licenses SET LicenseType='Perpetual'"));
    r.close();r=new Registry(root);assert.deepEqual(r.summary(),{customers:1,devices:1,licenses:1});assert.equal(r.history()[0].SignedLicenseText,first.SignedLicenseText);
    r.issue({...input,requestCode:request('second')});assert.equal(r.summary().licenses,2);
  }finally{r.close();remove(root);}
});
test('missing and wrong signing keys fail without partial customer or device writes',()=>{
  const root=temp();const r=new Registry(root);try{
    const input={customerName:'عميل',requestCode:request(),password};assert.throws(()=>r.issue(input));assert.equal(r.summary().customers,0);
    r.installKey(encrypted,password);assert.throws(()=>r.issue({...input,password:'wrong'}));assert.equal(r.summary().devices,0);assert.throws(()=>r.installKey(encrypted,password));
  }finally{r.close();remove(root);}
});
test('encrypted key backup restore and transactional registry merge preserve history and reject conflicts',()=>{
  const root=temp();const other=temp();const r=setup(root);const restored=new Registry(other);try{
    r.issue({customerName:'عميل',requestCode:request(),password});const backup=r.backup();const keyBackup=r.backupKey();assert(fs.readFileSync(keyBackup.path,'utf8').startsWith('-----BEGIN ENCRYPTED PRIVATE KEY-----'));
    restored.importKey(keyBackup.path,password);restored.restore(backup);restored.restore(backup);assert.equal(restored.summary().licenses,1);
    const conflict=JSON.parse(JSON.stringify(backup));conflict.customers[0].CustomerName='تغيير';assert.throws(()=>restored.restore(conflict));assert.equal(restored.customers()[0].CustomerName,'عميل');
    const broken=JSON.parse(JSON.stringify(backup));broken.licenses[0].DeviceId='missing';assert.throws(()=>restored.restore(broken));assert.equal(restored.summary().licenses,1);
    const tampered=JSON.parse(JSON.stringify(backup));tampered.licenses[0].SignedLicenseText=tampered.licenses[0].SignedLicenseText.replace('AlKhayalPOS','OtherPOS');assert.throws(()=>restored.restore(tampered));
    assert.throws(()=>restored.restore({format:'wrong',version:1}));
  }finally{r.close();restored.close();remove(root);remove(other);}
});
test('explicit first-time key initialization and strong password confirmation are required',()=>{
  const root=temp();const r=new Registry(root);try{assert.throws(()=>r.initializeKey(password,''));assert.throws(()=>r.initializeKey('short','CREATE_OWNER_KEY'));assert(!r.keyStatus().available);r.initializeKey(password,'CREATE_OWNER_KEY');assert(r.keyStatus().available);assert.throws(()=>r.initializeKey(password,'CREATE_OWNER_KEY'));}finally{r.close();remove(root);}
});
test('loopback HTTP rejects missing authentication hostile origins host rebinding and private-file requests',async()=>{
  const root=temp();const app=await startServer({dataDir:root});const headers={Authorization:'Bearer '+app.token,Origin:app.origin,'Content-Type':'application/json'};
  try {
    assert.equal(app.server.address().address,'127.0.0.1');
    assert.equal((await fetch(app.origin+'/api/status')).status,401);
    assert.equal((await fetch(app.origin+'/api/issue',{method:'POST',headers:{Origin:app.origin,'Content-Type':'application/json'},body:'{}'})).status,401);
    assert.equal((await fetch(app.origin+'/api/status',{headers:{...headers,Origin:'https://evil.invalid'}})).status,403);
    const rebound=await new Promise((resolve,reject)=>{const req=require('node:http').get(app.origin+'/api/status',{headers:{...headers,Host:'evil.invalid'}},res=>{res.resume();resolve(res.statusCode);});req.on('error',reject);});assert.equal(rebound,403);
    assert.equal((await fetch(app.origin+'/api/customers',{method:'POST',headers:{Authorization:headers.Authorization,'Content-Type':'application/json'},body:'{}'})).status,403);
    for(const route of ['/signing-key.pem','/registry.sqlite','/../registry.cjs','/api/key/private','/session.json'])assert.equal((await fetch(app.origin+route,{headers})).status,404);
    const response=await fetch(app.origin+'/api/customers',{method:'POST',headers,body:JSON.stringify({customerName:'عميل HTTP'})});assert.equal(response.status,201);
    const status=await(await fetch(app.origin+'/api/status',{headers})).json();assert.equal(status.customers,1);assert(!JSON.stringify(status).includes('PRIVATE KEY'));
    assert.equal((await fetch(app.origin+'/api/validate',{method:'POST',headers,body:JSON.stringify({requestCode:request()})})).status,200);
    app.registry.installKey(encrypted,password);
    const signed=await(await fetch(app.origin+'/api/issue',{method:'POST',headers,body:JSON.stringify({customerName:'عميل HTTP',requestCode:request(),password})})).json();assert(protocol.verify(signed.SignedLicenseText,privateKey));
    const backup=await(await fetch(app.origin+'/api/backup',{headers})).text();assert(!backup.includes('PRIVATE KEY'));assert.equal(JSON.parse(backup).licenses.length,1);
    const html=await(await fetch(app.origin)).text();assert(!/https?:\/\//.test(html));assert(!html.includes(app.token));
    const oversized=await fetch(app.origin+'/api/customers',{method:'POST',headers,body:JSON.stringify({notes:'x'.repeat(4*1024*1024)})});assert.equal(oversized.status,413);
  }finally{await app.close();remove(root);}
});
