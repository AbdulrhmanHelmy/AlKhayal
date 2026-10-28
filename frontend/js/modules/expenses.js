(function (global) {
  "use strict";
  var R = global.AlKhayalRenderers, U = global.AlKhayalUtils, UI = global.AlKhayalUI, Api = global.AlKhayalApi;
  function key() { var bytes = new Uint8Array(16); global.crypto.getRandomValues(bytes); return Array.prototype.map.call(bytes, function (x) { return ("0" + x.toString(16)).slice(-2); }).join(""); }
  function today() { var d = new Date(); return d.getFullYear() + "-" + ("0" + (d.getMonth() + 1)).slice(-2) + "-" + ("0" + d.getDate()).slice(-2); }
  function post(path, body) { return Api.request("/api/expenses/" + path, { method: "POST", body: JSON.stringify(body) }); }
  function field(id, label, value, extra) { return '<div class="field"><label for="' + id + '">' + label + '</label><input class="input" id="' + id + '" value="' + U.escape(value || "") + '" ' + (extra || "") + '></div>'; }
  function error(node, reason) { node.hidden = false; node.textContent = reason.message || reason; }

  function categories(screen, container, context) {
    var state = context.state, draft = state.expenseCategoryDraft || (state.expenseCategoryDraft = { name: "", busy: false }), rows = [], sequence = 0;
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body"><form id="expenseCategoryForm" class="form-grid">' + field("expenseCategoryName", "اسم نوع المصروف", draft.name, 'required maxlength="160" autocomplete="off"') +
      '<div class="field"><label>إضافة نوع</label><button class="button primary" type="submit" data-category-save>+ إضافة نوع مصروف</button></div><p class="field-error span-2" data-category-error role="alert" hidden></p></form>' + field("expenseCategorySearch", "بحث في أنواع المصروفات", state.expenseSearch, 'type="search"') + '</div><div data-expense-categories aria-live="polite"></div></section>';
    var name = container.querySelector("#expenseCategoryName"), button = container.querySelector("[data-category-save]"), search = container.querySelector("#expenseCategorySearch"), results = container.querySelector("[data-expense-categories]"), message = container.querySelector("[data-category-error]");
    function lock() { name.disabled = !!(draft.busy || draft.pending); button.disabled = !!draft.busy; button.textContent = draft.pending ? "إعادة المحاولة للتحقق من الحفظ" : "+ إضافة نوع مصروف"; }
    function draw() {
      var query = U.normalize(search.value), visible = rows.filter(function (row) { return U.normalize(row.name).indexOf(query) >= 0; });
      results.innerHTML = '<div class="table-wrap"><table class="data-table"><thead><tr><th>الرقم</th><th>نوع المصروف</th></tr></thead><tbody>' + (visible.length ? visible.map(function (row) { return '<tr><td>' + U.escape(row.id) + '</td><td>' + U.escape(row.name) + '</td></tr>'; }).join("") : '<tr><td colspan="2"><div class="empty-state">' + (rows.length ? "لا توجد نتائج مطابقة." : "لا توجد أنواع مصروفات. أضف النوع الأول لاستخدامه في سند الصرف.") + '</div></td></tr>') + '</tbody></table></div>';
    }
    function load() {
      var current = ++sequence; results.innerHTML = '<div class="empty-state" role="status">جارٍ تحميل أنواع المصروفات...</div>';
      Api.request("/api/expenses/categories").then(function (data) { if (!context.isActive() || current !== sequence) return; rows = data; draw(); }).catch(function (reason) {
        if (!context.isActive() || current !== sequence) return;
        results.innerHTML = '<div class="empty-state">' + U.escape(reason.message) + '<button class="button" data-categories-retry>إعادة المحاولة</button></div>';
        results.querySelector("[data-categories-retry]").onclick = load;
      });
    }
    name.oninput = function () { draft.name = name.value; };
    search.oninput = function () { state.expenseSearch = search.value; draw(); };
    draft.onSettled = function (saved, reason) {
      if (!context.isActive()) return;
      lock();
      if (saved) { name.value = ""; UI.toast("تم حفظ نوع المصروف.", "success"); load(); }
      else error(message, reason);
    };
    container.querySelector("#expenseCategoryForm").onsubmit = function (event) {
      event.preventDefault(); if (draft.busy) return;
      var command = draft.pending || { name: name.value.trim() };
      if (!command.name) { error(message, "أدخل اسم نوع المصروف."); return; }
      draft.name = command.name; draft.pending = command; draft.busy = true; message.hidden = true; lock();
      post("categories", command).then(function () {
        draft.busy = false; draft.pending = null; draft.name = "";
        draft.onSettled(true);
      }).catch(function (reason) {
        draft.busy = false; if (reason.status && reason.status < 500) draft.pending = null;
        draft.onSettled(false, reason);
      });
    };
    lock(); load();
  }

  function voucher(screen, container, context) {
    var state = context.state, draft = state.expenseDraft || (state.expenseDraft = { categoryId: "", treasuryAccountId: "", amount: "", expenseDate: today(), payee: "", notes: "", busy: false }), types = [], accounts = [];
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body" data-expense-form role="region" aria-label="تسجيل سند صرف">جارٍ تحميل الأنواع والحسابات...</div></section><section class="card"><div class="card-header"><h2>آخر 200 سند صرف محفوظ</h2><button class="button" data-vouchers-refresh>تحديث السندات</button></div><div data-expense-vouchers aria-live="polite"></div></section>';
    var host = container.querySelector("[data-expense-form]"), history = container.querySelector("[data-expense-vouchers]"), sequence = 0, historySequence = 0;
    function load() {
      var current = ++sequence;
      Promise.all([Api.request("/api/expenses/categories"), Api.request("/api/treasury/accounts")]).then(function (data) {
        if (!context.isActive() || current !== sequence) return;
        types = data[0]; accounts = data[1]; draw();
      }).catch(function (reason) {
        if (!context.isActive() || current !== sequence) return;
        host.innerHTML = '<div class="empty-state">' + U.escape(reason.message) + '<button class="button" data-expense-retry>إعادة المحاولة</button></div>';
        host.querySelector("[data-expense-retry]").onclick = load;
      });
    }
    function draw() {
      if (!types.length || !accounts.length) { host.innerHTML = '<div class="empty-state">' + (!types.length ? "أضف نوع مصروف من صفحة إدارة المصروفات أولًا." : "أضف خزنة أو بنك من قائمة الخزنة والبنك أولًا.") + '<button class="button" data-expense-retry>تحديث</button></div>'; host.querySelector("[data-expense-retry]").onclick = load; return; }
      host.innerHTML = '<form id="expenseVoucherForm" class="form-grid"><div class="field"><label for="expenseType">نوع المصروف</label><select class="select" id="expenseType" required><option value="">اختر نوع المصروف</option>' + types.map(function (row) { return '<option value="' + row.id + '"' + (String(row.id) === String(draft.categoryId) ? ' selected' : '') + '>' + U.escape(row.name) + '</option>'; }).join("") + '</select></div><div class="field"><label for="expenseAccount">الصرف من خزنة أو بنك</label><select class="select" id="expenseAccount" required><option value="">اختر الحساب</option>' + accounts.map(function (row) { return '<option value="' + row.id + '"' + (String(row.id) === String(draft.treasuryAccountId) ? ' selected' : '') + '>' + U.escape(row.name + " — " + row.code) + '</option>'; }).join("") + '</select></div>' +
        field("expenseAmount", "المبلغ (ج.م)", draft.amount, 'required inputmode="decimal" dir="ltr" maxlength="20" autocomplete="off"') + field("expenseDate", "تاريخ المصروف", draft.expenseDate, 'type="date" required') + field("expensePayee", "المستفيد (اختياري)", draft.payee, 'maxlength="200"') + '<div class="field"><label>رصيد الحساب الحالي (ج.م)</label><output class="ltr" data-expense-balance></output></div><div class="field span-2"><label for="expenseNotes">البيان / ملاحظات</label><textarea class="textarea" id="expenseNotes" maxlength="1000">' + U.escape(draft.notes) + '</textarea></div><p class="muted span-2">حفظ السند يخصم المبلغ من الحساب المختار. السند المحفوظ لا يُعدّل أو يُحذف.</p><p class="field-error span-2" data-expense-error role="alert" hidden></p><div class="span-2"><button class="button primary" type="submit" data-expense-save>حفظ سند الصرف</button></div></form><div data-expense-receipt role="status"></div>';
      var type = host.querySelector("#expenseType"), account = host.querySelector("#expenseAccount"), amount = host.querySelector("#expenseAmount"), date = host.querySelector("#expenseDate"), payee = host.querySelector("#expensePayee"), notes = host.querySelector("#expenseNotes"), button = host.querySelector("[data-expense-save]"), message = host.querySelector("[data-expense-error]");
      var fields = [type, account, amount, date, payee, notes];
      function lock() { fields.forEach(function (node) { node.disabled = !!(draft.busy || draft.pending); }); button.disabled = !!draft.busy; button.textContent = draft.pending ? "إعادة المحاولة للتحقق من الحفظ" : "حفظ سند الصرف"; }
      function balance() { var row = accounts.find(function (item) { return String(item.id) === account.value; }); host.querySelector("[data-expense-balance]").textContent = row ? row.balance : "—"; }
      type.onchange = function () { draft.categoryId = type.value; };
      account.onchange = function () { draft.treasuryAccountId = account.value; balance(); };
      amount.oninput = function () { draft.amount = amount.value; };
      date.onchange = function () { draft.expenseDate = date.value; };
      payee.oninput = function () { draft.payee = payee.value; };
      notes.oninput = function () { draft.notes = notes.value; };
      lock(); balance();
      // The latest mounted tab receives completion even when the request began in an older render.
      draft.onSettled = function (result, reason) {
        if (!context.isActive()) return;
        if (result) { UI.toast("تم حفظ سند الصرف وخصم المبلغ من الحساب.", "success"); load(); loadHistory(); }
        else { lock(); error(message, reason); }
      };
      if (draft.pending && !draft.busy) error(message, "لم يتم تأكيد نتيجة الطلب. أعد المحاولة بنفس بيانات السند للتحقق من الحفظ.");
      if (state.expenseReceipt) host.querySelector("[data-expense-receipt]").innerHTML = '<div class="notice">تم حفظ سند ' + U.escape(state.expenseReceipt.number) + ' — ' + U.escape(state.expenseReceipt.categoryName) + ' — ' + U.escape(state.expenseReceipt.amount) + ' ج.م' + (state.expenseReceipt.replayed ? " — تم التحقق من السند السابق دون تكراره." : "") + '</div>';
      host.querySelector("#expenseVoucherForm").onsubmit = function (event) {
        event.preventDefault(); if (draft.busy) return;
        var command = draft.pending || { categoryId: Number(type.value), treasuryAccountId: Number(account.value), amount: amount.value.trim(), expenseDate: date.value, payee: payee.value.trim(), notes: notes.value.trim(), operationKey: key() };
        if (!command.categoryId || !command.treasuryAccountId || !/^\d+(?:\.\d{1,2})?$/.test(command.amount) || !/[1-9]/.test(command.amount) || !/^\d{4}-\d{2}-\d{2}$/.test(command.expenseDate)) { error(message, "اختر نوع المصروف والحساب وأدخل تاريخًا ومبلغًا صحيحين بدقة خانتين عشريتين بحد أقصى."); return; }
        draft.pending = command; draft.busy = true; message.hidden = true; lock();
        post("vouchers", command).then(function (result) {
          draft.busy = false; draft.pending = null; draft.amount = ""; draft.payee = ""; draft.notes = ""; state.expenseReceipt = result;
          draft.onSettled(result);
        }).catch(function (reason) {
          draft.busy = false; if (reason.status && reason.status < 500) draft.pending = null;
          draft.onSettled(null, reason);
        });
      };
    }
    function loadHistory() {
      var current = ++historySequence; history.innerHTML = '<div class="empty-state" role="status">جارٍ تحميل السندات...</div>';
      Api.request("/api/expenses/vouchers").then(function (rows) {
        if (!context.isActive() || current !== historySequence) return;
        history.innerHTML = '<div class="table-wrap"><table class="data-table"><thead><tr><th>رقم السند</th><th>التاريخ</th><th>نوع المصروف</th><th>الخزنة / البنك</th><th>المبلغ (ج.م)</th><th>المستفيد</th><th>البيان</th></tr></thead><tbody>' + (rows.length ? rows.map(function (row) { return '<tr><td class="ltr">' + U.escape(row.number) + '</td><td class="ltr">' + U.escape(row.expenseDate) + '</td><td>' + U.escape(row.categoryName) + '</td><td>' + U.escape(row.accountName) + '</td><td class="ltr">' + U.escape(row.amount) + '</td><td>' + U.escape(row.payee || "—") + '</td><td>' + U.escape(row.notes || "—") + '</td></tr>'; }).join("") : '<tr><td colspan="7"><div class="empty-state">لا توجد سندات صرف محفوظة.</div></td></tr>') + '</tbody></table></div>';
      }).catch(function (reason) {
        if (!context.isActive() || current !== historySequence) return;
        history.innerHTML = '<div class="empty-state">' + U.escape(reason.message) + '<button class="button" data-vouchers-retry>إعادة المحاولة</button></div>';
        history.querySelector("[data-vouchers-retry]").onclick = loadHistory;
      });
    }
    container.querySelector("[data-vouchers-refresh]").onclick = loadHistory;
    load(); loadHistory();
  }
  R["expense-categories"] = categories;
  R["expense-voucher"] = voucher;
}(window));
