(function (global) {
  "use strict";
  var H = global.AlKhayalReportUI, esc = H.esc;
  function render(screen, host, context) {
    var kind = screen.route.slice(7), payments = kind === 'payment-methods', returns = kind === 'returns', page = 1, token = 0;
    var typeField = payments || returns ? '<label class="field"><span>النوع</span><select class="select" name="type"><option value="">الكل</option>' + (payments ? '<option value="wallet">محفظة</option><option value="card">فيزا</option>' : '<option value="sales">مرتجع بيع</option><option value="purchase">مرتجع شراء</option>') + '</select></label>' : '';
    var note = payments ? 'مدفوعات الفواتير الفورية حسب وقت تسجيل الدفع؛ قيمة الآجل حسب النشر، منفصلة عن التحصيلات اللاحقة. الأرقام ليست أرصدة محافظ أو بنوك أو درج نقدي. أسماء الوسائل الحالية؛ الهوية المحفوظة ثابتة. مرشح الوسيلة يطبّق على الملخص والجدول.' : returns ? 'المرتجعات حسب وقت تسجيلها الفعلي. القيم هي الإجمالي المحفوظ؛ لا يوجد توزيع مستقل لضريبة المرتجع الجزئي. مرجع مرتجع الشراء هو معرّفه المحفوظ.' : 'الفواتير حسب وقت النشر والمرتجعات حسب وقت نشر المرتجع. القيم إجمالي المستند المحفوظ؛ لا يتضمن نموذج الشراء الحالي ضريبة منفصلة. طريقة التسوية الأصلية لا تعني الرصيد المستحق الحالي.';
    host.innerHTML = global.AlKhayalUI.pageHeader(screen) + '<section class="card report-view"><div class="card-body"><p>' + note + '</p><form class="form-grid report-filters">' + H.periodFields() + '<label class="field"><span>' + (payments ? 'اسم / رقم الوسيلة' : 'مرجع المستند') + '</span><input class="input" type="search" name="search" maxlength="120"></label>' + typeField + (!payments ? '<label class="field"><span>' + (returns ? 'العميل / المورد' : 'اسم المورد') + '</span><input class="input" name="party" maxlength="120"></label>' : '') + '<button class="button primary" type="submit">عرض</button><button class="button" type="button" data-reset>إعادة ضبط</button></form><div data-results aria-live="polite"></div></div></section>';
    var form = host.querySelector('form'), target = host.querySelector('[data-results]');
    form.elements.period.onchange = function () { H.preset(form); };
    ['from', 'to'].forEach(function (name) { form.elements[name].onchange = function () { form.elements.period.value = 'custom'; }; });
    form.onsubmit = function (e) { e.preventDefault(); page = 1; load(); };
    host.querySelector('[data-reset]').onclick = function () { form.reset(); H.preset(form); page = 1; load(); };
    function active(current) { return current === token && (!context.isActive || context.isActive()); }
    function summary(d) {
      return H.cards(payments ? [['مدفوعات كاش فورية', d.cash], ['مدفوعات محافظ فورية', d.wallet], ['مدفوعات فيزا فورية', d.card], ['قيمة فواتير آجلة — ليست تحصيلًا', d.credit], ['مدفوعات تاريخية غير مصنفة', d.unclassified]] : returns ? [['عدد مرتجعات البيع', d.salesReturnCount], ['إجمالي مرتجعات البيع المحفوظ', d.salesReturns], ['عدد مرتجعات الشراء', d.purchaseReturnCount], ['إجمالي مرتجعات الشراء المحفوظ', d.purchaseReturns]] : [['الفواتير المنشورة', d.count], ['إجمالي المشتريات المحفوظ', d.total], ['مرتجعات الفترة المحفوظة', d.returns], ['صافي أحداث المشتريات', d.netEvents]]);
    }
    function table(d) {
      var headers = payments ? ['النوع', 'الاسم / السجل', 'الرقم', 'الحالة', 'عدد الفواتير المرتبطة', 'المدفوعات المسجلة'] : returns ? ['مرجع المرتجع', 'وقت المرتجع', 'النوع', 'الفاتورة الأصلية', 'العميل / المورد', 'الإجمالي المحفوظ', 'الحالة'] : ['الفاتورة', 'وقت النشر', 'المورد', 'الإجمالي المحفوظ', 'التسوية الأصلية', 'الحالة'];
      var rows = (payments ? d.instruments : d.items).map(function (x) {
        if (payments) return [esc(x.type === 'wallet' ? 'محفظة' : 'فيزا'), H.link(context, 'payment-history', x.id, x.name), esc(x.number), esc(x.isActive ? 'نشطة' : 'غير نشطة'), esc(x.invoiceCount), esc(x.total)];
        var route = returns && x.type === 'sales' ? 'sales-management' : 'purchase-management';
        var ref = H.link(context, route, x.invoiceId, returns ? x.invoiceNumber : x.reference), when = esc(new Date(x.eventUtc).toLocaleString('ar-EG'));
        return returns ? [esc(x.reference), when, esc(x.type === 'sales' ? 'مرتجع بيع' : 'مرتجع شراء'), ref, esc(x.party || 'نقدي'), esc(x.total), 'منشور'] : [ref, when, esc(x.party), esc(x.total), esc(x.paymentMode === 'cash' ? 'فوري' : 'آجل'), 'منشورة'];
      });
      return '<div class="table-wrap"><table class="data-table"><thead><tr>' + headers.map(function (h) { return '<th>' + h + '</th>'; }).join('') + '</tr></thead><tbody>' + rows.map(function (cells) { return '<tr>' + cells.map(function (cell) { return '<td>' + cell + '</td>'; }).join('') + '</tr>'; }).join('') + '</tbody></table></div>' + (d.count ? '' : '<p class="empty-state">لا توجد سجلات مطابقة.</p>');
    }
    async function load() {
      var current = ++token;
      if (!H.valid(form)) { target.innerHTML = '<p class="field-error" role="alert">تحقق من تاريخ البداية والنهاية.</p>'; return; }
      var params = new URLSearchParams();
      ['from', 'to', 'search', 'type', 'party'].forEach(function (name) { if (form.elements[name] && form.elements[name].value) params.set(name, form.elements[name].value); });
      params.set('page', page);
      target.innerHTML = '<p role="status">جارٍ تحميل التقرير...</p>';
      try {
        var d = await global.AlKhayalApi.request('/api/reports/' + kind + '?' + params.toString());
        if (!active(current)) return;
        target.innerHTML = summary(d) + table(d) + H.pager(page, d.count);
        target.querySelector('[data-prev]').onclick = function () { page--; load(); };
        target.querySelector('[data-next]').onclick = function () { page++; load(); };
        H.bindLinks(target, context);
      } catch (e) {
        if (!active(current)) return;
        target.innerHTML = '<p class="field-error" role="alert">' + esc(e.message) + '</p><button class="button" data-retry>إعادة المحاولة</button>';
        target.querySelector('[data-retry]').onclick = load;
      }
    }
    H.preset(form); load();
  }
  global.AlKhayalRenderers['transaction-reports'] = render;
})(window);
