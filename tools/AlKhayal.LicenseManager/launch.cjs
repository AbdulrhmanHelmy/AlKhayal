'use strict';
const fs=require('node:fs');const path=require('node:path');const os=require('node:os');const {spawnSync,spawn}=require('node:child_process');
const {startServer}=require('./server.cjs');
function browser(url) { spawn('wscript.exe',[path.join(__dirname,'open-browser.vbs'),url],{windowsHide:true,stdio:'ignore'}).unref(); }
async function launch() {
  const dataDir=process.env.ALKHAYAL_LICENSE_MANAGER_DATA||path.join(process.env.LOCALAPPDATA||os.homedir(),'AlKhayalLicenseManager');
  fs.mkdirSync(dataDir,{recursive:true});
  if(process.platform==='win32') {
    const secured=spawnSync('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(__dirname,'secure-storage.ps1'),'-DataDirectory',dataDir],{windowsHide:true,encoding:'utf8'});
    if(secured.status!==0)throw new Error('Cannot protect owner storage.');
  }
  const sessionFile=path.join(dataDir,'session.json');
  let existing;
  try {existing=JSON.parse(fs.readFileSync(sessionFile,'utf8'));}catch(error){if(error.code!=='ENOENT'&&!(error instanceof SyntaxError))throw error;}
  if(existing && /^http:\/\/127\.0\.0\.1:\d+$/.test(existing.origin) && /^[a-f0-9]{64}$/.test(existing.token)) {
    try {
      const response=await fetch(existing.origin+'/api/status',{headers:{Authorization:'Bearer '+existing.token},signal:AbortSignal.timeout(1500)});
      if(response.ok&&(await response.json()).application==='AlKhayalLicenseManager') {if(!process.argv.includes('--no-browser'))browser(existing.origin+'/#'+existing.token);return;}
    }catch{/* A stale session file is replaced; registry/key files are never removed. */}
  }
  const app=await startServer({dataDir});
  fs.writeFileSync(sessionFile,JSON.stringify({origin:app.origin,token:app.token,pid:process.pid}),{mode:0o600});
  if(!process.argv.includes('--no-browser'))browser(app.url);
  const stop=async()=>{await app.close();process.exit(0);}; process.once('SIGTERM',stop);process.once('SIGINT',stop);
}
launch().catch(error=>{
  fs.appendFileSync(path.join(process.env.TEMP||os.tmpdir(),'alkhayal-license-manager-startup.log'),new Date().toISOString()+' License Manager startup failed: '+error.message+'\n');
  if(process.platform==='win32')spawnSync('wscript.exe',[path.join(__dirname,'error.vbs')],{windowsHide:true});
  process.exitCode=1;
});
