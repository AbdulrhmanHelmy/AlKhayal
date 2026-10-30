"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
Element.prototype.validity={customError:false};
var originalMatches=Element.prototype.matches;
Element.prototype.matches=function(selector){return originalMatches.call(this,selector.replace(/\[([^=\]]+)=([^"\]]+)\]/g,'[$1="$2"]'));};
function settle(){return new Promise(function(resolve){setImmediate(resolve);});}
var sources={sales:fs.readFileSync(path.join(__dirname,"../js/modules/sales.js"),"utf8"),payments:fs.readFileSync(path.join(__dirname,"../js/modules/payment-methods.js"),"utf8")};
function harness(){
  var pending=[],calls=[],modal,modalOptions,active=true,navigation=[],storage={};
  var win={AlKhayalRenderers:{},AlKhayalSettings:{current:function(){return {};}},AlKhayalUtils:{escape:function(v){return String(v==null?'':v);},money:String,normalize:function(v){return v.toLowerCase();},debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return '';},toast:function(){},modal:function(o){modalOptions=o;modal=new Element('section');modal.innerHTML=o.body+o.actions;o.onOpen(modal);},closeModal:function(){}},AlKhayalApi:{request:function(url,options){calls.push({url:url,options:options});return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});}}};
  var context={window:win,URLSearchParams:URLSearchParams,Date:Date,BigInt:BigInt,setTimeout:setTimeout,clearTimeout:clearTimeout,sessionStorage:{getItem:function(k){return storage[k]||null;},setItem:function(k,v){storage[k]=v;},removeItem:function(k){delete storage[k];}}};
  return {win:win,context:context,pending:pending,calls:calls,host:new Element('section'),renderContext:{state:{},isActive:function(){return active;},navigate:function(route,state){navigation.push({route:route,state:state});}},navigation:navigation,modal:function(){return modal;},modalOptions:function(){return modalOptions;},leave:function(){active=false;}};
}
async function checkout(route){
  var h=harness();
  // Expose closures only in the VM, keeping production free of test APIs.
  var source=sources.sales.replace('    init();','    global.__paymentTest={state:state,draw:draw,payload:payload,postSale:postSale};');
  vm.runInNewContext(source,h.context);h.win.AlKhayalRenderers[route==='pos'?'pos':'sales-invoices']({route:route},h.host,h.renderContext);
  var test=h.win.__paymentTest;test.state.mode=route==='pos'?'touch':'edit';test.state.warehouses=[{id:1,name:'مخزن'}];test.state.warehouseId=1;test.state.lines=[{variantId:1,productUnitId:1,quantity:'1',unitPrice:'10',discount:'0',productName:'صنف'}];test.state.instruments=[{id:7,type:'wallet',name:'محفظة',number:'00123'},{id:8,type:'card',name:'فيزا',number:'00124'}];test.draw();
  var search=h.host.querySelector('[data-search]'),method=h.host.querySelector('[data-payment]');
  assert.ok(h.host.innerHTML.includes('محفظة إلكترونية')&&h.host.innerHTML.includes('فيزا'));
  method.value='wallet';method.onchange();assert.strictEqual(h.host.querySelector('[data-search]'),search,'method selection must preserve barcode focus node');
  assert.ok(h.host.querySelector('[data-payment-instruments]').innerHTML.includes('00123'));assert.ok(!h.host.querySelector('[data-payment-instruments]').innerHTML.includes('00124'));
  assert.throws(function(){test.payload();},/اختر وسيلة/);
  var instrument=h.host.querySelector('[data-instrument]');instrument.value='7';instrument.onchange();var wallet=test.payload();assert.equal(wallet.paymentMode,'cash');assert.equal(wallet.immediateMethod,'wallet');assert.equal(wallet.paymentInstrumentId,7);assert.equal(wallet.amount,undefined);
  method.value='card';method.onchange();assert.equal(test.state.paymentInstrumentId,'');assert.ok(h.host.querySelector('[data-payment-instruments]').innerHTML.includes('00124'));instrument=h.host.querySelector('[data-instrument]');instrument.value='8';instrument.onchange();assert.equal(test.payload().immediateMethod,'card');
  test.state.instruments=[];method.onchange();assert.ok(h.host.querySelector('[data-payment-instruments]').innerHTML.includes('لا توجد وسائل نشطة'));
  method.value='credit';method.onchange();assert.equal(test.payload().paymentMode,'credit');assert.equal(test.payload().paymentInstrumentId,null);
  method.value='cash';method.onchange();assert.equal(test.payload().immediateMethod,'cash');
  var first=test.postSale();test.postSale();assert.equal(h.calls.length,1,'duplicate checkout must issue one save');assert.ok(test.state.busy);h.pending[0].reject(new Error('رفض'));await first;assert.equal(test.state.busy,false);
  test.state.invoice={id:9,status:'draft'};h.context.sessionStorage.setItem('alkhayal_sale_post_9','uncertain-key');
  var retry=test.postSale();assert.equal(h.calls[1].url,'/api/sales/9');assert.equal(h.calls[1].options,undefined);
  h.pending[1].resolve({id:9,status:'posted'});await settle();assert.equal(h.calls[2].url,'/api/sales/9/post');assert.equal(JSON.parse(h.calls[2].options.body).operationKey,'uncertain-key');
  h.pending[2].resolve({id:9,status:'posted',lines:test.state.lines,totalScaled:100000});await retry;assert.equal(h.calls.length,3,'uncertain posted response must replay without a draft update');

}
async function management(){
  var h=harness();vm.runInNewContext(sources.payments,h.context);h.win.AlKhayalRenderers['payment-methods']({route:'payment-wallets'},h.host,h.renderContext);
  assert.ok(h.host.querySelector('[data-payment-results]').innerHTML.includes('جارٍ'));h.pending[0].resolve([{id:7,type:'wallet',name:'محفظة',number:'00123',isActive:true,version:1}]);await settle();
  var search=h.host.querySelector('[name="search"]');search.value='absent';search.oninput();assert.ok(h.host.querySelector('[data-payment-results]').innerHTML.includes('لا توجد وسائل مطابقة'));assert.strictEqual(h.host.querySelector('[name="search"]'),search);search.value='';search.oninput();
  h.host.querySelector('[data-edit="7"]').onclick();assert.equal(h.modalOptions().focusSelector,'[name=name]');var modal=h.modal();modal.querySelector('[name="name"]').value='جديدة';modal.querySelector('[name="number"]').value='00123';var saving=modal.querySelector('form').onsubmit({preventDefault:function(){}});modal.querySelector('form').onsubmit({preventDefault:function(){}});assert.equal(h.calls.length,2);assert.equal(h.modalOptions().beforeClose(),false);var body=JSON.parse(h.calls[1].options.body);assert.equal(body.number,'00123');assert.equal(body.version,1);h.pending[1].reject(new Error('تكرار'));await saving;assert.equal(modal.querySelector('[data-form-error]').textContent,'تكرار');
  h.host.querySelector('[data-details="7"]').onclick();h.pending[2].resolve({count:1,invoiceCount:1,totalScaled:100000,items:[{invoiceId:9,invoiceNumber:'INV-9',invoiceDate:'2026-10-28',totalScaled:100000,amountScaled:100000,method:'wallet',instrumentName:'محفظة',status:'posted'}]});await settle();assert.ok(h.host.querySelector('[data-history-results]').innerHTML.includes('INV-9'));h.host.querySelector('[data-invoice="9"]').onclick();assert.equal(h.navigation[0].route,'sales-management');assert.equal(h.navigation[0].state.saleInvoiceId,9);
  var filters=h.host.querySelector('[data-history-filters]');filters.querySelector('[name="from"]').value='2026-10-01';filters.onsubmit({preventDefault:function(){}});assert.ok(h.calls[3].url.includes('instrumentId=7')&&h.calls[3].url.includes('from=2026-10-01'));h.leave();h.pending[3].resolve({count:0,invoiceCount:0,totalScaled:0,items:[]});await settle();assert.ok(h.host.querySelector('[data-history-results]').innerHTML.includes('جارٍ'),'stale result must not repaint after leaving');
}
module.exports=async function(){await checkout('pos');await checkout('sales-management');await management();console.log('Payment methods: both checkout selectors, full-payment payload, focus node, validation, duplicate clicks, CRUD, history navigation, filters and stale response tests passed.');};
if(require.main===module)module.exports().catch(function(e){console.error(e);process.exitCode=1;});
