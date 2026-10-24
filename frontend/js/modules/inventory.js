(function (global) {
  "use strict";
  var U=global.AlKhayalUtils,UI=global.AlKhayalUI,Api=global.AlKhayalApi,R=global.AlKhayalRenderers;
  function json(value){return JSON.stringify(value);}
  function key(){var bytes=new Uint8Array(16);if(global.crypto&&global.crypto.getRandomValues)global.crypto.getRandomValues(bytes);else for(var i=0;i<16;i++)bytes[i]=Math.floor(Math.random()*256);return Array.prototype.map.call(bytes,function(x){return ("0"+x.toString(16)).slice(-2);}).join("");}
  function formatQty(value){return (Number(value||0)/1000000).toLocaleString("ar-EG",{maximumFractionDigits:6});}
  function formatValue(value){return (Number(value||0)/10000).toLocaleString("ar-EG",{minimumFractionDigits:2,maximumFractionDigits:2});}
  function error(host,reason){var target=host.querySelector("[data-inventory-error]");if(target){target.textContent=reason.message||reason;target.hidden=false;}else UI.toast(reason.message||reason,"danger");}
  function request(path,method,data){return Api.request(path,{method:method,body:json(data)});}
  function label(type){return ({opening:"رصيد افتتاحي",manual_receipt:"إضافة بضاعة",manual_issue:"إخراج بضاعة",adjustment:"تسوية مخزون",count:"جرد المخزون",transfer:"نقل بضاعة",reversal:"عكس حركة"})[type]||"حركة مخزون";}
  function countStatus(status){return ({draft:"مسودة",posted:"مرحّل",canceled:"ملغى"})[status]||"غير معروفة";}
  function warehouseOptions(rows,current){return rows.filter(function(w){return w.isActive;}).map(function(w){return '<option value="'+w.id+'"'+(String(w.id)===String(current)?' selected':'')+'>'+U.escape(w.name)+'</option>';}).join("");}
  function formatExactValue(value){var negative=value<0n;if(negative)value=-value;var cents=(value+50n)/100n;return (negative?"−":"")+(cents/100n).toLocaleString("ar-EG")+"٫"+String(cents%100n).padStart(2,"0");}
  function inventoryValuation(rows){
    var cost=0n,sales=0n,costComplete=true,saleComplete=true;
    rows.forEach(function(x){
      if(!Number.isSafeInteger(x.quantityBaseScaled)){costComplete=false;saleComplete=false;return;}
      if(x.quantityBaseScaled===0)return;
      if(!x.costBasisComplete||!Number.isSafeInteger(x.valueScaled))costComplete=false;else cost+=BigInt(x.valueScaled);
      if(x.salePriceScaled===null||x.salePriceScaled===undefined||!Number.isSafeInteger(x.salePriceScaled))saleComplete=false;
      else sales+=(BigInt(x.quantityBaseScaled)*BigInt(x.salePriceScaled)+500000n)/1000000n;
    });
    return {cost:cost,sales:sales,costComplete:costComplete,saleComplete:saleComplete,complete:costComplete&&saleComplete};
  }
  function directory(screen,container,context){var state=context.state;state.warehouseId=state.warehouseId||null;state.view=state.view||"balances";load();
    function load(){container.innerHTML=UI.pageHeader(screen)+'<div class="card"><div class="card-body">جارٍ تحميل المخازن...</div></div>';Promise.all([Api.request("/api/inventory/warehouses"),Api.request("/api/features/effective")]).then(function(results){if(!context.isActive())return;var rows=results[0],snapshot=results[1],multiple=(snapshot.modules||[]).some(function(x){return x.key==="multiple-warehouses"&&x.isEnabled;});if(!state.warehouseId||!rows.some(function(x){return x.id===Number(state.warehouseId);}))state.warehouseId=(rows.find(function(x){return x.isDefault;})||rows[0]||{}).id;draw(rows,multiple);}).catch(function(reason){if(!context.isActive())return;container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state"><strong>تعذر تحميل المخازن</strong><span>'+U.escape(reason.message)+'</span><button class="button" data-retry>إعادة المحاولة</button></div>';container.querySelector("[data-retry]").onclick=load;});}
    function draw(rows,multiple){var selected=rows.find(function(w){return w.id===Number(state.warehouseId);})||rows[0],admin=context.user.role==="administrator";if(!selected){container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state">لا توجد مخازن</div>';return;}
      var actions=admin?'<button class="button" data-warehouse-edit>تعديل الاسم</button>'+(multiple?'<button class="button primary" data-warehouse-add>+ إضافة مخزن</button>':''):'';
      container.innerHTML=UI.pageHeader(screen,actions)+'<section class="card inventory-directory"><div class="card-body"><div class="inventory-head"><div class="field grow"><label for="inventoryWarehouse">المخزن</label><select class="select" id="inventoryWarehouse">'+warehouseOptions(rows,selected.id)+'</select></div><div class="inventory-summary"><strong>'+U.escape(selected.name)+'</strong><small>'+(selected.isDefault?'المخزن الافتراضي · ':'')+(selected.isActive?'نشط':'غير نشط')+' · '+U.escape(selected.productCount)+' أصناف · '+U.escape(selected.quantity)+' وحدة</small></div></div><div class="inventory-actions"><button class="button" data-view="balances">الأرصدة والتقييم</button><button class="button" data-operation="manual_receipt">إضافة بضاعة</button><button class="button" data-operation="manual_issue">إخراج بضاعة</button><button class="button" data-operation="adjustment">تسوية مخزون</button><button class="button" data-view="count">جرد المخزون</button>' +(multiple?'<button class="button primary" data-operation="transfer">نقل بضاعة</button>':'')+'<button class="button" data-view="history">حركة المخزون</button></div></div></section><div id="inventoryContent" aria-live="polite"></div>';
      var warehouseBrowser='<section class="card inventory-warehouse-browser"><div class="card-body"><div class="inventory-history-filters"><div class="field grow"><label for="warehouseSearch">بحث المخازن</label><input class="input" id="warehouseSearch" placeholder="اسم المخزن"></div></div><div class="inventory-warehouse-choices">'+rows.map(function(w){return '<button class="button" type="button" data-select-warehouse="'+w.id+'" data-search-text="'+U.escape(U.normalize(w.name))+'" data-active="'+(w.isActive?'active':'inactive')+'">'+U.escape(w.name)+'</button>';}).join("")+'</div></div></section>';
      container.querySelector(".inventory-directory").insertAdjacentHTML("beforebegin",warehouseBrowser);
      var warehouseSearch=container.querySelector("#warehouseSearch");
      function filterWarehouses(){var needle=U.normalize(warehouseSearch.value);container.querySelectorAll("[data-select-warehouse]").forEach(function(button){button.hidden=button.getAttribute("data-search-text").indexOf(needle)<0;});}
      warehouseSearch.oninput=filterWarehouses;
      container.querySelectorAll("[data-select-warehouse]").forEach(function(button){button.onclick=function(){state.warehouseId=Number(this.getAttribute("data-select-warehouse"));draw(rows,multiple);};});
      container.querySelector("#inventoryWarehouse").onchange=function(){state.warehouseId=Number(this.value);draw(rows,multiple);};
      container.onclick=function(event){if(event.target.closest("[data-warehouse-add]")){warehouseModal(null,rows,multiple);return;}if(event.target.closest("[data-warehouse-edit]")){warehouseModal(selected,rows,multiple);return;}var op=event.target.closest("[data-operation]");if(op){if(op.getAttribute("data-operation")==="manual_receipt")receiptModal(selected,rows);else operationModal(op.getAttribute("data-operation"),selected,rows);return;}var view=event.target.closest("[data-view]");if(view){state.view=view.getAttribute("data-view");renderContent(selected);}};
      renderContent(selected);
      function warehouseModal(record,all,enabled){var body='<form id="warehouseForm" class="form-grid"><div class="field"><label>اسم المخزن *</label><input class="input" name="name" maxlength="120" required value="'+U.escape(record?record.name:"")+'"></div><p class="muted span-2">يُنشأ الرمز الداخلي تلقائيًا ولا تتغير بيانات المخزن الأخرى.</p><p class="field-error span-2" data-inventory-error hidden></p></form>';
        UI.modal({title:record?"تعديل اسم المخزن":"إضافة مخزن",body:body,actions:'<button class="button primary" data-save-warehouse>حفظ</button><button class="button" data-modal-close>إلغاء</button>',onOpen:function(modal){modal.querySelector("[data-save-warehouse]").onclick=function(){var form=modal.querySelector("#warehouseForm");if(!form.reportValidity())return;var data={name:form.elements.name.value.trim(),code:record?record.code:"",location:record?record.location:null,description:record?record.description:null,isDefault:record?record.isDefault:false,isActive:record?record.isActive:true},button=this;button.disabled=true;request("/api/inventory/warehouses"+(record?"/"+record.id:""),record?"PUT":"POST",data).then(function(){UI.closeModal();UI.toast("تم حفظ المخزن.","success");load();}).catch(function(reason){error(modal,reason);button.disabled=false;});};}});
      }
    }
    function renderContent(warehouse){var target=container.querySelector("#inventoryContent");if(!target)return;if(state.view==="history")historyV2(target,warehouse);else if(state.view==="count")count(target,warehouse);else balances(target,warehouse);}
    function balances(target,warehouse){
      target.innerHTML='<section class="card"><div class="card-body">جارٍ تحميل الأرصدة...</div></section>';
      Api.request("/api/inventory/balances?warehouseId="+warehouse.id).then(function(rows){
        if(!context.isActive()||state.view!=="balances")return;
        var valuation=inventoryValuation(rows),complete=valuation.complete;
        target.innerHTML='<div class="stats-grid"><article class="card stat-card"><span class="stat-label">قيمة تكلفة الشراء — الأقدم أولًا</span><strong class="stat-value">'+(valuation.costComplete?formatExactValue(valuation.cost):"تقييم غير مكتمل")+'</strong></article><article class="card stat-card"><span class="stat-label">قيمة البيع المتوقعة</span><strong class="stat-value">'+(valuation.saleComplete?formatExactValue(valuation.sales):"تقييم غير مكتمل")+'</strong></article><article class="card stat-card"><span class="stat-label">الربح الإجمالي المتوقع (تقديري)</span><strong class="stat-value">'+(complete?formatExactValue(valuation.sales-valuation.cost):"تقييم غير مكتمل")+'</strong></article></div><p class="muted">'+(complete?'تكلفة البضاعة المتبقية حسب الأقدم أولًا. البيع والربح تقديريان وليسا ربحًا محققًا.':'لا يمكن إظهار إجمالي مؤكد قبل توثيق تكلفة الرصيد القديم واستكمال أسعار البيع. لا تُحتسب القيم الناقصة بصفر.')+(rows.some(function(x){return x.hasReconciledCostBasis;})?' يشمل التقييم أساس تكلفة افتتاحيًا موثقًا للرصيد القديم؛ لا يمثل تاريخ شراء مستعادًا.':'')+'</p><section class="card"><div class="card-head"><h2>الأرصدة الفعلية</h2></div><div class="table-wrap"><table class="data-table"><thead><tr><th>المنتج</th><th>اللون / المقاس</th><th>التشغيلة</th><th>الصلاحية</th><th>الكمية</th><th>تكلفة الشراء</th><th>قيمة البيع</th></tr></thead><tbody>'+(rows.length?rows.map(function(x,i){var line=inventoryValuation([x]);return '<tr><td>'+U.escape(x.productName)+'</td><td>'+U.escape(x.variantName)+'</td><td>'+U.escape(x.lotNumber||"—")+'</td><td>'+U.escape(x.expirationDate||"—")+'</td><td class="ltr">'+formatQty(x.quantityBaseScaled)+'</td><td>'+(line.costComplete?formatExactValue(line.cost):'تكلفة غير مؤكدة'+(context.user.role==="administrator"&&x.unresolvedLayerId?' <button class="table-action" data-reconcile-cost="'+i+'">توثيق التكلفة</button>':''))+'</td><td>'+(line.saleComplete?formatExactValue(line.sales):"سعر البيع غير محدد")+'</td></tr>';}).join(""):'<tr><td colspan="7"><div class="empty-state">لا توجد بضاعة في هذا المخزن بعد.</div></td></tr>')+'</tbody></table></div></section>';
        target.querySelectorAll("[data-reconcile-cost]").forEach(function(button){button.onclick=function(){reconcileModal(rows[Number(this.getAttribute("data-reconcile-cost"))],warehouse);};});
      }).catch(function(reason){error(target,reason);});
    }
    function reconcileModal(balance,warehouse){
      var operationKey=key(),signature=null,busy=false;
      UI.modal({title:"توثيق تكلفة الرصيد القديم",body:'<form class="form-grid"><p class="muted span-2">لا توجد تفاصيل مؤكدة لتكاليف هذا الرصيد القديم. سجّل أساس تكلفة افتتاحيًا موثقًا؛ هذا لا يعيد إنشاء تاريخ شراء سابق. يجب أن يطابق مجموع التكلفة القيمة التاريخية المحفوظة. عند اختلافها يلزم فحص محاسبي مستقل دون تعديل التاريخ.</p><div class="field"><label>تكلفة الوحدة الأساسية الموثقة</label><input class="input ltr" data-basis-cost inputmode="decimal"></div><div class="field span-2"><label>مصدر إثبات التكلفة</label><textarea class="textarea" data-basis-evidence></textarea></div><p class="field-error span-2" data-inventory-error hidden></p></form>',actions:'<button class="button primary" data-save-basis>حفظ الأساس الموثق</button><button class="button" data-modal-close>إلغاء</button>',onOpen:function(modal){modal.querySelector("[data-save-basis]").onclick=function(){
        if(busy)return;var button=this,data={layerId:balance.unresolvedLayerId,expectedVersion:balance.version,unitCost:modal.querySelector("[data-basis-cost]").value.trim(),evidence:modal.querySelector("[data-basis-evidence]").value.trim()};
        if(!/^\d+(?:\.\d{1,2})?$/.test(data.unitCost)||!data.evidence){error(modal,"أدخل التكلفة بدقة لا تتجاوز خانتين ومصدر إثباتها.");return;}
        var next=json(data);if(next!==signature){signature=next;operationKey=key();}data.operationKey=operationKey;busy=true;button.disabled=true;
        request("/api/inventory/cost-basis/reconcile","POST",data).then(function(){UI.closeModal();UI.toast("تم حفظ أساس التكلفة الموثق.","success");load();}).catch(function(reason){busy=false;button.disabled=false;error(modal,reason);});
      };}});
    }
    function historyV2(target,warehouse){
      var page=state.historyPage||1,params=["warehouseId="+warehouse.id,"page="+page,"pageSize=25"];
      if(state.reference)params.push("reference="+encodeURIComponent(state.reference));
      if(state.historyType)params.push("type="+encodeURIComponent(state.historyType));
      if(state.historyFrom)params.push("from="+encodeURIComponent(state.historyFrom));
      if(state.historyTo)params.push("to="+encodeURIComponent(state.historyTo));
      if(state.historyProductId)params.push("productId="+state.historyProductId);
      target.innerHTML='<section class="card"><div class="card-body">جارٍ تحميل حركة المخزون...</div></section>';
      Api.request("/api/inventory/movements?"+params.join("&")).then(function(data){
        if(!context.isActive()||state.view!=="history")return;
        var rows=data.items||[];
        target.innerHTML='<section class="card"><div class="card-head"><h2>حركة المخزون</h2></div><div class="card-body inventory-history-filters">'+
          '<div class="field"><label>رقم الحركة</label><input class="input ltr" data-history-reference value="'+U.escape(state.reference||"")+'" placeholder="مثال: 123"></div>'+
          '<div class="field"><label>نوع الحركة</label><select class="select" data-history-type><option value="">الكل</option>'+["opening","manual_receipt","manual_issue","adjustment","count","transfer","reversal"].map(function(type){return '<option value="'+type+'"'+(state.historyType===type?' selected':'')+'>'+label(type)+'</option>';}).join("")+'</select></div>'+
          '<div class="field"><label>من تاريخ</label><input class="input ltr" type="date" data-history-from value="'+U.escape(state.historyFrom||"")+'"></div><div class="field"><label>إلى تاريخ</label><input class="input ltr" type="date" data-history-to value="'+U.escape(state.historyTo||"")+'"></div>'+
          '<div class="field"><label>بحث عن منتج</label><input class="input" data-history-search value="'+U.escape(state.historyProductName||"")+'" placeholder="اسم أو كود المنتج"><select class="select" data-history-product><option value="'+U.escape(state.historyProductId||"")+'">'+U.escape(state.historyProductName||"كل المنتجات")+'</option></select></div><button class="button" data-history-clear>مسح المرشحات</button></div>'+
          '<div class="table-wrap"><table class="data-table"><thead><tr><th>المرجع</th><th>التاريخ</th><th>النوع</th><th>المخزن</th><th>المنتج / اللون والمقاس</th><th>التشغيلة</th><th>الكمية</th><th>المخزن الآخر</th><th>ملاحظات</th><th>إجراء</th></tr></thead><tbody>'+
          (rows.length?rows.map(function(x){return '<tr><td>حركة رقم '+U.escape(x.documentId)+'</td><td>'+U.escape(new Date(x.createdUtc).toLocaleString("ar-EG"))+'</td><td>'+U.escape(label(x.documentType))+'</td><td>'+U.escape(x.warehouseName||"—")+'</td><td>'+U.escape(x.productName+' / '+x.variantName)+'</td><td>'+U.escape(x.lotNumber||"—")+'</td><td class="ltr">'+(x.quantityBaseScaled>0?'+':'')+formatQty(x.quantityBaseScaled)+'</td><td>'+U.escape(x.documentType==="transfer"?(x.quantityBaseScaled<0?"إلى ":"من ")+(x.relatedWarehouseName||"—"):"—")+'</td><td>'+U.escape(x.notes||"—")+'</td><td>'+(context.user.role==="administrator"&&x.quantityBaseScaled>0&&["opening","manual_receipt","adjustment"].indexOf(x.documentType)>=0?'<button class="table-action" data-reverse="'+x.documentId+'">عكس</button>':'')+'</td></tr>';}).join(""):'<tr><td colspan="10"><div class="empty-state">لا توجد حركات مطابقة.</div></td></tr>')+'</tbody></table></div><div class="pagination"><span>'+U.escape(data.totalCount)+' حركة</span><div class="pagination-buttons"><button class="button" data-history-prev'+(page<=1?' disabled':'')+'>السابق</button><button class="button" data-history-next'+(page*25>=data.totalCount?' disabled':'')+'>التالي</button></div></div><p class="field-error" data-inventory-error hidden></p></section>';
        function change(field,value){state[field]=value;state.historyPage=1;historyV2(target,warehouse);}
        target.querySelector("[data-history-reference]").onchange=function(){change("reference",this.value.trim());};
        target.querySelector("[data-history-type]").onchange=function(){change("historyType",this.value);};
        target.querySelector("[data-history-from]").onchange=function(){change("historyFrom",this.value);};
        target.querySelector("[data-history-to]").onchange=function(){change("historyTo",this.value);};
        target.querySelector("[data-history-clear]").onclick=function(){state.reference="";state.historyType="";state.historyFrom="";state.historyTo="";state.historyProductId="";state.historyProductName="";state.historyPage=1;historyV2(target,warehouse);};
        target.querySelector("[data-history-prev]").onclick=function(){state.historyPage=page-1;historyV2(target,warehouse);};
        target.querySelector("[data-history-next]").onclick=function(){state.historyPage=page+1;historyV2(target,warehouse);};
        var search=target.querySelector("[data-history-search]"),select=target.querySelector("[data-history-product]");
        search.oninput=U.debounce(function(){var q=search.value.trim();if(!q){change("historyProductId","");state.historyProductName="";return;}Api.request("/api/inventory/products?search="+encodeURIComponent(q)+"&pageSize=25").then(function(result){if(!context.isActive()||!select.isConnected)return;select.innerHTML='<option value="">اختر المنتج</option>'+result.items.map(function(p){return '<option value="'+p.id+'">'+U.escape(p.name)+'</option>';}).join("");}).catch(function(reason){error(target,reason);});},220);
        select.onchange=function(){state.historyProductId=this.value;state.historyProductName=this.options[this.selectedIndex].textContent;state.historyPage=1;historyV2(target,warehouse);};
        target.querySelectorAll("[data-reverse]").forEach(function(button){var operationKey=key();button.onclick=function(){var id=this.getAttribute("data-reverse");UI.confirm({title:"عكس حركة المخزون",message:"سيُسجل مستند معاكس جديد إذا لم يتغير رصيد البضاعة منذ الحركة الأصلية.",confirmText:"عكس الحركة",danger:true}).then(function(ok){if(!ok)return;button.disabled=true;request("/api/inventory/documents/"+id+"/reverse","POST",{operationKey:operationKey,reason:"تصحيح حركة منشورة"}).then(function(saved){UI.toast("تم عكس الحركة وتحديث الرصيد.","success");historyV2(target,warehouse);}).catch(function(reason){error(target,reason);button.disabled=false;});});};});
      }).catch(function(reason){if(context.isActive())target.innerHTML='<div class="empty-state"><strong>تعذر تحميل الحركة</strong><span>'+U.escape(reason.message)+'</span><button class="button" data-retry-history>إعادة المحاولة</button></div>';var retry=target.querySelector("[data-retry-history]");if(retry)retry.onclick=function(){historyV2(target,warehouse);};});
    }
    function count(target,warehouse){
      target.innerHTML='<section class="card"><div class="card-head"><h2>جرد المخزون</h2></div><div class="card-body"><p>أنشئ مسودة جرد من الأرصدة الحالية، ثم أدخل الكميات الفعلية. يرفض النظام الترحيل إذا تغير الرصيد بعد إنشاء المسودة.</p><div class="field"><label>جرد صنف محدد (اختياري)</label><input class="input" data-count-search placeholder="ابحث عن منتج"><select class="select" data-count-product><option value="">كل المخزن</option></select><select class="select" data-count-variant><option value="">كل المخزن</option></select></div><button class="button primary" data-start-count>بدء الجرد</button><p class="field-error" data-inventory-error hidden></p><div data-count-valuation></div><div id="countDraft"></div><div id="countHistory"></div></div></section>';
      var search=target.querySelector("[data-count-search]"),products=target.querySelector("[data-count-product]"),variants=target.querySelector("[data-count-variant]");
      search.oninput=U.debounce(function(){var q=search.value.trim();if(!q)return;Api.request("/api/inventory/products?search="+encodeURIComponent(q)+"&pageSize=25").then(function(result){if(!context.isActive()||!products.isConnected)return;products.innerHTML='<option value="">كل المخزن</option>'+result.items.filter(function(p){return p.productType==="stock";}).map(function(p){return '<option value="'+p.id+'">'+U.escape(p.name)+'</option>';}).join("");}).catch(function(reason){error(target,reason);});},220);
      products.onchange=function(){if(!this.value){variants.innerHTML='<option value="">كل المخزن</option>';return;}Api.request("/api/inventory/products/"+this.value).then(function(product){if(!context.isActive()||!variants.isConnected)return;variants.innerHTML='<option value="">اختر المتغير</option>'+(product.variants||[]).filter(function(v){return v.isActive&&!v.isArchived;}).map(function(v){return '<option value="'+v.id+'">'+U.escape(v.name)+'</option>';}).join("");}).catch(function(reason){error(target,reason);});};
      target.querySelector("[data-start-count]").onclick=function(){var button=this,variantId=Number(variants.value);if(products.value&&!variantId){error(target,"اختر المتغير قبل بدء الجرد المحدد.");return;}button.disabled=true;request("/api/inventory/counts","POST",{warehouseId:warehouse.id,variantIds:variantId?[variantId]:null}).then(function(draft){showCount(draft);loadCountHistory();}).catch(function(reason){error(target,reason);button.disabled=false;});};
      Api.request("/api/inventory/balances?warehouseId="+warehouse.id).then(function(rows){if(!context.isActive()||state.view!=="count")return;var host=target.querySelector("[data-count-valuation]"),value=inventoryValuation(rows);if(host)host.textContent=value.complete?"تقييم الرصيد الحالي — تكلفة الشراء: "+formatExactValue(value.cost)+" · البيع المتوقع: "+formatExactValue(value.sales)+" · الربح الإجمالي المتوقع (تقديري): "+formatExactValue(value.sales-value.cost):"تقييم غير مكتمل — يلزم توثيق التكلفة وأسعار البيع. لا يُحتسب الناقص بصفر.";}).catch(function(reason){error(target,reason);});
      loadCountHistory();
      function loadCountHistory(){Api.request("/api/inventory/counts?warehouseId="+warehouse.id).then(function(rows){if(!context.isActive()||state.view!=="count")return;var host=target.querySelector("#countHistory");if(!host)return;host.innerHTML='<h3>جلسات الجرد</h3>'+(rows.length?rows.map(function(x){return '<div class="list-item"><span>جرد رقم '+x.id+' · '+countStatus(x.status)+' · '+U.escape(new Date(x.createdUtc).toLocaleString("ar-EG"))+'</span>'+(x.status==="draft"?'<span><button class="button" data-open-count="'+x.id+'">فتح</button><button class="button" data-cancel-count="'+x.id+'">إلغاء</button></span>':'')+'</div>';}).join(""):'<p class="muted">لا توجد جلسات جرد سابقة.</p>');host.querySelectorAll("[data-open-count]").forEach(function(button){button.onclick=function(){Api.request("/api/inventory/counts/"+this.getAttribute("data-open-count")).then(showCount).catch(function(reason){error(target,reason);});};});host.querySelectorAll("[data-cancel-count]").forEach(function(button){button.onclick=function(){var id=this.getAttribute("data-cancel-count");UI.confirm({title:"إلغاء مسودة الجرد",message:"ستبقى المسودة في السجل دون ترحيل فروق.",confirmText:"إلغاء الجرد",danger:true}).then(function(ok){if(!ok)return;request("/api/inventory/counts/"+id+"/cancel","POST",{}).then(function(){UI.toast("أُلغي الجرد.","success");count(target,warehouse);}).catch(function(reason){error(target,reason);});});};});}).catch(function(reason){error(target,reason);});}
      function showCount(draft){var host=target.querySelector("#countDraft");if(!draft.lines.length){host.innerHTML='<div class="empty-state">لا توجد أرصدة مسجلة للجرد. استخدم رصيدًا افتتاحيًا أو إضافة بضاعة أولًا.</div>';return;}host.innerHTML='<div class="table-wrap"><table class="data-table"><thead><tr><th>المنتج / اللون والمقاس</th><th>المسجل</th><th>المعدود</th><th>الفرق</th><th>تكلفة الزيادة</th><th>أرقام الوحدات عند الحاجة</th></tr></thead><tbody>'+draft.lines.map(function(x,i){return '<tr data-count-row="'+i+'"><td>'+U.escape(x.productName+' / '+x.variantName)+(x.lotNumber?'<small>'+U.escape(x.lotNumber+' · '+(x.expirationDate||''))+'</small>':'')+'</td><td>'+formatQty(x.recordedQuantityScaled)+'</td><td><input class="input ltr" data-counted value="'+(x.recordedQuantityScaled/1000000)+'" inputmode="decimal"></td><td data-difference>0</td><td><input class="input ltr" data-cost hidden placeholder="سعر شراء الوحدة الأساسية"></td><td><textarea class="textarea ltr" data-identifiers hidden placeholder="سيريال|IMEI1|IMEI2 لكل سطر"></textarea></td></tr>';}).join("")+'</tbody></table></div><button class="button primary" data-post-count>حفظ الجرد</button><p class="field-error" data-inventory-error hidden></p>';host.querySelectorAll("[data-counted]").forEach(function(input){input.oninput=function(){var row=this.closest("[data-count-row]"),index=Number(row.getAttribute("data-count-row")),value=Number(this.value);var difference=value-draft.lines[index].recordedQuantityScaled/1000000;row.querySelector("[data-difference]").textContent=isFinite(value)?String(difference):"—";row.querySelector("[data-cost]").hidden=!(difference>0);row.querySelector("[data-identifiers]").hidden=!draft.lines[index].serialTracked||difference===0;};});var operationKey=key(),countSignature=null,countSubmitting=false;host.querySelector("[data-post-count]").onclick=function(){if(countSubmitting)return;var button=this,lines;try{lines=draft.lines.map(function(x,i){var row=host.querySelector('[data-count-row="'+i+'"]'),counted=row.querySelector("[data-counted]").value.trim();if(!/^\d+(?:\.\d{1,6})?$/.test(counted))throw new Error("أدخل كمية معدودة صحيحة.");return {variantId:x.variantId,batchId:x.batchId,recordedQuantityScaled:x.recordedQuantityScaled,countedQuantity:counted,balanceVersion:x.balanceVersion,unitCost:Number(counted)>x.recordedQuantityScaled/1000000?row.querySelector("[data-cost]").value.trim()||null:null,physicalUnits:Number(counted)!==x.recordedQuantityScaled/1000000&&x.serialTracked?parseUnits(row.querySelector("[data-identifiers]").value):null};});}catch(reason){error(host,reason);return;}var nextSignature=json(lines);if(nextSignature!==countSignature){countSignature=nextSignature;operationKey=key();}var data={operationKey:operationKey,lines:lines};countSubmitting=true;button.disabled=true;request("/api/inventory/counts/"+draft.id+"/post","POST",data).then(function(saved){UI.toast("تم حفظ الجرد وتحديث الأرصدة.","success");state.view="balances";load();}).catch(function(reason){error(host,reason);countSubmitting=false;button.disabled=false;});};}
    }
    function parseUnits(text){return String(text||"").split(/\r?\n/).map(function(row){var parts=row.trim().split("|");return {serial:(parts.shift()||"").trim(),imeis:parts.filter(Boolean).map(function(x){return x.trim();})};}).filter(function(x){return x.serial;});}
    function operationModal(type,warehouse,warehouses){receiptModal(warehouse,warehouses,type);}
    function receiptModal(warehouse,warehouses,type){
      type=type||"manual_receipt";var receipt=type==="manual_receipt",adjustment=type==="adjustment",transfer=type==="transfer",submitting=false;
      var attemptKey=key(),signature=null,active=warehouses.filter(function(w){return w.isActive;}),lineStates=[];
      var hasWarehouse=!!warehouse;warehouse=warehouse||active.filter(function(w){return w.isDefault;})[0]||active[0];
      if(!warehouse){UI.toast("أضف مخزنًا نشطًا أولًا.","danger");return;}
      var body='<form id="receiptForm" class="inventory-operation"><div class="form-grid">'+(!hasWarehouse&&active.length>1?'<div class="field span-2"><label>المخزن</label><select class="select" name="warehouse">'+warehouseOptions(active,warehouse.id)+'</select></div>':'<p class="muted span-2">المخزن: '+U.escape(warehouse.name)+'</p>')+(transfer?'<div class="field span-2"><label>إلى مخزن</label><select class="select" name="destination">'+warehouseOptions(active.filter(function(w){return w.id!==warehouse.id;}),null)+'</select></div>':'')+(!receipt?'<div class="field span-2"><label>ملاحظات (اختياري)</label><input class="input" name="notes"></div>':'')+'</div><div data-line-list></div><button class="button" type="button" data-add-line>+ إضافة صنف آخر</button><p class="field-error" data-inventory-error hidden></p></form>';
      UI.modal({title:label(type)+' — '+warehouse.name,wide:false,body:body,beforeClose:function(){return !submitting;},focusSelector:"[data-search]",actions:'<button class="button primary" data-save-receipt>حفظ</button><button class="button" data-modal-close>إلغاء</button>',onOpen:function(modal){
        var form=modal.querySelector("#receiptForm"),list=form.querySelector("[data-line-list]");
        modal.querySelector("[data-add-line]").onclick=function(){addLine();};
        modal.querySelector("[data-save-receipt]").onclick=function(){save(this);};
        form.onsubmit=function(event){event.preventDefault();save(modal.querySelector("[data-save-receipt]"));};
        addLine();
        function addLine(){
          var row=document.createElement("section");row.className="card inventory-line";row.setAttribute("data-stock-line","");
          var state={row:row,product:null,caps:null,variants:[],units:[]};lineStates.push(state);
          row.innerHTML='<div class="card-body form-grid"><div class="field span-2"><label>المنتج (بالاسم أو الباركود)</label><input class="input" data-search placeholder="اكتب اسم المنتج أو امسح الباركود"><select class="select" data-product><option value="">اختر المنتج</option></select></div><div class="field" data-variant-field hidden><label>اللون / المقاس</label><select class="select" data-variant></select></div><div class="field" data-unit-field hidden><label>الوحدة</label><select class="select" data-unit></select></div><div class="field"><label>الكمية</label><input class="input ltr" data-quantity inputmode="decimal" required></div>'+(adjustment?'<div class="field"><label>تغيير الكمية</label><select class="select" data-direction><option value="plus">زيادة</option><option value="minus">نقص</option></select></div>':'')+(receipt||adjustment?'<div class="field" data-cost-field><label>سعر الشراء الجديد</label><input class="input ltr" data-cost inputmode="decimal" required></div>':'')+(receipt?'<div class="field"><label>سعر البيع الجديد</label><input class="input ltr" data-sale inputmode="decimal" required></div>':'')+'<div class="field" data-expiry-field hidden><label>تاريخ الصلاحية</label><input class="input ltr" type="date" data-expiry></div><div class="field" data-lot-field hidden><label>رقم التشغيلة</label><input class="input ltr" data-lot></div><div class="field span-2" data-serial-field hidden><label>الأرقام التسلسلية لكل وحدة</label><textarea class="textarea ltr" data-serials placeholder="سيريال|IMEI1|IMEI2 — سطر لكل وحدة"></textarea><small class="muted"></small></div><div class="span-2" data-cost-note></div><p class="field-error span-2" data-line-error hidden></p><button class="button" type="button" data-remove-line>حذف الصنف</button></div>';
          list.appendChild(row);
          row.querySelector("[data-remove-line]").onclick=function(){lineStates=lineStates.filter(function(item){return item.row!==row;});row.remove();};
          var search=row.querySelector("[data-search]"),select=row.querySelector("[data-product]");
          search.oninput=U.debounce(function(){var q=search.value.trim();if(!q)return;Api.request("/api/inventory/products?search="+encodeURIComponent(q)+"&pageSize=30").then(function(page){if(!modal.isConnected)return;select.innerHTML='<option value="">اختر المنتج</option>'+page.items.filter(function(p){return p.productType==="stock"&&p.isActive&&!p.isArchived;}).map(function(p){return '<option value="'+p.id+'">'+U.escape(p.name+(p.primarySku?' · '+p.primarySku:''))+'</option>';}).join("");}).catch(function(reason){error(modal,reason);});},220);
          select.onchange=function(){
            state.product=null;state.balances=[];
            if(!this.value)return;
            var selectedProductId=this.value;
            Api.request("/api/inventory/products/"+selectedProductId).then(function(detail){
              if(!modal.isConnected||select.value!==selectedProductId)return;
              var map={};(detail.capabilities||[]).forEach(function(item){map[item.key]=!!item.isAssigned;});
              state.product=detail;state.caps={batch:!!map["batch-tracking"],expiry:!!map["expiry-dates"],serial:!!map["serial-numbers"],imei:!!map["imei-tracking"]};
              state.variants=(detail.variants||[]).filter(function(v){return v.isActive&&!v.isArchived;});
              state.units=(detail.units||[]).filter(function(u){return u.isActive;});
              var variantField=row.querySelector("[data-variant-field]"),variantSelect=row.querySelector("[data-variant]"),unitField=row.querySelector("[data-unit-field]"),unitSelect=row.querySelector("[data-unit]");
              variantField.hidden=state.variants.length<=1;
              variantSelect.innerHTML=state.variants.map(function(v){return '<option value="'+v.id+'">'+U.escape(v.name+(v.sku?' · '+v.sku:''))+'</option>';}).join("");
              var base=state.units.filter(function(u){return u.isBase;})[0]||state.units[0];
              unitSelect.innerHTML=state.units.map(function(u){return '<option value="'+u.id+'"'+(u===base?' selected':'')+'>'+U.escape(u.name)+'</option>';}).join("");
              unitField.hidden=state.units.length<=1;
              row.querySelector("[data-expiry-field]").hidden=!state.caps.expiry||!(receipt||adjustment);
              row.querySelector("[data-lot-field]").hidden=!state.caps.batch;
              var serialField=row.querySelector("[data-serial-field]");
              serialField.hidden=!state.caps.serial;
              serialField.querySelector("small").textContent=state.caps.imei?"سجل سيريال كل وحدة وكل أرقام IMEI في السطر نفسه بصيغة: سيريال|IMEI1|IMEI2.":"سجل سيريال كل وحدة في سطر مستقل.";
              function updateSelection(){
                var selectedVariant=state.variants.filter(function(v){return String(v.id)===variantSelect.value;})[0]||{},selectedUnit=state.units.filter(function(u){return String(u.id)===unitSelect.value;})[0]||{};
                var plus=receipt||adjustment&&row.querySelector("[data-direction]").value==="plus";
                var costField=row.querySelector("[data-cost-field]");if(costField)costField.hidden=!plus;
                row.querySelector("[data-expiry-field]").hidden=!state.caps.expiry||!plus;
                var sale=selectedVariant.effectiveSalePrice,ratio=Number(selectedUnit.numerator||1)/Number(selectedUnit.denominator||1);
                // Retail guidance follows the selected variant/unit. Acquisition cost stays blank.
                if(receipt)row.querySelector("[data-sale]").placeholder=sale===null||sale===undefined?"أدخل سعر البيع":("السعر الحالي: "+(Number(sale)*ratio).toFixed(2));
                var matching=(state.balances||[]).filter(function(b){return String(b.variantId)===variantSelect.value&&(!state.caps.batch||b.lotNumber===row.querySelector("[data-lot]").value.trim());});
                var current=matching.reduce(function(sum,b){return sum+Number(b.quantityBaseScaled);},0),entered=Number(row.querySelector("[data-quantity]").value||0)*ratio*1000000;
                row.querySelector("[data-cost-note]").textContent="الرصيد الحالي: "+formatQty(current)+(adjustment?" — الرصيد بعد التسوية: "+formatQty(current+(plus?entered:-entered)):"");
              }
              variantSelect.onchange=updateSelection;unitSelect.onchange=updateSelection;
              row.querySelector("[data-quantity]").oninput=updateSelection;row.querySelector("[data-lot]").oninput=updateSelection;
              if(adjustment)row.querySelector("[data-direction]").onchange=updateSelection;
              Api.request("/api/inventory/balances?warehouseId="+warehouse.id+"&productId="+detail.id).then(function(balances){if(state.product!==detail)return;state.balances=balances;updateSelection();}).catch(function(reason){error(modal,reason);});
              updateSelection();
            }).catch(function(reason){error(modal,reason);});
          };
          search.focus();
        }
        function fail(row,message){var target=row.querySelector("[data-line-error]");target.hidden=false;target.textContent=message;}
        function save(button){
          if(submitting)return;
          modal.querySelector("[data-inventory-error]").hidden=true;
          var lines=[],valid=true;
          lineStates.forEach(function(state){
            state.row.querySelector("[data-line-error]").hidden=true;
            var row=state.row,caps=state.caps||{},productSelect=row.querySelector("[data-product]"),variantSelect=row.querySelector("[data-variant]"),unitSelect=row.querySelector("[data-unit]"),quantity=row.querySelector("[data-quantity]").value.trim(),expiry=row.querySelector("[data-expiry]").value,lot=row.querySelector("[data-lot]").value.trim(),serialText=row.querySelector("[data-serials]").value;
            if(!productSelect.value){fail(row,"اختر المنتج أولًا.");valid=false;return;}
            if(!state.product||String(state.product.id)!==productSelect.value){fail(row,"انتظر تحميل بيانات المنتج ثم أعد الحفظ.");valid=false;return;}
            if(!variantSelect.value){fail(row,"لا يوجد متغير نشط لهذا الصنف.");valid=false;return;}
            if(!unitSelect.value){fail(row,"لا توجد وحدة قياس نشطة لهذا الصنف.");valid=false;return;}
            if(!/^\d+(?:\.\d{1,6})?$/.test(quantity)||Number(quantity)<=0){fail(row,"أدخل كمية صحيحة أكبر من صفر.");valid=false;return;}
            var plus=receipt||adjustment&&row.querySelector("[data-direction]").value==="plus",costInput=row.querySelector("[data-cost]"),saleInput=row.querySelector("[data-sale]"),cost=costInput?costInput.value.trim():null,sale=saleInput?saleInput.value.trim():null;
            if(plus&&!/^\d+(?:\.\d{1,2})?$/.test(cost||"")){fail(row,"أدخل سعر شراء صحيحًا بدقة خانتين عشريتين كحد أقصى.");valid=false;return;}
            if(receipt&&!/^\d+(?:\.\d{1,2})?$/.test(sale||"")){fail(row,"أدخل سعر بيع صحيحًا بدقة خانتين عشريتين كحد أقصى.");valid=false;return;}
            var selectedUnit=state.units.filter(function(u){return String(u.id)===unitSelect.value;})[0]||{},baseCount=Number(quantity)*Number(selectedUnit.numerator||1)/Number(selectedUnit.denominator||1);
            var units=[],count=baseCount;
            if(caps.serial){
              if(!Number.isInteger(baseCount)){fail(row,"الصنف المتسلسل يحتاج كمية صحيحة من الوحدات.");valid=false;return;}
              units=String(serialText||"").split(/\r?\n/).map(function(line){return line.trim();}).filter(Boolean).map(function(line){var parts=line.split("|");return {serial:(parts.shift()||"").trim(),imeis:parts.map(function(x){return x.trim();}).filter(Boolean)};}).filter(function(unit){return unit.serial;});
              if(units.length!==count){fail(row,"عدد الأرقام التسلسلية ("+units.length+") لا يطابق الكمية ("+count+").");valid=false;return;}
              if(caps.imei&&units.some(function(unit){return !unit.imeis.length;})){fail(row,"أضف رقم IMEI واحدًا على الأقل في السطر نفسه لكل جهاز.");valid=false;return;}
              if(!caps.imei&&units.some(function(unit){return unit.imeis.length;})){fail(row,"هذا الصنف لا يتتبع أرقام IMEI؛ اكتب السيريال فقط.");valid=false;return;}
            }
            if(caps.batch&&!lot){fail(row,"أدخل رقم التشغيلة.");valid=false;return;}
            if(caps.expiry&&plus&&!expiry){fail(row,"أدخل تاريخ الصلاحية.");valid=false;return;}
            lines.push({variantId:Number(variantSelect.value),productUnitId:Number(unitSelect.value),quantity:adjustment&&!plus?"-"+quantity:quantity,unitCost:plus?cost:null,salePrice:receipt?sale:null,lotNumber:lot||null,expirationDate:plus?expiry||null:null,physicalUnits:caps.serial?units:null});
          });
          if(!lines.length){error(modal,"أضف صنفًا واحدًا على الأقل.");return;}
          if(!valid){error(modal,"أكمل البيانات المطلوبة قبل الحفظ.");return;}
          var warehouseInput=form.elements.warehouse,targetWarehouseId=warehouseInput&&warehouseInput.value?Number(warehouseInput.value):warehouse.id;
          var data={type:type,warehouseId:targetWarehouseId,destinationWarehouseId:transfer?Number(form.elements.destination.value):null,notes:!receipt?form.elements.notes.value.trim()||null:null,lines:lines};
          if(transfer&&(!data.destinationWarehouseId||data.destinationWarehouseId===targetWarehouseId)){error(modal,"اختر مخزنًا آخر للنقل.");return;}
          var nextSignature=json(data);
          if(nextSignature!==signature){signature=nextSignature;attemptKey=key();}
          data.operationKey=attemptKey;
          submitting=true;button.disabled=true;button.textContent="جارٍ الحفظ…";
          form.querySelectorAll("input,select,textarea,button").forEach(function(input){input.disabled=true;});
          request("/api/inventory/post","POST",data).then(function(){
            UI.closeModal();
            var target=active.filter(function(w){return w.id===targetWarehouseId;})[0]||warehouse;
            UI.toast("تم حفظ "+label(type)+" وتحديث رصيد "+target.name+".","success");
            load();
          }).catch(function(reason){error(modal,reason);submitting=false;form.querySelectorAll("input,select,textarea,button").forEach(function(input){input.disabled=false;});button.disabled=false;button.textContent="حفظ";});
        }
      }});
    }
  }
  R["warehouse-directory"]=directory;
}(window));



