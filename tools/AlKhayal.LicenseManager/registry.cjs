'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const {DatabaseSync} = require('node:sqlite');
const protocol = require('./protocol.cjs');
const requiredText = (value,max=500) => { if(typeof value!=='string' || !value.trim() || value.length>max) throw new Error('تحقق من الحقول المطلوبة وأطوالها.'); return value.trim(); };
const optionalText = (value,max=2000) => value == null || value === '' ? '' : requiredText(value,max);
class Registry {
  constructor(dataDir) {
    this.dataDir=path.resolve(dataDir); fs.mkdirSync(this.dataDir,{recursive:true,mode:0o700});
    this.keyPath=path.join(this.dataDir,'signing-key.pem'); this.publicPath=path.join(this.dataDir,'public.xml');
    this.db = new DatabaseSync(path.join(this.dataDir,'registry.sqlite'));
    this.db.exec(`PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;
      CREATE TABLE IF NOT EXISTS Customers(CustomerId TEXT PRIMARY KEY,CustomerName TEXT NOT NULL,Phone TEXT NOT NULL,Notes TEXT NOT NULL,CreatedUtc TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS Devices(DeviceId TEXT PRIMARY KEY,CustomerId TEXT NOT NULL REFERENCES Customers(CustomerId),DeviceRequestCode TEXT NOT NULL,FingerprintReference TEXT NOT NULL UNIQUE,FirstSeenUtc TEXT NOT NULL,DeviceLabel TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS Licenses(LicenseId TEXT PRIMARY KEY,CustomerId TEXT NOT NULL REFERENCES Customers(CustomerId),DeviceId TEXT NOT NULL REFERENCES Devices(DeviceId),SignedLicenseText TEXT NOT NULL,IssuedUtc TEXT NOT NULL,SigningKeyId TEXT NOT NULL,LicenseType TEXT NOT NULL CHECK(LicenseType='Perpetual'));
      CREATE INDEX IF NOT EXISTS IX_Devices_Customer ON Devices(CustomerId);
      CREATE INDEX IF NOT EXISTS IX_Licenses_Device ON Licenses(DeviceId,IssuedUtc);
      CREATE TRIGGER IF NOT EXISTS licenses_no_update BEFORE UPDATE ON Licenses BEGIN SELECT RAISE(ABORT,'License history is immutable'); END;
      CREATE TRIGGER IF NOT EXISTS licenses_no_delete BEFORE DELETE ON Licenses BEGIN SELECT RAISE(ABORT,'License history is immutable'); END;`);
  }
  close() { this.db.close(); }
  summary() {
    const count = table => this.db.prepare('SELECT COUNT(*) AS count FROM '+table).get().count;
    return {customers:count('Customers'),devices:count('Devices'),licenses:count('Licenses')};
  }
  customers(search='') {
    const term=optionalText(search,200);
    return this.db.prepare(`SELECT c.*, (SELECT COUNT(*) FROM Devices d WHERE d.CustomerId=c.CustomerId) DeviceCount,
      (SELECT COUNT(*) FROM Licenses l WHERE l.CustomerId=c.CustomerId) LicenseCount FROM Customers c
      WHERE instr(c.CustomerName,?)>0 OR instr(c.Phone,?)>0 OR EXISTS(SELECT 1 FROM Devices d WHERE d.CustomerId=c.CustomerId AND (instr(d.FingerprintReference,?)>0 OR instr(d.DeviceLabel,?)>0)) ORDER BY c.CreatedUtc DESC LIMIT 500`).all(term,term,term,term);
  }
  addCustomer(input) {
    const customer={CustomerId:crypto.randomUUID(),CustomerName:requiredText(input.customerName,200),Phone:optionalText(input.phone,100),Notes:optionalText(input.notes),CreatedUtc:new Date().toISOString()};
    this.db.prepare('INSERT INTO Customers VALUES (?,?,?,?,?)').run(...Object.values(customer)); return customer;
  }
  history(search='') {
    const term=optionalText(search,200);
    return this.db.prepare(`SELECT l.*,c.CustomerName,d.FingerprintReference,d.DeviceLabel FROM Licenses l JOIN Customers c ON c.CustomerId=l.CustomerId JOIN Devices d ON d.DeviceId=l.DeviceId
      WHERE instr(c.CustomerName,?)>0 OR instr(d.FingerprintReference,?)>0 OR instr(l.LicenseId,?)>0 OR c.CustomerId=? ORDER BY l.IssuedUtc DESC LIMIT 500`).all(term,term,term,term);
  }
  devices(search='') {
    const term=optionalText(search,200);
    return this.db.prepare(`SELECT d.*,c.CustomerName FROM Devices d JOIN Customers c ON c.CustomerId=d.CustomerId
      WHERE instr(c.CustomerName,?)>0 OR instr(c.Phone,?)>0 OR instr(d.FingerprintReference,?)>0 OR instr(d.DeviceLabel,?)>0 ORDER BY d.FirstSeenUtc DESC LIMIT 500`).all(term,term,term,term);
  }
  keyStatus() {
    const available=fs.existsSync(this.keyPath)&&fs.existsSync(this.publicPath);
    return {available,signingKeyId:available?protocol.keyId(fs.readFileSync(this.publicPath,'utf8')):null,dataDirectory:this.dataDir};
  }
  installKey(pem,password) {
    for(let dir=this.dataDir;;dir=path.dirname(dir)) {
      if(fs.existsSync(path.join(dir,'.git'))) throw new Error('يجب حفظ المفتاح الخاص خارج المستودع.');
      if(path.dirname(dir)===dir)break;
    }
    if(typeof password!=='string'||password.length<16) throw new Error('استخدم كلمة مرور قوية من 16 حرفًا على الأقل.');
    if(fs.existsSync(this.keyPath)||fs.existsSync(this.publicPath)) throw new Error('المفتاح موجود بالفعل. لن يتم استبداله.');
    const key=protocol.privateKey(pem,password);
    const encrypted=key.export({type:'pkcs8',format:'pem',cipher:'aes-256-cbc',passphrase:password});
    fs.writeFileSync(this.keyPath,encrypted,{flag:'wx',mode:0o600});
    fs.writeFileSync(this.publicPath,protocol.publicXml(key),{flag:'wx',mode:0o600});
    return this.keyStatus();
  }
  initializeKey(password,confirmation) {
    if(confirmation!=='CREATE_OWNER_KEY') throw new Error('يلزم تأكيد إنشاء مفتاح المالك صراحة.');
    if(typeof password!=='string'||password.length<16) throw new Error('استخدم كلمة مرور قوية من 16 حرفًا على الأقل.');
    if(fs.existsSync(this.keyPath)||fs.existsSync(this.publicPath)) throw new Error('المفتاح موجود بالفعل.');
    const {privateKey}=crypto.generateKeyPairSync('rsa',{modulusLength:3072});
    const pem=privateKey.export({type:'pkcs8',format:'pem',cipher:'aes-256-cbc',passphrase:password});
    return this.installKey(pem,password);
  }
  importKey(source,password) {
    const resolved=path.resolve(requiredText(source,2000));
    if(!resolved.toLowerCase().endsWith('.pem')||fs.statSync(resolved).size>20000) throw new Error('اختر ملف PEM مشفرًا صالحًا.');
    const pem=fs.readFileSync(resolved,'utf8');
    if(!pem.startsWith('-----BEGIN ENCRYPTED PRIVATE KEY-----')) throw new Error('يلزم ملف PKCS#8 مشفر.');
    return this.installKey(pem,password);
  }
  issue(input) {
    const request=requiredText(input.requestCode,8192); const parsed=protocol.parseRequest(request);
    const name=requiredText(input.customerName,200); const phone=optionalText(input.phone,100); const notes=optionalText(input.notes); const label=optionalText(input.deviceLabel,200);
    const existingDevice=this.db.prepare('SELECT * FROM Devices WHERE FingerprintReference=?').get(parsed.fingerprint);
    if(existingDevice) {
      const owner=this.db.prepare('SELECT * FROM Customers WHERE CustomerId=?').get(existingDevice.CustomerId);
      if((input.customerId && owner.CustomerId!==input.customerId)||owner.CustomerName!==name) throw new Error('الجهاز مسجل لعميل آخر. راجع السجل قبل الإصدار.');
      const existing=this.db.prepare('SELECT * FROM Licenses WHERE DeviceId=? ORDER BY IssuedUtc DESC LIMIT 1').get(existingDevice.DeviceId);
      if(existing) return {...existing,reused:true};
    }
    if(!this.keyStatus().available) throw new Error('مفتاح التوقيع غير مهيأ. افتح الإعدادات.');
    if(typeof input.password!=='string'||!input.password||input.password.length>500) throw new Error('أدخل كلمة مرور مفتاح التوقيع.');
    const key=protocol.privateKey(fs.readFileSync(this.keyPath,'utf8'),input.password);
    const signed=protocol.issue(request,key); // Sign before opening the short SQLite transaction.
    this.db.exec('BEGIN IMMEDIATE');
    try {
      let customer=existingDevice?this.db.prepare('SELECT * FROM Customers WHERE CustomerId=?').get(existingDevice.CustomerId):input.customerId?this.db.prepare('SELECT * FROM Customers WHERE CustomerId=?').get(requiredText(input.customerId,100)):this.db.prepare('SELECT * FROM Customers WHERE CustomerName=? AND Phone=? ORDER BY CreatedUtc LIMIT 1').get(name,phone);
      if(input.customerId&&!customer) throw new Error('العميل غير موجود.');
      if(customer&&customer.CustomerName!==name) throw new Error('اسم العميل لا يطابق العميل المحدد.');
      if(!customer) customer=this.addCustomer({customerName:name,phone,notes});
      const deviceId=existingDevice?existingDevice.DeviceId:crypto.randomUUID();
      if(!existingDevice) this.db.prepare('INSERT INTO Devices VALUES (?,?,?,?,?,?)').run(deviceId,customer.CustomerId,request,parsed.fingerprint,new Date().toISOString(),label);
      const row={LicenseId:signed.licenseId,CustomerId:customer.CustomerId,DeviceId:deviceId,SignedLicenseText:signed.signedLicenseText,IssuedUtc:signed.issuedUtc,SigningKeyId:signed.signingKeyId,LicenseType:'Perpetual'};
      this.db.prepare('INSERT INTO Licenses VALUES (?,?,?,?,?,?,?)').run(...Object.values(row));
      this.db.exec('COMMIT'); return {...row,reused:false};
    } catch(error) { this.db.exec('ROLLBACK'); throw error; }
  }
  backup() {
    return {format:'AlKhayalOwnerRegistry',version:1,createdUtc:new Date().toISOString(),customers:this.db.prepare('SELECT * FROM Customers').all(),devices:this.db.prepare('SELECT * FROM Devices').all(),licenses:this.db.prepare('SELECT * FROM Licenses').all()};
  }
  restore(data) {
    if(!data||data.format!=='AlKhayalOwnerRegistry'||data.version!==1||!['customers','devices','licenses'].every(k=>Array.isArray(data[k])&&data[k].length<=10000)) throw new Error('نسخة السجل غير مدعومة.');
    const schema={customers:['Customers',['CustomerId','CustomerName','Phone','Notes','CreatedUtc']],devices:['Devices',['DeviceId','CustomerId','DeviceRequestCode','FingerprintReference','FirstSeenUtc','DeviceLabel']],licenses:['Licenses',['LicenseId','CustomerId','DeviceId','SignedLicenseText','IssuedUtc','SigningKeyId','LicenseType']]};
    this.db.exec('BEGIN IMMEDIATE');
    try {
      for(const [name,[table,fields]] of Object.entries(schema)) for(const row of data[name]) {
        if(!row||Object.keys(row).sort().join()!==fields.slice().sort().join()||fields.some(f=>typeof row[f]!=='string'||row[f].length>8192)) throw new Error('حقول النسخة الاحتياطية غير صالحة.');
        if(name==='devices' && protocol.parseRequest(row.DeviceRequestCode).fingerprint!==row.FingerprintReference) throw new Error('بصمة النسخة الاحتياطية غير صحيحة.');
        if(name==='licenses') {
          if(!this.keyStatus().available || !protocol.verify(row.SignedLicenseText,protocol.publicKeyFromXml(fs.readFileSync(this.publicPath,'utf8')))) throw new Error('استعد مفتاح المالك الصحيح أولًا؛ توقيع السجل غير موثوق.');
          const p=row.SignedLicenseText.split('|');
          if(row.LicenseType!=='Perpetual'||p.length!==11||p[3]!==row.LicenseId||p[7]!==row.IssuedUtc||p[8]!==row.SigningKeyId||p[4]!=='Perpetual') throw new Error('سجل الترخيص غير صالح.');
          const device=this.db.prepare('SELECT * FROM Devices WHERE DeviceId=?').get(row.DeviceId);
          if(!device||device.CustomerId!==row.CustomerId||p[6]!==protocol.parseRequest(device.DeviceRequestCode).claims) throw new Error('علاقة الترخيص بالجهاز غير صحيحة.');
        }
        const existing=this.db.prepare(`SELECT * FROM ${table} WHERE ${fields[0]}=?`).get(row[fields[0]]);
        if(existing) { if(fields.some(f=>existing[f]!==row[f])) throw new Error('تعارض مع سجل محفوظ؛ لم يتم استبدال التاريخ.'); }
        else this.db.prepare(`INSERT INTO ${table} (${fields.join(',')}) VALUES (${fields.map(()=>'?').join(',')})`).run(...fields.map(f=>row[f]));
      }
      this.db.exec('COMMIT'); return this.summary();
    } catch(error) { this.db.exec('ROLLBACK'); throw error; }
  }
  backupKey() {
    if(!this.keyStatus().available) throw new Error('لا يوجد مفتاح لنسخه.');
    const dir=path.join(this.dataDir,'backups'); fs.mkdirSync(dir,{recursive:true,mode:0o700});
    const destination=path.join(dir,'signing-key-'+Date.now()+'.pem'); fs.copyFileSync(this.keyPath,destination,fs.constants.COPYFILE_EXCL);
    fs.copyFileSync(this.publicPath,destination+'.public.xml',fs.constants.COPYFILE_EXCL);
    return {path:destination}; // Key bytes never traverse HTTP, including encrypted backups.
  }
}
module.exports={Registry};
