(function (global) {
  "use strict";
  var R=global.AlKhayalRenderers, Api=global.AlKhayalApi, UI=global.AlKhayalUI, U=global.AlKhayalUtils;
  function esc(x){return U.escape(x==null?'':String(x));}
  function render(screen,host,context){
    var page=1, token=0, H=global.AlKhayalReportUI;
    host.innerHTML=UI.pageHeader(screen)+'<section class="card report-view"><div class="card-body"><p>الفواتير حسب وقت النشر المحلي، والمرتجعات والإلغاءات حسب وقت تسجيلها. القيم شاملة الضريبة. تقسيم الدفع هو قيمة الفواتير حسب تسويتها الأصلية؛ المدفوعات الفعلية في تقرير وسائل الدفع.</p><form class="form-grid report-filters">'+H.periodFields()+'<label class="field"><span>رقم الفاتورة</span><input class="input" type="search" name="search" maxlength="120"></label><label class="field"><span>طريقة الدفع</span><select class="select" name="method"><option value="">الكل</option><option value="cash">كاش</option><option value="wallet">محفظة</option><option value="card">فيزا</option><option value="credit">آجل</option><option value="unclassified">غير مصنف</option></select></label><button class="button primary">عرض</button><button type="button" class="button" data-reset>إعادة ضبط</button></form><div data-results aria-live="polite"></div></div></section>';
    var form=host.querySelector('form'), target=host.querySelector('[data-results]');
    function preset(){H.preset(form);}
    form.elements.period.value='month'; form.elements.period.onchange=function(){preset();}; ['from','to'].forEach(function(n){form.elements[n].onchange=function(){form.elements.period.value='custom';};});
    form.onsubmit=function(e){e.preventDefault();page=1;load();};
    host.querySelector('[data-reset]').onclick=function(){form.reset();form.elements.period.value='month';preset();page=1;load();};
    async function load(){
      var current=++token, params=new URLSearchParams(); if(!H.valid(form)){target.innerHTML='<p class="field-error" role="alert">تحقق من تاريخ البداية والنهاية.</p>';return;}['from','to','search','method'].forEach(function(n){var v=form.elements[n].value;if(v)params.set(n,v);});params.set('page',page);
      target.innerHTML='<p role="status">جارٍ تحميل التقرير...</p>';
      try {var d=await Api.request('/api/reports/sales?'+params.toString());if(current!==token||(context.isActive&&!context.isActive()))return;
        var summary=[['الفواتير المنشورة',d.count],['إجمالي المنشور شامل الضريبة',d.total],['مرتجعات الفترة شامل الضريبة',d.returns],['إلغاءات الفترة',d.cancellations],['صافي أحداث الفترة شامل الضريبة',d.netEvents],['قيمة فواتير كاش فورية',d.cash],['قيمة فواتير محفظة فورية',d.wallet],['قيمة فواتير فيزا فورية',d.card],['قيمة فواتير آجلة — ليست تحصيلًا',d.credit],['غير مصنف',d.unclassified]];
        target.innerHTML='<p>الفترة: '+esc(params.get('from'))+' — '+esc(params.get('to'))+'</p>'+H.cards(summary)+'<div class="table-wrap"><table class="data-table"><thead><tr><th>الفاتورة</th><th>تاريخ الفاتورة</th><th>وقت النشر</th><th>العميل</th><th>الكاشير</th><th>الدفع</th><th>الإجمالي شامل الضريبة</th><th>الحالة الحالية</th></tr></thead><tbody>'+d.items.map(function(x){return '<tr><td>'+H.link(context,'sales-management',x.id,x.invoiceNumber)+'</td><td>'+esc(x.invoiceDate)+'</td><td>'+esc(new Date(x.postedUtc).toLocaleString('ar-EG'))+'</td><td>'+esc(x.customerName||'نقدي')+'</td><td>'+esc(x.cashierName)+'</td><td>'+esc(({cash:'كاش',wallet:'محفظة',card:'فيزا',credit:'آجل',unclassified:'غير مصنف'})[x.method])+'</td><td>'+esc(x.total)+'</td><td>'+esc(x.status==='canceled'?'ملغاة':'منشورة')+'</td></tr>';}).join('')+'</tbody></table></div>'+(d.count?'':'<p class="empty-state">لا توجد فواتير في الفترة.</p>')+H.pager(page,d.count);
        target.querySelector('[data-prev]').onclick=function(){page--;load();};target.querySelector('[data-next]').onclick=function(){page++;load();};H.bindLinks(target,context);
      }catch(e){if(current===token&&(!context.isActive||context.isActive()))target.innerHTML='<p class="field-error" role="alert">'+esc(e.message)+'</p><button class="button" data-retry>إعادة المحاولة</button>';var retry=target.querySelector('[data-retry]');if(retry)retry.onclick=load;}
    }
    preset();load();
  }
  R['sales-report']=render;
})(window);
