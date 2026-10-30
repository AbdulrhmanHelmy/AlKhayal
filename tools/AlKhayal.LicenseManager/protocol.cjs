'use strict';
const crypto = require('node:crypto');
const names = ['uuid','board','bios'];
const hash = text => crypto.createHash('sha256').update(text,'utf8').digest('hex');
function parseRequest(text) {
  if(typeof text !== 'string' || !text || text.length > 8192 || /[^\x21-\x7e]/.test(text)) throw new Error('كود الجهاز غير صالح. انسخ الكود كاملًا دون تعديل.');
  const p = text.split('|');
  if(p.length !== 6 || p[0] !== 'ALKREQ' || p[1] !== '1' || p[2] !== 'AlKhayalPOS' || p[3] !== '1' || p[5] !== hash(p.slice(0,5).join('|'))) throw new Error('صيغة طلب الجهاز أو بصمته غير صحيحة.');
  const pairs = p[4].split(';');
  let previous = -1;
  if(pairs.length < 2 || pairs.length > 3) throw new Error('يلزم معرفان صالحان للجهاز على الأقل.');
  for(const pair of pairs) {
    const [name,value,...extra] = pair.split(':'); const index = names.indexOf(name);
    if(extra.length || index <= previous || index < 0 || !/^[a-f0-9]{64}$/.test(value || '')) throw new Error('بصمة الجهاز غير صالحة أو مكررة.');
    previous = index;
  }
  return { claims:p[4], requestId:p[5], fingerprint:hash(p[4]) };
}
function publicXml(key) {
  const jwk = (key.type==='public'?key:crypto.createPublicKey(key)).export({format:'jwk'});
  const b64 = value => Buffer.from(value,'base64url').toString('base64');
  return `<RSAKeyValue><Modulus>${b64(jwk.n)}</Modulus><Exponent>${b64(jwk.e)}</Exponent></RSAKeyValue>`;
}
function keyId(xml) { return 'rsa-' + hash(xml).slice(0,24); }
function publicKeyFromXml(xml) {
  const match=/^<RSAKeyValue><Modulus>([A-Za-z0-9+/=]+)<\/Modulus><Exponent>([A-Za-z0-9+/=]+)<\/Exponent><\/RSAKeyValue>$/.exec(xml);
  if(!match)throw new Error('ملف المفتاح العام غير صالح.');
  return crypto.createPublicKey({key:{kty:'RSA',n:Buffer.from(match[1],'base64').toString('base64url'),e:Buffer.from(match[2],'base64').toString('base64url')},format:'jwk'});
}
function privateKey(encryptedPem,password) {
  let key;
  try { key = crypto.createPrivateKey({key:encryptedPem,format:'pem',passphrase:password}); }
  catch { throw new Error('تعذر فتح مفتاح التوقيع. تحقق من كلمة المرور والملف.'); }
  if(key.asymmetricKeyType !== 'rsa' || key.asymmetricKeyDetails.modulusLength !== 3072) throw new Error('يلزم مفتاح RSA-3072.');
  return key;
}
function issue(request, key, id = crypto.randomUUID(), issuedUtc = new Date().toISOString().slice(0,19)+'Z') {
  const parsed = parseRequest(request); const xml = publicXml(key); const signingKeyId = keyId(xml);
  const payload = ['ALKLIC','1','AlKhayalPOS',id,'Perpetual','1',parsed.claims,issuedUtc,signingKeyId,'RSA3072-SHA256-PKCS1'].join('|');
  const signature = crypto.sign('sha256',Buffer.from(payload,'utf8'),{key,padding:crypto.constants.RSA_PKCS1_PADDING}).toString('base64');
  return {licenseId:id,issuedUtc,signingKeyId,licenseType:'Perpetual',signedLicenseText:payload+'|'+signature};
}
function verify(text, key) {
  try {
  if(typeof text !== 'string' || text.length > 8192 || /[^\x21-\x7e]/.test(text)) return false;
  const p=text.split('|');
  if(p.length!==11 || p[0]!=='ALKLIC' || p[1]!=='1' || p[2]!=='AlKhayalPOS' || p[4]!=='Perpetual' || p[5]!=='1' || p[9]!=='RSA3072-SHA256-PKCS1') return false;
  if(!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(p[3])||p[3]==='00000000-0000-0000-0000-000000000000'||!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/.test(p[7])||new Date(p[7]).toISOString().replace('.000Z','Z')!==p[7]||p[8]!==keyId(publicXml(key)))return false;
  const body='ALKREQ|1|AlKhayalPOS|1|'+p[6];parseRequest(body+'|'+hash(body));
  const signature=Buffer.from(p[10],'base64');
  if(signature.length!==384 || signature.toString('base64')!==p[10]) return false;
  return crypto.verify('sha256',Buffer.from(p.slice(0,10).join('|')),{key:key.type==='public'?key:crypto.createPublicKey(key),padding:crypto.constants.RSA_PKCS1_PADDING},signature);
  }catch{return false;}
}
module.exports={hash,parseRequest,publicXml,publicKeyFromXml,keyId,privateKey,issue,verify};
