(function (global) {
  "use strict";
  var U = global.AlKhayalUtils, UI = global.AlKhayalUI, selectedInvoiceId = null;
  function esc(value) { return U.escape(value == null ? "" : String(value)); }
  function moneyScaled(value) { var n = BigInt(String(value || "0")), negative = n < 0n; if (negative) n = -n; var cents = (n + 50n) / 100n, whole = (cents / 100n).toString(), fraction = (cents % 100n).toString().padStart(2, "0"); return (negative ? "-" : "") + whole + "." + fraction; }
  function numScale(value, places) {
    var text = String(value || "0").trim(), parts = text.split("."), fraction = (parts[1] || "").padEnd(places, "0");
    if (fraction.length > places || !/^\d+$/.test(parts[0] || "0") || !/^\d*$/.test(fraction)) throw new Error("تحقق من دقة الكمية والسعر.");
    return BigInt(parts[0] || "0") * (10n ** BigInt(places)) + BigInt(fraction || "0");
  }
  function parsePhysicalUnits(text, withImei) {
    return String(text || "").split(/\r?\n/).map(function (row) { var parts = row.trim().split("|"), serial = (parts[0] || "").trim(); return serial ? { serial: serial, imeis: withImei ? (parts[1] || "").split(",").map(function (x) { return x.trim(); }).filter(Boolean) : [] } : null; }).filter(Boolean);
  }
  function totalFor(lines) {
    return lines.reduce(function (sum, line) {
      var raw = numScale(line.quantity, 6) * numScale(line.purchaseUnitPrice, 2), total = ((raw + 500000n) / 1000000n) * 100n;
      return sum + total;
    }, 0n);
  }
  function makeKey(id) {
    var keyName = "alkhayal_purchase_post_" + id, key = sessionStorage.getItem(keyName);
    if (!key) { key = "p-" + id + "-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10); sessionStorage.setItem(keyName, key); }
    return key;
  }
  function options(rows, value, label) {
    return (rows || []).map(function (x) { return '<option value="' + esc(x.id) + '"' + (String(x.id) === String(value) ? " selected" : "") + ">" + esc(label(x)) + "</option>"; }).join("");
  }
  function renderPurchase(screen, container, context) {
    var state = { invoice: null, suppliers: [], warehouses: [], lines: [], products: [], busy: false, supplierId: null, warehouseId: null, invoiceNumber: "", invoiceDate: null, paymentMode: "credit", notes: "" };
    function page() {
      var total = "0";
      try { total = totalFor(state.lines).toString(); } catch (e) { total = "0"; }
      container.innerHTML = UI.pageHeader(screen) +
        '<section class="card purchase-head"><div class="card-body form-grid"><div class="field"><label for="purchaseSupplier">المورد</label><select class="select" id="purchaseSupplier" required>' + options(state.suppliers, state.supplierId || state.invoice && state.invoice.supplierId, function (x) { return x.name + " — مستحق " + U.money(x.payable || 0); }) + '</select></div>' +
        '<div class="field"><label for="purchaseWarehouse">المخزن</label><select class="select" id="purchaseWarehouse" required>' + options(state.warehouses, state.warehouseId || state.invoice && state.invoice.warehouseId, function (x) { return x.name; }) + '</select></div>' +
        '<div class="field"><label for="purchaseNumber">رقم فاتورة المورد</label><input class="input" id="purchaseNumber" maxlength="80" value="' + esc(state.invoiceNumber || state.invoice && state.invoice.invoiceNumber || "") + '"></div>' +
        '<div class="field"><label for="purchaseDate">تاريخ الفاتورة</label><input class="input" id="purchaseDate" type="date" required value="' + esc(state.invoiceDate || state.invoice && state.invoice.invoiceDate || new Date().toISOString().slice(0,10)) + '"></div>' +
        '<div class="field"><label for="purchaseMode">طريقة الشراء</label><select class="select" id="purchaseMode"><option value="credit"' + ((state.paymentMode || state.invoice && state.invoice.paymentMode) === "credit" ? " selected" : "") + '>آجل</option><option value="cash"' + ((state.paymentMode || state.invoice && state.invoice.paymentMode) === "cash" ? " selected" : "") + '>نقدي — يسجل تسوية للمورد فقط</option></select></div>' +
        '<div class="field"><label for="purchaseNotes">ملاحظات</label><input class="input" id="purchaseNotes" value="' + esc(state.notes || state.invoice && state.invoice.notes || "") + '"></div></div></section>' +
        '<section class="card"><div class="card-head"><h2>أصناف الفاتورة</h2><div class="purchase-search"><label class="sr-only" for="purchaseSearch">ابحث عن صنف</label><input class="input" id="purchaseSearch" placeholder="ابحث عن صنف بالاسم أو الباركود"><div id="purchaseResults" class="purchase-results" hidden></div></div></div><div class="card-body"><div class="table-wrap"><table class="data-table"><thead><tr><th>الصنف والمتغير</th><th>الوحدة</th><th>الكمية</th><th>سعر الشراء</th><th>سعر البيع</th><th>الإجمالي</th><th></th></tr></thead><tbody id="purchaseLines">' + state.lines.map(lineHtml).join("") + '</tbody></table></div><p class="purchase-total"><strong>إجمالي البنود</strong><strong>' + esc(moneyScaled(total)) + '</strong></p><p class="muted">الإجمالي = مجموع (كمية البند × سعر الشراء). لا تُطبق ضرائب أو خصومات.</p></div></section>' +
        '<div class="purchase-actions"><span class="muted">' + (state.invoice ? (state.invoice.status === "posted" ? "فاتورة منشورة — السجل غير قابل للتعديل" : "مسودة محفوظة — يمكن للمستخدم المخول تعديلها") : "احفظ المسودة قبل النشر") + '</span><div><button class="button" type="button" data-purchase-save ' + (state.busy || state.invoice && state.invoice.status === "posted" ? "disabled" : "") + '>حفظ المسودة</button> ' + (state.invoice ? '<button class="button primary" type="button" data-purchase-post ' + (state.busy || state.invoice.status === "posted" ? "disabled" : "") + '>نشر الفاتورة واستلام البضاعة</button>' : "") + '</div></div>';
      container.querySelectorAll("[data-line-field]").forEach(function (input) { input.onchange = lineChanged; });
      container.onclick = click;
      container.oninput = function (event) { syncHeader(event.target); };
      container.onchange = function (event) { syncHeader(event.target); };
      var search = container.querySelector("#purchaseSearch");
      var delayedSearch=U.debounce(searchProducts,180);
      search.oninput=function(){++productSearchToken;delayedSearch(search.value.trim());};
    }
    function lineHtml(line, index) {
      var track = line.tracking || {};
      var trackingHtml = (track.batch || track.expiry ? '<label>رقم التشغيلة<input class="input" data-line-field="lotNumber" value="' + esc(line.lotNumber || "") + '" maxlength="80"></label>' : "") + (track.expiry ? '<label>الصلاحية<input class="input" type="date" data-line-field="expirationDate" value="' + esc(line.expirationDate || "") + '"></label>' : "") + (track.serial ? '<label>الأرقام التسلسلية' + (track.imei ? ' (اكتب الرقم التسلسلي ثم | ثم IMEI مفصولًا بفاصلة)' : '') + '<textarea class="input" data-line-field="identifiers" rows="2" placeholder="رقم لكل جهاز" >' + esc(line.identifierText || "") + '</textarea></label>' : "");
      return '<tr data-line="' + index + '"><td><strong>' + esc(line.productName) + '</strong><br><select class="select" data-line-field="variant">' + options(line.variants, line.variantId, function (v) { return v.name; }) + '</select>' + (trackingHtml ? '<div class="purchase-tracking">' + trackingHtml + '</div>' : '') + '</td><td><select class="select" data-line-field="unit">' + options(line.units, line.productUnitId, function (u) { return u.name + (u.isBase ? " (أساسية)" : ""); }) + '</select></td><td><input class="input" data-line-field="quantity" inputmode="decimal" value="' + esc(line.quantity) + '"></td><td><input class="input" data-line-field="purchaseUnitPrice" inputmode="decimal" value="' + esc(line.purchaseUnitPrice) + '"></td><td><input class="input" data-line-field="saleUnitPrice" inputmode="decimal" value="' + esc(line.saleUnitPrice) + '"></td><td>' + esc(line.lineTotal || "—") + '</td><td><button class="button danger" type="button" data-remove-line="' + index + '">حذف</button></td></tr>';
    }
    function lineChanged(event) {
      var row = event.target.closest("[data-line]"), index = Number(row.getAttribute("data-line")), line = state.lines[index], key = event.target.getAttribute("data-line-field");
      if (key === "variant") line.variantId = Number(event.target.value);
      if (key === "unit") line.productUnitId = Number(event.target.value);
      if (key === "quantity" || key === "purchaseUnitPrice" || key === "saleUnitPrice") line[key] = event.target.value;
      if (key === "lotNumber" || key === "expirationDate") line[key] = event.target.value;
      if (key === "identifiers") { line.identifierText = event.target.value; line.physicalUnits = parsePhysicalUnits(line.identifierText, line.tracking && line.tracking.imei); }
      line.lineTotal = "—";
      var totalCell = row.cells[5]; if (totalCell) totalCell.textContent = "—";
      try { container.querySelector(".purchase-total strong:last-child").textContent = moneyScaled(totalFor(state.lines).toString()); } catch (ignore) {}
    }
    function syncHeader(input) {
      if (!input || !input.id) return;
      if (input.id === "purchaseSupplier") state.supplierId = Number(input.value);
      if (input.id === "purchaseWarehouse") state.warehouseId = Number(input.value);
      if (input.id === "purchaseNumber") state.invoiceNumber = input.value;
      if (input.id === "purchaseDate") state.invoiceDate = input.value;
      if (input.id === "purchaseMode") state.paymentMode = input.value;
      if (input.id === "purchaseNotes") state.notes = input.value;
    }
    var productSearchToken=0;
    async function searchProducts(query) {
      var target = container.querySelector("#purchaseResults"),token=productSearchToken;
      if (!target)return;
      if (query.length < 2) { target.hidden = true; return; }
      target.innerHTML='<span class="muted">جارٍ البحث عن المنتجات...</span>';target.hidden=false;
      try {
        var result = await AlKhayalApi.request("/api/inventory/products?search=" + encodeURIComponent(query) + "&page=1&pageSize=20");
        if (token!==productSearchToken||!container.contains(target)||(context.isActive&&!context.isActive())) return;
        state.products = (result.items || []).filter(function (x) { return x.productType === "stock" && x.isActive && !x.isArchived; });
        target.innerHTML = state.products.map(function (x) { return '<button class="purchase-result" type="button" data-add-product="' + esc(x.id) + '">' + esc(x.name) + (x.primarySku ? " — " + esc(x.primarySku) : "") + '</button>'; }).join("") || '<span class="muted">لا توجد أصناف مطابقة</span>';
        target.hidden = false;
      } catch (error) { if(token===productSearchToken&&container.contains(target))target.innerHTML='<span class="field-error">'+esc(error.message||"تعذر البحث عن المنتجات.")+'</span>'; }
    }
    async function addProduct(id) {
      try {
        var product = await AlKhayalApi.request("/api/inventory/products/" + encodeURIComponent(id));
        var variant = (product.variants || []).filter(function (x) { return x.isActive && !x.isArchived; })[0], unit = (product.units || []).filter(function (x) { return x.isActive && x.isPurchaseUnit; })[0] || (product.units || []).filter(function (x) { return x.isActive && x.isBase; })[0];
        if (!variant || !unit) throw new Error("لا يوجد متغير أو وحدة شراء نشطة لهذا الصنف.");
        var enabled = function (key) { return (product.capabilities || []).some(function (x) { return x.key === key && x.state === "enabled-and-applicable"; }); };
        state.lines.push({ productName: product.name, variants: product.variants.filter(function (x) { return x.isActive && !x.isArchived; }), units: product.units.filter(function (x) { return x.isActive; }), variantId: variant.id, productUnitId: unit.id, quantity: "1", purchaseUnitPrice: variant.effectivePurchasePrice || product.purchasePrice || "0", saleUnitPrice: variant.effectiveSalePrice || product.salePrice || "0", lotNumber: "", expirationDate: "", tracking: { batch: enabled("batch-tracking"), expiry: enabled("expiry-dates"), serial: enabled("serial-numbers"), imei: enabled("imei-tracking") }, identifierText: "", physicalUnits: [] });
        page();
      } catch (error) { UI.toast(error.message, "danger"); }
    }
    function formData() {
      return { supplierId: Number(container.querySelector("#purchaseSupplier").value), warehouseId: Number(container.querySelector("#purchaseWarehouse").value), invoiceNumber: container.querySelector("#purchaseNumber").value, invoiceDate: container.querySelector("#purchaseDate").value, paymentMode: container.querySelector("#purchaseMode").value, notes: container.querySelector("#purchaseNotes").value,
        lines: state.lines.map(function (x) { return { variantId: x.variantId, productUnitId: x.productUnitId, quantity: x.quantity, purchaseUnitPrice: x.purchaseUnitPrice, saleUnitPrice: x.saleUnitPrice, lotNumber: x.lotNumber || null, expirationDate: x.expirationDate || null, physicalUnits: x.tracking && x.tracking.serial ? parsePhysicalUnits(x.identifierText || "", x.tracking.imei) : [] }; }) };
    }
    async function saveDraft() {
      state.busy = true; page();
      try {
        var body = JSON.stringify(formData()), saved = await AlKhayalApi.request(state.invoice ? "/api/purchases/" + state.invoice.id : "/api/purchases/drafts", { method: state.invoice ? "PUT" : "POST", body: body });
        state.invoice = saved; state.lines = saved.lines || state.lines; page(); UI.toast("تم حفظ مسودة فاتورة الشراء.", "success");
      } catch (error) { state.busy = false; page(); UI.toast(error.message || "تعذر حفظ المسودة.", "danger"); }
    }
    async function postInvoice() {
      if (!state.invoice) return;
      state.busy = true; page();
      try {
        var result = await AlKhayalApi.request("/api/purchases/" + state.invoice.id + "/post", { method: "POST", body: JSON.stringify({ operationKey: makeKey(state.invoice.id) }) });
        state.invoice = result.invoice; state.busy = false; page(); UI.toast("تم نشر الفاتورة واستلام البضاعة وربطها بالمورد.", "success");
      } catch (error) { state.busy = false; page(); UI.toast((error.message || "تعذر النشر.") + " — أعد المحاولة دون تغيير الفاتورة.", "danger"); }
    }
    async function loadInvoice(id) {
      if (!id) return;
      try {
        var invoice = await AlKhayalApi.request("/api/purchases/" + id); state.invoice = invoice; state.supplierId = invoice.supplierId; state.warehouseId = invoice.warehouseId; state.paymentMode = invoice.paymentMode; state.invoiceDate = invoice.invoiceDate; state.invoiceNumber = invoice.invoiceNumber; state.notes = invoice.notes;
        state.lines = await Promise.all((invoice.lines || []).map(async function (line) {
          var product = await AlKhayalApi.request("/api/inventory/products/" + line.productId);
          line.productName = product.name; line.variants = product.variants.filter(function (x) { return x.isActive && !x.isArchived; }); line.units = product.units.filter(function (x) { return x.isActive; });
          var enabled = function (key) { return (product.capabilities || []).some(function (x) { return x.key === key && x.state === "enabled-and-applicable"; }); };
          line.tracking = { batch: enabled("batch-tracking"), expiry: enabled("expiry-dates"), serial: enabled("serial-numbers"), imei: enabled("imei-tracking") };
          line.identifierText = (line.physicalUnits || []).map(function (u) { return u.serial + (line.tracking.imei && u.imeis && u.imeis.length ? "|" + u.imeis.join(",") : ""); }).join("\n");
          line.lineTotal = line.lineTotalFormatted || moneyScaled(line.lineTotalScaled); return line;
        }));
        page();
      }
      catch (error) { UI.toast(error.message, "danger"); }
    }
    function click(event) {
      var add = event.target.closest("[data-add-product]"), remove = event.target.closest("[data-remove-line]"), save = event.target.closest("[data-purchase-save]"), post = event.target.closest("[data-purchase-post]");
      if (add) { var id = Number(add.getAttribute("data-add-product")); container.querySelector("#purchaseSearch").value = ""; addProduct(id); }
      if (remove) { state.lines.splice(Number(remove.getAttribute("data-remove-line")), 1); page(); }
      if (save) saveDraft(); if (post) postInvoice();
    }
    async function init() {
      try {
        var data = await Promise.all([AlKhayalApi.request("/api/suppliers"), AlKhayalApi.request("/api/inventory/warehouses")]);
        state.suppliers = data[0] || []; state.warehouses = (data[1] || []).filter(function (x) { return x.isActive; });
        page();
        if (selectedInvoiceId) { var id = selectedInvoiceId; selectedInvoiceId = null; await loadInvoice(id); }
        else if (context && context.state && context.state.invoiceId) await loadInvoice(context.state.invoiceId);
      } catch (error) { container.innerHTML = UI.pageHeader(screen) + '<div class="empty-state"><strong>تعذر تحميل بيانات الشراء</strong><span>' + esc(error.message) + '</span><button class="button" data-purchase-retry>إعادة المحاولة</button></div>'; container.onclick = function (e) { if (e.target.closest("[data-purchase-retry]")) init(); }; }
    }
    init();
  }
  function renderPurchaseList(screen, container, context) {
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-head"><h2>فواتير الشراء</h2><button class="button primary" data-new-purchase>فاتورة جديدة</button></div><div class="card-body" data-purchase-list>جارٍ التحميل…</div></section>';
    var body = container.querySelector("[data-purchase-list]");
    AlKhayalApi.request("/api/purchases").then(function (rows) {
      body.innerHTML = rows.length ? '<div class="table-wrap"><table class="data-table"><thead><tr><th>التاريخ</th><th>رقم الفاتورة</th><th>المورد</th><th>المخزن</th><th>الإجمالي</th><th>الحالة</th><th></th></tr></thead><tbody>' + rows.map(function (x) { return '<tr><td>' + esc(x.invoiceDate) + '</td><td>' + esc(x.invoiceNumber) + '</td><td>' + esc(x.supplierName) + '</td><td>' + esc(x.warehouseName) + '</td><td>' + esc(x.totalFormatted || moneyScaled(x.totalScaled)) + '</td><td>' + (x.status === "posted" ? "منشورة" : "مسودة") + '</td><td>' + (x.status === "draft" ? '<button class="button" data-edit-purchase="' + esc(x.id) + '">استكمال المسودة</button>' : "") + '</td></tr>'; }).join("") + '</tbody></table></div>' : '<div class="empty-state"><strong>لا توجد فواتير شراء بعد</strong><span>ستظهر الفواتير المحفوظة هنا.</span></div>';
    }).catch(function (error) { body.innerHTML = '<div class="empty-state"><strong>تعذر تحميل سجل المشتريات</strong><span>' + esc(error.message) + '</span><button class="button" data-reload-purchases>إعادة المحاولة</button></div>'; });
    container.onclick = function (event) { if (event.target.closest("[data-new-purchase]")) context.navigate("purchase-management"); var edit = event.target.closest("[data-edit-purchase]"); if (edit) { selectedInvoiceId = Number(edit.getAttribute("data-edit-purchase")); context.navigate("purchase-management"); } if (event.target.closest("[data-reload-purchases]")) renderPurchaseList(screen, container, context); };
  }
  function renderPurchaseReturns(screen, container) {
    var state = { invoices: [], warehouses: [], lines: [], invoiceId: null, warehouseId: null, mode: "supplier_credit", key: null, busy: false, lastResult: null, inputLines: {} };
    function render() {
      var rows = state.lines.map(function (x, i) {
        var saved = state.inputLines[i] || {}, selected = saved.serials || [];
        var serials = (x.physicalUnits || []).map(function (u, j) { return '<label class="return-serial"><input type="checkbox" data-return-unit-line="' + i + '" value="' + j + '"' + (selected.indexOf(u.serial) >= 0 ? " checked" : "") + '> ' + esc(u.serial) + (u.imeis && u.imeis.length ? ' — IMEI ' + esc(u.imeis.join("، ")) : "") + '</label>'; }).join("");
        return '<tr><td>' + esc(x.productName + (x.variantName ? " — " + x.variantName : "")) + (serials ? '<div class="purchase-tracking">اختر الرقم/الأرقام الأصلية المتاحة' + serials + '</div>' : '') + '</td><td>' + esc(x.quantity) + ' ' + esc(x.unitName) + '</td><td>' + esc(x.availableQuantity) + '</td><td><input class="input" data-return-qty="' + i + '" inputmode="decimal" value="' + esc(saved.quantity || "0") + '" ' + (Number(x.availableQuantity) <= 0 ? "disabled" : "") + '></td></tr>';
      }).join("");
      var resultHtml = state.lastResult ? '<div class="notice">تم تسجيل المرتجع. إجمالي تسوية المورد: ' + esc(state.lastResult.totalFormatted || moneyScaled(state.lastResult.totalScaled)) + ' — لا توجد حركة خزينة فعلية.</div>' + (state.lastResult.lines || []).map(function (x) { return '<p>' + esc(x.productName) + ': سعر الفاتورة ' + esc(x.accountAmountFormatted || moneyScaled(x.accountAmountScaled)) + '، تكلفة FIFO الفعلية ' + esc(x.inventoryValueFormatted || moneyScaled(x.inventoryValueScaled)) + '، الفرق المسجل ' + esc(x.varianceFormatted || moneyScaled(x.varianceScaled)) + '</p>'; }).join("") : "";
      container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-head"><h2>مرتجع شراء</h2></div><div class="card-body form-grid"><div class="field"><label for="returnInvoice">فاتورة الشراء المنشورة</label><select class="select" id="returnInvoice"><option value="">اختر الفاتورة</option>' + options(state.invoices, state.invoiceId, function (x) { return x.invoiceNumber + " — " + x.supplierName + " — " + x.invoiceDate; }) + '</select></div><div class="field"><label for="returnWarehouse">مخزن البضاعة المرتجعة</label><select class="select" id="returnWarehouse"><option value="">اختر المخزن</option>' + options(state.warehouses, state.warehouseId, function (x) { return x.name; }) + '</select></div><div class="field"><label for="returnMode">تسوية المورد</label><select class="select" id="returnMode"><option value="supplier_credit"' + (state.mode === "supplier_credit" ? " selected" : "") + '>رصيد دائن لدى المورد</option><option value="cash_refund"' + (state.mode === "cash_refund" ? " selected" : "") + '>استرداد نقدي مسجل — دون حركة خزينة</option></select></div></div><div class="card-body" id="returnLines">' + (rows ? '<div class="table-wrap"><table class="data-table"><thead><tr><th>الصنف</th><th>الكمية الأصلية</th><th>المتاح للإرجاع</th><th>الكمية المرتجعة</th></tr></thead><tbody>' + rows + '</tbody></table></div>' : '<p class="muted">اختر الفاتورة والمخزن لعرض الكميات القابلة للإرجاع ذات التكلفة المتتبعة.</p>') + '</div><div class="purchase-actions"><span class="muted">تُحسب تسوية المورد بسعر الشراء الأصلي، وتُسجل تكلفة FIFO الفعلية والفرق صراحةً.</span><button class="button primary" data-post-return ' + (state.busy || !state.lines.length ? "disabled" : "") + '>تسجيل المرتجع</button></div><div id="returnResult" class="card-body" aria-live="polite">' + resultHtml + '</div></section>';
      container.querySelector("#returnInvoice").onchange = function (e) { state.invoiceId = Number(e.target.value) || null; state.key = null; state.inputLines = {}; loadLines(); };
      container.querySelector("#returnWarehouse").onchange = function (e) { state.warehouseId = Number(e.target.value) || null; state.key = null; state.inputLines = {}; loadLines(); };
      container.querySelector("#returnMode").onchange = function (e) { state.mode = e.target.value; state.key = null; };
      container.querySelector("[data-post-return]").onclick = postReturn;
    }
    async function loadLines() {
      state.lines = [];
      if (state.invoiceId && state.warehouseId) {
        try { state.lines = await AlKhayalApi.request("/api/purchases/" + state.invoiceId + "/returnable-lines?warehouseId=" + state.warehouseId); }
        catch (error) { UI.toast(error.message, "danger"); }
      }
      render();
    }
    async function postReturn() {
      var lines = state.lines.map(function (x, i) { var quantity = container.querySelector('[data-return-qty="' + i + '"]').value, physicalUnits = Array.prototype.map.call(container.querySelectorAll('[data-return-unit-line="' + i + '"]:checked'), function (checkbox) { return x.physicalUnits[Number(checkbox.value)]; }); state.inputLines[i] = { quantity: quantity, serials: physicalUnits.map(function (u) { return u.serial; }) }; return { purchaseInvoiceLineId: x.purchaseInvoiceLineId, quantity: quantity, physicalUnits: physicalUnits }; }).filter(function (x) { return Number(x.quantity) > 0; });
      if (!lines.length) { UI.toast("أدخل كمية مرتجع لبند واحد على الأقل.", "danger"); return; }
      state.key = state.key || ("purchase-return-" + state.invoiceId + "-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10));
      state.busy = true; render();
      try {
        var result = await AlKhayalApi.request("/api/purchases/" + state.invoiceId + "/returns", { method: "POST", body: JSON.stringify({ warehouseId: state.warehouseId, returnMode: state.mode, operationKey: state.key, lines: lines }) });
        state.key = null;
        state.lastResult = result;
        state.inputLines = {};
        await loadLines();
      } catch (error) { state.busy = false; render(); UI.toast(error.message || "تعذر تسجيل المرتجع.", "danger"); }
      state.busy = false;
    }
    Promise.all([AlKhayalApi.request("/api/purchases"), AlKhayalApi.request("/api/inventory/warehouses")]).then(function (data) { state.invoices = (data[0] || []).filter(function (x) { return x.status === "posted"; }); state.warehouses = (data[1] || []).filter(function (x) { return x.isActive; }); render(); }).catch(function (error) { container.innerHTML = UI.pageHeader(screen) + '<div class="empty-state">' + esc(error.message) + '</div>'; });
  }
  global.AlKhayalRenderers["purchase-management"] = renderPurchase;
  global.AlKhayalRenderers["purchase-list"] = renderPurchaseList;
  global.AlKhayalRenderers["purchase-returns"] = renderPurchaseReturns;
}(window));
