(function (global) {
  "use strict";
  function esc(x) { return global.AlKhayalUtils.escape(x == null ? "" : String(x)); }
  function date(d) { return d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0"); }
  function preset(form) {
    var end = new Date(), start = new Date(end.getFullYear(), end.getMonth(), end.getDate());
    if (form.elements.period.value === "yesterday") { start.setDate(start.getDate() - 1); end = new Date(start); }
    if (form.elements.period.value === "week") start.setDate(start.getDate() - 6);
    if (form.elements.period.value === "current-week") start.setDate(start.getDate() - ((start.getDay() + 1) % 7));
    if (form.elements.period.value === "month") start.setDate(1);
    if (form.elements.period.value === "previous-month") { start = new Date(end.getFullYear(), end.getMonth() - 1, 1); end = new Date(end.getFullYear(), end.getMonth(), 0); }
    if (form.elements.period.value !== "custom") { form.elements.from.value = date(start); form.elements.to.value = date(end); }
  }
  function periodFields() {
    return '<label class="field"><span>الفترة</span><select name="period" class="select"><option value="month">الشهر الحالي</option><option value="today">اليوم</option><option value="yesterday">أمس</option><option value="week">آخر 7 أيام</option><option value="current-week">الأسبوع الحالي (من السبت)</option><option value="previous-month">الشهر السابق</option><option value="custom">فترة مخصصة</option></select></label><label class="field"><span>من</span><input class="input" type="date" name="from" required></label><label class="field"><span>إلى</span><input class="input" type="date" name="to" required></label>';
  }
  function valid(form) { return form.elements.from.value && form.elements.to.value && form.elements.from.value <= form.elements.to.value; }
  function cards(values) { return '<div class="stats-grid">' + values.map(function (x) { return '<article class="card stat-card"><span class="stat-label">' + esc(x[0]) + '</span><strong class="stat-value">' + esc(x[1]) + '</strong></article>'; }).join('') + '</div>'; }
  function pager(page, count) { return '<div class="sale-actions"><button type="button" class="button" data-prev ' + (page <= 1 ? 'disabled' : '') + '>السابق</button><span>صفحة ' + page + ' · ' + esc(count) + ' سجل</span><button type="button" class="button" data-next ' + (page * 20 >= count ? 'disabled' : '') + '>التالي</button></div>'; }
  function can(context, route) { return !context.canNavigate || context.canNavigate(route); }
  function link(context, route, id, label) { return can(context, route) ? '<button type="button" class="table-action" data-report-route="' + esc(route) + '" data-report-id="' + esc(id) + '">' + esc(label) + '</button>' : '<span title="فتح المستند يتطلب صلاحية شاشته">' + esc(label) + '</span>'; }
  function bindLinks(host, context) {
    host.querySelectorAll('[data-report-route]').forEach(function (button) { button.onclick = function () {
      var route = button.getAttribute('data-report-route'), id = Number(button.getAttribute('data-report-id'));
      context.navigate(route, route === 'sales-management' ? { saleInvoiceId: id } : route === 'purchase-management' ? { invoiceId: id } : { instrumentId: id });
    }; });
  }
  global.AlKhayalReportUI = { esc: esc, date: date, preset: preset, periodFields: periodFields, valid: valid, cards: cards, pager: pager, link: link, bindLinks: bindLinks };
})(window);
