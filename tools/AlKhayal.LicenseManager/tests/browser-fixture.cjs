'use strict';
// Disposable browser acceptance fixture against the installed Desktop package.
const fs=require('node:fs');const path=require('node:path');const os=require('node:os');
const {startServer}=require(path.join(process.argv[2],'server.cjs'));
(async()=>{const root=fs.mkdtempSync(path.join(os.tmpdir(),'alk-owner-browser-test-'));const app=await startServer({dataDir:root,token:'f'.repeat(64)});app.registry.initializeKey('Disposable browser test password only','CREATE_OWNER_KEY');console.log(JSON.stringify({origin:app.origin,root}));process.on('SIGINT',async()=>{await app.close();process.exit(0)});})();
