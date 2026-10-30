"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
module.exports=async function(){
  var source=fs.readFileSync(path.join(__dirname,"../js/modules/sales.js"),"utf8").replace("    init();","    global.__pricing={state:state,appendLine:appendLine,chooseLine:chooseLine};");
  for(var route of ["pos","sales-management"]){
    var modal;
    var win={AlKhayalRenderers:{},AlKhayalSettings:{current:function(){return {};}},AlKhayalUtils:{escape:String,money:String,number:String,normalize:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return "";},badge:String,toast:function(){},closeModal:function(){},modal:function(o){modal=new Element("section");modal.innerHTML=o.body+o.actions;o.onOpen(modal);}},AlKhayalApi:{request:function(){throw new Error("Unexpected API request");}}};
    vm.runInNewContext(source,{window:win,Date:Date,BigInt:BigInt,setTimeout:setTimeout,clearTimeout:clearTimeout});
    var host=new Element("section");
    win.AlKhayalRenderers[route==="pos"?"pos":"sales-invoices"]({route:route},host,{state:{},user:{role:"administrator"},isActive:function(){return true;}});
    var t=win.__pricing;t.state.mode=route==="pos"?"touch":"edit";t.state.warehouseId=1;t.state.warehouses=[{id:1,name:"مخزن"}];
    var product={id:1,name:"صنف",salePrice:"200.00",attributes:[],capabilities:[]},variant={id:11,name:"افتراضي",isActive:true,effectiveSalePrice:"200.00",effectiveSalePriceExact:"200.0000"},unit={id:21,name:"علبة",isActive:true,isSaleUnit:true,numerator:3,denominator:1};
    t.appendLine(product,variant,unit,null,null);
    assert.strictEqual(t.state.lines[0].unitPrice,"600.00",route+": newly added old stock uses current selected-unit price");
    variant.effectiveSalePriceExact="250.0000";
    t.appendLine(product,variant,unit,null,null);
    assert.strictEqual(t.state.lines[0].unitPrice,"600.00",route+": adding quantity preserves an existing draft line price");
    t.state.lines=[];variant.effectiveSalePrice="3.33";variant.effectiveSalePriceExact="3.3333";
    t.appendLine(product,variant,unit,null,null);
    assert.strictEqual(t.state.lines[0].unitPrice,"10.00",route+": convert exact base price before piastre rounding");
    t.state.lines=[];
    var other={id:12,name:"خاص",isActive:true,effectiveSalePrice:"225.00",effectiveSalePriceExact:"225.0000"};
    product.variants=[variant,other];product.units=[unit];
    await t.chooseLine(product,variant,unit,"150.00",true);
    modal.querySelector("[data-pick-variant]").value="12";
    modal.querySelector("[data-confirm-pick]").onclick();
    assert.strictEqual(t.state.lines[0].variantId,12);
    assert.strictEqual(t.state.lines[0].unitPrice,"675.00",route+": picker uses the chosen variant override instead of the scanned variant price");
  }
  console.log("Pricing/FIFO frontend: current price, exact unit conversion, saved line policy and variant picker passed for Standard Sales and Touch POS.");
};
if(require.main===module)module.exports().catch(function(e){console.error(e);process.exitCode=1;});
