(function (global) {
  "use strict";
  var H = global.AlKhayalReportUI, esc = H.esc;
  var names = {included:'مصروف تشغيلي',excluded:'مستبعد من الربح',unclassified:'يتطلب التصنيف'};
  function amount(value) { return value == null ? 'غير مكتمل' : value + ' ج.م'; }
  global.AlKhayalRenderers['net-profit-report'] = function (screen, host, context) {
    if (context.canNavigate && !context.canNavigate('report-net-profit')) { host.innerHTML = '<p role="alert">لا تملك صلاحية التقرير.</p>'; return; }
    var page = 1, token = 0;
    host.innerHTML = global.AlKhayalUI.pageHeader(screen) + '<section class="card report-view"><div class="card-body"><p><strong>صافي ربح المبيعات بعد المصروفات المسجلة</strong></p><p class="muted">يشمل المبيعات وتكلفة FIFO الأصلية والمرتجعات والإلغاءات والمصروفات المصنفة فقط. لا يشمل ربح الصيانة أو مصادر دخل وتكاليف غير موثقة. الفترة حسب وقت التسجيل المحلي؛ أقصى فترة 3660 يومًا. تغيير تصنيف سند يعيد عرضه بالتصنيف الحالي مع الاحتفاظ بسجل التصحيحات.</p><form class="form-grid report-filters">' + H.periodFields() + '<label class="field"><span>التفصيل</span><select class="select" name="breakdown"><option value="daily">يومي</option><option value="monthly">شهري</option></select></label><button class="button primary" type="submit">عرض</button><button class="button" type="button" data-reset>إعادة ضبط</button></form><div data-results aria-live="polite"></div></div></section>';
    var form = host.querySelector('form'), results = host.querySelector('[data-results]');
    function active(current) { return current === token && (!context.isActive || context.isActive()); }
    form.elements.period.onchange = function () { H.preset(form); };
    ['from','to'].forEach(function (name) { form.elements[name].onchange = function () { form.elements.period.value = 'custom'; }; });
    form.onsubmit = function (e) { e.preventDefault(); page = 1; load(); };
    host.querySelector('[data-reset]').onclick = function () { form.reset(); H.preset(form); page = 1; load(); };
    function warning(s) {
      if (s.complete) return '<p role="status">الحساب مكتمل ضمن نطاق المبيعات والمصروفات المسجلة.</p>';
      return '<div class="field-error" role="alert"><strong>الحساب غير مكتمل — لا يمكن اعتماد صافي الربح.</strong><p>سندات غير مصنفة: ' + esc(s.unclassifiedCount) + ' بقيمة ' + esc(amount(s.unclassifiedAmount)) + '. أحداث بإيراد غير موثق: ' + esc(s.missingRevenueCount) + '؛ تكلفة غير موثقة: ' + esc(s.missingCostCount) + '؛ إجمالي القيمة المرتبطة بأحداث ناقصة: ' + esc(amount(s.incompleteGrossAmount)) + '.</p></div>';
    }
    function table(headers, rows) { return '<div class="table-wrap"><table class="data-table"><thead><tr>' + headers.map(function (x) { return '<th>' + esc(x) + '</th>'; }).join('') + '</tr></thead><tbody>' + rows.map(function (row) { return '<tr>' + row.map(function (x) { return '<td>' + x + '</td>'; }).join('') + '</tr>'; }).join('') + '</tbody></table></div>'; }
    async function load() {
      var current = ++token;
      if (!H.valid(form)) { results.innerHTML = '<p class="field-error" role="alert">تحقق من تاريخ البداية والنهاية.</p>'; return; }
      var params = new URLSearchParams(); ['from','to','breakdown'].forEach(function (n) { params.set(n,form.elements[n].value); }); params.set('page',page);
      results.innerHTML = '<p role="status">جارٍ تحميل تقرير صافي الأرباح...</p>';
      try {
        var d = await global.AlKhayalApi.request('/api/reports/net-profit?' + params.toString()); if (!active(current)) return;
        var s = d.summary;
        // Coverage is authoritative even if a malformed response contains a numeric subtotal.
        if (!s.complete) { s.netProfit = null; s.netMargin = null; }
        d.items.forEach(function (x) { if (!x.complete) x.netProfit = null; });
        results.innerHTML = warning(s) + H.cards([['صافي إيراد المبيعات',amount(s.revenue)],['تكلفة البضاعة المباعة',amount(s.cogs)],['الربح الإجمالي',amount(s.grossProfit)],['المصروفات التشغيلية المصنفة',amount(s.expenses)],['صافي الربح',amount(s.netProfit)],['هامش صافي الربح',s.netMargin == null ? 'غير متاح' : s.netMargin + '%'],['مصروفات مستبعدة',amount(s.excludedExpenses)]]) + '<h3>تفصيل الفترة</h3>' + table(['الفترة','صافي إيراد المبيعات','تكلفة FIFO','الربح الإجمالي','المصروفات','صافي الربح','اكتمال البيانات'],d.items.map(function (x) { return [esc(x.period),esc(amount(x.revenue)),esc(amount(x.cogs)),esc(amount(x.grossProfit)),esc(amount(x.expenses)),esc(amount(x.netProfit)),x.complete ? 'مكتمل بالنطاق المحدد' : 'غير مكتمل']; })) + (s.eventCount ? '' : '<p class="empty-state">لا توجد أحداث مالية في الفترة المحددة.</p>') + '<h3>تصنيف سندات المصروفات</h3><p>تاريخ المصروف هو تاريخ العمل الأصلي؛ الاعتراف في الربح حسب وقت التسجيل. يعرض آخر تصنيف وسببه ومن سجله.</p>' + table(['السند','النوع / المستفيد','تاريخ المصروف / التسجيل','المبلغ','التصنيف','آخر مراجعة','تصحيح التصنيف (المدير)'],d.expenseItems.map(function (x) {
          var edit = d.canClassify ? '<div data-classify="' + esc(x.id) + '"><label class="field"><span>التصنيف</span><select class="select" name="classification">' + ['unclassified','included','excluded'].map(function (v) { return '<option value="' + v + '" ' + (v === x.classification ? 'selected' : '') + '>' + names[v] + '</option>'; }).join('') + '</select></label><label class="field"><span>سبب التصنيف / التصحيح</span><input class="input" name="reason" maxlength="500" required></label><button class="button" type="button" data-save>حفظ التصنيف</button><p data-save-status role="status"></p></div>' : 'للمدير فقط';
          return [esc(x.id),esc(x.category + ' / ' + x.payee),esc(x.expenseDate + ' / ' + new Date(x.postedUtc).toLocaleString('ar-EG')),esc(amount(x.amount)),esc(names[x.classification]),esc((x.reason || '') + ' · ' + (x.classifiedBy || '') + ' · ' + (x.classifiedUtc ? new Date(x.classifiedUtc).toLocaleString('ar-EG') : 'لم يصنف')),edit];
        })) + (d.expenseCount ? '' : '<p class="empty-state">لا توجد سندات مصروفات في الفترة.</p>') + H.pager(page,d.expenseCount);
        results.querySelector('[data-prev]').onclick = function () { page--; load(); }; results.querySelector('[data-next]').onclick = function () { page++; load(); };
        results.querySelectorAll('[data-classify]').forEach(function (panel) {
          var row = d.expenseItems.find(function (x) { return String(x.id) === panel.getAttribute('data-classify'); }), pending = null, busy = false;
          panel.querySelector('[data-save]').onclick = async function () {
            if (busy) return;
            var select = panel.querySelector('[name="classification"]'), reason = panel.querySelector('[name="reason"]'), status = panel.querySelector('[data-save-status]'), button = panel.querySelector('[data-save]');
            if (reason.value.trim().length < 2) { status.textContent = 'أدخل سببًا من حرفين على الأقل.'; return; }
            if (!pending || pending.classification !== select.value || pending.reason !== reason.value.trim()) pending = {classification:select.value,reason:reason.value.trim(),expectedRevision:row.revision,operationKey:'profit-class-' + Date.now() + '-' + Math.random().toString(36).slice(2)};
            busy = true; button.disabled = true; status.textContent = 'جارٍ حفظ التصنيف...';
            try { await global.AlKhayalApi.request('/api/reports/net-profit/expenses/' + row.id + '/classification',{method:'POST',body:JSON.stringify(pending)}); if (active(current)) load(); }
            catch (e) { if (active(current)) { status.textContent = e.message; button.disabled = false; } }
            finally { busy = false; }
          };
        });
      } catch (e) {
        if (!active(current)) return;
        results.innerHTML = '<p class="field-error" role="alert">' + esc(e.message) + '</p><button class="button" data-retry>إعادة المحاولة</button>'; results.querySelector('[data-retry]').onclick = load;
      }
    }
    H.preset(form); load();
  };
})(window);
