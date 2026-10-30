'use strict';
// Test-only bridge. All generated keys/data live in the disposable directory passed by xUnit.
const fs=require('node:fs');const path=require('node:path');
const {Registry}=require('../registry.cjs');const protocol=require('../protocol.cjs');
const [root,requestFile]=process.argv.slice(2);const registry=new Registry(root);
try {
  const password=' Disposable cross language test password ';
  registry.initializeKey(password,'CREATE_OWNER_KEY');
  const requestCode=fs.readFileSync(requestFile,'utf8').replace(/^\uFEFF/,'').trim();
  const input={customerName:'عميل اختبار',phone:'',requestCode,password};
  const issued=registry.issue(input);const duplicate=registry.issue(input);
  if(!duplicate.reused||duplicate.LicenseId!==issued.LicenseId)throw new Error('Duplicate suppression failed');
  const publicXml=fs.readFileSync(registry.publicPath,'utf8');
  const backup=registry.backup();const restored=new Registry(path.join(root,'restored'));
  try {
    restored.importKey(registry.keyPath,password);
    restored.restore(backup);
    if(restored.summary().licenses!==1)throw new Error('Restore failed');
  } finally{restored.close();}
  fs.writeFileSync(path.join(root,'cross-output.json'),JSON.stringify({publicXml,keyId:protocol.keyId(publicXml),license:issued.SignedLicenseText}));
}finally{registry.close();}
