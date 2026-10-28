"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
var source=fs.readFileSync(path.join(__dirname,"../js/modules/renderers.js"),"utf8"),registry=JSON.parse(fs.readFileSync(path.join(__dirname,"../data/screens.json"),"utf8"));
function settle(){return new Promise(function(resolve){setImmediate(resolve);});}
function harness(){
  var modal,modalOptions,calls=[],reads=[],writes=[],pending=[],active=true;
  var rows=[{id:1,code:"S1",name:"مورد أول",payable:100},{id:2,code:"S2",name:"مورد ثان",payable:0}];
  var api={request:function(url,options){calls.push({url:url,options:options});if(options){writes.push({url:url,data:JSON.parse(options.body)});return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});}
    if(url.indexOf("/api/suppliers?")===0)return Promise.resolve({items:rows,totalCount:2,page:1,pageSize:10});
    if(url==="/api/treasury/accounts")return Promise.resolve([{id:9,name:"الخزنة",type:"cash",balance:"100.00"},{id:10,name:"بنك",type:"bank",balance:"100.00"}]);
    if(/\/invoices$/.test(url))return new Promise(function(resolve,reject){reads.push({url:url,resolve:resolve,reject:reject});});throw new Error(url);}};
  var ui={pageHeader:function(){return "";},toast:function(){},closeModal:function(){},field:function(def,value){return '<div><input id="'+def.id+'" value="'+String(value==null?"":value)+'"><small data-error-for="'+def.id+'"></small></div>';},
    collectForm:function(form,fields){var data={},valid=true;fields.forEach(function(def){var value=form.elements[def.id].value;if(def.required&&!value)valid=false;data[def.id]=def.type==="number"?Number(value):value;});return{valid:valid,data:data};},
    modal:function(options){modalOptions=options;modal=new Element("section");modal.innerHTML=options.body+options.actions;modal.querySelectorAll("form").forEach(function(form){form.elements={};form.querySelectorAll("input,select,textarea").forEach(function(input){form.elements[input.attrs.id]=input;});});if(options.onOpen)options.onOpen(modal);return modal;}};
  var win={AlKhayalUtils:{escape:function(x){return String(x==null?"":x);},money:String,date:String,debounce:function(fn){return fn;}},AlKhayalUI:ui,AlKhayalServices:{},AlKhayalCalculations:{}};
  vm.runInNewContext(source,{window:win,AlKhayalApi:api},{filename:"supplier-renderers.js"});
  var node=new Element("section");win.AlKhayalRenderers.payment({party:"supplier"},node,{state:{},isActive:function(){return active;}});
  return {node:node,reads:reads,writes:writes,pending:pending,calls:calls,modal:function(){return modal;},options:function(){return modalOptions;},leave:function(){active=false;},click:function(selector){var target=node.querySelector(selector);assert.ok(target,selector);node.onclick({target:target});}};
}
function invoice(id,type,status){return{id:id,invoiceType:type,purchaseInvoiceId:type==="purchase"?id:null,documentStatus:status||"posted",invoiceNumber:type+id,invoiceDate:"2026-10-28",total:50,paidAmount:10,returnedAmount:5,remaining:35,status:"غير مسدد"};}
async function selectSupplier(h,id){var select=h.node.querySelector("#supplierAccountsSelect");select.value=String(id);select.onchange();await settle();}
async function groupingAndPayments(){
  var h=harness();await settle();assert.equal(h.reads.length,0,"no invoices load before the operator chooses a supplier");
  await selectSupplier(h,1);assert.equal(h.reads.length,1,"only the selected supplier's invoices load");
  h.reads[0].resolve([invoice(1,"legacy"),invoice(1,"purchase"),invoice(2,"purchase","draft")]);await settle();
  var first=h.node.querySelector('[data-account-invoices="1"]');assert.ok(first.innerHTML.includes("legacy1")&&first.innerHTML.includes("purchase1"));
  assert.ok(!first.querySelector('[data-account-invoice="purchase:2"]'),"draft invoices cannot be paid");
  assert.equal(h.node.querySelectorAll("[data-supplier-account]").length,1,"exactly one supplier account is shown");
  await selectSupplier(h,"");assert.equal(h.node.querySelectorAll("[data-supplier-account]").length,0);assert.equal(h.reads.length,1,"clearing the supplier must hide details without another invoice request");
  await selectSupplier(h,2);h.reads[1].resolve([]);await settle();
  assert.equal(h.node.querySelector('[data-supplier-account="1"]'),null);assert.ok(h.node.querySelector('[data-account-invoices="2"]').innerHTML.includes("لا توجد فواتير"));
  await selectSupplier(h,1);h.reads[2].resolve([invoice(1,"legacy"),invoice(1,"purchase")]);await settle();
  h.click('[data-account-invoice="purchase:1"]');await settle();var modal=h.modal();modal.querySelector("#treasuryAccountId").value="9";modal.querySelector("#amount").value="5.10";modal.querySelector("[data-save-supplier-payment]").onclick();
  assert.equal(h.writes[0].data.purchaseInvoiceId,1);assert.equal(h.writes[0].data.invoiceId,null);assert.equal(h.writes[0].data.amount,"5.10");
  assert.equal(h.writes[0].data.treasuryAccountId,9);
  h.pending[0].reject({status:400,message:"رفض"});await settle();
  h.click('[data-account-invoice="legacy:1"]');await settle();modal=h.modal();modal.querySelector("#treasuryAccountId").value="10";modal.querySelector("#amount").value="2";modal.querySelector("[data-save-supplier-payment]").onclick();
  assert.equal(h.writes[1].data.invoiceId,1);assert.equal(h.writes[1].data.purchaseInvoiceId,null);
  assert.equal(h.writes[1].data.treasuryAccountId,10);
}
async function newDebtAndRetry(){
  var h=harness();await settle();await selectSupplier(h,2);h.reads[0].resolve([]);await settle();
  var input=h.node.querySelector("#supplierAccountsSearch");
  h.click('[data-add-account-invoice="2"]');var modal=h.modal();modal.querySelector("#total").value="12.34";modal.querySelector("#invoiceNumber").value="INV-9";
  var save=modal.querySelector("[data-save-supplier-invoice]");save.onclick();save.onclick();assert.equal(h.writes.length,1);
  assert.equal(h.writes[0].url,"/api/suppliers/2/invoices");assert.equal(h.writes[0].data.total,"12.34");assert.ok(h.writes[0].data.operationKey);assert.equal(h.options().beforeClose(),false);
  var original=JSON.stringify(h.writes[0].data);h.pending[0].reject({status:0,message:"فقد الرد"});await settle();
  assert.ok(modal.querySelector("#total").disabled);assert.equal(h.options().beforeClose(),false);save.onclick();
  assert.equal(JSON.stringify(h.writes[1].data),original,"lost response retry must keep the same command and key");h.pending[1].resolve({id:8,replayed:true});await settle();
  assert.strictEqual(h.node.querySelector("#supplierAccountsSearch"),input,"save refresh must keep the search mounted");
  assert.equal(h.node.querySelector("#supplierAccountsSelect").value,"2","saved supplier selection must survive refresh");
}
async function searchAndStale(){
  var h=harness();await settle();await selectSupplier(h,1);var input=h.node.querySelector("#supplierAccountsSearch");input.value="مورد";input.oninput();await settle();
  assert.strictEqual(h.node.querySelector("#supplierAccountsSearch"),input);
  h.reads[0].resolve([invoice(99,"legacy")]);await settle();assert.ok(!h.node.querySelector('[data-account-invoices="1"]').innerHTML.includes("legacy99"));
  h.reads[1].reject({message:"تعذر الاتصال"});await settle();h.click('[data-retry-account="1"]');assert.equal(h.reads.length,3);
  await selectSupplier(h,2);h.reads[2].resolve([invoice(88,"legacy")]);await settle();assert.equal(h.node.querySelector('[data-account-invoices="1"]'),null,"a late previous supplier response cannot reopen that account");
  h.leave();var html=h.node.querySelector('[data-account-invoices="2"]').innerHTML;h.reads[3].resolve([invoice(33,"legacy")]);await settle();assert.equal(h.node.querySelector('[data-account-invoices="2"]').innerHTML,html);
}
async function onAccountTreasuryRetry(){
  var h=harness();await settle();await selectSupplier(h,1);h.reads[0].resolve([invoice(1,"legacy")]);await settle();
  h.click('[data-pay-supplier-account="1"]');await settle();var modal=h.modal(),save=modal.querySelector("[data-save-supplier-payment]");
  modal.querySelector("#amount").value="5.00";save.onclick();assert.equal(h.writes.length,0,"payment account must be explicitly selected");
  modal.querySelector("#treasuryAccountId").value="9";save.onclick();save.onclick();assert.equal(h.writes.length,1);
  assert.equal(h.writes[0].data.invoiceId,null);assert.equal(h.writes[0].data.purchaseInvoiceId,null);assert.equal(h.writes[0].data.treasuryAccountId,9);
  var original=JSON.stringify(h.writes[0].data);h.pending[0].reject({status:0,message:"فقد الرد"});await settle();assert.ok(modal.querySelector("#treasuryAccountId").disabled);assert.ok(modal.querySelector("#amount").disabled);
  save.onclick();assert.equal(JSON.stringify(h.writes[1].data),original);h.pending[1].resolve({reference:"PAY-1",treasuryMovementId:7});await settle();
  var updated=invoice(1,"legacy");updated.paidAmount=15;updated.remaining=30;h.reads[1].resolve([updated]);await settle();
  assert.ok(h.node.querySelector('[data-account-invoices="1"]').innerHTML.includes('<td>15</td><td>5</td><td>30</td>'),"refresh must render the server's allocated paid and remaining balances");
}
module.exports=async function(){
  assert.deepStrictEqual(registry.modules.find(function(x){return x.id==="suppliers";}).items,["suppliers-list","supplier-payment"]);
  assert.equal(registry.screens.find(function(x){return x.route==="supplier-payment";}).title,"حسابات الموردين");
  registry.screens.filter(function(x){return x.module==="الموردين"&&x.route!=="suppliers-list"&&x.route!=="supplier-payment";}).forEach(function(x){assert.ok(x.navigationHidden);assert.equal(x.redirectTo,"supplier-payment");});
  await groupingAndPayments();await newDebtAndRetry();await searchAndStale();await onAccountTreasuryRetry();console.log("Supplier accounts, treasury selection, on-account retry/refresh, invoice families, navigation and stale-response tests passed.");
};
if(require.main===module)module.exports().catch(function(error){console.error(error);process.exitCode=1;});
