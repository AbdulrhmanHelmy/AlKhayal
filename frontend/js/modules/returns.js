(function (global) {
  "use strict";
  var R = global.AlKhayalRenderers, U = global.AlKhayalUtils, UI = global.AlKhayalUI, Api = global.AlKhayalApi;
  function key() { var b = new Uint8Array(16); global.crypto.getRandomValues(b); return Array.prototype.map.call(b, function (v) { return ("0" + v.toString(16)).slice(-2); }).join(""); }
  function label(type) { return type === "sales" ? "مرتجع بيع" : "مرتجع شراء"; }
  function message(node, reason) { node.hidden = false; node.textContent = reason.message || reason; }
  function management(screen, container, context) {
    var state = context.state, draft = state.returnDraft || (state.returnDraft = { type: "sales", number: "", invoice: null, quantities: {}, units: {}, reason: "", mode: "cash_refund" }), listToken = 0, lookupToken = 0;
    container.innerHTML = UI.pageHeader(screen, '<button class="button primary" data-add-return>+ إضافة مرتجع برقم الفاتورة</button>') + '<section class="card" data-return-editor' + (state.returnEditor ? '' : ' hidden') + '><div class="card-body"><form id="returnLookup" class="form-grid"><div class="field"><label for="returnType">نوع الفاتورة</label><select class="select" id="returnType"><option value="sales">فاتورة بيع</option><option value="purchase"' + (draft.type === "purchase" ? ' selected' : '') + '>فاتورة شراء</option></select></div><div class="field"><label for="returnNumber">رقم الفاتورة</label><input class="input" id="returnNumber" required dir="ltr" maxlength="120" value="' + U.escape(draft.number) + '"></div><div class="span-2"><button class="button" data-return-lookup type="submit">عرض الفاتورة</button></div></form><p class="field-error" data-return-error role="alert" hidden></p><div data-return-lines></div></div></section><section class="card"><div class="card-body"><div class="field"><label for="returnSearch">بحث برقم المرتجع أو الفاتورة أو العميل / المورد</label><input class="input" type="search" id="returnSearch" value="' + U.escape(state.returnSearch || "") + '"></div><p class="muted">آخر 200 مرتجع مطابق للبحث. اضغط على أي مرتجع لعرض تفاصيله.</p></div><div data-return-results aria-live="polite"></div></section>';
    var editor = container.querySelector("[data-return-editor]"), preview = container.querySelector("[data-return-lines]"), error = container.querySelector("[data-return-error]"), type = container.querySelector("#returnType"), number = container.querySelector("#returnNumber"), search = container.querySelector("#returnSearch"), results = container.querySelector("[data-return-results]");
    function frozen() { return !!(draft.busy || draft.pending); }
    function lock() {
      type.disabled = number.disabled = container.querySelector("[data-return-lookup]").disabled = frozen();
      preview.querySelectorAll("input,select,textarea").forEach(function (input) { input.disabled = frozen(); });
      var save = preview.querySelector("[data-return-save]"); if (save) { save.disabled = !!draft.busy; save.textContent = draft.pending ? "إعادة المحاولة للتحقق من الحفظ" : "تسجيل المرتجع"; }
    }
    function drawInvoice() {
      var invoice = draft.invoice; if (!invoice) { preview.innerHTML = ""; lock(); return; }
      preview.innerHTML = '<h2>' + U.escape(invoice.number) + ' — ' + U.escape(invoice.partyName || "نقدي مباشر") + '</h2><form id="managedReturnForm"><div class="table-wrap"><table class="data-table"><thead><tr><th>الصنف</th><th>الأصلية</th><th>المتاح</th><th>الكمية المرتجعة</th></tr></thead><tbody>' + invoice.lines.map(function (line) {
        return '<tr><td>' + U.escape(line.productName) + ' — ' + U.escape(line.unitName) + (line.units || []).map(function (unit, index) { return '<label class="return-serial"><input type="checkbox" data-return-serial-line="' + line.id + '" data-unit-index="' + index + '"' + ((draft.units[line.id] || []).indexOf(unit.serial) >= 0 ? ' checked' : '') + '> ' + U.escape(unit.serial + (unit.imeis.length ? " — " + unit.imeis.join("، ") : "")) + '</label>'; }).join("") + '</td><td class="ltr">' + U.escape(line.quantity) + '</td><td class="ltr">' + U.escape(line.available) + '</td><td><input class="input" data-return-quantity="' + line.id + '" inputmode="decimal" dir="ltr" maxlength="24" value="' + U.escape(draft.quantities[line.id] || "") + '" aria-label="كمية إرجاع ' + U.escape(line.productName) + '"></td></tr>';
      }).join("") + '</tbody></table></div><div class="form-grid">' + (invoice.type === "sales" ? '<div class="field span-2"><label for="returnReason">سبب الإرجاع</label><input class="input" id="returnReason" required minlength="2" maxlength="240" value="' + U.escape(draft.reason) + '"></div><p class="muted span-2">المبلغ النقدي يُصرف من خزنة مرتجعات البيع فقط، ويُسمح برصيد سالب عند عدم كفايته. في الفاتورة الآجلة تُخفض المديونية أولًا حتى الصفر، ويُصرف الباقي نقدًا.</p>' : '<div class="field"><label for="returnSettlement">تسوية المورد</label><select class="select" id="returnSettlement"><option value="cash_refund"' + (draft.mode === "cash_refund" ? ' selected' : '') + '>استرداد نقدي في خزنة مرتجعات الشراء</option><option value="supplier_credit"' + (draft.mode === "supplier_credit" ? ' selected' : '') + '>تسوية حساب المورد دون حركة نقدية</option></select></div>') + '<div class="span-2"><button class="button primary" data-return-save type="submit">تسجيل المرتجع</button></div></div></form>';
      preview.querySelectorAll("[data-return-quantity]").forEach(function (input) { input.oninput = function () { draft.quantities[input.getAttribute("data-return-quantity")] = input.value; }; });
      preview.querySelectorAll("[data-return-serial-line]").forEach(function (input) { input.onchange = function () { var id = input.getAttribute("data-return-serial-line"), line = invoice.lines.find(function (x) { return String(x.id) === id; }), serial = line.units[Number(input.getAttribute("data-unit-index"))].serial; draft.units[id] = (draft.units[id] || []).filter(function (x) { return x !== serial; }); if (input.checked) draft.units[id].push(serial); }; });
      var reason = preview.querySelector("#returnReason"), mode = preview.querySelector("#returnSettlement"); if (reason) reason.oninput = function () { draft.reason = reason.value; }; if (mode) mode.onchange = function () { draft.mode = mode.value; };
      preview.querySelector("#managedReturnForm").onsubmit = save; lock();
    }
    function resetLookup() { if (frozen()) return; ++lookupToken; draft.invoice = null; draft.quantities = {}; draft.units = {}; drawInvoice(); error.hidden = true; }
    type.onchange = function () { draft.type = type.value; resetLookup(); }; number.oninput = function () { draft.number = number.value; resetLookup(); };
    container.querySelector("[data-add-return]").onclick = function () { state.returnEditor = true; editor.hidden = false; number.focus(); };
    container.querySelector("#returnLookup").onsubmit = function (event) {
      event.preventDefault(); if (frozen()) return; var current = ++lookupToken; error.hidden = true;
      Api.request("/api/returns/invoice?type=" + encodeURIComponent(draft.type) + "&number=" + encodeURIComponent(number.value.trim())).then(function (invoice) {
        if (!context.isActive() || current !== lookupToken) return; draft.invoice = invoice; draft.quantities = {}; draft.units = {}; draft.mode = invoice.paymentMode === "credit" ? "supplier_credit" : "cash_refund"; drawInvoice();
      }).catch(function (reason) { if (context.isActive() && current === lookupToken) { draft.invoice = null; drawInvoice(); message(error, reason); } });
    };
    draft.settled = function (result, reason) {
      if (!context.isActive()) return;
      if (result) { drawInvoice(); context.navigate("return-details", { returnType: result.type, returnId: result.id }); }
      else { lock(); message(error, reason); }
    };
    function save(event) {
      event.preventDefault(); if (draft.busy) return; var invoice = draft.invoice, command = draft.pending;
      if (!command) {
        var invalid = false, lines = invoice.lines.map(function (line) {
          var quantity = (draft.quantities[line.id] || "").trim(); if (quantity && (!/^\d+(?:\.\d{1,6})?$/.test(quantity) || Number(quantity) > Number(line.available))) invalid = true;
          return { invoiceLineId: line.id, quantity: quantity, units: (line.units || []).filter(function (unit) { return (draft.units[line.id] || []).indexOf(unit.serial) >= 0; }) };
        }).filter(function (line) { return Number(line.quantity) > 0; });
        if (invalid || !lines.length || (invoice.type === "sales" && draft.reason.trim().length < 2)) { message(error, "أدخل كمية صحيحة ضمن المتاح وسبب الإرجاع للبيع، واختر الأرقام التسلسلية عند الحاجة."); return; }
        command = { type: invoice.type, invoiceId: invoice.id, operationKey: key(), reason: draft.reason.trim(), returnMode: draft.mode, lines: lines };
      }
      draft.pending = command; draft.busy = true; lock(); error.hidden = true;
      Api.request("/api/returns", { method: "POST", body: JSON.stringify(command) }).then(function (result) {
        draft.busy = false; draft.pending = null; draft.invoice = null; draft.quantities = {}; draft.units = {}; draft.reason = ""; UI.toast("تم حفظ المرتجع وتسويته.", "success"); draft.settled(result);
      }).catch(function (reason) { draft.busy = false; if (reason.status && reason.status < 500) draft.pending = null; draft.settled(null, reason); });
    }
    function loadList() {
      var current = ++listToken; results.innerHTML = '<div class="empty-state" role="status">جارٍ تحميل المرتجعات...</div>';
      Api.request("/api/returns?search=" + encodeURIComponent(search.value.trim())).then(function (rows) {
        if (!context.isActive() || current !== listToken) return;
        results.innerHTML = '<div class="table-wrap"><table class="data-table"><thead><tr><th>المرتجع</th><th>النوع</th><th>الفاتورة الأصلية</th><th>العميل / المورد</th><th>التاريخ</th><th>الإجمالي (ج.م)</th></tr></thead><tbody>' + (rows.length ? rows.map(function (row) { return '<tr data-return-id="' + row.id + '" data-return-type="' + row.type + '"><td><button class="table-action" type="button">' + U.escape(row.number) + '</button></td><td>' + label(row.type) + '</td><td>' + U.escape(row.invoiceNumber) + '</td><td>' + U.escape(row.partyName) + '</td><td>' + U.escape(new Date(row.createdUtc).toLocaleString("ar-EG")) + '</td><td class="ltr">' + U.escape(row.total) + '</td></tr>'; }).join("") : '<tr><td colspan="6"><div class="empty-state">لا توجد مرتجعات مطابقة.</div></td></tr>') + '</tbody></table></div>';
        results.onclick = function (event) { var row = event.target.closest("[data-return-id]"); if (row) context.navigate("return-details", { returnType: row.getAttribute("data-return-type"), returnId: Number(row.getAttribute("data-return-id")) }); };
      }).catch(function (reason) { if (!context.isActive() || current !== listToken) return; results.innerHTML = '<div class="empty-state">' + U.escape(reason.message) + '<button class="button" data-return-list-retry>إعادة المحاولة</button></div>'; results.querySelector("[data-return-list-retry]").onclick = loadList; });
    }
    var delayed = U.debounce(loadList, 180); search.oninput = function () { state.returnSearch = search.value; ++listToken; delayed(); };
    drawInvoice(); loadList();
  }
  function detail(screen, container, context) {
    var state = context.state, sequence = 0, pickerSequence = 0;
    container.innerHTML = UI.pageHeader(screen, '<button class="button" data-returns-back>إدارة المرتجعات</button>') + '<section class="card"><div class="card-body form-grid"><div class="field"><label for="returnDetailSearch">بحث برقم المرتجع أو الفاتورة أو العميل / المورد</label><input class="input" type="search" id="returnDetailSearch" value="' + U.escape(state.returnDetailSearch || "") + '"></div><div class="field"><label for="returnDetailChoice">المرتجع</label><select class="select" id="returnDetailChoice" disabled><option value="">جارٍ تحميل المرتجعات...</option></select></div><p class="field-error span-2" data-return-selector-error role="alert" hidden></p><div><button class="button" data-return-selector-retry hidden>إعادة تحميل القائمة</button></div></div></section><section class="card"><div class="card-body" data-return-detail aria-live="polite"></div></section>';
    container.querySelector("[data-returns-back]").onclick = function () { context.navigate("returns-overview"); };
    var host = container.querySelector("[data-return-detail]"), picker = container.querySelector("#returnDetailChoice"), search = container.querySelector("#returnDetailSearch"), pickerError = container.querySelector("[data-return-selector-error]"), pickerRetry = container.querySelector("[data-return-selector-retry]");
    function loadPicker() {
      var current = ++pickerSequence; picker.disabled = true; pickerError.hidden = pickerRetry.hidden = true;
      Api.request("/api/returns?search=" + encodeURIComponent(search.value.trim())).then(function (rows) {
        if (!context.isActive() || current !== pickerSequence) return;
        var selected = state.returnType && state.returnId ? state.returnType + ":" + state.returnId : "";
        picker.innerHTML = '<option value="">' + (rows.length ? "اختر المرتجع — آخر 200 نتيجة" : "لا توجد مرتجعات مطابقة") + '</option>' + (selected && !rows.some(function (row) { return row.type + ":" + row.id === selected; }) ? '<option value="' + U.escape(selected) + '" selected>المرتجع المحدد</option>' : '') + rows.map(function (row) { var value = row.type + ":" + row.id; return '<option value="' + value + '"' + (value === selected ? ' selected' : '') + '>' + U.escape(label(row.type) + " — " + row.number + " — فاتورة " + row.invoiceNumber + " — " + row.partyName) + '</option>'; }).join("");
        picker.disabled = false;
      }).catch(function (reason) { if (!context.isActive() || current !== pickerSequence) return; message(pickerError, reason); pickerRetry.hidden = false; });
    }
    picker.onchange = function () { var identity = picker.value.split(":"); state.returnType = identity[0]; state.returnId = Number(identity[1]) || null; load(); };
    var delayed = U.debounce(loadPicker, 180); search.oninput = function () { state.returnDetailSearch = search.value; ++pickerSequence; delayed(); };
    pickerRetry.onclick = loadPicker;
    function load() {
      var current = ++sequence;
      if (!state.returnId || !state.returnType) { host.innerHTML = '<div class="empty-state">اختر مرتجعًا من القائمة لعرض تفاصيله، أو أضف مرتجعًا من إدارة المرتجعات.</div>'; return; }
      host.innerHTML = '<div class="empty-state" role="status">جارٍ تحميل تفاصيل المرتجع...</div>';
      Api.request("/api/returns/" + encodeURIComponent(state.returnType) + "/" + state.returnId).then(function (row) {
        if (!context.isActive() || current !== sequence) return;
        host.innerHTML = '<h2>' + label(row.type) + ' — ' + U.escape(row.number) + '</h2><p>الفاتورة الأصلية: <bdi>' + U.escape(row.invoiceNumber) + '</bdi> — ' + U.escape(row.partyName) + '</p><p>المخزن: ' + U.escape(row.warehouseName) + ' — المستخدم: ' + U.escape(row.actorName || "—") + ' — ' + U.escape(new Date(row.createdUtc).toLocaleString("ar-EG")) + '</p><p>الإجمالي: <bdi>' + U.escape(row.total) + '</bdi> ج.م — النقدي: <bdi>' + U.escape(row.cash) + '</bdi> ج.م — تسوية الحساب: <bdi>' + U.escape(row.accountAdjustment) + '</bdi> ج.م — التسوية: ' + (row.settlement === "cash" ? "نقدي" : row.settlement === "credit" ? "حساب العميل / المورد" : "نقدي وحساب") + '</p><p>' + U.escape(row.reason || "") + '</p><div class="table-wrap"><table class="data-table"><thead><tr><th>الصنف</th><th>الوحدة</th><th>الكمية</th><th>القيمة (ج.م)</th><th>الأرقام التسلسلية</th></tr></thead><tbody>' + row.lines.map(function (line) { return '<tr><td>' + U.escape(line.productName) + '</td><td>' + U.escape(line.unitName) + '</td><td>' + U.escape(line.quantity) + '</td><td>' + U.escape(line.amount) + '</td><td>' + U.escape(line.serials.join("، ") || "—") + '</td></tr>'; }).join("") + '</tbody></table></div><h3>حركات الخزنة المرتبطة</h3>' + (row.movements.length ? '<div class="table-wrap"><table class="data-table"><thead><tr><th>الحركة</th><th>الخزنة</th><th>الاتجاه</th><th>المبلغ (ج.م)</th></tr></thead><tbody>' + row.movements.map(function (m) { return '<tr><td>' + U.escape(m.id) + '</td><td>' + U.escape(m.accountName) + '</td><td>' + (m.type === "deposit" ? "إيداع" : "صرف") + '</td><td>' + U.escape(m.amount) + '</td></tr>'; }).join("") + '</tbody></table></div>' : '<p class="muted">لا توجد حركات خزنة مرتبطة بهذا المرتجع. التسويات الآجلة والمرتجعات السابقة للربط تحتفظ بسجلها الأصلي.</p>');
      }).catch(function (reason) { if (!context.isActive() || current !== sequence) return; host.innerHTML = '<div class="empty-state">' + U.escape(reason.message) + '<button class="button" data-detail-retry>إعادة المحاولة</button></div>'; host.querySelector("[data-detail-retry]").onclick = load; });
    }
    loadPicker(); load();
  }
  R["returns-management"] = management;
  R["return-details"] = detail;
}(window));
