'use strict';
const http=require('node:http');
const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const {Registry}=require('./registry.cjs');
const protocol=require('./protocol.cjs');
async function startServer({dataDir,port=0,token=crypto.randomBytes(32).toString('hex')}={}) {
  const registry=new Registry(dataDir); let origin;
  const staticFiles={'/':['index.html','text/html; charset=utf-8'],'/app.js':['app.js','application/javascript; charset=utf-8'],'/style.css':['style.css','text/css; charset=utf-8']};
  const server=http.createServer(async(req,res)=>{
    const send=(status,data)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8'});res.end(JSON.stringify(data));};
    res.setHeader('Cache-Control','no-store');res.setHeader('X-Content-Type-Options','nosniff');res.setHeader('Referrer-Policy','no-referrer');
    res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'");
    try {
      if(req.headers.host!==new URL(origin).host || (req.headers.origin && req.headers.origin!==origin) || (req.headers['sec-fetch-site'] && !['same-origin','none'].includes(req.headers['sec-fetch-site']))) return send(403,{error:'مصدر الطلب غير مسموح.'});
      const url=new URL(req.url,origin);
      if(url.pathname.startsWith('/api/')) {
        const authorization=req.headers.authorization||''; const expected='Bearer '+token;
        if(authorization.length!==expected.length || !crypto.timingSafeEqual(Buffer.from(authorization),Buffer.from(expected))) return send(401,{error:'افتح الأداة من اختصار سطح المكتب لتسجيل جلسة آمنة.'});
        if(req.method!=='GET' && req.headers.origin!==origin) return send(403,{error:'مصدر الطلب مطلوب.'});
        let input={};
        if(req.method==='POST') {
          if(req.headers['content-type']!=='application/json') return send(415,{error:'يلزم JSON.'});
          const chunks=[];let size=0;
          for await(const chunk of req) {size+=chunk.length;if(size>4*1024*1024){send(413,{error:'الطلب كبير جدًا.'});return;}chunks.push(chunk);}
          try {input=JSON.parse(Buffer.concat(chunks).toString('utf8'));} catch {return send(400,{error:'JSON غير صالح.'});}
          if(!input||typeof input!=='object'||Array.isArray(input))return send(400,{error:'الطلب غير صالح.'});
        }
        const route=req.method+' '+url.pathname;
        switch(route) {
          case 'GET /api/status':return send(200,{...registry.summary(),key:registry.keyStatus(),application:'AlKhayalLicenseManager'});
          case 'GET /api/customers':return send(200,registry.customers(url.searchParams.get('search')||''));
          case 'POST /api/customers':return send(201,registry.addCustomer(input));
          case 'GET /api/history':return send(200,registry.history(url.searchParams.get('search')||''));
          case 'GET /api/devices':return send(200,registry.devices(url.searchParams.get('search')||''));
          case 'POST /api/validate':return send(200,protocol.parseRequest(input.requestCode));
          case 'POST /api/issue':return send(200,registry.issue(input));
          case 'GET /api/public-key': {
            if(!registry.keyStatus().available)throw new Error('مفتاح التوقيع غير مهيأ.');
            return send(200,{xml:fs.readFileSync(registry.publicPath,'utf8'),signingKeyId:registry.keyStatus().signingKeyId});
          }
          case 'GET /api/backup':return send(200,registry.backup());
          case 'POST /api/restore':return send(200,registry.restore(input));
          case 'POST /api/key/initialize':return send(201,registry.initializeKey(input.password,input.confirmation));
          case 'POST /api/key/import':return send(201,registry.importKey(input.path,input.password));
          case 'POST /api/key/backup':return send(200,registry.backupKey());
          default: return send(404,{error:'المسار غير موجود.'});
        }
      }
      const file=staticFiles[url.pathname];
      if(req.method!=='GET'||!file)return send(404,{error:'المسار غير موجود.'});
      res.writeHead(200,{'Content-Type':file[1]});res.end(fs.readFileSync(path.join(__dirname,'public',file[0])));
    } catch(error) {
      // Never return/log request bodies, passwords, key material or SQLite internals.
      const safe=/^[\u0600-\u06ff]/.test(error.message)?error.message:'تعذر إكمال العملية. تحقق من البيانات والصلاحيات والنسخة الاحتياطية.';
      send(400,{error:safe});
    }
  });
  server.requestTimeout=15000;server.headersTimeout=10000;
  await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',resolve);});
  origin='http://127.0.0.1:'+server.address().port;
  return {server,registry,origin,token,url:origin+'/#'+token,close:async()=>{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));registry.close();}};
}
module.exports={startServer};
