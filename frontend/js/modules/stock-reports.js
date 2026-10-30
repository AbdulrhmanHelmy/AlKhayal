(function (global) {
  "use strict";
  var H = global.AlKhayalReportUI, esc = H.esc;
  var types = {purchase:'استلام مشتريات',sale:'صرف مبيعات','sales-return':'مرتجع مبيعات','purchase-return':'مرتجع مشتريات',cancellation:'إلغاء مبيعات',opening:'رصيد افتتاحي',manual_receipt:'إضافة يدوية',manual_issue:'صرف يدوي',adjustment:'تسوية',count:'جرد',transfer:'تحويل مخزني',reversal:'عكس حركة',projection_rebuild:'إعادة بناء الرصيد'};
  function field(name, label, type) { return '<label class="field"><span>' + label + '</span><input class="input" name="' + name + '" type="' + (type || 'search') + '" ' + (type === 'number' ? 'min="1"' : 'maxlength="120"') + '></label>'; }
  function select(name, label, options) { return '<label class="field"><span>' + label + '</span><select class="select" name="' + name + '">' + options.map(function (x) { return '<option value="' + esc(x[0]) + '">' + esc(x[1]) + '</option>'; }).join('') + '</select></label>'; }
  global.AlKhayalRenderers['stock-reports'] = function (screen, host, context) {
    if (context.canNavigate && !context.canNavigate(screen.route)) { host.innerHTML = '<p class="field-error">لا تملك صلاحية التقرير.</p>'; return; }
    var report = screen.route.substring(7), current = report === 'low-stock' || report === 'inventory-valuation', low = report === 'low-stock', moves = report === 'inventory-movements', top = report === 'top-products', page = 1, token = 0;
    host.innerHTML = global.AlKhayalUI.pageHeader(screen) + '<section class="card report-view"><div class="card-body"><p>' + (current ? 'المخزون الحالي فقط؛ الكميات حسب الوحدة الأساسية لكل منتج، ولا تجمع بين الوحدات المختلفة. ' + (low ? 'لا توجد حدود دنيا مضبوطة؛ يعرض التوافر في المخازن ذات الرصيد المسجل سابقًا فقط.' : 'الهامش المتوقع ليس ربحًا محققًا. التكلفة الناقصة تظل غير معروفة.') : 'حسب وقت تسجيل البيع والمرتجع والإلغاء أو حركة المخزون. الأسماء والتصنيفات الحالية للبحث؛ القيم التاريخية من المستندات الأصلية.') + '</p><form class="form-grid report-filters">' + (current ? '' : H.periodFields()) + field('search','بحث بالمنتج أو المتغير أو SKU') + field('categoryId','رقم التصنيف الحالي (اختياري)','number') + ((current || moves) ? field('warehouseId','رقم المخزن (اختياري)','number') : '') + (current ? select('status','حالة المخزون',low ? [['zero','نفد المخزون'],['','الكل'],['available','متاح']] : [['','الكل'],['available','متاح'],['incomplete','تكلفة غير مكتملة']]) : '') + (moves ? select('type','نوع الحركة',[['','الكل']].concat(Object.keys(types).map(function (k) { return [k,types[k]]; }))) + field('reference','مرجع المستند') : '') + ((!current && !moves) ? select('sort','الترتيب',[['quantity','صافي الكمية'],['amount','صافي قيمة البيع المسجلة']]) : '') + '<button class="button primary" type="submit">عرض</button><button class="button" type="button" data-reset>إعادة ضبط</button></form><div data-results aria-live="polite"></div></div></section>';
    var form = host.querySelector('form'), target = host.querySelector('[data-results]');
    if (!current) {
      form.elements.period.onchange = function () { H.preset(form); };
      ['from','to'].forEach(function (name) { form.elements[name].onchange = function () { form.elements.period.value = 'custom'; }; });
      H.preset(form);
    }
    form.onsubmit = function (e) { e.preventDefault(); page = 1; load(); };
    host.querySelector('[data-reset]').onclick = function () { form.reset(); if (!current) H.preset(form); page = 1; load(); };
    function value(x) { return esc(x == null ? 'غير مكتمل' : x); }
    function table(headers, rows) { return '<div class="table-wrap"><table class="data-table"><thead><tr>' + headers.map(function (x) { return '<th>' + esc(x) + '</th>'; }).join('') + '</tr></thead><tbody>' + rows.map(function (r) { return '<tr>' + r.map(function (x) { return '<td>' + x + '</td>'; }).join('') + '</tr>'; }).join('') + '</tbody></table></div>'; }
    async function load() {
      var generation = ++token;
      if (!current && !H.valid(form)) { target.innerHTML = '<p class="field-error" role="alert">تحقق من تاريخ البداية والنهاية.</p>'; return; }
      var params = new URLSearchParams();
      ['from','to','search','categoryId','warehouseId','status','type','reference','sort'].forEach(function (name) { var input = form.elements[name]; if (input && input.value) params.set(name,input.value); });
      params.set('page',page); target.innerHTML = '<p role="status">جارٍ تحميل التقرير...</p>';
      try {
        var d = await global.AlKhayalApi.request('/api/reports/' + report + '?' + params.toString());
        if (generation !== token || (context.isActive && !context.isActive())) return;
        var s = d.summary, cards = [['عدد السجلات',d.count]], headers, rows;
        if (low) cards.push(['نفد المخزون',s.zeroCount]);
        else if (current) cards.push(['تكلفة FIFO المتبقية',s.cost],['قيمة البيع المحتملة',s.sellingValue],['الهامش الإجمالي المتوقع',s.profit]);
        else if (moves) cards.push(['صافي القيمة التاريخية للحركات',s.cost]);
        else if (!top) cards.push(['إيراد معترف به بدون الضريبة الأصلية',s.revenue],['تكلفة FIFO الفعلية',s.cost],['الربح الإجمالي المحقق',s.profit]);
        cards = cards.map(function (x) { return [x[0],x[1] == null ? 'غير مكتمل' : x[1]]; });
        if (moves) {
          headers = ['الوقت','المنتج','المتغير','المخزن','الوحدة الأساسية','الحركة','دخول','خروج','القيمة المسجلة','نوع المستند','المرجع'];
          rows = d.items.map(function (x) { return [esc(new Date(x.eventUtc).toLocaleString('ar-EG')),esc(x.product),esc(x.variant),esc(x.warehouse),esc(x.unit),esc(types[x.type] || x.type),value(x.quantityIn),value(x.quantityOut),value(x.cost),esc(types[x.documentType] || x.documentType),x.route ? H.link(context,x.route,x.documentId,x.reference) : esc(x.reference)]; });
        } else if (current) {
          headers = ['المنتج','المتغير','المخزن','الوحدة الأساسية','المتاح'].concat(low ? ['الحالة'] : ['سعر البيع الحالي','تكلفة FIFO','قيمة البيع المحتملة','الهامش المتوقع','التكلفة']);
          rows = d.items.map(function (x) { return [esc(x.product),esc(x.variant),esc(x.warehouse),esc(x.unit),value(x.quantity)].concat(low ? [x.status === 'zero' ? 'نفد المخزون' : 'متاح'] : [value(x.salePrice),value(x.cost),value(x.sellingValue),value(x.profit),x.complete ? 'مكتملة' : 'غير مكتملة']); });
        } else {
          headers = ['المنتج','SKU','المتغير','الوحدة الأساسية','المباع','المرتجع','الملغى','صافي الكمية'].concat(top ? ['صافي قيمة البيع المسجلة شامل الضريبة'] : ['الإيراد بدون الضريبة الأصلية','تكلفة FIFO','الربح الإجمالي','الهامش ٪']);
          rows = d.items.map(function (x) { return [esc(x.product),esc(x.sku || '—'),esc(x.variant),esc(x.unit),value(x.sold),value(x.returned),value(x.cancelled),value(x.quantity)].concat(top ? [value(x.recordedAmount)] : [value(x.revenue),value(x.cost),value(x.profit),x.margin == null ? '—' : value(x.margin)]); });
        }
        target.innerHTML = H.cards(cards) + '<p class="muted">' + esc(d.scope) + '</p>' + (!s.complete && !low ? '<p class="field-error" role="status">أدلة غير مكتملة: ' + esc(s.incompleteCount) + ' — القيم المجهولة لا تحسب بصفر.</p>' : '') + table(headers,rows) + (!rows.length ? '<p class="empty-state">لا توجد سجلات مطابقة.</p>' : '') + H.pager(page,d.count);
        target.querySelector('[data-prev]').onclick = function () { if (page > 1) { page--; load(); } };
        target.querySelector('[data-next]').onclick = function () { if (page * 20 < d.count) { page++; load(); } };
        H.bindLinks(target,context);
      } catch (e) {
        if (generation !== token || (context.isActive && !context.isActive())) return;
        target.innerHTML = '<p class="field-error" role="alert">' + esc(e.message) + '</p><button type="button" class="button" data-retry>إعادة المحاولة</button>';
        target.querySelector('[data-retry]').onclick = load;
      }
    }
    load();
  };
})(window);
