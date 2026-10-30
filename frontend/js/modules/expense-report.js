(function (global) {
  "use strict";
  var H = global.AlKhayalReportUI, esc = H.esc;
  global.AlKhayalRenderers['expense-report'] = function (screen, host, context) {
    var page = 1, token = 0;
    host.innerHTML = global.AlKhayalUI.pageHeader(screen) + '<section class="card report-view"><div class="card-body"><p>السندات المدفوعة المحفوظة حسب وقت التسجيل؛ تاريخ المصروف المعروض قد يختلف. التصنيفات يحددها المستخدم ولا تثبت وحدها أن السند مصروف تشغيلي. لا يشمل التقرير المصروفات غير المدفوعة أو الإلغاءات.</p><form class="form-grid report-filters">' + H.periodFields() + '<label class="field"><span>رقم نوع المصروف (اختياري)</span><input class="input" type="number" min="1" name="categoryId"></label><label class="field"><span>بحث في النوع أو المستفيد أو الملاحظات</span><input class="input" type="search" maxlength="120" name="search"></label><button class="button primary" type="submit">عرض</button><button class="button" type="button" data-reset>إعادة ضبط</button></form><div data-results aria-live="polite"></div></div></section>';
    var form = host.querySelector('form'), target = host.querySelector('[data-results]');
    form.elements.period.onchange = function () { H.preset(form); };
    ['from','to'].forEach(function (name) { form.elements[name].onchange = function () { form.elements.period.value = 'custom'; }; });
    form.onsubmit = function (e) { e.preventDefault(); page = 1; load(); };
    host.querySelector('[data-reset]').onclick = function () { form.reset(); H.preset(form); page = 1; load(); };
    function table(headers, rows) { return '<div class="table-wrap"><table class="data-table"><thead><tr>' + headers.map(function (x) { return '<th>' + esc(x) + '</th>'; }).join('') + '</tr></thead><tbody>' + rows.map(function (row) { return '<tr>' + row.map(function (x) { return '<td>' + esc(x) + '</td>'; }).join('') + '</tr>'; }).join('') + '</tbody></table></div>'; }
    async function load() {
      var current = ++token;
      if (!H.valid(form)) { target.innerHTML = '<p class="field-error" role="alert">تحقق من تاريخ البداية والنهاية.</p>'; return; }
      var params = new URLSearchParams();
      ['from','to','categoryId','search'].forEach(function (name) { if (form.elements[name].value) params.set(name, form.elements[name].value); });
      params.set('page', page); target.innerHTML = '<p role="status">جارٍ تحميل التقرير...</p>';
      try {
        var d = await global.AlKhayalApi.request('/api/reports/expenses?' + params.toString());
        if (current !== token || (context.isActive && !context.isActive())) return;
        target.innerHTML = H.cards([['عدد السندات',d.count],['إجمالي السندات المسجلة',d.total]]) + '<h3>حسب النوع — أول 100 نوع</h3>' + table(['رقم النوع','النوع','العدد','الإجمالي'],d.categories.map(function (x) { return [x.categoryId,x.category,x.count,x.total]; })) + '<h3>السندات</h3>' + table(['رقم السند','تاريخ المصروف','وقت التسجيل','النوع','المستفيد','الملاحظات','المستخدم','المبلغ'],d.items.map(function (x) { return [x.id,x.expenseDate,new Date(x.postedUtc).toLocaleString('ar-EG'),x.category,x.payee,x.notes,x.userName,x.amount]; })) + (d.count ? '' : '<p class="empty-state">لا توجد سجلات مطابقة.</p>') + H.pager(page,d.count);
        target.querySelector('[data-prev]').onclick = function () { page--; load(); }; target.querySelector('[data-next]').onclick = function () { page++; load(); };
      } catch (e) {
        if (current !== token || (context.isActive && !context.isActive())) return;
        target.innerHTML = '<p class="field-error" role="alert">' + esc(e.message) + '</p><button class="button" data-retry>إعادة المحاولة</button>'; target.querySelector('[data-retry]').onclick = load;
      }
    }
    H.preset(form); load();
  };
})(window);
