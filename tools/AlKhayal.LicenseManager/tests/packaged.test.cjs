'use strict';
const {test}=require('node:test');const assert=require('node:assert/strict');const fs=require('node:fs');const path=require('node:path');const os=require('node:os');const {spawn,spawnSync}=require('node:child_process');const protocol=require('../protocol.cjs');
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
test('Desktop packaged launcher works with outbound denial, reuses one instance and persists SQLite/key/licenses across restart',{skip:process.platform!=='win32'},async()=>{
  const desktop=spawnSync('powershell.exe',['-NoProfile','-Command',"[Environment]::GetFolderPath('DesktopDirectory')"],{encoding:'utf8',windowsHide:true}).stdout.trim();
  const packageRoot=process.env.ALKHAYAL_LICENSE_MANAGER_PACKAGE||path.join(desktop,'AlKhayal License Manager');
  const runtime=path.join(packageRoot,'runtime','node.exe');assert(fs.existsSync(runtime));
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'alk-owner-test-'));let child;
  const start=()=>{const process=spawn(runtime,[path.join(packageRoot,'launch.cjs'),'--no-browser'],{windowsHide:true,stdio:['ignore','ignore','pipe'],env:{...globalThis.process.env,ALKHAYAL_LICENSE_MANAGER_DATA:root,NODE_OPTIONS:'--require "'+path.join(__dirname,'offline-guard.cjs').replace(/\\/g,'/')+'"'}});process.startupError='';process.stderr.on('data',chunk=>{process.startupError=(process.startupError+chunk.toString()).slice(0,2000);});return process;};
  async function ready(previousPid){for(let attempt=0;attempt<100;attempt++){try{const info=JSON.parse(fs.readFileSync(path.join(root,'session.json'),'utf8'));if(info.pid!==previousPid){const response=await fetch(info.origin+'/api/status',{headers:{Authorization:'Bearer '+info.token}});if(response.ok)return info;}}catch{/* Wait only for this newly launched disposable process. */}if(child.exitCode!==null)throw new Error('Packaged launcher exited early: '+child.startupError);await pause(100);}throw new Error('Packaged launcher readiness timeout');}
  async function stop(){if(child&&child.exitCode===null){const exited=new Promise(resolve=>child.once('exit',resolve));child.kill();await exited;}}
  try {
    child=start();let session=await ready();
    const call=async(route,input)=>{const response=await fetch(session.origin+'/api/'+route,{method:input===undefined?'GET':'POST',headers:{Authorization:'Bearer '+session.token,...(input===undefined?{}:{Origin:session.origin,'Content-Type':'application/json'})},...(input===undefined?{}:{body:JSON.stringify(input)})});const data=await response.json();assert(response.ok,data.error);return data;};
    const password='Disposable packaged test password';await call('key/initialize',{password,confirmation:'CREATE_OWNER_KEY'});
    const body='ALKREQ|1|AlKhayalPOS|1|'+['uuid','board','bios'].map(n=>n+':'+protocol.hash('packaged-fake-'+n)).join(';');
    const issued=await call('issue',{customerName:'عميل اختبار الحزمة',requestCode:body+'|'+protocol.hash(body),password});
    assert.equal((await call('status')).licenses,1);const firstPid=session.pid;
    const duplicate=start();await new Promise((resolve,reject)=>{duplicate.once('error',reject);duplicate.once('exit',code=>code===0?resolve():reject(new Error('Duplicate launcher failed')));});
    assert.equal(JSON.parse(fs.readFileSync(path.join(root,'session.json'),'utf8')).pid,firstPid);
    await stop();child=start();session=await ready(firstPid);
    const status=await call('status');assert.equal(status.customers,1);assert.equal(status.devices,1);assert.equal(status.licenses,1);assert(status.key.available);assert.equal((await call('history'))[0].SignedLicenseText,issued.SignedLicenseText);
    assert.equal((await fetch(session.origin+'/signing-key.pem')).status,404);
    assert.equal((await fetch(session.origin+'/')).status,200);
  }finally{
    await stop();const full=path.resolve(root);assert.equal(path.dirname(full),path.resolve(os.tmpdir()));assert(path.basename(full).startsWith('alk-owner-test-'));assert(!fs.lstatSync(full).isSymbolicLink());fs.rmSync(full,{recursive:true,force:true});
  }
});
