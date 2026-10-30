"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
function settle(){return new Promise(function(resolve){setImmediate(resolve);});}
async function verify(module,renderer,route,host){
  var rows=[{id:1,invoiceNumber:"INV-NONE",invoiceDate:"2026-10-29",status:"posted",returnStatus:"none",totalScaled:100000,paidScaled:100000},{id:2,invoiceNumber:"INV-PART",invoiceDate:"2026-10-29",status:"posted",returnStatus:"partial",totalScaled:100000,paidScaled:100000},{id:3,invoiceNumber:"INV-FULL",invoiceDate:"2026-10-29",status:"posted",returnStatus:"full",totalScaled:100000,paidScaled:0},{id:4,invoiceNumber:"INV-DRAFT",invoiceDate:"2026-10-29",status:"draft",returnStatus:"none",totalScaled:100000,paidScaled:0}];
  var api={request:function(url){if(url==="/api/payment-methods/active")return Promise.resolve([]);if(url==="/api/purchases"||url.indexOf("/api/sales?")===0)return Promise.resolve(rows);if(url==="/api/inventory/warehouses")return Promise.resolve([{id:1,name:"مخزن",isActive:true,isDefault:true}]);if(url==="/api/settings/company")return Promise.resolve({});if(url==="/api/customers")return Promise.resolve([]);throw new Error("Unexpected invoice-list request: "+url);}};
  var win={AlKhayalRenderers:{},AlKhayalUtils:{escape:function(v){return String(v==null?"":v);},money:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return "";},toast:function(){}},AlKhayalApi:api,AlKhayalSettings:{current:function(){return {};}}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,"../js/modules/"+module+".js"),"utf8"),{window:win,AlKhayalApi:api,BigInt:BigInt,Date:Date,setTimeout:setTimeout,clearTimeout:clearTimeout});
  var node=new Element("section");win.AlKhayalRenderers[renderer]({route:route},node,{state:{},isActive:function(){return true;},navigate:function(){}});await settle();await settle();
  var html=node.querySelector(host).innerHTML||node.innerHTML;
  function invoiceRow(number){return html.slice(html.indexOf(number)).split("</tr>")[0];}
  assert.ok(invoiceRow("INV-PART").includes("مرتجعة جزئيًا"));assert.ok(invoiceRow("INV-FULL").includes("مرتجعة بالكامل"));
  assert.ok(!invoiceRow("INV-NONE").includes("data-invoice-return-status"));assert.ok(!invoiceRow("INV-DRAFT").includes("data-invoice-return-status"));
  assert.ok(invoiceRow("INV-PART").includes("منشورة"),"return badge must preserve the original posting status");
}
module.exports=async function(){await verify("purchasing","purchase-list","purchases-list","[data-purchase-list]");await verify("sales","sales-invoices","sales-list","#salesListResults");console.log("Sales and purchase invoice lists display persisted partial/full return badges without marking untouched invoices.");};
