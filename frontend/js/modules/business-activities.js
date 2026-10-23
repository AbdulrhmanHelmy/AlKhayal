(function (global) {
  "use strict";
  var U = global.AlKhayalUtils, UI = global.AlKhayalUI, Api = global.AlKhayalApi, Features = global.AlKhayalFeatures;
  var capabilityNames = {
    inventory: "إدارة المخزون", sales: "المبيعات", "barcode-scanning": "مسح الباركود", "expiry-dates": "تواريخ الصلاحية",
    "batch-tracking": "تتبع التشغيلات", "multiple-units": "وحدات متعددة", sizes: "المقاسات", colors: "الألوان",
    "product-variants": "متغيرات المنتج", "barcode-labels": "ملصقات الباركود", "serial-numbers": "الأرقام التسلسلية",
    "imei-tracking": "تتبع IMEI", "warranty-tracking": "تتبع الضمان", "maintenance-management": "إدارة الصيانة", "wholesale-pricing": "أسعار الجملة"
  };
  var hiddenProductCapabilityKeys = ["batch-tracking", "serial-numbers", "imei-tracking", "product-barcode-settings", "barcode-labels", "multiple-units", "warranty-tracking", "weight-based-products", "wholesale-pricing", "product-variants"];
  function capabilityLabel(key) { return capabilityNames[key] || key; }
  function render(screen, container, context) {
    var canEdit = context.user && context.user.role === "administrator", catalog = [], selected = [], primary = "", configured = false, comparison = null, query = "", loading = true, error = "";
    function selectedDefinitions() { return catalog.filter(function (item) { return selected.indexOf(item.key) >= 0; }); }
    function draw() {
      var visible = catalog.filter(function (item) { return U.normalize(item.arabicName + " " + item.description).indexOf(U.normalize(query)) >= 0; });
      var firstRun = !configured ? '<div class="activity-setup-note"><strong>إعداد أول مرة</strong><span>اختر نشاطًا واحدًا على الأقل، وحدد النشاط الرئيسي، ثم راجع التوصيات قبل الحفظ.</span></div>' : "";
      var status = loading ? '<div class="empty-state"><strong>جارٍ تحميل الأنشطة…</strong></div>' : error ? '<div class="empty-state"><strong>تعذر تحميل نوع النشاط</strong><span>' + U.escape(error) + '</span><br><button class="button" data-retry>إعادة المحاولة</button></div>' : "";
      var cards = visible.map(function (item) { var checked = selected.indexOf(item.key) >= 0; return '<label class="activity-card' + (checked ? " selected" : "") + '"><span class="activity-check"><input type="checkbox" value="' + U.escape(item.key) + '"' + (checked ? " checked" : "") + (canEdit ? "" : " disabled") + '><strong>' + U.escape(item.arabicName) + '</strong></span><span>' + U.escape(item.description) + '</span><small>' + item.suggestedCapabilities.length + ' إمكانات مقترحة</small></label>'; }).join("");
      var primaryOptions = selectedDefinitions().map(function (item) { return '<option value="' + U.escape(item.key) + '"' + (item.key === primary ? " selected" : "") + '>' + U.escape(item.arabicName) + '</option>'; }).join("");
      var recs = comparison ? comparison.recommendations.filter(function(item){return hiddenProductCapabilityKeys.indexOf(item.capabilityKey)<0;}).map(function (item) { var configuredFeature = Features.find(item.capabilityKey) || (item.capabilityKey === "maintenance-management" ? Features.find("maintenance-management") : null); return '<li><strong>' + U.escape(capabilityLabel(item.capabilityKey)) + '</strong><span>' + U.escape(item.reason) + '</span>' + (configuredFeature ? '<span class="badge ' + (configuredFeature.isEnabled ? "success" : "warning") + '">' + (configuredFeature.isEnabled ? "مفعلة فعليًا" : "غير مفعلة") + '</span>' : "") + '</li>'; }).join("") || '<li class="muted">لا توجد خصائص إضافية ظاهرة لهذا النشاط.</li>' : '<li class="muted">اختر الأنشطة لمعاينة التوصيات.</li>';
      var changes = comparison ? '<div class="recommendation-delta"><span class="badge success">جديد: ' + comparison.newlyRecommended.length + '</span><span class="badge warning">لم يعد موصى به: ' + comparison.noLongerRecommended.length + '</span><span class="badge info">تخصيصات محفوظة: ' + comparison.administratorOverrides.length + '</span></div>' : "";
      container.innerHTML = UI.pageHeader(screen) + firstRun + (!canEdit ? '<div class="mock-banner"><strong>للعرض فقط</strong><span>يمكن لمسؤول النظام فقط تغيير نوع النشاط.</span></div>' : "") + status + (loading || error ? "" : '<div class="activity-layout"><section class="card"><div class="card-head"><h2>الأنشطة المتاحة</h2><span class="badge info">يمكن اختيار أكثر من نشاط</span></div><div class="card-body"><label class="field activity-search"><span>البحث في الأنشطة</span><input class="input" id="activitySearch" value="' + U.escape(query) + '" placeholder="مثال: ملابس أو صيانة"></label><div class="activity-grid">' + (cards || '<div class="empty-state"><strong>لا توجد نتائج مطابقة</strong></div>') + '</div></div></section><aside class="card"><div class="card-head"><h2>النشاط الرئيسي والتوصيات</h2></div><div class="card-body"><label class="field"><span>النشاط الرئيسي</span><select class="select" id="primaryActivity"' + (canEdit && selected.length ? "" : " disabled") + '><option value="">اختر النشاط الرئيسي</option>' + primaryOptions + '</select></label>' + changes + '<ul class="recommendation-list">' + recs + '</ul><p class="muted">التوصيات إعدادات مبدئية قابلة للتخصيص. لن يؤدي حذف نشاط إلى تعطيل إمكانات أو حذف بيانات أعمال.</p>' + (canEdit ? '<label class="switch apply-recommendations"><input id="applyRecommendations" type="checkbox" checked><span>تطبيق التوصيات الجديدة مع الحفاظ على تخصيصات المسؤول</span></label><div class="page-actions"><button class="button primary" data-save-activities' + (!selected.length || !primary ? " disabled" : "") + '>مراجعة وحفظ</button><button class="button" data-refresh-recommendations>تحديث المعاينة</button></div>' : "") + '</div></aside></div>');
      bind();
    }
    function preview() {
      comparison = null; draw();
      if (!selected.length) return;
      Api.request("/api/business-activities/recommendations?activityKeys=" + encodeURIComponent(selected.join(","))).then(function (value) { comparison = value; if (container.isConnected) draw(); }).catch(function (reason) { UI.toast(reason.message, "danger"); });
    }
    function bind() {
      var retry = container.querySelector("[data-retry]"); if (retry) retry.onclick = load;
      var search = container.querySelector("#activitySearch"); if (search) search.oninput = function () { query = this.value; draw(); var next = container.querySelector("#activitySearch"); if (next) { next.focus(); next.setSelectionRange(query.length, query.length); } };
      container.querySelectorAll('.activity-card input[type="checkbox"]').forEach(function (input) { input.onchange = function () { if (this.checked) selected.push(this.value); else selected = selected.filter(function (key) { return key !== input.value; }); if (selected.indexOf(primary) < 0) primary = selected[0] || ""; preview(); }; });
      var primarySelect = container.querySelector("#primaryActivity"); if (primarySelect) primarySelect.onchange = function () { primary = this.value; draw(); };
      var refresh = container.querySelector("[data-refresh-recommendations]"); if (refresh) refresh.onclick = preview;
      var save = container.querySelector("[data-save-activities]"); if (save) save.onclick = function () {
        var apply = container.querySelector("#applyRecommendations").checked;
        UI.confirm({ title: "تأكيد نوع النشاط التجاري", message: "سيتم حفظ الأنشطة المحددة والنشاط الرئيسي" + (apply ? " وتطبيق التوصيات الجديدة دون تغيير تخصيصات المسؤول." : " دون تطبيق التوصيات.") , confirmText: "حفظ الإعداد" }).then(function (confirmed) {
          if (!confirmed) return;
          Api.request("/api/business-activities/selected", { method: "PUT", body: JSON.stringify({ activityKeys: selected, primaryActivityKey: primary, applyRecommendations: apply }) }).then(function () { configured = true; return Features.load(); }).then(function () { UI.toast("تم حفظ نوع النشاط التجاري في قاعدة البيانات.", "success"); load(); }).catch(function (reason) { UI.toast(reason.message, "danger"); });
        });
      };
    }
    function load() {
      loading = true; error = ""; draw();
      Promise.all([Api.request("/api/business-activities"), Api.request("/api/business-activities/selected")]).then(function (values) {
        catalog = values[0]; selected = values[1].activityKeys.slice(); primary = values[1].primaryActivityKey || selected[0] || ""; configured = values[1].isConfigured; loading = false; draw(); preview();
      }).catch(function (reason) { loading = false; error = reason.message; draw(); });
    }
    load();
  }
  global.AlKhayalRenderers["business-activities"] = render;
}(window));
