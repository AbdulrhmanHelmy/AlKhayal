"use strict";
var assert = require('assert'), fs = require('fs'), path = require('path'), vm = require('vm');
var Element = require('./inventory-tests.js').Element;
Element.prototype.hasAttribute = function (name) { return Object.prototype.hasOwnProperty.call(this.attrs, name); };
Element.prototype.removeAttribute = function (name) { delete this.attrs[name]; };
var source = fs.readFileSync(path.join(__dirname, '../js/modules/printing.js'), 'utf8');
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
var sections = ['company','logo','address','phone','taxInfo','header','title','reference','date','cashier','party','items','variant','unitPrice','discount','tax','quantity','lineTotal','subtotal','discountTotal','taxTotal','grandTotal','paid','remaining','footer','policy'];
function defaults() {
  return { version: 0, autoPrintSale: false, profiles: ['sale','sales-return','purchase','purchase-return','customer-payment','supplier-payment','shift-summary','shift-closing'].map(function (t) { return { documentType:t, printerName:'Recording', templateId:'builtin-sale', copies:1 }; }), templates:[{ id:'builtin-sale', name:'قالب', paper:{preset:'80mm',widthMm:80,heightMm:297,marginLeftMm:3,marginRightMm:3,marginTopMm:3,marginBottomMm:3,scale:1,continuous:false,landscape:false},nameColumnPercent:52,quantityColumnPercent:16,logoWidthMm:25,logoHeightMm:15,sections:sections.map(function(k){return {key:k,visible:true,bold:false,fontSize:10,spacingMm:1,alignment:'right'};}),footerText:'شكرًا',headerText:'',returnPolicy:'',dividers:true}] };
}
function harness() {
  var calls = [], pending = [], toasts = [], active = true, sequence = 0;
  var win = { crypto:{getRandomValues:function(bytes){bytes.fill(++sequence);}},document:{createElement:function(tag){return new Element(tag);}},AlKhayalRenderers:{},AlKhayalUtils:{escape:function(v){return String(v==null?'':v);}},AlKhayalUI:{pageHeader:function(){return '';},toast:function(message,tone){toasts.push({message:message,tone:tone});}},AlKhayalApi:{request:function(url,config){
    calls.push({url:url,config:config});
    if(config)return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});
    if(url==='/api/printing/printers')return Promise.resolve([{name:'Recording',papers:[],capabilityNote:'تعريف اختبار'}]);
    return Promise.resolve(defaults());
  }}};
  vm.runInNewContext(source,{window:win,Uint8Array:Uint8Array,FileReader:function(){}});
  var context={state:{},isActive:function(){return active;}};
  return {win:win,calls:calls,pending:pending,toasts:toasts,context:context,leave:function(){active=false;},render:function(){var node=new Element('section');win.AlKhayalRenderers['native-printing']({},node,context);return node;}};
}
async function directPrinting() {
  var h=harness(),button=new Element('button');
  var a=h.win.AlKhayalPrinting.submit('sale',42,false,button),b=h.win.AlKhayalPrinting.submit('sale',42,false,button);
  assert.strictEqual(a,b);assert.equal(h.calls.length,1);assert.equal(button.disabled,true);
  var first=JSON.parse(h.calls[0].config.body);assert.equal(first.documentId,42);assert.equal(first.documentType,'sale');assert.equal(first.automatic,false);assert.equal(first.reprint,true);assert.equal(first.totalScaled,undefined);
  h.pending[0].reject({status:0,message:'فقد الرد'});await a;assert.equal(button.disabled,false);
  a=h.win.AlKhayalPrinting.submit('sale',42,false,button);assert.equal(JSON.parse(h.calls[1].config.body).operationKey,first.operationKey);
  h.pending[1].resolve({status:'submitted_unknown',message:'خروج الورق غير مؤكد'});await a;
  a=h.win.AlKhayalPrinting.submit('sale',42,false,button);assert.notEqual(JSON.parse(h.calls[2].config.body).operationKey,first.operationKey);h.pending[2].resolve({status:'submitted_unknown',message:'أُرسلت'});await a;
  a=h.win.AlKhayalPrinting.submit('sale',42,true);assert.equal(JSON.parse(h.calls[3].config.body).automatic,true);h.pending[3].resolve({status:'failed',message:'الطابعة غير متصلة'});await a;assert.equal(h.toasts[h.toasts.length-1].tone,'warning');
  var count=h.toasts.length;a=h.win.AlKhayalPrinting.submit('sale',43,true);h.pending[4].resolve({status:'disabled'});await a;assert.equal(h.toasts.length,count);
  assert.ok(!source.includes('window.print')&&!source.includes('createObjectURL')&&!source.includes('.pdf'));
}
async function designerAndPersistence() {
  var h=harness(),node=h.render();await settle();await settle();var host=node.querySelector('[data-printing-host]');
  assert.ok(host.innerHTML.includes('مصمم القوالب'));assert.ok(host.innerHTML.includes('طباعة الفاتورة تلقائيًا بعد البيع'));
  var name=host.querySelector('[data-design="name"]');name.value='قالب معدل';host.oninput({target:name});assert.strictEqual(host.querySelector('[data-design="name"]'),name);
  var auto=host.querySelector('[data-auto]');auto.checked=true;host.oninput({target:auto});
  var width=host.querySelector('[data-design="paper.widthMm"]');width.type='number';width.value='58';host.oninput({target:width});
  var preset=host.querySelector('[data-design="paper.preset"]');preset.value='58mm';host.oninput({target:preset});host.onchange({target:preset});
  assert.equal(h.context.state.printing.settings.templates[0].paper.widthMm,58);
  var newButton=host.querySelector('[data-copy-template]');await host.onclick({target:newButton});assert.equal(h.context.state.printing.settings.templates.length,2);assert.ok(h.context.state.printing.selected.indexOf('custom-')===0);
  var deleteButton=host.querySelector('[data-delete-template]');await host.onclick({target:deleteButton});assert.equal(h.context.state.printing.settings.templates.length,1);
  var save=host.querySelector('[data-save-printing]'),task=host.onclick({target:save});host.onclick({target:save});assert.equal(h.pending.length,1);assert.equal(save.disabled,true);
  var saved=JSON.parse(h.calls[h.calls.length-1].config.body);assert.equal(saved.templates[0].name,'قالب معدل');assert.equal(saved.autoPrintSale,true);assert.equal(saved.templates[0].paper.widthMm,58);
  h.pending[0].resolve({version:1});await task;assert.equal(h.context.state.printing.settings.version,1);assert.equal(save.disabled,false);
  var preview=host.querySelector('[data-preview-template]');task=host.onclick({target:preview});assert.equal(h.calls[h.calls.length-1].url,'/api/printing/preview');h.pending[1].resolve({pngBase64:'abc'});await task;assert.equal(host.querySelector('[data-preview-image]').src,'data:image/png;base64,abc');
  task=host.onclick({target:preview});name=host.querySelector('[data-design="name"]');name.value='تعديل أثناء المعاينة';host.oninput({target:name});h.pending[2].resolve({pngBase64:'edited-stale'});await task;assert.equal(host.querySelector('[data-preview-image]').src,'data:image/png;base64,abc');assert.equal(host.querySelector('[data-preview-image]').hidden,true);
  task=host.onclick({target:preview});h.leave();h.pending[3].resolve({pngBase64:'stale'});await task;assert.equal(host.querySelector('[data-preview-image]').src,'data:image/png;base64,abc');
}
async function run(){await directPrinting();await designerAndPersistence();console.log('Native print transport, duplicate suppression, uncertain retry, explicit reprint, auto status, designer, atomic save and stale preview tests passed.');}
if(require.main===module)run().catch(function(e){console.error(e);process.exitCode=1;});
module.exports=run;
