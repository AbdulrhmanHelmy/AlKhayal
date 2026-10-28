(function (global) {
  "use strict";
  var R = global.AlKhayalRenderers, U = global.AlKhayalUtils, UI = global.AlKhayalUI, Api = global.AlKhayalApi;
  function key() { var bytes = new Uint8Array(16); global.crypto.getRandomValues(bytes); return Array.prototype.map.call(bytes, function (x) { return ("0" + x.toString(16)).slice(-2); }).join(""); }
  function write(path, method, body) { return Api.request(path, { method: method, body: body ? JSON.stringify(body) : undefined }); }
  function accountType(type) { return type === "bank" ? "بنك" : "خزنة"; }
  function showError(host, reason) { var node = host.querySelector("[data-treasury-error]"); node.hidden = false; node.textContent = reason.message || reason; }
  function field(id, label, value, extra) { return '<div class="field"><label for="' + id + '">' + label + '</label><input class="input" id="' + id + '" value="' + U.escape(value || "") + '" ' + (extra || "") + '></div>'; }

  function directory(screen, container, context) {
    var rows = [], token = 0, state = context.state;
    container.innerHTML = UI.pageHeader(screen, '<button class="button primary" data-account-add>+ إضافة خزنة أو بنك</button>') +
      '<section class="card"><div class="card-body">' + field("treasurySearch", "بحث بالاسم أو الكود", state.treasurySearch, 'type="search"') +
      '<p class="muted">تبدأ الحسابات بصفر. استخدم الإيداع لإضافة رصيد والسحب لصرفه. الحساب الذي له حركات مالية لا يمكن حذفه.</p><p class="field-error" data-treasury-error role="alert" hidden></p></div><div data-treasury-results aria-live="polite"></div></section>';
    var results = container.querySelector("[data-treasury-results]"), search = container.querySelector("#treasurySearch");
    search.oninput = function () { state.treasurySearch = search.value; draw(); };
    container.querySelector("[data-account-add]").onclick = function () { edit(null); };
    function draw() {
      var query = U.normalize(search.value), visible = rows.filter(function (row) { return U.normalize(row.code + " " + row.name).indexOf(query) >= 0; });
      results.innerHTML = '<div class="table-wrap"><table class="data-table"><thead><tr><th>الكود</th><th>الاسم</th><th>النوع</th><th>الرصيد (ج.م)</th><th>ملاحظات</th><th>الإجراءات</th></tr></thead><tbody>' +
        (visible.length ? visible.map(function (row) {
          return '<tr><td>' + U.escape(row.code) + '</td><td>' + U.escape(row.name) + '</td><td>' + accountType(row.type) + '</td><td class="ltr">' + U.escape(row.balance) + '</td><td>' + U.escape(row.notes || "—") + '</td><td><button class="table-action" data-account-edit="' + row.id + '">تعديل</button><button class="table-action danger" data-account-delete="' + row.id + '">حذف</button></td></tr>';
        }).join("") : '<tr><td colspan="6"><div class="empty-state">' + (rows.length ? "لا توجد نتائج مطابقة." : "لا توجد خزن أو بنوك. أضف أول حساب للبدء.") + '</div></td></tr>') + '</tbody></table></div>';
      results.querySelectorAll("[data-account-edit]").forEach(function (button) { button.onclick = function () { edit(rows.find(function (row) { return String(row.id) === button.getAttribute("data-account-edit"); })); }; });
      results.querySelectorAll("[data-account-delete]").forEach(function (button) {
        button.onclick = function () {
          var row = rows.find(function (item) { return String(item.id) === button.getAttribute("data-account-delete"); });
          UI.confirm({ title: "حذف خزنة أو بنك", message: "هل تريد حذف «" + row.name + "»؟ لا يمكن حذف حساب عليه حركات مالية.", danger: true }).then(function (confirmed) {
            if (!confirmed || !context.isActive()) return;
            button.disabled = true;
            write("/api/treasury/accounts/" + row.id + "?version=" + row.version, "DELETE").then(function () { if (context.isActive()) { UI.toast("تم حذف الحساب.", "success"); load(); } }).catch(function (reason) { if (context.isActive()) { button.disabled = false; showError(container, reason); } });
          });
        };
      });
    }
    function load() {
      var current = ++token;
      results.innerHTML = '<div class="empty-state" role="status">جارٍ تحميل الخزن والبنوك...</div>';
      container.querySelector("[data-treasury-error]").hidden = true;
      Api.request("/api/treasury/accounts").then(function (data) { if (current !== token || !context.isActive()) return; rows = data; draw(); }).catch(function (reason) {
        if (current !== token || !context.isActive()) return;
        results.innerHTML = '<div class="empty-state"><span>' + U.escape(reason.message) + '</span><button class="button" data-retry>إعادة المحاولة</button></div>';
        results.querySelector("[data-retry]").onclick = load;
      });
    }
    function edit(row) {
      var busy = false;
      UI.modal({ title: row ? "تعديل الخزنة أو البنك" : "إضافة خزنة أو بنك", compact: true, focusSelector: "#treasuryName", beforeClose: function () { return !busy; },
        body: '<form id="treasuryAccountForm" class="form-grid">' + field("treasuryCode", "الكود", row && row.code, 'required maxlength="40" dir="ltr"') + field("treasuryName", "الاسم", row && row.name, 'required maxlength="160"') +
          '<div class="field"><label for="treasuryType">النوع</label><select class="select" id="treasuryType"><option value="cash">خزنة</option><option value="bank"' + (row && row.type === "bank" ? ' selected' : '') + '>بنك</option></select></div><div class="field"><label for="treasuryNotes">ملاحظات</label><textarea class="textarea" id="treasuryNotes" maxlength="1000">' + U.escape(row && row.notes || "") + '</textarea></div><p class="field-error span-2" data-treasury-error role="alert" hidden></p></form>',
        actions: '<button class="button primary" type="submit" form="treasuryAccountForm" data-account-save>حفظ</button><button class="button" data-modal-close>إلغاء</button>',
        onOpen: function (modal) {
          modal.querySelector("#treasuryAccountForm").onsubmit = function (event) {
            event.preventDefault(); if (busy) return;
            var command = { code: modal.querySelector("#treasuryCode").value.trim(), name: modal.querySelector("#treasuryName").value.trim(), type: modal.querySelector("#treasuryType").value, notes: modal.querySelector("#treasuryNotes").value.trim(), version: row ? row.version : 0 };
            if (!command.code || !command.name) { showError(modal, "أدخل الكود والاسم."); return; }
            busy = true; modal.querySelector("[data-account-save]").disabled = true;
            write("/api/treasury/accounts" + (row ? "/" + row.id : ""), row ? "PUT" : "POST", command).then(function () {
              UI.closeModal(); if (context.isActive()) { UI.toast("تم حفظ الحساب.", "success"); load(); }
            }).catch(function (reason) { busy = false; modal.querySelector("[data-account-save]").disabled = false; showError(modal, reason); });
          };
        }
      });
    }
    load();
  }

  function movement(screen, container, context) {
    var type = screen.route === "account-deposit" ? "deposit" : "withdraw", label = type === "deposit" ? "إيداع" : "سحب", state = context.state;
    var draft = state.treasuryDraft || (state.treasuryDraft = { accountId: "", amount: "", notes: "", operationKey: key() }), rows = [], busy = false;
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body" data-treasury-form>جارٍ تحميل الحسابات...</div></section>';
    var host = container.querySelector("[data-treasury-form]");
    function load() {
      Api.request("/api/treasury/accounts").then(function (data) { if (!context.isActive()) return; rows = data; draw(); }).catch(function (reason) {
        if (!context.isActive()) return;
        host.innerHTML = '<div class="empty-state"><span>' + U.escape(reason.message) + '</span><button class="button" data-retry>إعادة المحاولة</button></div>';
        host.querySelector("[data-retry]").onclick = load;
      });
    }
    function draw() {
      if (!rows.length) { host.innerHTML = '<div class="empty-state">لا توجد خزن أو بنوك. أضف حسابًا من قائمة الخزنة والبنك أولًا.</div>'; return; }
      host.innerHTML = '<form id="treasuryMovementForm" class="form-grid"><div class="field"><label for="treasuryAccount">الخزنة أو البنك</label><select class="select" id="treasuryAccount" required><option value="">اختر الحساب</option>' + rows.map(function (row) { return '<option value="' + row.id + '"' + (String(row.id) === String(draft.accountId) ? ' selected' : '') + '>' + U.escape(row.name + " — " + accountType(row.type) + " — " + row.code) + '</option>'; }).join("") + '</select></div><div class="field"><label>الرصيد الحالي (ج.م)</label><output data-account-balance class="ltr"></output></div>' +
        field("treasuryAmount", "المبلغ (ج.م)", draft.amount, 'required inputmode="decimal" dir="ltr" maxlength="20" autocomplete="off"') + '<div class="field"><label for="treasuryMovementNotes">البيان / ملاحظات</label><textarea class="textarea" id="treasuryMovementNotes" maxlength="1000">' + U.escape(draft.notes) + '</textarea></div><p class="field-error span-2" data-treasury-error role="alert" hidden></p><div class="span-2"><button class="button primary" type="submit" data-movement-save>' + (draft.pending ? "إعادة المحاولة للتحقق من الحفظ" : "حفظ " + label + " الرصيد") + '</button></div></form><div data-treasury-receipt role="status"></div>';
      var account = host.querySelector("#treasuryAccount"), amount = host.querySelector("#treasuryAmount"), notes = host.querySelector("#treasuryMovementNotes"), form = host.querySelector("#treasuryMovementForm");
      function balance() { var row = rows.find(function (item) { return String(item.id) === account.value; }); host.querySelector("[data-account-balance]").textContent = row ? row.balance : "—"; }
      account.onchange = function () { draft.accountId = account.value; balance(); };
      amount.oninput = function () { draft.amount = amount.value; };
      notes.oninput = function () { draft.notes = notes.value; };
      balance();
      if (draft.pending) { account.disabled = amount.disabled = notes.disabled = true; showError(host, "لم يتم تأكيد نتيجة الطلب. أعد المحاولة بنفس البيانات للتحقق من الحفظ قبل بدء عملية جديدة."); }
      if (state.treasuryReceipt) receipt(state.treasuryReceipt);
      form.onsubmit = function (event) {
        event.preventDefault(); if (busy) return;
        var command = draft.pending || { accountId: Number(account.value), amount: amount.value.trim(), notes: notes.value.trim() };
        if (!command.accountId || !/^\d+(?:\.\d{1,2})?$/.test(command.amount) || !/[1-9]/.test(command.amount)) { showError(host, "اختر الحساب وأدخل مبلغًا أكبر من صفر بدقة خانتين عشريتين بحد أقصى."); return; }
        var signature = JSON.stringify([command.accountId, command.amount, command.notes]);
        if (!draft.pending && draft.signature !== signature) { draft.operationKey = key(); draft.signature = signature; }
        command.operationKey = draft.operationKey;
        draft.pending = command; busy = true;
        account.disabled = amount.disabled = notes.disabled = true;
        var button = host.querySelector("[data-movement-save]"); button.disabled = true;
        write("/api/treasury/" + type, "POST", command).then(function (result) {
          draft.pending = null; draft.amount = ""; draft.notes = ""; draft.operationKey = key(); draft.signature = null; state.treasuryReceipt = result; busy = false;
          if (context.isActive()) { UI.toast("تم حفظ " + label + " الرصيد.", "success"); draw(); load(); }
        }).catch(function (reason) {
          busy = false;
          // A transport/5xx error can follow a committed transaction; retain its identity across tab switches.
          if (reason.status && reason.status < 500) draft.pending = null;
          if (!context.isActive()) return;
          draw(); showError(host, reason);
        });
      };
    }
    function receipt(result) {
      host.querySelector("[data-treasury-receipt]").innerHTML = '<div class="notice"><strong>تم حفظ ' + label + ' الرصيد — حركة رقم ' + U.escape(result.id) + '</strong><p>المبلغ: <bdi>' + U.escape(result.amount) + '</bdi> ج.م — رصيد الحساب بعد الحركة: <bdi>' + U.escape(result.balanceAfter) + '</bdi> ج.م</p><p>' + U.escape(new Date(result.createdUtc).toLocaleString("ar-EG")) + (result.replayed ? " — تم التحقق من العملية السابقة دون تكرارها." : "") + '</p></div>';
    }
    load();
  }
  R["treasury-accounts"] = directory;
  R["treasury-movement"] = movement;
}(window));
