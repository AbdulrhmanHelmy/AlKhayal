(function (global) {
  "use strict";
  var R = global.AlKhayalRenderers, U = global.AlKhayalUtils, UI = global.AlKhayalUI, Api = global.AlKhayalApi;
  var base = "/api/workforce/";
  function esc(x) { return U.escape(x == null ? "" : x); }
  function key() { var b = new Uint8Array(16); global.crypto.getRandomValues(b); return Array.from(b).map(function (v) { return ("0" + v.toString(16)).slice(-2); }).join(""); }
  function active(c) { return !c.isActive || c.isActive(); }
  var timeFormat = new Intl.DateTimeFormat("ar-EG", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
  function time(x) { return x ? timeFormat.format(new Date(x)) : "—"; }
  function discrepancy(s) { return s.difference == null ? "—" : (s.difference === "0.00" ? "مطابق" : s.difference.charAt(0) === "-" ? "عجز" : "زيادة") + " — " + s.difference; }
  function enabled(feature) { return global.AlKhayalFeatures && global.AlKhayalFeatures.isEnabled(feature); }
  function money(value, optional, positive) {
    value = String(value == null ? '' : value).trim();
    if (optional && !value) return null;
    if (!/^\d+(?:\.\d{1,2})?$/.test(value) || positive && !/[1-9]/.test(value)) throw new Error('أدخل مبلغًا ' + (positive ? 'أكبر من صفر' : 'غير سالب') + ' بدقة خانتين عشريتين بحد أقصى.');
    return value;
  }
  function field(name, label, value, type, required) { return UI.field({ id: name, label: label, type: type || "text", required: !!required }, value); }
  function select(name, label, value, options) { return UI.field({ id: name, label: label, type: "select", options: options }, value); }
  function yesNo(name, label, value) { return select(name, label, value ? "1" : "0", [{ value: "0", label: "لا" }, { value: "1", label: "نعم" }]); }
  function table(headers, rows) { return '<div class="table-wrap"><table class="data-table"><thead><tr>' + headers.map(function (h) { return '<th>' + esc(h) + '</th>'; }).join("") + '</tr></thead><tbody>' + (rows.join("") || '<tr><td colspan="' + headers.length + '"><div class="empty-state">لا توجد سجلات مطابقة.</div></td></tr>') + '</tbody></table></div>'; }
  function pager(page) { return '<div class="workforce-actions"><button class="button" data-page="' + (page.page - 1) + '"' + (page.page <= 1 ? ' disabled' : '') + '>السابق</button><span>صفحة ' + page.page + ' — ' + page.total + ' سجل</span><button class="button" data-page="' + (page.page + 1) + '"' + (page.page * page.pageSize >= page.total ? ' disabled' : '') + '>التالي</button></div>'; }
  function qs(q) { return Object.keys(q).filter(function (k) { return q[k] !== "" && q[k] != null; }).map(function (k) { return encodeURIComponent(k) + '=' + encodeURIComponent(q[k]); }).join('&'); }
  function showError(node, e) { node.hidden = false; node.textContent = e.message || String(e); }

  // Keep the command and key after an uncertain response. Retrying never creates a second event.
  function editModal(context, slot, title, body, build, path, saved, initial) {
    var drafts = context.state.workforceDrafts || (context.state.workforceDrafts = {}), draft = drafts[slot] || (drafts[slot] = { values: initial || {} });
    UI.modal({ title: title, wide: true, body: '<form data-workforce-form class="form-grid">' + body(draft.values) + '<p class="field-error span-2" data-error role="alert" hidden></p></form>',
      actions: '<button class="button primary" data-save type="button">حفظ</button><button class="button" data-modal-close type="button">إلغاء</button>',
      beforeClose: function () { return !draft.busy && !draft.pending; },
      onOpen: function (modal) {
        var form = modal.querySelector('[data-workforce-form]'), button = modal.querySelector('[data-save]'), error = modal.querySelector('[data-error]');
        function capture() { Array.from(form.elements).forEach(function (n) { if (n.name) draft.values[n.name] = n.value; }); }
        function lock() { form.querySelectorAll('input,select,textarea').forEach(function (n) { n.disabled = !!(draft.busy || draft.pending); }); button.disabled = !!draft.busy; button.textContent = draft.busy ? "جارٍ الحفظ…" : draft.pending ? "إعادة المحاولة للتحقق من الحفظ" : "حفظ"; }
        form.oninput = capture; form.onchange = capture;
        function submit(e) {
          if (e) e.preventDefault(); if (draft.busy) return;
          if (!draft.pending && !form.reportValidity()) return;
          capture();
          try { draft.pending = draft.pending || Object.assign(build(draft.values), { operationKey: key() }); }
          catch (reason) { showError(error, reason); return; }
          draft.busy = true; lock(); error.hidden = true;
          Api.request(base + path, { method: "POST", body: JSON.stringify(draft.pending) }).then(function (result) {
            draft.busy = false; draft.pending = null; delete drafts[slot];
            if (!active(context) || !modal.isConnected) return;
            UI.closeModal(); UI.toast("تم الحفظ.", "success"); saved(result);
          }).catch(function (reason) {
            draft.busy = false;
            if (reason.status >= 400 && reason.status < 500) draft.pending = null;
            if (!active(context) || !modal.isConnected) return;
            lock(); showError(error, reason);
          });
        }
        button.onclick = submit; form.onsubmit = submit; lock();
        if (draft.pending && !draft.busy) showError(error, "النتيجة غير مؤكدة. أعد المحاولة بنفس البيانات للتحقق من الحفظ.");
      }
    });
  }
  function pageHost(screen, container, extra, filters) {
    container.innerHTML = UI.pageHeader(screen, extra || "") + '<section class="card"><div class="card-body"><form class="workforce-filters" data-filters>' + filters + '<button type="submit" class="button">تحديث</button></form></div><div data-results aria-live="polite"></div></section>';
    return container.querySelector('[data-results]');
  }
  function loader(context, host, request, draw) {
    var sequence = 0;
    return function () {
      var token = ++sequence;
      host.setAttribute('aria-busy', 'true');
      if (!host.innerHTML) host.innerHTML = '<div class="empty-state">جارٍ التحميل…</div>';
      request().then(function (data) { if (!active(context) || token !== sequence) return; host.setAttribute('aria-busy', 'false'); draw(data); }).catch(function (reason) {
        if (!active(context) || token !== sequence) return;
        host.setAttribute('aria-busy', 'false'); host.innerHTML = '<div class="empty-state" role="alert">' + esc(reason.message) + '</div>';
      });
    };
  }
  function employees(screen, container, context) {
    var q = context.state.employeeQuery || (context.state.employeeQuery = { search: "", status: "", page: 1 }), rows = [], users = [], autoAdd = screen.route === "employee-add";
    var host = pageHost(screen, container, '<button class="button primary" data-new>إضافة موظف</button>', field('search', 'بحث بالاسم أو الهاتف أو الكود', q.search, 'search') + select('status', 'الحالة', q.status, [{ value: '', label: 'الكل' }, { value: 'active', label: 'نشط' }, { value: 'inactive', label: 'موقوف' }]));
    var load = loader(context, host, function () { return Api.request(base + 'employees?' + qs(q)); }, function (page) {
      rows = page.items;
      host.innerHTML = table(['الكود', 'الموظف', 'الهاتف', 'الوظيفة', 'الحالة', ''], rows.map(function (e) {
        return '<tr><td>' + esc(e.code) + '</td><td>' + esc(e.name) + '</td><td>' + esc(e.phone) + '</td><td>' + esc(e.jobTitle) + '</td><td>' + (e.isActive ? 'نشط' : 'موقوف') + '</td><td><button class="button" data-employee="' + e.id + '">تفاصيل / تعديل</button></td></tr>';
      })) + pager(page);
      if (autoAdd) { autoAdd = false; edit(null); }
    });
    function edit(e) {
      var data = e ? Object.assign({}, e, { isActive: e.isActive ? '1' : '0', attendanceEnabled: e.attendanceEnabled ? '1' : '0' }) : { isActive: '1', attendanceEnabled: '0' };
      editModal(context, 'employee-' + (e ? e.id : 'new'), e ? 'بيانات ' + e.code : 'إضافة موظف', function (v) {
        return (e ? '<p class="muted span-2">تاريخ الإنشاء: ' + esc(time(e.createdUtc)) + '</p>' : '') + field('name', 'اسم الموظف', v.name, 'text', true) + field('phone', 'الهاتف (اختياري)', v.phone) + field('jobTitle', 'الوظيفة (اختياري)', v.jobTitle) + field('hireDate', 'تاريخ التعيين (اختياري)', v.hireDate, 'date') +
          field('address', 'العنوان (اختياري)', v.address) + field('notes', 'ملاحظات (اختياري)', v.notes, 'textarea') + select('linkedUserId', 'حساب الدخول (اختياري)', v.linkedUserId || '', [{ value: '', label: 'دون حساب دخول' }].concat(users.map(function (u) { return { value: u.id, label: u.displayName + ' — ' + u.username }; }))) +
          yesNo('isActive', 'نشط', v.isActive === '1') + yesNo('attendanceEnabled', 'تتبع الحضور لهذا الموظف (اختياري)', v.attendanceEnabled === '1') + '<p class="muted span-2">البيانات التالية معلومات اختيارية للمدير، ولا تسجل صرف راتب أو خصمًا نقديًا.</p>' +
          field('salary', 'الراتب (اختياري)', v.salary) + select('salaryFrequency', 'دورية الراتب (اختياري)', v.salaryFrequency || '', [{ value: '', label: 'غير محدد' }, { value: 'monthly', label: 'شهري' }, { value: 'weekly', label: 'أسبوعي' }, { value: 'other', label: 'أخرى' }]) + field('payday', 'يوم الدفع من الشهر 1–31 (اختياري)', v.payday, 'number') + field('compensationNotes', 'ملاحظات الراتب (اختياري)', v.compensationNotes, 'textarea');
      }, function (v) { if (v.payday && (!/^\d+$/.test(v.payday) || Number(v.payday) < 1 || Number(v.payday) > 31)) throw new Error('يوم الدفع الاختياري يجب أن يكون من 1 إلى 31.'); return { id: e ? e.id : 0, version: e ? e.version : 0, name: v.name, phone: v.phone, jobTitle: v.jobTitle, hireDate: v.hireDate, address: v.address, notes: v.notes, linkedUserId: v.linkedUserId ? Number(v.linkedUserId) : null, isActive: v.isActive === '1', attendanceEnabled: v.attendanceEnabled === '1', salary: money(v.salary, true), salaryFrequency: v.salaryFrequency, payday: v.payday ? Number(v.payday) : null, compensationNotes: v.compensationNotes }; }, 'employees/save', load, data);
    }
    container.querySelector('[data-new]').onclick = function () { edit(null); };
    wireFilters(container, q, load);
    host.onclick = function (event) { var b = event.target.closest('[data-employee]'); if (b) edit(rows.find(function (r) { return r.id === Number(b.getAttribute('data-employee')); })); else changePage(event, q, load); };
    Api.request('/api/users').then(function (value) { if (!active(context)) return; users = value; load(); }).catch(function (e) { showError(host, e); });
  }
  function wireFilters(container, q, load) {
    var form = container.querySelector('[data-filters]');
    function collect() {
      Array.from(form.elements).forEach(function (n) { if (!n.name) return; var value = n.value; if (n.name === 'fromUtc' || n.name === 'toUtc') value = value ? new Date(value + 'T00:00:00').toISOString() : ''; q[n.name] = value; }); q.page = 1;
    }
    form.onsubmit = function (e) { e.preventDefault(); collect(); load(); };
    var search = form.querySelector('[name="search"]'); if (search) search.oninput = U.debounce(function () { collect(); load(); }, 220);
    form.querySelectorAll('select,input[type="date"]').forEach(function (n) { n.onchange = function () { collect(); load(); }; });
  }
  function changePage(event, q, load) { var b = event.target.closest('[data-page]'); if (b && !b.disabled) { q.page = Number(b.getAttribute('data-page')); load(); } }
  function dateValue(utc) { if (!utc) return ''; var d = new Date(utc); return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2); }
  function dates(q) { return field('fromUtc', 'من تاريخ', dateValue(q.fromUtc), 'date') + field('toUtc', 'إلى تاريخ (غير شامل)', dateValue(q.toUtc), 'date'); }
  function attendance(screen, container, context) {
    var q = context.state.attendanceQuery || (context.state.attendanceQuery = { search: '', status: '', page: 1 }), rows = [];
    var host = pageHost(screen, container, '<button class="button primary" data-in>تسجيل حضور</button>', field('search', 'بحث باسم الموظف', q.search, 'search') + dates(q) + select('status', 'الحالة', q.status, [{ value: '', label: 'الكل' }, { value: 'open', label: 'حاضر الآن' }, { value: 'closed', label: 'انصرف' }]));
    var load = loader(context, host, function () { return Api.request(base + 'attendance?' + qs(q)); }, function (page) {
      rows = page.items;
      host.innerHTML = '<p class="muted workforce-note">الحضور مستقل عن وردية الكاشير. التأخير والغياب غير محسوبين لعدم وجود جدول عمل. الأوقات بالتوقيت المحلي للجهاز.</p>' + table(['الموظف', 'الحضور', 'الانصراف', 'المدة', ''], rows.map(function (r) {
        return '<tr><td>' + esc(r.employeeName) + '</td><td>' + esc(time(r.checkInUtc)) + '</td><td>' + esc(r.checkOutUtc ? time(r.checkOutUtc) : 'حاضر الآن') + '</td><td>' + Math.floor(r.workedSeconds / 3600) + ' س ' + Math.floor(r.workedSeconds % 3600 / 60) + ' د</td><td>' + (!r.checkOutUtc ? '<button class="button" data-out="' + r.id + '">انصراف</button>' : '') + '<button class="button" data-correct="' + r.id + '">تصحيح إداري</button><button class="button" data-attendance-audit="' + r.id + '">سجل التعديلات</button></td></tr>';
      })) + pager(page);
    });
    container.querySelector('[data-in]').onclick = function () { employeePicker(context, function (e) { editModal(context, 'attendance-in-' + e.id, 'حضور ' + e.name, function () { return '<p>تسجيل الحضور بالتوقيت الحالي.</p>'; }, function () { return { employeeId: e.id }; }, 'attendance/in', load); }); };
    wireFilters(container, q, load);
    host.onclick = function (event) {
      var out = event.target.closest('[data-out]'), correct = event.target.closest('[data-correct]'), audit = event.target.closest('[data-attendance-audit]');
      if (audit) { attendanceAudit(context, Number(audit.getAttribute('data-attendance-audit'))); return; }
      if (out) { var id = Number(out.getAttribute('data-out')); editModal(context, 'attendance-out-' + id, 'تسجيل الانصراف', function () { return '<p>تسجيل الانصراف بالتوقيت الحالي.</p>'; }, function () { return { id: id }; }, 'attendance/out', load); }
      else if (correct) {
        var row = rows.find(function (r) { return r.id === Number(correct.getAttribute('data-correct')); });
        editModal(context, 'attendance-correct-' + row.id, 'تصحيح حضور ' + row.employeeName, function (v) { return field('checkInUtc', 'الحضور بالتوقيت المحلي', v.checkInUtc, 'datetime-local', true) + field('checkOutUtc', 'الانصراف بالتوقيت المحلي (اختياري)', v.checkOutUtc, 'datetime-local') + field('reason', 'سبب التصحيح', v.reason, 'textarea', true); }, function (v) {
          return { id: row.id, version: row.version, checkInUtc: new Date(v.checkInUtc).toISOString(), checkOutUtc: v.checkOutUtc ? new Date(v.checkOutUtc).toISOString() : null, reason: v.reason };
        }, 'attendance/correct', load, { checkInUtc: localTimestamp(row.checkInUtc), checkOutUtc: localTimestamp(row.checkOutUtc) });
      } else changePage(event, q, load);
    }; load();
  }
  function attendanceAudit(context, id) {
    UI.modal({ title: 'سجل تعديلات الحضور', body: '<div data-attendance-audit-results></div>', onOpen: function (modal) {
      var host = modal.querySelector('[data-attendance-audit-results]'), page = 1;
      var labels = { 'attendance-in': 'حضور', 'attendance-out': 'انصراف', 'attendance-correct': 'تصحيح إداري' };
      var load = loader({ isActive: function () { return active(context) && modal.isConnected; } }, host, function () { return Api.request(base + 'attendance/' + id + '/audit?page=' + page); }, function (rows) {
        host.innerHTML = table(['الوقت', 'الإجراء', 'المستخدم', 'البيان'], rows.map(function (row) { var detail = (row.detail || '').replace('before=', 'السابق: ').replace('after=', 'الجديد: ').replace(/\d{4}-\d{2}-\d{2}T[\d:.]+Z/g, function (stamp) { return time(stamp); }); return '<tr><td>' + esc(time(row.createdUtc)) + '</td><td>' + esc(labels[row.action]) + '</td><td>' + esc(row.actorUserId) + '</td><td>' + esc(detail) + '</td></tr>'; })) + '<div class="workforce-actions"><button class="button" data-prev' + (page <= 1 ? ' disabled' : '') + '>السابق</button><button class="button" data-next' + (rows.length < 50 ? ' disabled' : '') + '>التالي</button></div>';
        host.querySelector('[data-prev]').onclick = function () { page--; load(); }; host.querySelector('[data-next]').onclick = function () { page++; load(); };
      }); load();
    } });
  }
  function localTimestamp(value) { if (!value) return ''; var d = new Date(value); return dateValue(value) + 'T' + ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2); }
  function employeePicker(context, selected) {
    UI.modal({ title: 'اختيار موظف لتسجيل الحضور', body: '<label class="field">بحث الموظفين<input class="input" data-pick-search type="search"></label><div data-pick-results></div>', onOpen: function (modal) {
      var input = modal.querySelector('[data-pick-search]'), host = modal.querySelector('[data-pick-results]'), q = { search: '', status: 'active', page: 1 }, rows = [];
      var load = loader({ isActive: function () { return active(context) && modal.isConnected; } }, host, function () { return Api.request(base + 'employees?' + qs(q)); }, function (page) {
        rows = page.items.filter(function (r) { return r.attendanceEnabled; }); host.innerHTML = rows.map(function (e) { return '<button class="button" data-pick="' + e.id + '">' + esc(e.code + ' — ' + e.name) + '</button>'; }).join(' ') + (!rows.length ? '<p>لا يوجد موظف مفعل للحضور في هذه الصفحة.</p>' : '') + pager(page);
      });
      input.oninput = U.debounce(function () { q.search = input.value; q.page = 1; load(); }, 220);
      host.onclick = function (event) { var b = event.target.closest('[data-pick]'); if (b) selected(rows.find(function (r) { return r.id === Number(b.getAttribute('data-pick')); })); else changePage(event, q, load); }; load();
    } });
  }
  function shiftOpen(context, saved) {
    function open(users) {
      editModal(context, 'shift-open', 'فتح وردية كاشير', function (v) { return field('amount', 'نقد افتتاح الدرج', v.amount || '0', 'text', true) + field('notes', 'ملاحظات الافتتاح (اختياري)', v.notes, 'textarea') + (users ? select('cashierId', 'الكاشير المسؤول', v.cashierId || context.user.id, users.filter(function (u) { return u.isActive; }).map(function (u) { return { value: u.id, label: u.displayName }; })) : '<p>المستخدم الحالي: ' + esc(context.user.displayName) + '</p>') + '<p class="muted span-2">جهاز مستقل واحد. مبلغ الافتتاح جرد نقدي ولا ينشئ إيداعًا في الخزنة.</p>'; }, function (v) { return { amount: money(v.amount), notes: v.notes, cashierId: users ? Number(v.cashierId || context.user.id) : null }; }, 'shifts/open', saved);
    }
    if (context.user.role === 'administrator') Api.request('/api/users').then(function (users) { if (active(context)) open(users); }).catch(function (e) { UI.toast(e.message, 'danger'); }); else open(null);
  }
  function shiftAction(context, shift, action, saved) {
    editModal(context, action + '-' + shift.id, action === 'close' ? 'إغلاق الوردية #' + shift.id : 'إدخال / إخراج نقد من الدرج', function (v) {
      return '<p class="span-2">النقد المتوقع: <strong>' + esc(shift.expected) + '</strong> ج.م</p>' + field('amount', action === 'close' ? 'النقد المعدود عند الإغلاق' : 'المبلغ', v.amount, 'text', true) + (action === 'close' ? field('notes', 'ملاحظات الإغلاق (اختياري)', v.notes, 'textarea') : select('direction', 'الاتجاه', v.direction || 'in', [{ value: 'in', label: 'إدخال نقد' }, { value: 'out', label: 'إخراج نقد' }]) + field('reason', 'سبب الحركة', v.reason, 'textarea', true) + '<p class="muted span-2">حركة موثقة للدرج فقط. لا تنقل رصيد الخزنة أو البنك ولا تسجل مصروفًا.</p>');
    }, function (v) { return { id: shift.id, amount: money(v.amount, false, action === 'adjust'), notes: v.notes, direction: v.direction, reason: v.reason }; }, 'shifts/' + action, saved);
  }
  function summary(s) {
    var values = [['الكاشير', s.cashierName], ['الجهاز', 'الجهاز المستقل'], ['الافتتاح', time(s.openedUtc)], ['الإغلاق', time(s.closedUtc)], ['نقد الافتتاح', s.opening], ['عدد المبيعات', s.salesCount], ['مبيعات نقدية', s.cashSales], ['مبيعات آجلة منشورة (إجمالي قبل الإلغاء)', s.creditSales], ['تحصيلات نقدية', s.collections], ['إدخال نقد', s.cashIn], ['إخراج نقد', s.cashOut], ['النقد المتوقع', s.expected], ['النقد المعدود', s.counted || '—'], ['فرق النقد', discrepancy(s)], ['ملاحظات الافتتاح', s.openingNotes], ['ملاحظات الإغلاق', s.closingNotes]];
    return '<dl class="workforce-summary">' + values.map(function (v) { return '<div><dt>' + esc(v[0]) + '</dt><dd>' + esc(v[1]) + '</dd></div>'; }).join('') + '</dl><p class="muted workforce-note">المتوقع = الافتتاح + المبيعات النقدية + التحصيل النقدي + الإدخال − الإخراج. المرتجعات من خزنة مستقلة؛ تحصيلات كشف حساب العميل والحركات العامة للخزنة غير مرتبطة بهذا الدرج.</p>';
  }
  function shifts(screen, container, context) {
    if (screen.route === 'shift-details') { details(screen, container, context); return; }
    var q = context.state.shiftQuery || (context.state.shiftQuery = { status: screen.route === 'shift-add' ? 'open' : '', page: 1 }), rows = [];
    var host = pageHost(screen, container, '<button class="button primary" data-open-shift>فتح وردية</button>', dates(q) + select('status', 'الحالة', q.status, [{ value: '', label: 'الكل' }, { value: 'open', label: 'مفتوحة' }, { value: 'closed', label: 'مغلقة' }]) + (context.user.role === 'administrator' ? field('cashierId', 'رقم مستخدم الكاشير (اختياري)', q.cashierId, 'number') : '') + select('discrepanciesOnly', 'تقرير الفروق', q.discrepanciesOnly || '', [{ value: '', label: 'جميع الورديات' }, { value: 'true', label: 'عجز أو زيادة فقط' }]));
    var load = loader(context, host, function () { return Api.request(base + 'shifts?' + qs(q)); }, function (page) {
      rows = page.items; host.innerHTML = table(['الوردية', 'الكاشير', 'الافتتاح', 'الحالة', 'الافتتاح النقدي', 'النقدي', 'الآجل', 'التحصيل', 'المتوقع', 'المعدود', 'الفرق', ''], rows.map(function (s) {
        return '<tr><td>#' + s.id + '</td><td>' + esc(s.cashierName) + '</td><td>' + esc(time(s.openedUtc)) + '</td><td>' + (s.closedUtc ? 'مغلقة' : 'مفتوحة') + '</td><td>' + esc(s.opening) + '</td><td>' + esc(s.cashSales) + '</td><td>' + esc(s.creditSales) + '</td><td>' + esc(s.collections) + '</td><td>' + esc(s.expected) + '</td><td>' + esc(s.counted || '—') + '</td><td>' + esc(discrepancy(s)) + '</td><td><button class="button" data-shift="' + s.id + '">التفاصيل</button>' + (!s.closedUtc ? '<button class="button" data-close-shift="' + s.id + '">إغلاق</button>' : '') + '</td></tr>';
      })) + pager(page);
    });
    container.querySelector('[data-open-shift]').onclick = function () { shiftOpen(context, load); }; wireFilters(container, q, load);
    host.onclick = function (event) { var b = event.target.closest('[data-shift]'), close = event.target.closest('[data-close-shift]'); if (b) context.navigate('shift-details', { shiftId: Number(b.getAttribute('data-shift')) }); else if (close) shiftAction(context, rows.find(function (r) { return r.id === Number(close.getAttribute('data-close-shift')); }), 'close', load); else changePage(event, q, load); }; load();
  }
  function details(screen, container, context) {
    var id = context.state.shiftId, eventPage = 1;
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div data-detail class="card-body"></div><div data-events></div></section>';
    var host = container.querySelector('[data-detail]'), events = container.querySelector('[data-events]');
    if (!id) { host.innerHTML = '<p>اختر وردية من السجل لعرض التفاصيل.</p><button class="button" data-history>سجل الورديات</button>'; host.querySelector('[data-history]').onclick = function () { context.navigate('shifts-list'); }; return; }
    var load = loader(context, host, function () { return Api.request(base + 'shifts/' + id); }, function (s) {
      host.innerHTML = summary(s) + (!s.closedUtc ? '<div class="workforce-actions"><button class="button" data-adjust>حركة نقد للدرج</button><button class="button primary" data-close>إغلاق الوردية</button></div>' : '');
      if(global.AlKhayalPrinting){global.AlKhayalPrinting.mount(host,"shift-summary",s.id);if(s.closedUtc)global.AlKhayalPrinting.mount(host,"shift-closing",s.id).textContent="طباعة إغلاق الوردية";}
      if (!s.closedUtc) { host.querySelector('[data-close]').onclick = function () { shiftAction(context, s, 'close', function () { load(); loadEvents(); }); }; host.querySelector('[data-adjust]').onclick = function () { shiftAction(context, s, 'adjust', function () { load(); loadEvents(); }); }; }
    });
    var labels = { cash_sale: 'بيع نقدي', credit_sale: 'بيع آجل', collection: 'تحصيل نقدي', in: 'إدخال نقد', out: 'إخراج نقد' };
    var loadEvents = loader(context, events, function () { return Api.request(base + 'shifts/' + id + '/events?page=' + eventPage); }, function (rows) {
      events.innerHTML = table(['الوقت', 'نوع الحركة', 'المبلغ', 'الفاتورة', 'الدفعة', 'المستخدم', 'السبب'], rows.map(function (e) { return '<tr><td>' + esc(time(e.createdUtc)) + '</td><td>' + esc(labels[e.kind]) + '</td><td>' + esc(e.amount) + '</td><td>' + esc(e.salesInvoiceId) + '</td><td>' + esc(e.salesPaymentId) + '</td><td>' + e.actorUserId + '</td><td>' + esc(e.reason) + '</td></tr>'; })) + '<div class="workforce-actions"><button class="button" data-event-prev' + (eventPage <= 1 ? ' disabled' : '') + '>السابق</button><span>صفحة ' + eventPage + '</span><button class="button" data-event-next' + (rows.length < 50 ? ' disabled' : '') + '>التالي</button></div>';
      events.querySelector('[data-event-prev]').onclick = function () { eventPage--; loadEvents(); }; events.querySelector('[data-event-next]').onclick = function () { eventPage++; loadEvents(); };
    }); load(); loadEvents();
  }
  function mount(container, context) {
    if (!enabled('advanced-shifts')) return;
    var anchor = container.querySelector('.touch-head') || container.querySelector('.page-header'); if (!anchor) return;
    var button = global.document.createElement('button'); button.type = 'button'; button.className = 'button'; button.textContent = 'الوردية الحالية'; button.setAttribute('data-workforce-current', '');
    button.onclick = function (event) { event.stopPropagation(); button.disabled = true; Api.request(base + 'shifts/current').then(function (s) {
      if (!active(context) || !button.isConnected) return;
      button.disabled = false;
      if (!s) { shiftOpen(context, function () { button.textContent = 'تم فتح الوردية'; }); return; }
      UI.modal({ title: 'الوردية الحالية #' + s.id, body: summary(s), actions: '<button class="button" data-adjust-current>حركة نقد للدرج</button><button class="button primary" data-close-current>إغلاق الوردية</button><button class="button" data-modal-close>رجوع</button>', onOpen: function (modal) { if(global.AlKhayalPrinting)global.AlKhayalPrinting.mount(modal.querySelector('.modal-actions')||modal,'shift-summary',s.id); modal.querySelector('[data-adjust-current]').onclick = function () { shiftAction(context, s, 'adjust', function () {}); }; modal.querySelector('[data-close-current]').onclick = function () { shiftAction(context, s, 'close', function () { button.textContent = 'الوردية الحالية'; }); }; } });
    }).catch(function (e) { button.disabled = false; if (active(context)) UI.toast(e.message, 'danger'); }); }; anchor.appendChild(button);
  }
  R['workforce-employees'] = employees; R['workforce-attendance'] = attendance; R['workforce-shifts'] = shifts;
  global.AlKhayalWorkforce = { mount: mount };
}(window));
