(function (global) {
  "use strict";
  var Api = global.AlKhayalApi, UI = global.AlKhayalUI, U = global.AlKhayalUtils;
  var inFlight = Object.create(null), pending = Object.create(null);
  function key() { var bytes = new Uint8Array(16); global.crypto.getRandomValues(bytes); return Array.from(bytes).map(function (n) { return n.toString(16).padStart(2, "0"); }).join(""); }
  function active(context) { return !context.isActive || context.isActive(); }
  function esc(x) { return U.escape(x == null ? "" : String(x)); }
  // One shared transport for all document screens. No financial mutation or browser print engine.
  function submit(type, id, automatic, button) {
    var identity = type + ":" + id + ":" + (automatic ? "auto" : "manual");
    if (inFlight[identity]) return inFlight[identity];
    if (button) { button.disabled = true; button.setAttribute("aria-busy", "true"); }
    var operationKey = pending[identity] || (pending[identity] = key());
    inFlight[identity] = Api.request("/api/printing/jobs", { method: "POST", body: JSON.stringify({ documentType: type, documentId: id, operationKey: operationKey, automatic: !!automatic, reprint: !automatic }) }).then(function (job) {
      delete pending[identity];
      if (job.status !== "disabled") UI.toast(job.message || "راجع سجل الطباعة.", job.status === "submitted_unknown" ? "success" : "warning");
      return job;
    }).catch(function (e) {
      // Retain key on uncertain network outcome: retry asks for the original job instead of printing twice.
      if (e.status && e.status < 500) delete pending[identity];
      UI.toast((automatic ? "تم حفظ البيع؛ " : "") + (e.message || "تعذر تأكيد الطباعة. راجع سجل المهام."), "warning");
      return null;
    }).finally(function () { delete inFlight[identity]; if (button) { button.disabled = false; button.removeAttribute("aria-busy"); } });
    return inFlight[identity];
  }
  function mount(host, type, id) {
    var button = global.document.createElement("button"); button.type = "button"; button.className = "button"; button.textContent = "طباعة / إعادة طباعة";
    button.onclick = function () { submit(type, id, false, button); }; host.appendChild(button); return button;
  }
  function supplierReceipts(host, supplierId) {
    var button = global.document.createElement('button'); button.type = 'button'; button.className = 'button'; button.textContent = 'طباعة سند دفع محفوظ'; host.appendChild(button);
    button.onclick = function () {
      button.disabled = true;
      Api.request('/api/suppliers/' + supplierId + '/ledger').then(function (rows) {
        if (!button.isConnected) return;
        var payments = rows.filter(function (p) { return p.entryType === 'payment' || p.entryType === 'legacy_payment'; });
        UI.modal({ title: 'سندات دفع المورد المحفوظة', body: payments.length ? '<label class="field"><span>اختر سند الدفع</span><select class="select" data-payment-print-choice>' + payments.map(function (p) { return '<option value="' + p.id + '">' + esc(p.reference + ' — ' + p.occurredOn) + '</option>'; }).join('') + '</select></label>' : '<p>لا توجد سندات دفع محفوظة لهذا المورد.</p>', actions: (payments.length ? '<button class="button primary" data-print-payment>طباعة / إعادة طباعة</button>' : '') + '<button class="button" data-modal-close>إغلاق</button>', onOpen: function (modal) { var print = modal.querySelector('[data-print-payment]'); if (print) print.onclick = function () { submit('supplier-payment', Number(modal.querySelector('[data-payment-print-choice]').value), false, print); }; } });
      }).catch(function (e) { UI.toast(e.message, 'warning'); }).finally(function () { button.disabled = false; });
    };
  }
  var types = ["sale", "sales-return", "purchase", "purchase-return", "customer-payment", "supplier-payment", "shift-summary", "shift-closing"];
  var titles = ["فاتورة بيع", "مرتجع بيع", "فاتورة شراء", "مرتجع شراء", "سند قبض عميل", "سند دفع مورد", "ملخص وردية", "إغلاق وردية"];
  var sectionNames = { company: "اسم الشركة", logo: "الشعار", address: "العنوان", phone: "الهاتف", taxInfo: "الرقم الضريبي", header: "رأس مخصص", title: "عنوان المستند", reference: "رقم المستند", date: "التاريخ والوقت", cashier: "المستخدم", party: "العميل / المورد", items: "بنود المستند", variant: "خصائص الصنف والوحدة", unitPrice: "سعر الوحدة", discount: "خصم البند", tax: "ضريبة البند", quantity: "الكمية", lineTotal: "قيمة البند", subtotal: "قبل الخصم", discountTotal: "إجمالي الخصم", taxTotal: "إجمالي الضريبة", grandTotal: "الإجمالي", paid: "المدفوع / المعدود", remaining: "المتبقي / الفرق", footer: "رسالة الشكر", policy: "سياسة الإرجاع" };
  var statuses = { accepted: "مقبول — الإرسال غير مؤكد", submitted_unknown: "أُرسل إلى Windows — خروج الورق غير مؤكد", failed: "فشل", uncertain: "النتيجة غير مؤكدة" };
  function option(value, label, selected) { return '<option value="' + esc(value) + '"' + (value === selected ? ' selected' : '') + '>' + esc(label) + '</option>'; }
  function field(label, keyName, value, min, max, step) { return '<label class="field"><span>' + label + '</span><input class="input" type="number" data-design="' + keyName + '" value="' + esc(value) + '" min="' + min + '" max="' + max + '" step="' + (step || '0.1') + '"></label>'; }
  function settings(screen, container, context) {
    var state = context.state.printing || (context.state.printing = {}), printers = [], defaults, saving = false, sequence = 0;
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body" data-printing-host><p>جارٍ تحميل إعدادات الطباعة…</p></div></section>';
    var host = container.querySelector('[data-printing-host]');
    function selected() { return state.settings.templates.find(function (t) { return t.id === state.selected; }) || state.settings.templates[0]; }
    function report(e) { if (active(context)) UI.toast(e.message || "تعذر تحميل إعدادات الطباعة.", "danger"); }
    function draw() {
      if (!active(context)) return;
      var s = state.settings;
      host.innerHTML = '<p>طباعة مباشرة بواسطة C# إلى طابعة Windows المختارة. تغيير الإعدادات يتطلب الحفظ. اختبار الطباعة يرسل ورقًا فعليًا.</p>' +
        '<p class="field-error" role="alert">' + esc(state.printerError || '') + '</p><button class="button" data-refresh-printers>تحديث قائمة الطابعات</button>' +
        '<label class="field"><span><input type="checkbox" data-auto' + (s.autoPrintSale ? ' checked' : '') + '> طباعة الفاتورة تلقائيًا بعد البيع</span></label>' +
        '<div class="table-wrap"><table class="data-table"><thead><tr><th>المستند</th><th>الطابعة</th><th>القالب الافتراضي</th><th>النسخ</th><th>اختبار</th></tr></thead><tbody>' + s.profiles.map(function (p, i) {
          var names = printers.map(function (x) { return x.name; }); if (p.printerName && names.indexOf(p.printerName) < 0) names.push(p.printerName);
          return '<tr><td>' + titles[types.indexOf(p.documentType)] + '</td><td><select class="select" data-profile="' + i + '" data-prop="printerName" aria-label="طابعة ' + esc(p.documentType) + '">' + option('', 'لم يتم الاختيار', p.printerName || '') + names.map(function (n) { return option(n, n + (printers.some(function (x) { return x.name === n; }) ? '' : ' — غير موجودة'), p.printerName); }).join('') + '</select></td><td><select class="select" data-profile="' + i + '" data-prop="templateId" aria-label="قالب المستند">' + s.templates.map(function (t) { return option(t.id, t.name, p.templateId); }).join('') + '</select></td><td><input class="input" type="number" min="1" max="10" value="' + p.copies + '" data-profile="' + i + '" data-prop="copies" aria-label="عدد النسخ"></td><td><button class="button" data-test="' + p.documentType + '">اختبار الإعداد المحفوظ</button></td></tr>';
        }).join('') + '</tbody></table></div>' +
        '<details><summary>مقاسات التعريفات المتاحة</summary>' + printers.map(function (p) { return '<p><strong>' + esc(p.name) + '</strong>: ' + esc(p.capabilityNote) + '</p><p>' + p.papers.map(function (paper) { return esc(paper.name + ' (' + Number(paper.widthMm).toFixed(1) + ' × ' + Number(paper.heightMm).toFixed(1) + ' مم)'); }).join('، ') + '</p>'; }).join('') + '</details>' +
        '<div class="printing-actions"><button class="button primary" data-save-printing>حفظ إعدادات الطباعة والقوالب</button><button class="button" data-reload-printing>إعادة تحميل المحفوظ</button></div>' +
        '<h2>مصمم القوالب</h2><div class="printing-actions"><label>القالب <select class="select" data-template>' + s.templates.map(function (t) { return option(t.id, t.name, selected().id); }).join('') + '</select></label><button class="button" data-new-template>إنشاء قالب</button><button class="button" data-copy-template>نسخ القالب</button><button class="button" data-delete-template>حذف قالب غير مستخدم</button><button class="button" data-restore-template>استعادة التصميم الافتراضي</button></div>' +
        '<div class="printing-designer"><div data-editor></div><aside class="printing-preview"><button class="button" data-preview-template>تحديث المعاينة</button><p>الصفحة الأولى ببيانات اختبار من محرّك C#؛ لا ترسل طباعة.</p><img alt="معاينة قالب الطباعة" data-preview-image hidden></aside></div>' +
        '<details><summary>آخر 100 مهمة طباعة</summary><button class="button" data-load-jobs>تحديث السجل</button><div data-print-jobs></div></details>';
      editor(); wire();
      if(global.AlKhayalLabels)global.AlKhayalLabels.mountSettings(host,context,printers);
    }
    function editor() {
      ++sequence;
      var previousPreview = host.querySelector('[data-preview-image]'); if (previousPreview) previousPreview.hidden = true;
      var t = selected(), p = t.paper, target = host.querySelector('[data-editor]');
      target.innerHTML = '<div class="form-grid"><label class="field"><span>اسم القالب</span><input class="input" data-design="name" value="' + esc(t.name) + '" maxlength="100"></label>' +
        '<label class="field"><span>مقاس الورق</span><select class="select" data-design="paper.preset">' + ['58mm','80mm','A4','A5','custom'].map(function (v) { return option(v, v === 'custom' ? 'مخصص' : v, p.preset); }).join('') + '</select></label>' +
        field('العرض مم', 'paper.widthMm', p.widthMm, 30, 420) + field('الارتفاع / أقصى طول صفحة مم', 'paper.heightMm', p.heightMm, 30, 3000) +
        field('هامش يمين مم', 'paper.marginRightMm', p.marginRightMm, 0, 100) + field('هامش يسار مم', 'paper.marginLeftMm', p.marginLeftMm, 0, 100) + field('هامش أعلى مم', 'paper.marginTopMm', p.marginTopMm, 0, 100) + field('هامش أسفل مم', 'paper.marginBottomMm', p.marginBottomMm, 0, 100) + field('مقياس الخط', 'paper.scale', p.scale, .5, 2, .05) +
        '<label class="field"><span><input type="checkbox" data-design="paper.continuous"' + (p.continuous ? ' checked' : '') + '> طول إيصال متغير ضمن حدود التعريف</span></label><label class="field"><span><input type="checkbox" data-design="paper.landscape"' + (p.landscape ? ' checked' : '') + '> اتجاه أفقي</span></label>' +
        field('عرض اسم الصنف %', 'nameColumnPercent', t.nameColumnPercent, 35, 70) + field('عرض الكمية %', 'quantityColumnPercent', t.quantityColumnPercent, 10, 25) +
        field('عرض الشعار مم', 'logoWidthMm', t.logoWidthMm, 1, 100) + field('ارتفاع الشعار مم', 'logoHeightMm', t.logoHeightMm, 1, 100) +
        '<label class="field"><span>شعار PNG حتى 130 كيلوبايت / 1200×1200</span><input type="file" accept="image/png" data-logo></label><button class="button" data-clear-logo>إزالة الشعار</button>' +
        ['headerText','footerText','returnPolicy'].map(function (k, i) { return '<label class="field"><span>' + ['نص رأس مخصص','رسالة الشكر','سياسة الإرجاع'][i] + '</span><textarea class="input" maxlength="1000" data-design="' + k + '">' + esc(t[k]) + '</textarea></label>'; }).join('') +
        '<label class="field"><span><input type="checkbox" data-design="dividers"' + (t.dividers ? ' checked' : '') + '> فواصل البنود</span></label></div>' +
        '<p>غيّر ترتيب الأقسام بالأسهم. سعر الوحدة والخصم والضريبة والخصائص تظهر أسفل اسم الصنف عند تفعيلها.</p><div class="table-wrap"><table class="data-table"><thead><tr><th>القسم</th><th>ظاهر</th><th>عريض</th><th>حجم الخط نقطة</th><th>مسافة مم</th><th>محاذاة</th><th>ترتيب</th></tr></thead><tbody>' + t.sections.map(function (s, i) {
          return '<tr><td>' + sectionNames[s.key] + '</td><td><input aria-label="إظهار ' + sectionNames[s.key] + '" type="checkbox" data-section="' + i + '" data-prop="visible"' + (s.visible ? ' checked' : '') + '></td><td><input aria-label="خط عريض" type="checkbox" data-section="' + i + '" data-prop="bold"' + (s.bold ? ' checked' : '') + '></td><td><input class="input" aria-label="حجم الخط" type="number" min="6" max="24" value="' + s.fontSize + '" data-section="' + i + '" data-prop="fontSize"></td><td><input class="input" aria-label="المسافة" type="number" min="0" max="15" step="0.1" value="' + s.spacingMm + '" data-section="' + i + '" data-prop="spacingMm"></td><td><select class="select" aria-label="المحاذاة" data-section="' + i + '" data-prop="alignment">' + ['right','center','left'].map(function (v, j) { return option(v, ['يمين','وسط','يسار'][j], s.alignment); }).join('') + '</select></td><td><button class="button" data-move="' + i + '" data-direction="-1" aria-label="أعلى">↑</button><button class="button" data-move="' + i + '" data-direction="1" aria-label="أسفل">↓</button></td></tr>';
        }).join('') + '</tbody></table></div>';
    }
    async function save(button) {
      if (saving) return; saving = true; button.disabled = true;
      var submitted = JSON.stringify(state.settings);
      try { var result = await Api.request('/api/printing/settings', { method: 'PUT', body: submitted }); var edited = JSON.stringify(state.settings) !== submitted; state.settings.version = result.version; if (active(context)) UI.toast(edited ? 'تم حفظ الإعدادات السابقة؛ توجد تعديلات جديدة غير محفوظة.' : 'تم حفظ إعدادات الطباعة والقوالب.', edited ? 'warning' : 'success'); }
      catch (e) { report(e); } finally { saving = false; if (active(context)) button.disabled = false; }
    }
    function wire() {
      host.oninput = function (event) {
        var n = event.target, value = n.type === 'checkbox' ? n.checked : n.type === 'number' ? Number(n.value) : n.value;
        if (n.hasAttribute('data-design') || n.hasAttribute('data-section')) { ++sequence; host.querySelector('[data-preview-image]').hidden = true; }
        if (n.hasAttribute('data-auto')) state.settings.autoPrintSale = n.checked;
        if (n.hasAttribute('data-profile')) state.settings.profiles[Number(n.getAttribute('data-profile'))][n.getAttribute('data-prop')] = value;
        if (n.hasAttribute('data-section')) selected().sections[Number(n.getAttribute('data-section'))][n.getAttribute('data-prop')] = value;
        if (n.hasAttribute('data-design')) {
          var path = n.getAttribute('data-design').split('.'), obj = path.length === 2 ? selected()[path[0]] : selected(); obj[path[path.length - 1]] = value;
        }
      };
      host.onchange = function (event) {
        var n = event.target;
        if (n.hasAttribute('data-template')) { state.selected = n.value; ++sequence; editor(); var img = host.querySelector('[data-preview-image]'); img.hidden = true; }
        if (n.getAttribute('data-design') === 'paper.preset') {
          var p = selected().paper; p.preset = n.value;
          if (p.preset === '58mm' || p.preset === '80mm') p.widthMm = p.preset === '58mm' ? 58 : 80;
          if (p.preset === 'A4' || p.preset === 'A5') { p.widthMm = p.preset === 'A4' ? 210 : 148; p.heightMm = p.preset === 'A4' ? 297 : 210; p.continuous = false; }
          editor();
        }
        if (n.hasAttribute('data-logo') && n.files && n.files[0]) {
          var file = n.files[0], target = selected(); if (file.type !== 'image/png' || file.size > 130000) { report(new Error('اختر PNG حتى 130 كيلوبايت.')); return; }
          var reader = new FileReader(); reader.onload = function () { target.logoPngBase64 = String(reader.result).split(',')[1]; if (active(context) && target === selected()) { ++sequence; host.querySelector('[data-preview-image]').hidden = true; } }; reader.onerror = function () { report(new Error('تعذر قراءة الشعار.')); }; reader.readAsDataURL(file);
        }
      };
      host.onclick = async function (event) {
        var b = event.target.closest('button'); if (!b || saving) return;
        if (b.hasAttribute('data-save-printing')) { await save(b); return; }
        if (b.hasAttribute('data-reload-printing')) { delete state.settings; await load(); return; }
        if (b.hasAttribute('data-refresh-printers')) { b.disabled = true; await loadPrinters(); if (active(context)) draw(); return; }
        if (b.hasAttribute('data-test')) {
          b.disabled = true; try { var job = await Api.request('/api/printing/test/' + b.getAttribute('data-test'), { method: 'POST', body: JSON.stringify({operationKey:key()}) }); if (active(context)) UI.toast(job.message, 'success'); } catch (e) { report(e); } finally { b.disabled = false; } return;
        }
        if (b.hasAttribute('data-preview-template')) {
          var current = ++sequence; b.disabled = true;
          try { var preview = await Api.request('/api/printing/preview', { method: 'POST', body: JSON.stringify(selected()) }); if (active(context) && current === sequence) { var image = host.querySelector('[data-preview-image]'); image.src = 'data:image/png;base64,' + preview.pngBase64; image.hidden = false; } } catch (e) { report(e); } finally { b.disabled = false; } return;
        }
        if (b.hasAttribute('data-load-jobs')) {
          b.disabled = true; try { var jobs = await Api.request('/api/printing/jobs'); if (active(context)) host.querySelector('[data-print-jobs]').innerHTML = '<div class="table-wrap"><table class="data-table"><thead><tr><th>المستند</th><th>الطابعة / القالب</th><th>الوقت</th><th>الحالة</th><th>التفاصيل</th></tr></thead><tbody>' + jobs.map(function (j) { return '<tr><td>' + esc(j.reference) + (j.reprint ? ' (إعادة طباعة)' : '') + '</td><td>' + esc(j.printerName) + ' / ' + esc(j.templateId) + '</td><td>' + esc(new Date(j.createdUtc).toLocaleString('ar-EG')) + '</td><td>' + esc(statuses[j.status]) + '</td><td>' + esc(j.message) + (j.batchSummaryJson ? '<details><summary>بنود الدفعة</summary><code>' + esc(j.batchSummaryJson) + '</code></details>' : '') + (j.status === 'accepted' ? '<button class="button" data-review-job="' + j.id + '">تأكيد مراجعة قائمة Windows (بعد 10 دقائق)</button>' : '') + '</td></tr>'; }).join('') + '</tbody></table></div>'; } catch (e) { report(e); } finally { b.disabled = false; } return;
        }
        if(b.hasAttribute('data-review-job')){b.disabled=true;try{var review=await Api.request('/api/printing/jobs/'+b.getAttribute('data-review-job')+'/review',{method:'POST',body:'{}'});if(active(context))UI.toast(review.message,'warning');}catch(e){report(e);}finally{b.disabled=false;}return;}
        if (b.hasAttribute('data-clear-logo')) { selected().logoPngBase64 = null; editor(); return; }
        if (b.hasAttribute('data-move')) { var from = Number(b.getAttribute('data-move')), to = from + Number(b.getAttribute('data-direction')), sections = selected().sections; if (to >= 0 && to < sections.length) { var temp = sections[from]; sections[from] = sections[to]; sections[to] = temp; editor(); } return; }
        if (b.hasAttribute('data-new-template') || b.hasAttribute('data-copy-template')) {
          if (state.settings.templates.length >= 40) { report(new Error('الحد الأقصى 40 قالبًا.')); return; }
          var template = JSON.parse(JSON.stringify(b.hasAttribute('data-copy-template') ? selected() : defaults.templates[0])); template.id = 'custom-' + key(); template.name = b.hasAttribute('data-copy-template') ? 'نسخة من ' + template.name : 'قالب جديد'; state.settings.templates.push(template); state.selected = template.id; draw(); return;
        }
        if (b.hasAttribute('data-delete-template')) {
          var id = selected().id; if (state.settings.profiles.some(function (p) { return p.templateId === id; }) || state.settings.templates.length === 1) { report(new Error('القالب مستخدم. اختر بديلًا للمستندات قبل حذفه.')); return; }
          state.settings.templates = state.settings.templates.filter(function (t) { return t.id !== id; }); state.selected = state.settings.templates[0].id; draw(); return;
        }
        if (b.hasAttribute('data-restore-template')) { var old = selected(), restored = JSON.parse(JSON.stringify(defaults.templates.find(function (t) { return t.id === old.id; }) || defaults.templates[0])); restored.id = old.id; restored.name = old.name; state.settings.templates[state.settings.templates.indexOf(old)] = restored; draw(); }
      };
    }
    async function loadPrinters() {
      try { printers = await Api.request('/api/printing/printers'); state.printerError = ''; }
      catch (e) { printers = []; state.printerError = e.message || 'تعذر قراءة طابعات Windows. يمكنك تعديل القوالب ثم إعادة تحميل الطابعات.'; }
    }
    async function load() {
      try {
        var result = await Promise.all([state.settings ? Promise.resolve(state.settings) : Api.request('/api/printing/settings'), Api.request('/api/printing/defaults'), loadPrinters()]);
        if (!active(context)) return; state.settings = result[0]; defaults = result[1]; draw();
      } catch (e) { if (active(context)) { host.innerHTML = '<p role="alert">' + esc(e.message) + '</p><button class="button" data-retry>إعادة المحاولة</button>'; host.querySelector('[data-retry]').onclick = load; } }
    }
    load();
  }
  global.AlKhayalPrinting = { submit: submit, mount: mount, supplierReceipts: supplierReceipts };
  global.AlKhayalRenderers['native-printing'] = settings;
}(window));
