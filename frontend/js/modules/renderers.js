(function (global) {
  "use strict";
  var U = global.AlKhayalUtils,
    UI = global.AlKhayalUI,
    S = global.AlKhayalServices,
    C = global.AlKhayalCalculations,
    Api = global.AlKhayalApi;
  function friendlyReference(reference) {
    var value = String(reference || "");
    if (!value || value === "OPEN") return "رصيد افتتاحي";
    var match = /^(CP|SI|SP)-(\d+)$/.exec(value);
    if (!match) return value;
    return (match[1] === "CP" ? "تحصيل رقم " : match[1] === "SI" ? "فاتورة رقم " : "سداد رقم ") + match[2];
  }
  var schemas = {
    customers: {
      columns: [
        { key: "code", label: "الكود", ltr: true },
        { key: "name", label: "اسم العميل" },
        { key: "phone", label: "الهاتف", ltr: true },
        { key: "address", label: "العنوان" },
        { key: "status", label: "الحالة", badge: true },
      ],
      fields: [
        { id: "code", label: "كود العميل", required: true, ltr: true },
        { id: "name", label: "اسم العميل", required: true },
        { id: "phone", label: "رقم الهاتف", required: true, ltr: true },
        { id: "address", label: "العنوان" },
        {
          id: "subscription",
          label: "الاشتراك",
          type: "select",
          options: ["أساسي", "فضي", "ذهبي"],
        },
        {
          id: "status",
          label: "الحالة",
          type: "select",
          options: ["نشط", "متوقف"],
        },
      ],
    },
    suppliers: {
      columns: [
        { key: "code", label: "الكود", ltr: true },
        { key: "name", label: "المورد" },
        { key: "contact", label: "مسؤول التواصل" },
        { key: "phone", label: "الهاتف", ltr: true },
        { key: "payable", label: "المستحق", money: true },
        { key: "paid", label: "المدفوع", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
      fields: [
        { id: "code", label: "كود المورد", required: true, ltr: true },
        { id: "name", label: "اسم المورد", required: true },
        { id: "contact", label: "مسؤول التواصل" },
        { id: "phone", label: "الهاتف", required: true, ltr: true },
        { id: "address", label: "العنوان" },
        {
          id: "status",
          label: "الحالة",
          type: "select",
          options: ["نشط", "موقوف"],
        },
      ],
    },
    products: {
      columns: [
        { key: "sku", label: "SKU", ltr: true },
        { key: "barcode", label: "الباركود", ltr: true },
        { key: "name", label: "الصنف" },
        { key: "category", label: "التصنيف" },
        { key: "salePrice", label: "سعر البيع", money: true },
        { key: "stock", label: "الرصيد", number: true },
        { key: "minimum", label: "حد الطلب", number: true },
        { key: "warehouse", label: "المخزن" },
      ],
      fields: [
        { id: "sku", label: "كود الصنف", required: true, ltr: true },
        { id: "barcode", label: "الباركود", required: true, ltr: true },
        { id: "name", label: "اسم الصنف", required: true },
        { id: "category", label: "التصنيف", required: true },
        {
          id: "unit",
          label: "الوحدة",
          type: "select",
          options: ["قطعة", "علبة", "كيس", "زجاجة", "عبوة", "باكو"],
        },
        {
          id: "salePrice",
          label: "سعر البيع",
          type: "number",
          required: true,
          min: 0,
          step: "0.01",
        },
        {
          id: "purchasePrice",
          label: "سعر الشراء",
          type: "number",
          required: true,
          min: 0,
          step: "0.01",
        },
        { id: "stock", label: "الرصيد الافتتاحي", type: "number", min: 0 },
        { id: "minimum", label: "حد الطلب", type: "number", min: 0 },
        {
          id: "warehouse",
          label: "المخزن",
          type: "select",
          options: ["المخزن الرئيسي", "فرع مدينة نصر", "مخزن المرتجعات"],
        },
        { id: "expiry", label: "تاريخ الصلاحية", type: "date" },
      ],
    },
    sales: {
      columns: [
        { key: "invoice", label: "رقم الفاتورة", ltr: true },
        { key: "date", label: "التاريخ", dateTime: true },
        { key: "customer", label: "العميل" },
        { key: "items", label: "الأصناف", number: true },
        { key: "total", label: "الإجمالي", money: true },
        { key: "payment", label: "السداد" },
        { key: "status", label: "الحالة", badge: true },
      ],
    },
    purchases: {
      columns: [
        { key: "invoice", label: "الفاتورة", ltr: true },
        { key: "date", label: "التاريخ", date: true },
        { key: "supplier", label: "المورد" },
        { key: "items", label: "الأصناف", number: true },
        { key: "total", label: "الإجمالي", money: true },
        { key: "paid", label: "المدفوع", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
    },
    expenses: {
      columns: [
        { key: "voucher", label: "السند", ltr: true },
        { key: "date", label: "التاريخ", dateTime: true },
        { key: "category", label: "النوع" },
        { key: "description", label: "البيان" },
        { key: "account", label: "الحساب" },
        { key: "amount", label: "القيمة", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
      fields: [
        {
          id: "category",
          label: "نوع المصروف",
          type: "select",
          options: ["نقل وشحن", "مرافق", "صيانة", "ضيافة", "إيجار", "أخرى"],
          required: true,
        },
        { id: "description", label: "البيان", required: true, span: true },
        {
          id: "amount",
          label: "القيمة",
          type: "number",
          min: 0,
          step: "0.01",
          required: true,
        },
        {
          id: "account",
          label: "الحساب",
          type: "select",
          options: ["الخزنة الرئيسية", "بنك مصر — حساب جاري"],
          required: true,
        },
        { id: "date", label: "التاريخ", type: "date", required: true },
      ],
    },
    accounts: {
      columns: [
        { key: "code", label: "الكود", ltr: true },
        { key: "name", label: "الحساب" },
        { key: "type", label: "النوع" },
        { key: "branch", label: "الفرع" },
        { key: "balance", label: "الرصيد", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
      fields: [
        { id: "code", label: "كود الخزنة", required: true, ltr: true },
        { id: "name", label: "اسم الخزنة", required: true },
        {
          id: "type",
          label: "النوع",
          type: "select",
          options: ["خزنة", "بنك"],
        },
        {
          id: "branch",
          label: "الفرع",
          type: "select",
          options: ["الفرع الرئيسي", "مدينة نصر", "الجيزة"],
        },
        { id: "balance", label: "الرصيد الافتتاحي", type: "number", min: 0, step: "0.01" },
        {
          id: "status",
          label: "الحالة",
          type: "select",
          options: ["نشط", "موقوف"],
        },
      ],
    },
    transactions: {
      columns: [
        { key: "reference", label: "المرجع", ltr: true },
        { key: "date", label: "التاريخ", dateTime: true },
        { key: "description", label: "البيان" },
        { key: "account", label: "الحساب" },
        { key: "direction", label: "الاتجاه", badge: true },
        { key: "amount", label: "القيمة", money: true },
      ],
    },
    returns: {
      columns: [
        { key: "reference", label: "المرجع", ltr: true },
        { key: "date", label: "التاريخ", date: true },
        { key: "original", label: "الفاتورة الأصلية", ltr: true },
        { key: "party", label: "الطرف" },
        { key: "type", label: "النوع" },
        { key: "amount", label: "القيمة", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
    },
    warehouses: {
      columns: [
        { key: "code", label: "الكود", ltr: true },
        { key: "name", label: "المخزن" },
        { key: "branch", label: "الفرع" },
        { key: "manager", label: "المسؤول" },
        { key: "products", label: "عدد الأصناف", number: true },
        { key: "value", label: "قيمة المخزون", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
      fields: [
        { id: "code", label: "كود المخزن", required: true, ltr: true },
        { id: "name", label: "اسم المخزن", required: true },
        {
          id: "branch",
          label: "الفرع",
          type: "select",
          options: ["الفرع الرئيسي", "مدينة نصر", "الجيزة"],
        },
        { id: "manager", label: "المسؤول", required: true },
        {
          id: "status",
          label: "الحالة",
          type: "select",
          options: ["نشط", "مراجعة"],
        },
      ],
    },
    movements: {
      columns: [
        { key: "reference", label: "المرجع", ltr: true },
        { key: "date", label: "التاريخ", dateTime: true },
        { key: "product", label: "الصنف" },
        { key: "source", label: "المصدر" },
        { key: "destination", label: "الوجهة" },
        { key: "quantity", label: "الكمية", number: true },
        { key: "type", label: "نوع الحركة", badge: true },
      ],
    },
    employees: {
      columns: [
        { key: "code", label: "الكود", ltr: true },
        { key: "name", label: "الموظف" },
        { key: "role", label: "الوظيفة" },
        { key: "phone", label: "الهاتف", ltr: true },
        { key: "shift", label: "الوردية" },
        { key: "attendance", label: "الحضور", badge: true },
        { key: "status", label: "الحالة", badge: true },
      ],
      fields: [
        { id: "code", label: "كود الموظف", required: true, ltr: true },
        { id: "name", label: "اسم الموظف", required: true },
        { id: "role", label: "الوظيفة", required: true },
        { id: "phone", label: "الهاتف", ltr: true },
        {
          id: "shift",
          label: "الوردية",
          type: "select",
          options: ["صباحية", "مسائية", "مرنة"],
        },
        {
          id: "status",
          label: "الحالة",
          type: "select",
          options: ["نشط", "موقوف"],
        },
      ],
    },
    shifts: {
      columns: [
        { key: "code", label: "الكود", ltr: true },
        { key: "name", label: "الوردية" },
        { key: "start", label: "البداية", ltr: true },
        { key: "end", label: "النهاية", ltr: true },
        { key: "employees", label: "الموظفون", number: true },
        { key: "sales", label: "المبيعات", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
      fields: [
        { id: "code", label: "كود الوردية", required: true, ltr: true },
        { id: "name", label: "اسم الوردية", required: true },
        { id: "start", label: "وقت البداية", type: "time", required: true },
        { id: "end", label: "وقت النهاية", type: "time", required: true },
        {
          id: "status",
          label: "الحالة",
          type: "select",
          options: ["مجدولة", "مفتوحة", "مغلقة"],
        },
      ],
    },
    installments: {
      columns: [
        { key: "contract", label: "العقد", ltr: true },
        { key: "customer", label: "العميل" },
        { key: "amount", label: "القيمة", money: true },
        { key: "downPayment", label: "المقدم", money: true },
        { key: "installment", label: "القسط", money: true },
        { key: "nextDue", label: "الاستحقاق", date: true },
        { key: "status", label: "الحالة", badge: true },
      ],
    },
    maintenance: {
      columns: [
        { key: "ticket", label: "التذكرة", ltr: true },
        { key: "customer", label: "العميل" },
        { key: "device", label: "الجهاز" },
        { key: "model", label: "الموديل", ltr: true },
        { key: "technician", label: "الفني" },
        { key: "expected", label: "التسليم", date: true },
        { key: "status", label: "الحالة", badge: true },
      ],
    },
    branches: {
      columns: [
        { key: "code", label: "الكود", ltr: true },
        { key: "name", label: "الفرع" },
        { key: "city", label: "المدينة" },
        { key: "manager", label: "المدير" },
        { key: "phone", label: "الهاتف", ltr: true },
        { key: "sales", label: "مبيعات اليوم", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
    },
    users: {
      columns: [
        { key: "username", label: "اسم المستخدم", ltr: true },
        { key: "displayName", label: "الاسم المعروض" },
        { key: "role", label: "الدور" },
        { key: "lastLogin", label: "آخر دخول", dateTime: true },
        { key: "status", label: "الحالة", badge: true },
      ],
    },
  };
  function schemaFor(entity) {
    return schemas[entity] || { columns: [] };
  }
  var persistedEntities = {
    customers: {
      path: "/api/customers",
      singular: "العميل",
      plural: "العملاء",
    },
    suppliers: {
      path: "/api/suppliers",
      singular: "المورد",
      plural: "الموردين",
    },
  };
  function persistedInfo(entity) {
    return persistedEntities[entity] || null;
  }
  function valueHtml(row, column) {
    var value = row[column.key];
    if (column.money) return '<span class="ltr">' + U.money(value) + "</span>";
    if (column.number)
      return '<span class="ltr">' + U.number(value) + "</span>";
    if (column.dateTime) return U.dateTime(value);
    if (column.date) return value === "—" ? "—" : U.date(value);
    if (column.badge) return UI.badge(value);
    return (
      '<span class="' +
      (column.ltr ? "ltr" : "") +
      '">' +
      U.escape(value) +
      "</span>"
    );
  }
  function rowsTable(columns, rows, actions) {
    if (!rows.length)
      return '<div class="empty-state"><strong>لا توجد نتائج مطابقة</strong><span>غيّر البحث أو أعد ضبط الفلاتر.</span></div>';
    return (
      '<div class="table-wrap"><table class="data-table"><thead><tr>' +
      columns
        .map(function (column) {
          return (
            '<th class="sortable" data-sort="' +
            U.escape(column.key) +
            '">' +
            U.escape(column.label) +
            "</th>"
          );
        })
        .join("") +
      (actions ? "<th>الإجراءات</th>" : "") +
      "</tr></thead><tbody>" +
      rows
        .map(function (row) {
          return (
            '<tr data-row-id="' +
            row.id +
            '">' +
            columns
              .map(function (column) {
                return "<td>" + valueHtml(row, column) + "</td>";
              })
              .join("") +
            (actions
              ? '<td><div class="row-actions"><button class="table-action" data-row-action="view">عرض</button><button class="table-action" data-row-action="edit">تعديل</button><button class="table-action danger-text" data-row-action="delete">حذف</button></div></td>'
              : "") +
            "</tr>"
          );
        })
        .join("") +
      "</tbody></table></div>"
    );
  }
  function openRecordModal(entity, record, mode, afterSave) {
    var schema = schemaFor(entity),
      fields = schema.fields || [];
    if (!fields.length || mode === "view") {
      UI.modal({
        title: "تفاصيل السجل",
        body:
          '<div class="form-grid">' +
          schema.columns
            .map(function (column) {
              return (
                '<div class="field"><label>' +
                U.escape(column.label) +
                "</label><div>" +
                valueHtml(record, column) +
                "</div></div>"
              );
            })
            .join("") +
          "</div>",
      });
      return;
    }
    var isEdit = mode === "edit",
      draft = record || {},
      info = persistedInfo(entity),
      persistent = !!info;
    if (entity === "customers" && !isEdit)
      fields = fields.filter(function (field) { return field.id !== "code"; });
    if (entity === "suppliers" && isEdit)
      fields = fields.filter(function (field) {
        return field.id !== "payable";
      });
    var layer = UI.modal({
      title: isEdit
        ? persistent
          ? "تعديل بيانات " + info.singular
          : "تعديل سجل تجريبي"
        : persistent
          ? "إضافة " + info.singular.replace(/^ال/, "")
          : "إضافة سجل تجريبي",
      body:
        (entity === "customers" && !isEdit ? '<p class="muted span-2">يُنشأ كود العميل تلقائيًا عند الحفظ.</p>' : '') +
        '<form id="recordForm" class="form-grid" novalidate>' +
        fields
          .map(function (field) {
            return UI.field(field, draft[field.id]);
          })
          .join("") +
        "</form>",
      actions:
        '<button class="button primary" type="button" data-save-record>' +
        (persistent ? "حفظ" : "حفظ مؤقت") +
        '</button><button class="button" type="button" data-modal-close>إلغاء</button>',
      onOpen: function (element) {
        element.querySelector("input,select,textarea").focus();
      },
    });
    layer.querySelector("[data-save-record]").onclick = async function () {
      var form = layer.querySelector("#recordForm"),
        result = UI.collectForm(form, fields);
      if (!result.valid) return;
      var button = layer.querySelector("[data-save-record]");
      if(button.disabled)return;
      button.disabled = true;
      button.textContent = "جارٍ الحفظ...";
      try {
        var saved;
        if (persistent) {
          saved = await AlKhayalApi.request(
            isEdit
              ? info.path + "/" + encodeURIComponent(record.id)
              : info.path,
            {
              method: isEdit ? "PUT" : "POST",
              body: JSON.stringify(result.data),
            },
          );
        } else if (isEdit) S.updateMock(entity, record.id, result.data);
        else S.createMock(entity, result.data);
        UI.closeModal();
        UI.toast(
          persistent
            ? "تم حفظ بيانات " + info.singular + " في قاعدة البيانات." + (entity === "customers" && !isEdit && saved && saved.code ? " الكود: " + saved.code : "")
            : "تم حفظ التغيير مؤقتًا داخل جلسة العرض.",
          "success",
        );
        if (afterSave) afterSave();
      } catch (error) {
        button.disabled = false;
        button.textContent = "حفظ";
        UI.toast(
          error.message ||
            (persistent
              ? "تعذر حفظ بيانات " + info.singular + "."
              : "تعذر حفظ التغيير."),
          "danger",
        );
      }
    };
  }
  function supplierList(screen, container, context) {
    var state=context.state,schema=schemaFor("suppliers"),token=0;state.supplierQuery=state.supplierQuery||"";state.supplierPage=state.supplierPage||1;
    container.innerHTML=UI.pageHeader(screen,'<button class="button primary" data-supplier-add>+ إضافة مورد</button><button class="button" data-supplier-export>تصدير CSV</button>')+'<div class="toolbar"><div class="field grow"><label for="supplierSearch">بحث</label><input class="input" id="supplierSearch" value="'+U.escape(state.supplierQuery)+'" placeholder="اسم المورد أو الكود أو الهاتف"></div><span class="muted" data-supplier-count></span></div><div data-supplier-results aria-live="polite"></div>';
    var search=container.querySelector("#supplierSearch"),host=container.querySelector("[data-supplier-results]"),count=container.querySelector("[data-supplier-count]");
    function load(){var requestToken=++token;host.innerHTML='<div class="card"><div class="card-body">جارٍ تحميل الموردين...</div></div>';AlKhayalApi.request("/api/suppliers?search="+encodeURIComponent(state.supplierQuery)+"&page="+state.supplierPage+"&pageSize=25").then(function(data){if(requestToken!==token||!context.isActive())return;var rows=data.items||[],pages=Math.max(1,Math.ceil((data.totalCount||0)/(data.pageSize||25)));if(data.page>pages){state.supplierPage=pages;load();return;}S.store.replace("suppliers",rows);count.textContent=U.number(data.totalCount||0)+" سجل";host.innerHTML=rowsTable(schema.columns,rows,true)+'<div class="pagination"><span>صفحة '+data.page+' من '+pages+'</span><div class="pagination-buttons"><button class="button" data-supplier-page="prev"'+(data.page<=1?' disabled':'')+'>السابق</button><button class="button" data-supplier-page="next"'+(data.page>=pages?' disabled':'')+'>التالي</button></div></div>';}).catch(function(error){if(requestToken===token&&context.isActive())host.innerHTML='<div class="empty-state"><strong>تعذر تحميل الموردين</strong><span>'+U.escape(error.message||"")+'</span><button class="button" data-supplier-retry>إعادة المحاولة</button></div>';});}
    var scheduleSearch=U.debounce(function(){state.supplierQuery=search.value;state.supplierPage=1;load();},180);search.oninput=function(){token++;scheduleSearch();};
    container.querySelector("[data-supplier-export]").onclick=async function(){var button=this;if(button.disabled)return;button.disabled=true;button.textContent="جارٍ التصدير...";var query=search.value,rows=[],pageNumber=1;try{var data;do{data=await AlKhayalApi.request("/api/suppliers?search="+encodeURIComponent(query)+"&page="+pageNumber+"&pageSize=100");rows=rows.concat(data.items||[]);pageNumber++;}while(rows.length<data.totalCount&&(data.items||[]).length);U.downloadCsv("suppliers.csv",schema.columns,rows);UI.toast("تم إنشاء ملف CSV متوافق مع Excel.","success");}catch(error){UI.toast(error.message||"تعذر تصدير الموردين.","danger");}finally{button.disabled=false;button.textContent="تصدير CSV";}};
    container.onclick=async function(event){var page=event.target.closest("[data-supplier-page]"),action=event.target.closest("[data-row-action]");if(event.target.closest("[data-supplier-retry]")){load();return;}if(event.target.closest("[data-supplier-add]")){openRecordModal("suppliers",null,"add",load);return;}if(page){state.supplierPage+=page.getAttribute("data-supplier-page")==="next"?1:-1;load();return;}if(!action)return;var row=S.store.find("suppliers",action.closest("tr").getAttribute("data-row-id")),operation=action.getAttribute("data-row-action");if(operation==="view")openRecordModal("suppliers",row,"view");else if(operation==="edit")openRecordModal("suppliers",row,"edit",load);else if(operation==="delete"&&await UI.confirm({title:"حذف مورد",message:"سيتم حذف المورد نهائيًا من قاعدة البيانات.",confirmText:"حذف",danger:true})){try{await AlKhayalApi.request("/api/suppliers/"+encodeURIComponent(row.id),{method:"DELETE"});UI.toast("تم حذف المورد من قاعدة البيانات.","success");load();}catch(error){UI.toast(error.message||"تعذر حذف المورد.","danger");}}};
    load();
  }
  function entityList(screen, container, context, dataLoaded) {
    var entity = screen.data,
      schema = schemaFor(entity),
      state = context.state,
      info = persistedInfo(entity);
    if(entity==="suppliers"){supplierList(screen,container,context);return;}
    if (info && !dataLoaded) {
      container.innerHTML =
        UI.pageHeader(screen) +
        '<section class="card"><div class="card-body">جارٍ تحميل بيانات ' +
        info.plural +
        " من قاعدة البيانات...</div></section>";
      AlKhayalApi.request(info.path)
        .then(function (rows) {
          if (!context.isActive()) return;
          S.store.replace(entity, rows);
          entityList(screen, container, context, true);
        })
        .catch(function (error) {
          if (!context.isActive()) return;
          container.innerHTML =
            UI.pageHeader(screen) +
            '<section class="card"><div class="card-body"><strong>تعذر تحميل ' +
            info.plural +
            ' من قاعدة البيانات</strong><p class="muted">' +
            U.escape(error.message) +
            '</p><button class="button" data-retry-customers>إعادة المحاولة</button></div></section>';
          container.querySelector("[data-retry-customers]").onclick =
            function () {
              entityList(screen, container, context, false);
            };
        });
      return;
    }
    state.query = state.query || "";
    state.page = state.page || 1;
    state.pageSize = state.pageSize || 7;
    state.sortKey =
      state.sortKey || (schema.columns[0] && schema.columns[0].key);
    state.sortDirection = state.sortDirection || "asc";
    function queryRows() {
      var allRows = S.query(entity, {
        query: state.query,
        keys: schema.columns.map(function (column) {
          return column.key;
        }),
        sortKey: state.sortKey,
        sortDirection: state.sortDirection,
      });
      if (screen.filter === "low-stock")
        allRows = allRows.filter(function (product) {
          return product.stock <= product.minimum;
        });
      return allRows;
    }
    var allRows = queryRows();
    var pages = Math.max(1, Math.ceil(allRows.length / state.pageSize));
    state.page = Math.min(state.page, pages);
    var rows = allRows.slice(
        (state.page - 1) * state.pageSize,
        state.page * state.pageSize,
      ),
      canAdd = !!schema.fields;
    var tableHtml = rowsTable(schema.columns, rows, true),
      countText = U.number(allRows.length) + " سجل",
      paginationInner =
        "<span>صفحة " +
        state.page +
        " من " +
        pages +
        '</span><div class="pagination-buttons"><button class="button" data-page="prev"' +
        (state.page <= 1 ? " disabled" : "") +
        '>السابق</button><button class="button" data-page="next"' +
        (state.page >= pages ? " disabled" : "") +
        ">التالي</button></div>",
      tableHost = container.querySelector("[data-table-host]");
    if (tableHost) {
      // تحديث القائمة في مكانها مع الحفاظ على حقل البحث وتركيزه وموضع المؤشر.
      tableHost.innerHTML = tableHtml;
      var count = container.querySelector("[data-list-count]");
      if (count) count.textContent = countText;
      var pagination = container.querySelector("[data-list-pagination]");
      if (pagination) pagination.innerHTML = paginationInner;
    } else {
      container.innerHTML =
        UI.pageHeader(
          screen,
          (canAdd
            ? '<button class="button primary" data-list-add>+ إضافة' +
              (info ? " " + info.singular.replace(/^ال/, "") : "") +
              "</button>"
            : "") + '<button class="button" data-list-export>تصدير CSV</button>',
        ) +
        (info
          ? ""
          : UI.mockBanner(
              '<button class="button ghost" data-reset-mocks>إعادة بيانات العرض</button>',
            )) +
        '<div class="toolbar"><div class="field grow"><label for="listSearch">بحث</label><input class="input" id="listSearch" value="' +
        U.escape(state.query) +
        '" placeholder="ابحث في السجلات…"></div><span class="muted" data-list-count>' +
        countText +
        "</span></div><div data-table-host>" +
        tableHtml +
        '</div><div class="pagination" data-list-pagination>' +
        paginationInner +
        "</div>";
      var search = container.querySelector("#listSearch");
      search.addEventListener(
        "input",
        U.debounce(function () {
          state.query = search.value;
          state.page = 1;
          entityList(screen, container, context, true);
        }, 180),
      );
    }
    container.onclick = async function (event) {
      var sort = event.target.closest("[data-sort]"),
        page = event.target.closest("[data-page]"),
        action = event.target.closest("[data-row-action]");
      var rowTarget=event.target.closest("tr[data-row-id]");
      if(entity==="customers"&&rowTarget&&!action){localStorage.setItem("alkhayal_customer_statement",rowTarget.getAttribute("data-row-id"));context.navigate("customer-statement");return;}
      if (sort) {
        var key = sort.getAttribute("data-sort");
        state.sortDirection =
          state.sortKey === key && state.sortDirection === "asc"
            ? "desc"
            : "asc";
        state.sortKey = key;
        entityList(screen, container, context, true);
        return;
      }
      if (page) {
        state.page += page.getAttribute("data-page") === "next" ? 1 : -1;
        entityList(screen, container, context, true);
        return;
      }
      if (event.target.closest("[data-list-add]")) {
        openRecordModal(entity, null, "add", function () {
          entityList(screen, container, context, info ? false : true);
        });
        return;
      }
      if (event.target.closest("[data-list-export]")) {
        U.downloadCsv(entity + ".csv", schema.columns, queryRows());
        UI.toast("تم إنشاء ملف CSV متوافق مع Excel.", "success");
        return;
      }
      if (event.target.closest("[data-reset-mocks]")) {
        S.store.reset();
        UI.toast("تمت استعادة بيانات العرض الأصلية.", "success");
        entityList(screen, container, context);
        return;
      }
      if (action) {
        var row = S.store.find(
            entity,
            action.closest("tr").getAttribute("data-row-id"),
          ),
          operation = action.getAttribute("data-row-action");
        if (operation === "view") openRecordModal(entity, row, "view");
        else if (operation === "edit")
          openRecordModal(entity, row, "edit", function () {
            entityList(screen, container, context, info ? false : true);
          });
        else if (
          operation === "delete" &&
          (await UI.confirm({
            title: info
              ? "حذف " + info.singular.replace(/^ال/, "")
              : "حذف سجل تجريبي",
            message: info
              ? "سيتم حذف " + info.singular + " نهائيًا من قاعدة البيانات."
              : "سيُحذف هذا السجل من جلسة العرض الحالية فقط.",
            confirmText: "حذف",
            danger: true,
          }))
        ) {
          if (info) {
            try {
              await AlKhayalApi.request(
                info.path + "/" + encodeURIComponent(row.id),
                { method: "DELETE" },
              );
              UI.toast(
                "تم حذف " + info.singular + " من قاعدة البيانات.",
                "success",
              );
              entityList(screen, container, context, false);
            } catch (error) {
              UI.toast(
                error.message || "تعذر حذف " + info.singular + ".",
                "danger",
              );
            }
          } else {
            S.deleteMock(entity, row.id);
            UI.toast("حُذف السجل من جلسة العرض.", "success");
            entityList(screen, container, context);
          }
        }
      }
    };
  }
  function entityForm(screen, container, context, dataLoaded) {
    var entity = screen.data,
      schema = schemaFor(entity),
      fields = (schema.fields || []).slice(),
      record = screen.mode === "edit" ? S.store.all(entity)[0] : {};
    if (entity === "customers" && !dataLoaded) {
      container.innerHTML =
        UI.pageHeader(screen) +
        '<section class="card"><div class="card-body">جارٍ تحميل بيانات العملاء...</div></section>';
      AlKhayalApi.request("/api/customers")
        .then(function (rows) {
          if (!context.isActive()) return;
          S.store.replace("customers", rows);
          entityForm(screen, container, context, true);
        })
        .catch(function (error) {
          if (!context.isActive()) return;
          container.innerHTML =
            UI.pageHeader(screen) +
            '<section class="card"><div class="card-body">تعذر تحميل العملاء: ' +
            U.escape(error.message) +
            "</div></section>";
        });
      return;
    }
    if (screen.mode === "edit" && entity === "customers")
      record = S.store.all(entity)[0] || {};
    if (entity === "customers" && screen.mode !== "edit")
      fields = fields.filter(function (field) { return field.id !== "code"; });
    if (screen.mode === "quick" && entity === "products")
      fields = fields.filter(function (field) {
        return (
          ["sku", "barcode", "name", "salePrice", "stock"].indexOf(field.id) >=
          0
        );
      });
    container.innerHTML =
      UI.pageHeader(screen) +
      (entity === "customers" ? "" : UI.mockBanner()) +
      '<section class="card"><div class="card-head"><h2>' +
      (screen.mode === "edit"
        ? "اختر السجل ثم عدّل البيانات"
        : "البيانات الأساسية") +
      '</h2></div><div class="card-body">' +
      (screen.mode === "edit"
        ? '<div class="field" style="margin-bottom:14px"><label for="recordSelector">السجل</label><select class="select" id="recordSelector">' +
          S.store
            .all(entity)
            .map(function (item) {
              return (
                '<option value="' +
                item.id +
                '">' +
                U.escape(item.name || item.code || item.sku || item.username) +
                "</option>"
              );
            })
            .join("") +
          "</select></div>"
        : "") +
      (entity === "customers" && screen.mode !== "edit" ? '<p class="muted span-2">يُنشأ كود العميل تلقائيًا عند الحفظ.</p>' : '') +
      '<form id="entityForm" class="form-grid" novalidate>' +
      fields
        .map(function (field) {
          return UI.field(field, record[field.id]);
        })
        .join("") +
      '<div class="span-2 page-actions"><button class="button primary" type="submit">' +
      (entity === "customers" ? "حفظ" : "حفظ مؤقت") +
      '</button><button class="button" type="reset">مسح الحقول</button></div></form></div></section>';
    var form = container.querySelector("#entityForm"),
      selector = container.querySelector("#recordSelector");
    function load(id) {
      record = S.store.find(entity, id);
      fields.forEach(function (field) {
        if (form.elements[field.id])
          form.elements[field.id].value =
            record[field.id] == null ? "" : record[field.id];
      });
    }
    if (selector)
      selector.onchange = function () {
        load(selector.value);
      };
    form.onsubmit = async function (event) {
      event.preventDefault();
      var result = UI.collectForm(form, fields);
      if (!result.valid) return;
      if (form.dataset && form.dataset.saving === "true") return;
      if (form.dataset) form.dataset.saving = "true";
      var submitButton = form.querySelector('button[type="submit"]');
      if (submitButton) { submitButton.disabled = true; submitButton.textContent = "جارٍ الحفظ..."; }
      try {
        if (entity === "customers") {
          var savedCustomer = await AlKhayalApi.request(
            screen.mode === "edit"
              ? "/api/customers/" + encodeURIComponent(record.id)
              : "/api/customers",
            {
              method: screen.mode === "edit" ? "PUT" : "POST",
              body: JSON.stringify(result.data),
            },
          );
          UI.toast("تم حفظ بيانات العميل في قاعدة البيانات." + (screen.mode !== "edit" && savedCustomer && savedCustomer.code ? " الكود: " + savedCustomer.code : ""), "success");
          S.store.replace(
            "customers",
            await AlKhayalApi.request("/api/customers"),
          );
          if (screen.mode !== "edit") form.reset();
        } else {
          if (screen.mode === "edit")
            S.updateMock(entity, record.id, result.data);
          else S.createMock(entity, result.data);
          UI.toast(
            "تم الحفظ مؤقتًا داخل جلسة العرض؛ لم تُكتب بيانات أعمال إلى SQLite.",
            "success",
          );
          if (screen.mode !== "edit") form.reset();
        }
      } catch (error) {
        UI.toast(error.message || "تعذر حفظ البيانات.", "danger");
      } finally {
        if (form.dataset) form.dataset.saving = "false";
        if (submitButton) { submitButton.disabled = false; submitButton.textContent = entity === "customers" ? "حفظ" : "حفظ مؤقت"; }
      }
    };
  }
  var settingsDefinitions = {
    program: [
      { id: "businessName", label: "اسم المنشأة", required: true },
      { id: "taxNumber", label: "الرقم الضريبي", ltr: true },
      {
        id: "language",
        label: "لغة الواجهة",
        type: "select",
        options: ["العربية"],
      },
      {
        id: "startPage",
        label: "شاشة البداية",
        type: "select",
        options: ["الرئيسية", "الكاشير اللمسي", "فواتير البيع"],
      },
    ],
    system: [
      {
        id: "serverMode",
        label: "نمط التشغيل",
        type: "select",
        options: ["مستقل محلي", "عميل شبكة محلية"],
      },
      { id: "serverUrl", label: "عنوان الخادم", ltr: true },
      {
        id: "timeZone",
        label: "المنطقة الزمنية",
        type: "select",
        options: ["Africa/Cairo"],
      },
      {
        id: "logLevel",
        label: "مستوى السجل",
        type: "select",
        options: ["معلومات", "تحذيرات", "أخطاء"],
      },
    ],
    general: [
      {
        id: "branch",
        label: "الفرع الافتراضي",
        type: "select",
        options: ["الفرع الرئيسي", "فرع مدينة نصر"],
      },
      { id: "currency", label: "العملة", type: "select", options: ["ج.م"] },
      {
        id: "rows",
        label: "عدد صفوف الجداول",
        type: "select",
        options: ["7", "15", "25"],
      },
      {
        id: "confirmDelete",
        label: "تأكيد الحذف",
        type: "select",
        options: ["دائمًا", "للعمليات الحساسة"],
      },
    ],
    invoice: [
      { id: "invoiceTitle", label: "عنوان الفاتورة", required: true },
      {
        id: "invoiceFooter",
        label: "تذييل الفاتورة",
        type: "textarea",
        span: true,
      },
      {
        id: "showTax",
        label: "إظهار بند الضريبة",
        type: "select",
        options: ["نعم", "لا"],
      },
      {
        id: "paper",
        label: "المقاس",
        type: "select",
        options: ["80 مم", "A4"],
      },
    ],
    backup: [
      { id: "backupFolder", label: "مجلد النسخ المقترح", ltr: true },
      {
        id: "frequency",
        label: "التكرار المقترح",
        type: "select",
        options: ["يدوي", "يومي", "أسبوعي"],
      },
      { id: "retain", label: "عدد النسخ المحتفظ بها", type: "number", min: 1 },
      {
        id: "verify",
        label: "تذكير باختبار الاستعادة",
        type: "select",
        options: ["شهري", "ربع سنوي"],
      },
    ],
    warehouse: [
      {
        id: "defaultWarehouse",
        label: "المخزن الافتراضي",
        type: "select",
        options: ["المخزن الرئيسي", "مخزن فرع مدينة نصر"],
      },
      {
        id: "allowNegative",
        label: "السماح برصيد سالب",
        type: "select",
        options: ["لا", "تحذير فقط"],
      },
      {
        id: "costing",
        label: "سياسة التكلفة المستقبلية",
        type: "select",
        options: ["غير محددة — تحتاج قرارًا"],
      },
      {
        id: "lowStock",
        label: "إظهار تنبيهات النواقص",
        type: "select",
        options: ["نعم", "لا"],
      },
    ],
  };
  function settings(screen, container) {
    var fields =
      settingsDefinitions[screen.category] || settingsDefinitions.general;
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<div class="settings-layout"><nav class="card settings-nav"><button class="active" data-settings-pane="الإعداد الحالي">الإعداد الحالي</button><button data-settings-pane="التفضيلات">التفضيلات</button><button data-settings-pane="المعاينة">المعاينة</button><button data-settings-pane="السجل المحلي">السجل المحلي</button></nav><section class="card"><div class="card-head"><h2>خيارات ' +
      U.escape(screen.title) +
      '</h2></div><div class="card-body"><form id="settingsForm" class="form-grid">' +
      fields
        .map(function (field) {
          var sample =
            field.id === "businessName"
              ? "مؤسسة الخيال للتجارة"
              : field.id === "invoiceTitle"
                ? "فاتورة مبيعات"
                : field.id === "serverUrl"
                  ? "http://127.0.0.1:5137"
                  : field.id === "backupFolder"
                    ? "%LOCALAPPDATA%\\AlKhayal\\Backups"
                    : "";
          return UI.field(field, sample);
        })
        .join("") +
      '<div class="span-2 page-actions"><button class="button primary" type="submit">حفظ الإعدادات التجريبية</button><button class="button" type="reset">إعادة الضبط</button></div></form>' +
      (screen.category === "invoice"
        ? '<div class="preview-paper"><div class="print-brand"><img src="assets/alkhayal-mark.png" alt="شعار الخيال"><strong>الخيال</strong><small>مع الخيال شغلك أسرع · آمن · أذكى</small></div><p>فاتورة مبيعات</p><hr><small>هذه معاينة فقط وليست فاتورة ضريبية</small></div>'
        : "") +
      "</div></section></div>";
    container.querySelector("#settingsForm").onsubmit = function (event) {
      event.preventDefault();
      UI.toast("حُفظت المعاينة محليًا لهذه الجلسة فقط.", "success");
    };
    container.querySelector(".settings-nav").onclick = function (event) {
      var button = event.target.closest("[data-settings-pane]");
      if (!button) return;
      this.querySelectorAll("button").forEach(function (item) {
        item.classList.toggle("active", item === button);
      });
      UI.toast(
        "تم اختيار قسم " +
          button.getAttribute("data-settings-pane") +
          " للمعاينة.",
        "info",
      );
    };
  }
  function customerStatement(screen, container, context) {
    var state=context&&context.state||{},customers=[],seq=0,currentBalance=0;
    state.customerStatementFrom=state.customerStatementFrom||"";state.customerStatementTo=state.customerStatementTo||"";
    function active(){return !context||!context.isActive||context.isActive();}
    function localDate(){var now=new Date();return now.getFullYear()+"-"+String(now.getMonth()+1).padStart(2,"0")+"-"+String(now.getDate()).padStart(2,"0");}
    function controls(ready){container.querySelector("[data-customer-increase]").disabled=!ready;container.querySelector("[data-customer-collect]").disabled=!ready||currentBalance<=0;}
    function shell(){
      container.innerHTML=UI.pageHeader(screen)+'<section class="card"><div class="card-body form-grid"><div class="field"><label for="statementCustomer">العميل</label><select class="select" id="statementCustomer">'+customers.map(function(customer){return '<option value="'+customer.id+'"'+(Number(customer.id)===Number(state.customerStatementId)?' selected':'')+'>'+U.escape(customer.name+(customer.code?' — '+customer.code:''))+'</option>';}).join("")+'</select></div><div class="field"><label for="statementFrom">من تاريخ</label><input class="input" type="date" id="statementFrom" value="'+U.escape(state.customerStatementFrom)+'"></div><div class="field"><label for="statementTo">إلى تاريخ</label><input class="input" type="date" id="statementTo" value="'+U.escape(state.customerStatementTo)+'"></div><div class="page-actions"><button class="button" id="statementReset">كل الفترات</button><button class="button primary" data-customer-collect disabled>تحصيل من العميل</button><button class="button" data-customer-increase disabled>+ إضافة مديونية</button></div><p class="field-error span-2" id="statementError" role="alert" hidden></p></div></section><div id="statementBody" aria-live="polite"></div>';
      container.querySelector("#statementCustomer").onchange=function(){state.customerStatementId=Number(this.value);load();};
      function dates(){var from=container.querySelector("#statementFrom").value,to=container.querySelector("#statementTo").value,error=container.querySelector("#statementError");error.hidden=true;if(from&&to&&from>to){error.hidden=false;error.textContent="تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.";return;}state.customerStatementFrom=from;state.customerStatementTo=to;load();}
      container.querySelector("#statementFrom").onchange=dates;container.querySelector("#statementTo").onchange=dates;
      container.querySelector("#statementReset").onclick=function(){state.customerStatementFrom=state.customerStatementTo="";container.querySelector("#statementError").hidden=true;container.querySelector("#statementFrom").value="";container.querySelector("#statementTo").value="";load();};
      container.querySelector("[data-customer-collect]").onclick=function(){operation("collection");};
      container.querySelector("[data-customer-increase]").onclick=function(){operation("increase");};
    }
    function draw(data){
      currentBalance=Number(data.currentBalance);controls(true);
      var movements=data.movements||[];
      container.querySelector("#statementBody").innerHTML='<div class="stats-grid">'+[
        ["المديونية الحالية على العميل",data.currentBalance],["رصيد بداية الفترة",data.openingBalance],["مديونيات خلال الفترة",data.totalDebit],["تحصيلات وتخفيضات خلال الفترة",data.totalCredit],["رصيد نهاية الفترة",data.closingBalance]
      ].map(function(item){return '<article class="card stat-card"><span class="stat-label">'+item[0]+'</span><strong class="stat-value">'+U.money(item[1])+'</strong></article>';}).join("")+'</div><section class="card"><div class="card-head"><h2>حركات حساب '+U.escape(data.customerName)+'</h2></div><div class="card-body"><div class="table-wrap"><table class="data-table"><thead><tr><th>التاريخ</th><th>المرجع</th><th>البيان</th><th>الملاحظات</th><th>زيادة المديونية</th><th>تحصيل / تخفيض</th><th>الرصيد</th><th>طباعة</th></tr></thead><tbody>'+movements.map(function(item){return '<tr><td>'+U.date(item.date)+'</td><td>'+U.escape(item.reference||"—")+'</td><td>'+U.escape(item.description)+'</td><td>'+U.escape(item.notes||"—")+'</td><td>'+U.money(item.debit)+'</td><td>'+U.money(item.credit)+'</td><td>'+U.money(item.balance)+'</td><td>'+((item.transactionId&&(item.kind==="collection"||item.kind==="legacy_payment"))?'<button class="button" data-customer-receipt="'+item.transactionId+'">طباعة السند</button>':'')+'</td></tr>';}).join("")+'</tbody></table></div>'+(movements.length<=1?'<p class="muted">لا توجد حركات خلال الفترة المحددة.</p>':'')+'</div></section>';
      container.querySelector("#statementBody").onclick=function(event){var button=event.target.closest("[data-customer-receipt]");if(button&&global.AlKhayalPrinting)global.AlKhayalPrinting.submit("customer-payment",Number(button.getAttribute("data-customer-receipt")),false,button);};
    }
    function load(){
      if(!active())return;var token=++seq;controls(false);
      var query=[];if(state.customerStatementFrom)query.push("from="+encodeURIComponent(state.customerStatementFrom));if(state.customerStatementTo)query.push("to="+encodeURIComponent(state.customerStatementTo));
      var target=container.querySelector("#statementBody");target.innerHTML='<div class="card card-body">جارٍ تحميل كشف الحساب من قاعدة البيانات...</div>';
      AlKhayalApi.request("/api/customers/"+state.customerStatementId+"/statement"+(query.length?'?'+query.join('&'):'')).then(function(data){if(token===seq&&active())draw(data);}).catch(function(error){if(token!==seq||!active())return;target.innerHTML='<div class="card empty-state"><span>'+U.escape(error.message||"تعذر تحميل كشف الحساب.")+'</span><button class="button" data-statement-retry>إعادة المحاولة</button></div>';target.querySelector("[data-statement-retry]").onclick=load;});
    }
    function operation(type){
      var customer=customers.find(function(item){return Number(item.id)===Number(state.customerStatementId);});if(!customer)return;
      var label=type==="increase"?"إضافة مديونية":"تحصيل من العميل",busy=false,uncertain=false;
      var operationKey="customer-account-"+customer.id+"-"+Date.now().toString(36)+"-"+Math.random().toString(36).slice(2,14);
      UI.modal({title:label+" — "+customer.name,compact:true,focusSelector:"#customerAccountAmount",beforeClose:function(){return !busy&&!uncertain;},
        body:'<p class="muted">المديونية الحالية: '+U.money(currentBalance)+'</p><form id="customerAccountForm" class="form-grid"><div class="field"><label for="customerAccountAmount">المبلغ</label><input class="input ltr" id="customerAccountAmount" inputmode="decimal" required maxlength="20"></div><div class="field"><label for="customerAccountDate">تاريخ العملية</label><input class="input" type="date" id="customerAccountDate" required value="'+localDate()+'"></div><div class="field span-2"><label for="customerAccountNotes">البيان / ملاحظات</label><textarea class="textarea" id="customerAccountNotes" maxlength="1000"></textarea></div><p class="field-error span-2" data-account-error role="alert" hidden></p></form>',
        actions:'<button class="button primary" type="submit" form="customerAccountForm" data-save-customer-account>حفظ '+label+'</button><button class="button" data-modal-close>إلغاء</button>',
        onOpen:function(modal){
          var form=modal.querySelector("#customerAccountForm"),button=modal.querySelector("[data-save-customer-account]"),error=modal.querySelector("[data-account-error]");
          form.onsubmit=async function(event){event.preventDefault();if(busy)return;error.hidden=true;
            var amount=modal.querySelector("#customerAccountAmount").value.trim(),date=modal.querySelector("#customerAccountDate").value,notes=modal.querySelector("#customerAccountNotes").value;
            if(!/^\d+(?:\.\d{1,2})?$/.test(amount)||!/[1-9]/.test(amount)||!date){error.hidden=false;error.textContent="أدخل مبلغًا أكبر من صفر بدقة خانتين عشريتين وتاريخًا صحيحًا.";return;}
            busy=true;button.disabled=true;modal.querySelectorAll("input,textarea").forEach(function(input){input.disabled=true;});
            try{
              await AlKhayalApi.request("/api/customers/"+customer.id+"/transactions",{method:"POST",body:JSON.stringify({type:type,amount:amount,occurredOn:date,notes:notes,operationKey:operationKey})});
              if(modal.isConnected)UI.closeModal();UI.toast("تم حفظ "+label+" وتحديث حساب العميل."+((state.customerStatementFrom&&date<state.customerStatementFrom||state.customerStatementTo&&date>state.customerStatementTo)?" الحركة خارج الفترة المختارة.":""),"success");
              if(active()&&Number(state.customerStatementId)===Number(customer.id))load();
            }catch(failure){busy=false;uncertain=!failure.status||failure.status>=500;button.disabled=false;modal.querySelectorAll("input,textarea").forEach(function(input){input.disabled=uncertain;});error.hidden=false;error.textContent=(failure.message||"تعذر حفظ العملية.")+(uncertain?" أعد المحاولة بنفس البيانات للتحقق من الحفظ.":"");}
          };
        }
      });
    }
    function init(){
      container.innerHTML=UI.pageHeader(screen)+'<div class="card card-body">جارٍ تحميل العملاء...</div>';
      AlKhayalApi.request("/api/customers").then(function(rows){if(!active())return;customers=rows;if(!customers.length){container.innerHTML=UI.pageHeader(screen)+'<div class="card empty-state">لا يوجد عملاء. أضف عميلًا من قائمة العملاء أولًا.</div>';return;}if(!customers.some(function(customer){return Number(customer.id)===Number(state.customerStatementId);}))state.customerStatementId=Number(customers[0].id);shell();load();}).catch(function(error){if(!active())return;container.innerHTML=UI.pageHeader(screen)+'<div class="card empty-state"><span>'+U.escape(error.message)+'</span><button class="button" data-customers-retry>إعادة المحاولة</button></div>';container.querySelector("[data-customers-retry]").onclick=init;});
    }
    init();
  }
  var supplierMethods = ["نقدي", "تحويل", "بطاقة"];
  function todayIso() {
    return new Date().toISOString().slice(0, 10);
  }
  function supplierInvoiceBadge(invoice) {
    var remaining = Number(invoice.remaining || 0),
      overdue =
        remaining > 0 && invoice.dueDate && invoice.dueDate < todayIso();
    var tone =
      remaining <= 0
        ? "success"
        : overdue
          ? "danger"
          : invoice.paidAmount > 0
            ? "warning"
            : "info";
    var label = overdue ? "متأخرة — " + invoice.status : invoice.status;
    return '<span class="badge ' + tone + '">' + U.escape(label) + "</span>";
  }
  function supplierOptions(rows, selectedId) {
    return rows
      .map(function (row) {
        return (
          '<option value="' +
          U.escape(row.id) +
          '"' +
          (Number(row.id) === Number(selectedId) ? " selected" : "") +
          ">" +
          U.escape(row.name) +
          " — المستحق " +
          U.money(row.payable || 0) +
          "</option>"
        );
      })
      .join("");
  }
  function openSupplierPaymentModal(supplier, invoice, afterSave) {
    var busy = false, uncertain = false, treasuryReady = false;
    var paymentOperationKey = "supplier-pay-" + supplier.id + "-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);
    var limit = invoice
      ? Math.min(Number(invoice.remaining || 0), Number(supplier.payable || 0))
      : Number(supplier.payable || 0);
    var fields = [
      {
        id: "amount",
        label: "قيمة الدفع",
        type: "number",
        min: 0.01,
        step: "0.01",
        required: true,
      },
      { id: "paymentDate", label: "تاريخ الدفع", type: "date", required: true },
      {
        id: "account",
        label: "مرجع الدفع (اختياري)",
        type: "text",
      },
      {
        id: "treasuryAccountId",
        label: "الخزنة أو البنك للسداد",
        type: "select",
        options: [{value:"",label:"جارٍ تحميل الحسابات..."}],
        required: true,
      },
      {
        id: "method",
        label: "طريقة السداد",
        type: "select",
        options: supplierMethods,
        required: true,
      },
      { id: "notes", label: "ملاحظات", type: "textarea", span: true },
    ];
    var defaults = {
      amount: limit,
      paymentDate: todayIso(),
      account: "",
      method: supplierMethods[0],
      notes: "",
    };
    var layer = UI.modal({
      beforeClose: function () { return !busy && !uncertain; },
      title: invoice
        ? "دفع آجل — فاتورة " + invoice.invoiceNumber
        : "سداد على حساب المورد",
      body:
        '<p class="muted">' +
        U.escape(supplier.name) +
        " — " +
        (invoice ? "المتبقي على الفاتورة " : "المستحق الحالي ") +
        U.money(limit) +
        "</p><p class=\"muted\">سيُخصم مبلغ السداد من الخزنة أو البنك المختار ويُحدّث حساب المورد معًا.</p>" +
        (invoice?"":'<p class="muted">يُوزع السداد تلقائيًا على الأقرب استحقاقًا، أو بتاريخ الفاتورة عند عدم وجود استحقاق. عند تساوي التاريخ تُسدد الفاتورة المنشأة أولًا.</p>') +
        '<form id="supplierPayForm" class="form-grid" novalidate>' +
        fields
          .map(function (field) {
            return UI.field(field, defaults[field.id]);
          })
          .join("") +
        '</form><p class="muted" data-supplier-treasury-status role="status">جارٍ تحميل حسابات السداد...</p><button class="button" type="button" data-supplier-treasury-retry hidden>إعادة تحميل الحسابات</button>',
      actions:
        '<button class="button primary" type="button" data-save-supplier-payment disabled>تأكيد الدفع</button><button class="button" type="button" data-modal-close>إلغاء</button>',
      onOpen: function (element) {
        element.querySelector("input").focus();
      },
    });
    var paymentForm=layer.querySelector("#supplierPayForm");
    function paymentModalActive(){return layer.isConnected&&layer.querySelector("#supplierPayForm")===paymentForm;}
    function loadPaymentAccounts() {
      var status=layer.querySelector("[data-supplier-treasury-status]"),retry=layer.querySelector("[data-supplier-treasury-retry]"),button=layer.querySelector("[data-save-supplier-payment]");
      treasuryReady=false;button.disabled=true;retry.hidden=true;status.textContent="جارٍ تحميل حسابات السداد...";
      AlKhayalApi.request("/api/treasury/accounts").then(function(accounts){
        if(!paymentModalActive())return;
        layer.querySelector("#treasuryAccountId").innerHTML='<option value="">اختر الخزنة أو البنك</option>'+accounts.map(function(account){return '<option value="'+U.escape(account.id)+'">'+U.escape(account.name+' — '+(account.type==="bank"?'بنك':'خزنة')+' — الرصيد '+account.balance+' ج.م')+'</option>';}).join("");
        treasuryReady=accounts.length>0;button.disabled=!treasuryReady;
        status.textContent=treasuryReady?"سيُخصم مبلغ السداد من الحساب المختار عند التأكيد.":"لا توجد خزن أو بنوك متاحة. أضف حسابًا وأودع رصيده أولًا.";
      }).catch(function(error){if(!paymentModalActive())return;status.textContent=error.message||"تعذر تحميل الخزن والبنوك.";retry.hidden=false;});
    }
    layer.querySelector("[data-supplier-treasury-retry]").onclick=loadPaymentAccounts;
    loadPaymentAccounts();
    layer.querySelector("[data-save-supplier-payment]").onclick =
      async function () {
        if (busy || !treasuryReady) return;
        var form = layer.querySelector("#supplierPayForm"),
          result = UI.collectForm(form, fields);
        if (!result.valid) return;
        var amount = Number(result.data.amount),
          error = form.querySelector('[data-error-for="amount"]');
        if (!(amount > 0)) {
          error.textContent = "أدخل قيمة أكبر من صفر.";
          return;
        }
        if (amount > limit + 0.000001) {
          error.textContent = invoice
            ? "القيمة أكبر من المتبقي على الفاتورة."
            : "القيمة أكبر من المستحق للمورد.";
          return;
        }
        var button = layer.querySelector("[data-save-supplier-payment]");
        button.disabled = true; busy = true;
        layer.querySelectorAll("input,select,textarea").forEach(function(input){input.disabled=true;});
        try {
          var saved = await AlKhayalApi.request(
            "/api/suppliers/" + encodeURIComponent(supplier.id) + "/payments",
            {
              method: "POST",
              body: JSON.stringify({
                invoiceId: invoice && invoice.invoiceType !== "purchase" ? invoice.id : null,
                purchaseInvoiceId: invoice && invoice.invoiceType === "purchase" ? (invoice.purchaseInvoiceId || invoice.id) : null,
                treasuryAccountId: Number(result.data.treasuryAccountId),
                operationKey: paymentOperationKey,
                amount: form.elements.amount.value.trim(),
                paymentDate: result.data.paymentDate,
                method: result.data.method,
                account: result.data.account,
                notes: result.data.notes,
              }),
            },
          );
          UI.closeModal();
          UI.toast(
            "تم حفظ الدفع في قاعدة البيانات — المرجع " +
              (saved && saved.reference ? saved.reference : ""),
            "success",
          );
          if (afterSave) afterSave(saved);
          if(saved&&saved.id&&global.AlKhayalPrinting)UI.modal({title:"سند دفع المورد",body:"<p>تم حفظ السداد. يمكنك طباعة السند دون إعادة تسجيل الدفع.</p>",actions:'<button class="button" data-modal-close>إغلاق</button>',onOpen:function(modal){global.AlKhayalPrinting.mount(modal.querySelector(".modal-actions")||modal,"supplier-payment",saved.id);}});
        } catch (failure) {
          button.disabled = false; busy = false; uncertain = !failure.status || failure.status >= 500;
          layer.querySelectorAll("input,select,textarea").forEach(function(input){input.disabled=uncertain;});
          UI.toast((failure.message || "تعذر حفظ الدفع.") + (uncertain ? " — أعد المحاولة بنفس البيانات للتحقق من الحفظ." : ""), "danger");
        }
      };
  }
  function openSupplierInvoiceModal(supplier, afterSave) {
    var busy = false, uncertain = false;
    var invoiceOperationKey = "supplier-invoice-" + supplier.id + "-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 12);
    var fields = [
      { id: "invoiceNumber", label: "رقم الفاتورة (اختياري)", ltr: true },
      {
        id: "invoiceDate",
        label: "تاريخ الفاتورة",
        type: "date",
        required: true,
      },
      { id: "dueDate", label: "تاريخ الاستحقاق", type: "date" },
      {
        id: "total",
        label: "إجمالي الفاتورة",
        type: "number",
        min: 0.01,
        step: "0.01",
        required: true,
      },
      { id: "notes", label: "ملاحظات", type: "textarea", span: true },
    ];
    var layer = UI.modal({
      title: "إضافة فاتورة مديونية — " + supplier.name,
      beforeClose: function () { return !busy && !uncertain; },
      body:
        '<p class="muted">تسجيل مبلغ مستحق للمورد فقط، دون إضافة بضاعة للمخزون. عند شراء بضاعة استخدم إدارة المشتريات.</p><form id="supplierInvoiceForm" class="form-grid" novalidate>' +
        fields
          .map(function (field) {
            return UI.field(
              field,
              field.id === "invoiceDate" ? todayIso() : "",
            );
          })
          .join("") +
        "</form>",
      actions:
        '<button class="button primary" type="button" data-save-supplier-invoice>حفظ الفاتورة</button><button class="button" type="button" data-modal-close>إلغاء</button>',
      onOpen: function (element) {
        element.querySelector("input").focus();
      },
    });
    layer.querySelector("[data-save-supplier-invoice]").onclick =
      async function () {
        if (busy) return;
        var form = layer.querySelector("#supplierInvoiceForm"),
          result = UI.collectForm(form, fields);
        if (!result.valid) return;
        if (!(Number(result.data.total) > 0)) {
          form.querySelector('[data-error-for="total"]').textContent =
            "أدخل إجمالي أكبر من صفر.";
          return;
        }
        if (
          result.data.dueDate &&
          result.data.dueDate < result.data.invoiceDate
        ) {
          form.querySelector('[data-error-for="dueDate"]').textContent =
            "الاستحقاق لا يسبق تاريخ الفاتورة.";
          return;
        }
        var button = layer.querySelector("[data-save-supplier-invoice]");
        button.disabled = true; busy = true;
        try {
          await AlKhayalApi.request(
            "/api/suppliers/" + encodeURIComponent(supplier.id) + "/invoices",
            {
              method: "POST",
              body: JSON.stringify({
                operationKey: invoiceOperationKey,
                invoiceNumber: result.data.invoiceNumber,
                invoiceDate: result.data.invoiceDate,
                dueDate: result.data.dueDate || null,
                total: form.elements.total.value.trim(),
                notes: result.data.notes,
              }),
            },
          );
          UI.closeModal();
          UI.toast(
            "تم حفظ الفاتورة وزيادة مستحقات المورد في قاعدة البيانات.",
            "success",
          );
          if (afterSave) afterSave();
        } catch (failure) {
          button.disabled = false; busy = false; uncertain = !failure.status || failure.status >= 500;
          layer.querySelectorAll("input,select,textarea").forEach(function(input){input.disabled=uncertain;});
          UI.toast((failure.message || "تعذر حفظ الفاتورة.") + (uncertain ? " — أعد المحاولة بنفس البيانات للتحقق من الحفظ." : ""), "danger");
        }
      };
  }
  function supplierStatement(screen, container, context) {
    var state = { id: context && context.state && context.state.supplierId || null, from: "", to: "" },
      suppliers = [],
      seq = 0;
    var movementColumns = [
      { key: "date", label: "التاريخ", date: true },
      { key: "reference", label: "المرجع", ltr: true },
      { key: "description", label: "البيان" },
      { key: "debit", label: "سداد (مدين)", money: true },
      { key: "credit", label: "فاتورة (دائن)", money: true },
      { key: "balance", label: "المستحق بعد الحركة", money: true },
    ];
    function current() {
      return (
        suppliers.find(function (row) {
          return Number(row.id) === Number(state.id);
        }) || null
      );
    }
    function failure(error, retry) {
      container.innerHTML =
        UI.pageHeader(screen) +
        '<section class="card"><div class="card-body"><strong>تعذر تحميل حسابات الموردين من قاعدة البيانات</strong><p class="muted">' +
        U.escape(error.message || "تأكد من تسجيل الدخول وتشغيل الخادم.") +
        '</p><button class="button" data-retry-supplier>إعادة المحاولة</button></div></section>';
      container.querySelector("[data-retry-supplier]").onclick = retry;
    }
    function shell() {
      container.innerHTML =
        UI.pageHeader(
          screen,
          '<button class="button" type="button" data-print-preview>معاينة الطباعة</button>',
        ) +
        '<div class="filter-bar"><div class="field grow"><label for="supplierSelect">المورد</label><select class="select" id="supplierSelect">' +
        supplierOptions(suppliers, state.id) +
        '</select></div><div class="field"><label for="supplierFrom">من</label><input class="input" type="date" id="supplierFrom" value="' +
        U.escape(state.from) +
        '"></div><div class="field"><label for="supplierTo">إلى</label><input class="input" type="date" id="supplierTo" value="' +
        U.escape(state.to) +
        '"></div><button class="button" type="button" id="supplierReset">كل الفترات</button></div><div id="supplierBody"><div class="card"><div class="card-body">جارٍ تحميل حساب المورد...</div></div></div>';
      container.querySelector("#supplierSelect").onchange = function () {
        state.id = Number(this.value);
        load();
      };
      container.querySelector("#supplierFrom").onchange = function () {
        state.from = this.value;
        applyDates();
      };
      container.querySelector("#supplierTo").onchange = function () {
        state.to = this.value;
        applyDates();
      };
      container.querySelector("#supplierReset").onclick = function () {
        state.from = "";
        state.to = "";
        container.querySelector("#supplierFrom").value = "";
        container.querySelector("#supplierTo").value = "";
        load();
      };
      container.onclick = function (event) {
        if (event.target.closest("[data-print-preview]")) {
          UI.toast("معاينة فقط؛ لم يُرسل شيء إلى طابعة.", "warning");
          return;
        }
        var pay = event.target.closest("[data-pay-invoice]");
        if (pay) {
          var owner = current(),
            invoiceId = pay.getAttribute("data-pay-invoice"),
            invoice = (container._invoices || []).find(function (row) {
              return String(row.id) === String(invoiceId);
            });
          if (owner && invoice)
            openSupplierPaymentModal(owner, invoice, reloadAll);
          return;
        }
        if (event.target.closest("[data-pay-account]")) {
          var account = current();
          if (account) openSupplierPaymentModal(account, null, reloadAll);
        }
      };
    }
    function applyDates() {
      if (state.from && state.to && state.from > state.to) {
        UI.toast("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.", "danger");
        return;
      }
      load();
    }
    function invoicesTable(invoices) {
      if (!invoices.length)
        return '<div class="empty-state"><strong>لا توجد فواتير لهذا المورد</strong><span>لا توجد فواتير شراء آجلة مسجلة لهذا المورد.</span></div>';
      return (
        '<div class="table-wrap"><table class="data-table"><thead><tr><th>رقم الفاتورة</th><th>التاريخ</th><th>الاستحقاق</th><th>الإجمالي</th><th>المدفوع</th><th>المتبقي</th><th>الحالة</th><th>الإجراءات</th></tr></thead><tbody>' +
        invoices
          .map(function (invoice) {
            var open = Number(invoice.remaining || 0) > 0;
            return (
              '<tr data-invoice-id="' +
              U.escape(invoice.id) +
              '"><td><span class="ltr">' +
              U.escape(invoice.invoiceNumber) +
              "</span></td><td>" +
              U.date(invoice.invoiceDate) +
              "</td><td>" +
              (invoice.dueDate ? U.date(invoice.dueDate) : "—") +
              '</td><td><span class="ltr">' +
              U.money(invoice.total) +
              '</span></td><td><span class="ltr">' +
              U.money(invoice.paidAmount) +
              '</span></td><td><span class="ltr">' +
              U.money(invoice.remaining) +
              "</span></td><td>" +
              supplierInvoiceBadge(invoice) +
              '</td><td><div class="row-actions">' +
              (open
                ? '<button class="button primary" type="button" data-pay-invoice="' +
                  U.escape(invoice.id) +
                  '">دفع آجل</button>'
                : '<span class="muted">مسددة بالكامل</span>') +
              "</div></td></tr>"
            );
          })
          .join("") +
        "</tbody></table></div>"
      );
    }
    function draw(data, invoices) {
      var host = container.querySelector("#supplierBody");
      if (!host) return;
      container._invoices = invoices;
      var supplier = current(),
        rows = (data.movements || []).map(function (item, index) {
          var isOpening = item.reference === "OPEN";
          return {
            id: index,
            date: isOpening && !data.from ? "—" : item.date,
            reference: friendlyReference(item.reference),
            description:
              isOpening && !data.from
                ? "الرصيد الافتتاحي قبل الفواتير"
                : item.description,
            debit: item.debit,
            credit: item.credit,
            balance: item.balance,
          };
        });
      host.innerHTML =
        '<div class="stats-grid"><article class="card stat-card"><span class="stat-label">المبلغ المستحق</span><strong class="stat-value">' +
        U.money(data.totalInvoices) +
        '</strong></article><article class="card stat-card"><span class="stat-label">المبلغ المدفوع</span><strong class="stat-value">' +
        U.money(data.totalPaid) +
        '</strong></article><article class="card stat-card"><span class="stat-label">الإجمالي</span><strong class="stat-value">' +
        U.money(Number(data.totalInvoices || 0) - Number(data.totalPaid || 0)) +
        "</strong></article></div>" +
        '<section class="card" style="margin-top:12px"><div class="card-head"><h2>فواتير ' +
        U.escape(supplier ? supplier.name : "") +
        '</h2><button class="button" type="button" data-pay-account' +
        (supplier && Number(supplier.payable) > 0 ? "" : " disabled") +
        '>سداد على الحساب</button></div><div class="card-body">' +
        invoicesTable(invoices) +
        '</div></section><section class="card" style="margin-top:12px"><div class="card-head"><h2>حركة الحساب</h2></div><div class="card-body">' +
        rowsTable(movementColumns, rows, false) +
        "</div></section>";
    }
    function load() {
      var mine = ++seq,
        query = [];
      if (state.from) query.push("from=" + encodeURIComponent(state.from));
      if (state.to) query.push("to=" + encodeURIComponent(state.to));
      var host = container.querySelector("#supplierBody");
      if (host)
        host.innerHTML =
          '<div class="card"><div class="card-body">جارٍ تحميل حساب المورد...</div></div>';
      var base = "/api/suppliers/" + encodeURIComponent(state.id);
      Promise.all([
        AlKhayalApi.request(
          base + "/statement" + (query.length ? "?" + query.join("&") : ""),
        ),
        AlKhayalApi.request(base + "/invoices"),
      ])
        .then(function (results) {
          if (mine === seq) draw(results[0], results[1]);
        })
        .catch(function (error) {
          if (mine !== seq) return;
          var target = container.querySelector("#supplierBody");
          if (target)
            target.innerHTML =
              '<div class="card"><div class="card-body"><strong>تعذر تحميل حساب المورد</strong><p class="muted">' +
              U.escape(error.message || "حاول مرة أخرى.") +
              "</p></div></div>";
        });
    }
    function reloadAll() {
      AlKhayalApi.request("/api/suppliers")
        .then(function (rows) {
          suppliers = rows;
          S.store.replace("suppliers", rows);
          var select = container.querySelector("#supplierSelect");
          if (select) select.innerHTML = supplierOptions(suppliers, state.id);
          load();
        })
        .catch(function (error) {
          UI.toast(error.message || "تعذر تحديث بيانات الموردين.", "danger");
        });
    }
    container.innerHTML =
      UI.pageHeader(screen) +
      '<section class="card"><div class="card-body">جارٍ تحميل الموردين من قاعدة البيانات...</div></section>';
    AlKhayalApi.request("/api/suppliers")
      .then(function (rows) {
        if (!context.isActive()) return;
        suppliers = rows;
        S.store.replace("suppliers", rows);
        if (!rows.length) {
          container.innerHTML =
            UI.pageHeader(screen) +
            '<section class="card"><div class="card-body">لا يوجد موردون. أضف موردًا من «قائمة الموردين» أولًا.</div></section>';
          return;
        }
        if (!rows.some(function(row) { return Number(row.id) === Number(state.id); })) state.id = Number(rows[0].id);
        shell();
        load();
      })
      .catch(function (error) {
        failure(error, function () {
          supplierStatement(screen, container, context);
        });
      });
  }
  function supplierPayment(screen, container, context) {
    var state = context && context.state || {}, rows = [], seq = 0, invoiceCache = {};
    function active() { return !context || !context.isActive || context.isActive(); }
    state.accountsPage = state.accountsPage || 1;
    state.accountsSearch = state.accountsSearch || "";
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body"><div class="field"><label for="supplierAccountsSearch">بحث عن مورد بالاسم أو الكود أو الهاتف</label><input class="input" type="search" id="supplierAccountsSearch" value="'+U.escape(state.accountsSearch)+'"></div></div></section><div id="supplierAccountsResults" aria-live="polite"></div>';
    var search = container.querySelector("#supplierAccountsSearch"), results = container.querySelector("#supplierAccountsResults");
    var delayed = U.debounce(load,180);
    search.oninput = function () { ++seq; state.accountsSearch=search.value;state.accountsPage=1;delayed(); };
    function invoiceKey(invoice) { return (invoice.invoiceType === "purchase" ? "purchase:" : "legacy:")+invoice.id; }
    function invoiceTable(invoices, supplier) {
      if(!invoices.length)return '<div class="empty-state">لا توجد فواتير لهذا المورد.</div>';
      return '<div class="table-wrap"><table class="data-table"><thead><tr><th>الفاتورة</th><th>التاريخ</th><th>النوع</th><th>الإجمالي</th><th>المدفوع</th><th>المرتجع</th><th>المتبقي</th><th>الحالة</th><th>السداد</th></tr></thead><tbody>'+invoices.map(function(invoice){
        var draft=invoice.documentStatus==="draft",canPay=!draft&&Number(invoice.remaining)>0&&Number(supplier.payable)>0;
        return '<tr><td>'+U.escape(invoice.invoiceNumber)+'</td><td>'+U.date(invoice.invoiceDate)+'</td><td>'+(invoice.invoiceType==="purchase"?'شراء بضاعة':'مديونية')+'</td><td>'+U.money(invoice.total)+'</td><td>'+U.money(invoice.paidAmount)+'</td><td>'+U.money(invoice.returnedAmount||0)+'</td><td>'+U.money(invoice.remaining)+'</td><td>'+(draft?'مسودة — غير مستحقة بعد':supplierInvoiceBadge(invoice))+'</td><td>'+(canPay?'<button class="button primary" type="button" data-account-invoice="'+U.escape(invoiceKey(invoice))+'">سداد الفاتورة</button>':'<span class="muted">'+(draft?'يجب نشر الفاتورة أولًا':'لا يوجد مستحق')+'</span>')+'</td></tr>';
      }).join("")+'</tbody></table></div>';
    }
    function host(id) { return results.querySelector('[data-account-invoices="'+id+'"]'); }
    async function loadInvoices(supplier, token) {
      try {
        var invoices=await AlKhayalApi.request("/api/suppliers/"+supplier.id+"/invoices");
        if(token!==seq||!active())return;
        invoiceCache[supplier.id]=invoices;var target=host(supplier.id);if(target)target.innerHTML=invoiceTable(invoices,supplier);
      } catch(error) {
        if(token!==seq||!active())return;var target=host(supplier.id);
        if(target)target.innerHTML='<div class="empty-state"><span>'+U.escape(error.message||"تعذر تحميل الفواتير.")+'</span><button class="button" data-retry-account="'+supplier.id+'">إعادة المحاولة</button></div>';
      }
    }
    function load() {
      if(!active())return;
      var token=++seq;
      results.innerHTML='<div class="card card-body">جارٍ تحميل حسابات الموردين...</div>';
      AlKhayalApi.request("/api/suppliers?search="+encodeURIComponent(state.accountsSearch)+"&page="+state.accountsPage+"&pageSize=10").then(function(data){
        if(token!==seq||!active())return;rows=data.items||[];invoiceCache={};
        if(!rows.some(function(supplier){return Number(supplier.id)===Number(state.selectedSupplierId);}))state.selectedSupplierId=null;
        results.innerHTML=(rows.length?'<section class="card"><div class="card-body field"><label for="supplierAccountsSelect">اختر المورد لعرض حسابه وفواتيره</label><select class="select" id="supplierAccountsSelect"><option value="">اختر المورد</option>'+rows.map(function(supplier){return '<option value="'+supplier.id+'"'+(Number(supplier.id)===Number(state.selectedSupplierId)?' selected':'')+'>'+U.escape(supplier.name+' — '+supplier.code)+' — المستحق '+U.money(supplier.payable||0)+'</option>';}).join("")+'</select></div></section>':'<section class="card"><div class="empty-state">لا يوجد موردون مطابقون. يمكنك إضافة مورد من قائمة الموردين.</div></section>')+
          '<div class="pagination"><span>'+data.totalCount+' مورد</span><div class="pagination-buttons"><button class="button" data-accounts-page="'+(state.accountsPage-1)+'"'+(state.accountsPage<=1?' disabled':'')+'>السابق</button><button class="button" data-accounts-page="'+(state.accountsPage+1)+'"'+(state.accountsPage*10>=data.totalCount?' disabled':'')+'>التالي</button></div></div><div id="selectedSupplierAccount"></div>';
        var select=results.querySelector("#supplierAccountsSelect");
        if(select)select.onchange=function(){state.selectedSupplierId=Number(this.value)||null;showSupplier();};
        showSupplier();      }).catch(function(error){if(token!==seq||!active())return;results.innerHTML='<div class="card empty-state"><span>'+U.escape(error.message||"تعذر تحميل الموردين.")+'</span><button class="button" data-accounts-retry>إعادة المحاولة</button></div>';});
    }
    function showSupplier(){
      var token=++seq,target=results.querySelector("#selectedSupplierAccount");invoiceCache={};
      var supplier=rows.find(function(row){return Number(row.id)===Number(state.selectedSupplierId);});
      if(!target)return;
      if(!supplier){target.innerHTML=rows.length?'<div class="empty-state">اختر موردًا من القائمة لعرض حسابه وفواتيره.</div>':'';return;}
      target.innerHTML='<section class="card supplier-account-card" data-supplier-account="'+supplier.id+'"><div class="card-head"><div><h2>'+U.escape(supplier.name)+'</h2><p class="muted">'+U.escape(supplier.code)+' — المستحق '+U.money(supplier.payable||0)+(Number(supplier.supplierCredit)>0?' — رصيد لدى المورد '+U.money(supplier.supplierCredit):'')+'</p></div><div class="page-actions"><button class="button" type="button" data-add-account-invoice="'+supplier.id+'">+ إضافة فاتورة مديونية</button><button class="button primary" type="button" data-pay-supplier-account="'+supplier.id+'"'+(Number(supplier.payable)>0?'':' disabled')+'>سداد على الحساب</button></div></div><div class="card-body" data-account-invoices="'+supplier.id+'">جارٍ تحميل فواتير المورد...</div></section>';
      if(global.AlKhayalPrinting)global.AlKhayalPrinting.supplierReceipts(target.querySelector("[data-supplier-account]"),supplier.id);
      loadInvoices(supplier,token);
    }
    container.onclick=function(event){
      var page=event.target.closest("[data-accounts-page]");if(page&&!page.disabled){state.accountsPage=Number(page.getAttribute("data-accounts-page"));load();return;}
      if(event.target.closest("[data-accounts-retry]")){load();return;}
      var retry=event.target.closest("[data-retry-account]");if(retry){var owner=rows.find(function(x){return String(x.id)===retry.getAttribute("data-retry-account");});if(owner)loadInvoices(owner,seq);return;}
      var add=event.target.closest("[data-add-account-invoice]"),pay=event.target.closest("[data-pay-supplier-account]"),invoiceButton=event.target.closest("[data-account-invoice]"),card=event.target.closest("[data-supplier-account]");
      if(!card)return;var supplier=rows.find(function(x){return String(x.id)===card.getAttribute("data-supplier-account");});if(!supplier)return;
      if(add)openSupplierInvoiceModal(supplier,function(){if(active())load();});
      if(pay&&!pay.disabled)openSupplierPaymentModal(supplier,null,function(){if(active())load();});
      if(invoiceButton){var invoice=(invoiceCache[supplier.id]||[]).find(function(x){return invoiceKey(x)===invoiceButton.getAttribute("data-account-invoice");});if(invoice&&invoice.documentStatus!=="draft")openSupplierPaymentModal(supplier,invoice,function(){if(active())load();});}
    };
    load();
  }
  function statement(screen, container, context) {
    if (screen.party === "supplier")
      return supplierStatement(screen, container, context);
    return customerStatement(screen, container, context);
  }
  function payment(screen, container, context) {
    var isSupplier = screen.party === "supplier";
    if (isSupplier) return supplierPayment(screen, container, context);
    function render(rows) {
      S.store.replace("customers", rows);
      if (!rows.length) {
        container.innerHTML =
          UI.pageHeader(screen) +
          '<section class="card"><div class="card-body">لا يوجد عملاء. أضف عميلًا أولًا ثم سجّل التحصيل.</div></section>';
        return;
      }
      var today = new Date().toISOString().slice(0, 10);
      container.innerHTML =
        UI.pageHeader(screen) +
        '<section class="card"><div class="card-head"><h2>المعاملات المالية للعميل</h2></div><div class="card-body"><p class="muted">تُسجّل كل عملية في كشف الحساب ولا يمكن تعديلها لاحقًا.</p><form id="paymentForm" class="form-grid"><div class="field"><label for="paymentParty">العميل</label><select class="select" id="paymentParty" required>' +
        rows
          .map(function (row) {
            return (
              '<option value="' +
              U.escape(row.id) +
              '">' +
              U.escape(row.name) +
              " — المديونية " +
              U.money(row.balance || 0) +
              "</option>"
            );
          })
          .join("") +
        "</select></div>" +
        UI.field({id:"transactionType",label:"العملية",type:"select",options:["إضافة مبلغ على العميل","تسجيل تحصيل","تخفيض الرصيد"],required:true},"تسجيل تحصيل") +
        UI.field(
          {
            id: "amount",
            label: "المبلغ",
            type: "number",
            min: 0.01,
            step: "0.01",
            required: true,
          },
          "",
        ) +
        UI.field(
          {
            id: "paymentDate",
            label: "التاريخ",
            type: "date",
            required: true,
          },
          today,
        ) +
        UI.field(
          { id: "notes", label: "ملاحظات", type: "textarea", span: true },
          "",
        ) +
        '<div class="span-2"><button class="button primary" type="submit">حفظ العملية</button></div></form><div id="paymentResult" class="muted" style="margin-top:12px">التحصيل دفع حقيقي، أما التخفيض فهو تسوية غير نقدية ظاهرة في كشف الحساب.</div></div></section>';
      var form = container.querySelector("#paymentForm");
      form.onsubmit = async function (event) {
        event.preventDefault();
        var button = form.querySelector('[type="submit"]');
        var customerId = Number(form.querySelector("#paymentParty").value);
        var amount = Number(form.querySelector("#amount").value);
        var customer = rows.find(function (row) {
          return Number(row.id) === customerId;
        });
        if (!(amount > 0)) {
          UI.toast("أدخل مبلغًا أكبر من صفر.", "danger");
          return;
        }
        if (customer && amount > Number(customer.balance || 0)) {
          UI.toast("قيمة التحصيل أكبر من المديونية الحالية للعميل.", "danger");
          return;
        }
        button.disabled = true;
        button.textContent = "جارٍ الحفظ...";
        try {
          var saved = await AlKhayalApi.request(
            "/api/customers/" + encodeURIComponent(customerId) + "/transactions",
            {
              method: "POST",
              body: JSON.stringify({
                amount: amount,
                type:({"إضافة مبلغ على العميل":"increase","تسجيل تحصيل":"collection","تخفيض الرصيد":"reduction"})[form.querySelector("#transactionType").value],
                occurredOn: form.querySelector("#paymentDate").value,
                operationKey:(global.crypto&&global.crypto.randomUUID?global.crypto.randomUUID():String(Date.now())+Math.random()),
                notes: form.querySelector("#notes").value,
              }),
            },
          );
          var updatedRows = await AlKhayalApi.request("/api/customers");
          S.store.replace("customers", updatedRows);
          UI.toast(
            "تم حفظ العملية وتحديث رصيد العميل.",
            "success",
          );
          render(updatedRows);
          var result = container.querySelector("#paymentResult");
          if (result && saved)
            result.textContent =
              "تم حفظ العملية في كشف الحساب.";
        } catch (error) {
          button.disabled = false;
          button.textContent = "حفظ العملية";
          UI.toast(
            error.message ||
              "تعذر حفظ التحصيل. تأكد من تسجيل الدخول واتصال الخادم.",
            "danger",
          );
        }
      };
    }
    container.innerHTML =
      UI.pageHeader(screen) +
      '<section class="card"><div class="card-body">جارٍ تحميل العملاء من قاعدة البيانات...</div></section>';
    AlKhayalApi.request("/api/customers")
      .then(render)
      .catch(function (error) {
        container.innerHTML =
          UI.pageHeader(screen) +
          '<section class="card"><div class="card-body"><strong>تعذر تحميل العملاء من قاعدة البيانات</strong><p class="muted">' +
          U.escape(error.message || "تأكد من تسجيل الدخول وتشغيل الخادم.") +
          '</p><button class="button" data-retry-payment>إعادة المحاولة</button></div></section>';
        container.querySelector("[data-retry-payment]").onclick = function () {
          payment(screen, container);
        };
      });
  }
  function exportScreen(screen, container) {
    var schema = schemaFor(screen.data),
      rows = S.store.all(screen.data);
    container.innerHTML =
      UI.pageHeader(
        screen,
        '<button class="button primary" data-export>تنزيل CSV</button>',
      ) +
      UI.mockBanner("<span>التنسيق CSV وليس XLSX.</span>") +
      rowsTable(schema.columns, rows, false);
    container.querySelector("[data-export]").onclick = function () {
      U.downloadCsv("customers-export.csv", schema.columns, rows);
      UI.toast("تم تنزيل CSV متوافق مع Excel.", "success");
    };
  }
  function report(screen, container) {
    var entity = screen.data,
      rows = S.store.all(entity),
      schema = schemaFor(entity),
      numericKey =
        entity === "sales" || entity === "purchases"
          ? "total"
          : entity === "suppliers"
            ? "payable"
            : entity === "accounts"
              ? "balance"
              : entity === "shifts"
                ? "sales"
                : entity === "branches"
                  ? "sales"
                  : "id",
      total = rows.reduce(function (sum, row) {
        return sum + Number(row[numericKey] || 0);
      }, 0),
      points = rows.slice(0, 7).map(function (row, index) {
        return {
          label: row.invoice || row.code || row.name || String(index + 1),
          value: Number(row[numericKey] || index + 1),
        };
      });
    container.innerHTML =
      UI.pageHeader(
        screen,
        '<button class="button" data-report-print>معاينة الطباعة</button><button class="button" data-report-export>تصدير CSV</button>',
      ) +
      UI.mockBanner() +
      '<div class="filter-bar"><div class="field"><label>من</label><input class="input" type="date" value="2026-10-01"></div><div class="field"><label>إلى</label><input class="input" type="date" value="2026-10-20"></div><div class="field"><label>الفرع</label><select class="select"><option>كل الفروع</option><option>الفرع الرئيسي</option><option>مدينة نصر</option></select></div><button class="button primary" data-apply-filter>تطبيق</button></div><div class="stats-grid"><article class="card stat-card"><span class="stat-label">إجمالي القيمة</span><strong class="stat-value">' +
      U.money(total) +
      '</strong></article><article class="card stat-card"><span class="stat-label">عدد السجلات</span><strong class="stat-value">' +
      U.number(rows.length) +
      '</strong></article><article class="card stat-card"><span class="stat-label">المتوسط</span><strong class="stat-value">' +
      U.money(rows.length ? total / rows.length : 0) +
      '</strong></article><article class="card stat-card"><span class="stat-label">النطاق</span><strong class="stat-value" style="font-size:16px">1–20 أكتوبر</strong></article></div><div class="content-grid"><section class="card"><div class="card-head"><h2>المؤشر</h2></div><div class="card-body">' +
      UI.chart(points) +
      '</div></section><section class="card"><div class="card-head"><h2>ملخص التقرير</h2></div><div class="card-body list"><div class="list-item"><span>أعلى قيمة</span><strong>' +
      U.money(
        Math.max.apply(
          Math,
          points
            .map(function (point) {
              return point.value;
            })
            .concat([0]),
        ),
      ) +
      '</strong></div><div class="list-item"><span>مصدر البيانات</span><strong>عرض تجريبي</strong></div><div class="list-item"><span>آخر تحديث</span><strong>الآن</strong></div></div></section></div>' +
      rowsTable(schema.columns.slice(0, 6), rows.slice(0, 8), false);
    container.onclick = function (event) {
      if (event.target.closest("[data-report-print]"))
        UI.toast("تم فتح معاينة منطقية فقط؛ لا توجد طباعة فعلية.", "warning");
      if (event.target.closest("[data-report-export]")) {
        U.downloadCsv(screen.route + ".csv", schema.columns, rows);
        UI.toast("تم إنشاء ملف CSV.", "success");
      }
      if (event.target.closest("[data-apply-filter]"))
        UI.toast("تم تطبيق فلاتر العرض على التقرير.", "success");
    };
  }
  function invoiceLookup(screen, container) {
    var sales = S.store.all("sales");
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<section class="card"><div class="card-body"><form id="lookupForm" class="filter-bar"><div class="field grow"><label for="invoiceNumber">رقم الفاتورة</label><input class="input ltr" id="invoiceNumber" placeholder="SAL-261020-001"></div><button class="button primary" type="submit">بحث</button></form><div id="lookupResult" class="empty-state"><strong>ابحث برقم الفاتورة</strong><span>ستظهر التفاصيل هنا دون تعديل بيانات دائمة.</span></div></div></section>';
    container.querySelector("#lookupForm").onsubmit = function (event) {
      event.preventDefault();
      var query = U.normalize(container.querySelector("#invoiceNumber").value),
        sale = sales.find(function (item) {
          return U.normalize(item.invoice) === query;
        }),
        host = container.querySelector("#lookupResult");
      if (!sale) {
        host.className = "empty-state";
        host.innerHTML =
          "<strong>لم يتم العثور على الفاتورة</strong><span>تحقق من الرقم ثم حاول مرة أخرى.</span>";
        return;
      }
      host.className = "";
      host.innerHTML =
        '<div class="stats-grid"><article class="card stat-card"><span class="stat-label">الإجمالي</span><strong class="stat-value">' +
        U.money(sale.total) +
        '</strong></article><article class="card stat-card"><span class="stat-label">العميل</span><strong style="display:block;margin-top:12px">' +
        U.escape(sale.customer) +
        '</strong></article><article class="card stat-card"><span class="stat-label">الحالة</span><div style="margin-top:12px">' +
        UI.badge(sale.status) +
        '</div></article><article class="card stat-card"><span class="stat-label">الدفع</span><strong style="display:block;margin-top:12px">' +
        U.escape(sale.payment) +
        '</strong></article></div><button class="button" data-mock-edit>فتح تعديل تجريبي</button>';
      host.querySelector("[data-mock-edit]").onclick = function () {
        UI.toast("تم فتح الفاتورة في وضع تعديل تجريبي فقط.", "warning");
      };
    };
  }
  function workflow(screen, container) {
    var entity = screen.data,
      schema = schemaFor(entity),
      rows = S.store.all(entity);
    var title =
      screen.workflow === "expiry"
        ? "تحديد الصلاحية"
        : screen.workflow === "expense-categories"
          ? "تصنيفات المصروف"
          : screen.workflow === "branch-finance"
            ? "تحويل بين الفروع"
            : screen.workflow === "unit"
              ? "تعريف وحدة"
              : "قائمة المراجعة";
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<section class="card"><div class="card-head"><h2>' +
      U.escape(title) +
      '</h2><button class="button primary" data-workflow-action>تنفيذ تجريبي</button></div><div class="card-body">' +
      (schema.columns.length
        ? rowsTable(schema.columns.slice(0, 6), rows.slice(0, 8), false)
        : '<div class="form-grid">' +
          UI.field({ id: "name", label: "الاسم", required: true }, "") +
          UI.field(
            { id: "notes", label: "ملاحظات", type: "textarea", span: true },
            "",
          ) +
          "</div>") +
      "</div></section>";
    container.querySelector("[data-workflow-action]").onclick = function () {
      UI.confirm({
        title: "تأكيد محاكاة",
        message: "سيتم تحديث حالة العرض داخل الجلسة فقط.",
        confirmText: "متابعة",
      }).then(function (confirmed) {
        if (confirmed) UI.toast("اكتملت المحاكاة دون ترحيل دائم.", "success");
      });
    };
  }
  function invoiceEditor(screen, container, context) {
    var products = S.store.all("products"),
      state = context.state;
    state.lines = state.lines || [
      {
        productId: products[0].id,
        quantity: 2,
        price: products[0].purchasePrice,
      },
      {
        productId: products[2].id,
        quantity: 4,
        price: products[2].purchasePrice,
      },
    ];
    state.discount = state.discount || 0;
    function draw() {
      var totals = C.invoice(state.lines, state.discount, 0);
      container.innerHTML =
        UI.pageHeader(
          screen,
          '<button class="button" data-hold>حفظ كمسودة تجريبية</button>',
        ) +
        UI.mockBanner() +
        '<div class="content-grid invoice-editor-layout"><section class="card"><div class="card-head"><h2>أصناف فاتورة الشراء</h2><button class="button primary" data-add-line>+ إضافة صنف</button></div><div class="card-body"><div class="form-grid"><div class="field"><label>المورد</label><select class="select">' +
        S.store
          .all("suppliers")
          .map(function (supplier) {
            return "<option>" + U.escape(supplier.name) + "</option>";
          })
          .join("") +
        '</select></div><div class="field"><label>رقم فاتورة المورد</label><input class="input ltr" value="SUP-INV-8841"></div></div><div class="table-wrap invoice-lines" style="margin-top:12px"><table class="data-table"><thead><tr><th>الصنف</th><th>الكمية</th><th>سعر الشراء</th><th>الصلاحية</th><th>الإجمالي</th><th></th></tr></thead><tbody>' +
        state.lines
          .map(function (line, index) {
            var product = products.find(function (item) {
              return item.id === line.productId;
            });
            return (
              '<tr data-line="' +
              index +
              '"><td><select class="select" data-line-field="productId">' +
              products
                .map(function (item) {
                  return (
                    '<option value="' +
                    item.id +
                    '"' +
                    (item.id === line.productId ? " selected" : "") +
                    ">" +
                    U.escape(item.name) +
                    "</option>"
                  );
                })
                .join("") +
              '</select></td><td><input class="input ltr" type="number" min="1" data-line-field="quantity" value="' +
              line.quantity +
              '"></td><td><input class="input ltr" type="number" min="0" step="0.01" data-line-field="price" value="' +
              line.price +
              '"></td><td><input class="input" type="date" value="' +
              (product.expiry === "—" ? "" : product.expiry) +
              '"></td><td>' +
              U.money(line.quantity * line.price) +
              '</td><td><button class="table-action danger-text" data-remove-line>حذف</button></td></tr>'
            );
          })
          .join("") +
        '</tbody></table></div></div></section><aside class="card"><div class="card-head"><h2>ملخص الفاتورة</h2></div><div class="card-body"><div class="field"><label>خصم الفاتورة</label><input class="input ltr" id="purchaseDiscount" type="number" min="0" step="0.01" value="' +
        state.discount +
        '"></div><div class="totals"><div class="total-row"><span>الإجمالي الفرعي</span><strong>' +
        U.money(totals.subtotal) +
        '</strong></div><div class="total-row"><span>الخصم</span><strong>' +
        U.money(totals.discount) +
        '</strong></div><div class="total-row grand"><span>الإجمالي</span><strong>' +
        U.money(totals.total) +
        '</strong></div></div><button class="button primary" style="width:100%;margin-top:14px" data-confirm-purchase>معاينة التأكيد</button></div></aside></div>';
      bind();
    }
    function bind() {
      container.querySelectorAll("[data-line-field]").forEach(function (input) {
        input.onchange = function () {
          var index = Number(this.closest("tr").getAttribute("data-line")),
            key = this.getAttribute("data-line-field");
          state.lines[index][key] =
            key === "productId" ? Number(this.value) : Number(this.value);
          if (key === "productId")
            state.lines[index].price = products.find(function (item) {
              return item.id === Number(input.value);
            }).purchasePrice;
          draw();
        };
      });
      container.onclick = function (event) {
        var row = event.target.closest("tr[data-line]");
        if (event.target.closest("[data-add-line]")) {
          state.lines.push({
            productId: products[0].id,
            quantity: 1,
            price: products[0].purchasePrice,
          });
          draw();
        } else if (event.target.closest("[data-remove-line]") && row) {
          state.lines.splice(Number(row.getAttribute("data-line")), 1);
          draw();
        } else if (event.target.closest("[data-hold]"))
          UI.toast("حُفظت المسودة في ذاكرة الجلسة فقط.", "success");
        else if (event.target.closest("[data-confirm-purchase]"))
          UI.toast(
            "هذه معاينة تأكيد؛ لم يتغير المخزون أو حساب المورد.",
            "warning",
          );
      };
      container.querySelector("#purchaseDiscount").onchange = function () {
        state.discount = Number(this.value || 0);
        draw();
      };
    }
    draw();
  }
  function picker(screen, container) {
    var rows = S.store.all(screen.data);
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<div class="filter-bar"><div class="field grow"><label>بحث المورد</label><input class="input" id="pickerSearch" placeholder="اسم المورد أو الهاتف"></div></div><div class="stats-grid" id="pickerGrid">' +
      rows
        .map(function (row) {
          return (
            '<button class="card stat-card" data-picker="' +
            row.id +
            '" style="text-align:start"><span class="stat-label">' +
            U.escape(row.code) +
            '</span><strong style="display:block;margin:10px 0">' +
            U.escape(row.name) +
            '</strong><span class="muted">مستحق: ' +
            U.money(row.payable) +
            "</span></button>"
          );
        })
        .join("") +
      "</div>";
    container.onclick = function (event) {
      var selected = event.target.closest("[data-picker]");
      if (selected)
        UI.toast("تم اختيار المورد لفترة العمل الحالية.", "success");
    };
    var search = container.querySelector("#pickerSearch");
    search.oninput = function () {
      var needle = U.normalize(this.value);
      container.querySelectorAll("[data-picker]").forEach(function (card) {
        card.hidden =
          needle && U.normalize(card.textContent).indexOf(needle) < 0;
      });
    };
  }
  function returns(screen, container) {
    var invoices = S.store.all(
        screen.returnType === "sales" ? "sales" : "purchases",
      ),
      products = S.store.all("products");
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<section class="card"><div class="card-head"><h2>بيانات المرتجع</h2></div><div class="card-body"><form id="returnForm" class="form-grid"><div class="field"><label>الفاتورة الأصلية</label><select class="select">' +
      invoices
        .map(function (invoice) {
          return "<option>" + U.escape(invoice.invoice) + "</option>";
        })
        .join("") +
      '</select></div><div class="field"><label>الصنف</label><select class="select">' +
      products
        .map(function (product) {
          return "<option>" + U.escape(product.name) + "</option>";
        })
        .join("") +
      "</select></div>" +
      UI.field(
        {
          id: "quantity",
          label: "الكمية المرتجعة",
          type: "number",
          min: 1,
          required: true,
        },
        1,
      ) +
      UI.field(
        {
          id: "reason",
          label: "سبب المرتجع",
          type: "select",
          options: [
            "عبوة تالفة",
            "صنف غير مطلوب",
            "صلاحية قصيرة",
            "خطأ في الفاتورة",
          ],
        },
        "عبوة تالفة",
      ) +
      UI.field(
        {
          id: "method",
          label: "طريقة التسوية",
          type: "select",
          options: ["نقدي", "رصيد عميل", "رصيد مورد"],
        },
        "نقدي",
      ) +
      '<div class="span-2"><button class="button primary" type="submit">مراجعة المرتجع</button></div></form></div></section><section class="card" style="margin-top:12px"><div class="card-head"><h2>سجل المرتجعات</h2></div><div class="card-body">' +
      rowsTable(schemaFor("returns").columns, S.store.all("returns"), false) +
      "</div></section>";
    container.querySelector("#returnForm").onsubmit = function (event) {
      event.preventDefault();
      UI.confirm({
        title: "تأكيد مرتجع تجريبي",
        message: "لن تتغير أرصدة المخزون أو الحسابات الفعلية.",
        confirmText: "تأكيد المحاكاة",
      }).then(function (confirmed) {
        if (confirmed) UI.toast("تمت محاكاة المرتجع داخل الجلسة.", "success");
      });
    };
  }
  function printer(screen, container) {
    container.innerHTML =
      UI.pageHeader(
        screen,
        '<button class="button" data-test-preview>اختبار معاينة</button>',
      ) +
      UI.mockBanner("<span>لن يتم تثبيت تعريف أو تغيير طابعة Windows.</span>") +
      '<div class="content-grid"><section class="card"><div class="card-head"><h2>إعدادات الاتصال</h2></div><div class="card-body form-grid">' +
      UI.field(
        {
          id: "printer",
          label: "الطابعة",
          type: "select",
          options: ["طابعة تجريبية — غير متصلة", "حفظ PDF — معاينة"],
        },
        "طابعة تجريبية — غير متصلة",
      ) +
      UI.field(
        {
          id: "paper",
          label: "مقاس الورق",
          type: "select",
          options: ["80 مم", "58 مم", "A4"],
        },
        "80 مم",
      ) +
      UI.field(
        { id: "copies", label: "عدد النسخ", type: "number", min: 1 },
        1,
      ) +
      UI.field(
        {
          id: "density",
          label: "الكثافة",
          type: "select",
          options: ["خفيفة", "متوسطة", "عالية"],
        },
        "متوسطة",
      ) +
      '</div></section><aside class="card"><div class="card-head"><h2>المعاينة</h2></div><div class="card-body"><div class="preview-paper"><div class="print-brand"><img src="assets/alkhayal-mark.png" alt="شعار الخيال"><strong>الخيال</strong><small>مع الخيال شغلك أسرع · آمن · أذكى</small></div><p>فاتورة تجريبية</p><hr><small>لا توجد طباعة فعلية</small></div></div></aside></div>';
    container.querySelector("[data-test-preview]").onclick = function () {
      UI.toast("المعاينة جاهزة؛ لم يُرسل أمر إلى أي جهاز.", "warning");
    };
  }
  function barcode(screen, container) {
    var product = S.store.all("products")[0];
    container.innerHTML =
      UI.pageHeader(
        screen,
        '<button class="button primary" data-barcode-preview>معاينة الملصقات</button>',
      ) +
      UI.mockBanner("<span>الطباعة الفعلية غير مفعلة.</span>") +
      '<div class="content-grid"><section class="card"><div class="card-head"><h2>خصائص الملصق</h2></div><div class="card-body form-grid"><div class="field"><label>المنتج</label><select class="select" id="barcodeProduct">' +
      S.store
        .all("products")
        .map(function (item) {
          return (
            '<option value="' +
            item.id +
            '">' +
            U.escape(item.name) +
            "</option>"
          );
        })
        .join("") +
      "</select></div>" +
      UI.field(
        {
          id: "labelSize",
          label: "المقاس",
          type: "select",
          options: ["40 × 25 مم", "50 × 30 مم", "60 × 40 مم"],
        },
        "40 × 25 مم",
      ) +
      UI.field(
        { id: "printQuantity", label: "عدد الملصقات", type: "number", min: 1 },
        12,
      ) +
      UI.field(
        {
          id: "alignment",
          label: "المحاذاة",
          type: "select",
          options: ["وسط", "يمين", "يسار"],
        },
        "وسط",
      ) +
      UI.field(
        {
          id: "showPrice",
          label: "إظهار السعر",
          type: "select",
          options: ["نعم", "لا"],
        },
        "نعم",
      ) +
      UI.field(
        { id: "fontSize", label: "حجم الخط", type: "number", min: 8 },
        12,
      ) +
      '</div></section><aside class="card"><div class="card-head"><h2>معاينة الباركود</h2></div><div class="card-body" style="text-align:center"><div class="barcode-preview" id="barcodePreview"><strong>' +
      U.escape(product.name) +
      '</strong><div class="barcode-bars"></div><span class="ltr">' +
      U.escape(product.barcode) +
      "</span><b>" +
      U.money(product.salePrice) +
      "</b></div></div></aside></div>";
    container.querySelector("#barcodeProduct").onchange = function () {
      var selected = S.store.find("products", this.value),
        preview = container.querySelector("#barcodePreview");
      preview.querySelector("strong").textContent = selected.name;
      preview.querySelector("span").textContent = selected.barcode;
      preview.querySelector("b").textContent = U.money(selected.salePrice);
    };
    container.querySelector("[data-barcode-preview]").onclick = function () {
      UI.toast("تم تحديث معاينة الملصق فقط.", "warning");
    };
  }
  function expenseForm(screen, container) {
    entityForm(
      Object.assign({}, screen, { renderer: "entity-form", mode: "add" }),
      container,
      { state: {} },
    );
  }
  function alerts(screen, container) {
    function draw() {
      var rows = S.store.all("notifications");
      container.innerHTML =
        UI.pageHeader(
          screen,
          '<button class="button" data-mark-all>تعليم الكل كمقروء</button>',
        ) +
        UI.mockBanner() +
        '<section class="card"><div class="card-body list">' +
        rows
          .map(function (row) {
            return (
              '<article class="list-item" data-alert="' +
              row.id +
              '"><div><div style="display:flex;gap:8px;align-items:center">' +
              UI.badge(row.severity) +
              "<strong>" +
              U.escape(row.title) +
              "</strong>" +
              (!row.read ? '<span class="badge info">جديد</span>' : "") +
              '</div><p class="muted" style="margin:6px 0 0">' +
              U.escape(row.detail) +
              " · " +
              U.dateTime(row.date) +
              '</p></div><button class="button ghost" data-toggle-read>' +
              (row.read ? "غير مقروء" : "مقروء") +
              "</button></article>"
            );
          })
          .join("") +
        "</div></section>";
      container.onclick = function (event) {
        if (event.target.closest("[data-mark-all]")) {
          rows.forEach(function (row) {
            S.updateMock("notifications", row.id, { read: true });
          });
          draw();
        }
        var item = event.target.closest("[data-alert]");
        if (item && event.target.closest("[data-toggle-read]")) {
          var row = S.store.find(
            "notifications",
            item.getAttribute("data-alert"),
          );
          S.updateMock("notifications", row.id, { read: !row.read });
          draw();
        }
      };
    }
    draw();
  }
  function threshold(screen, container) {
    var products = S.store.all("products");
    container.innerHTML =
      UI.pageHeader(
        screen,
        '<button class="button primary" data-save-threshold>حفظ مؤقت</button>',
      ) +
      UI.mockBanner() +
      rowsTable(
        [
          { key: "sku", label: "SKU", ltr: true },
          { key: "name", label: "الصنف" },
          { key: "stock", label: "الرصيد", number: true },
          { key: "minimum", label: "حد الطلب", number: true },
          { key: "warehouse", label: "المخزن" },
        ],
        products,
        false,
      );
    container.querySelector("[data-save-threshold]").onclick = function () {
      UI.toast("حُفظت حدود العرض لهذه الجلسة فقط.", "success");
    };
  }
  function operationForm(screen, container, type) {
    var products = S.store.all("products"),
      selector =
        '<select class="select" id="operationProduct">' +
        products
          .map(function (product) {
            return (
              '<option value="' +
              product.id +
              '">' +
              U.escape(product.name) +
              " — " +
              product.stock +
              "</option>"
            );
          })
          .join("") +
        "</select>",
      branches =
        screen.scope === "branches"
          ? ["الفرع الرئيسي", "فرع مدينة نصر", "فرع الجيزة"]
          : ["المخزن الرئيسي", "مخزن فرع مدينة نصر", "مخزن المرتجعات"];
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<section class="card"><div class="card-head"><h2>' +
      (type === "transfer" ? "مستند نقل" : "مستند تسوية") +
      '</h2></div><div class="card-body"><form id="operationForm" class="form-grid"><div class="field"><label>الصنف</label>' +
      selector +
      "</div>" +
      (type === "transfer"
        ? UI.field(
            {
              id: "source",
              label: "من",
              type: "select",
              options: branches,
              required: true,
            },
            branches[0],
          ) +
          UI.field(
            {
              id: "destination",
              label: "إلى",
              type: "select",
              options: branches.slice().reverse(),
              required: true,
            },
            branches[1],
          )
        : UI.field(
            {
              id: "adjustmentType",
              label: "نوع التسوية",
              type: "select",
              options: ["زيادة جردية", "عجز جردي"],
            },
            "عجز جردي",
          )) +
      UI.field(
        {
          id: "quantity",
          label: "الكمية",
          type: "number",
          min: 1,
          required: true,
        },
        1,
      ) +
      UI.field(
        {
          id: "reason",
          label: "السبب والمرجع",
          type: "textarea",
          span: true,
          required: true,
        },
        "",
      ) +
      '<div class="span-2"><button class="button primary" type="submit">مراجعة المحاكاة</button></div></form></div></section>';
    container.querySelector("#operationForm").onsubmit = function (event) {
      event.preventDefault();
      if (
        type === "transfer" &&
        this.elements.source.value === this.elements.destination.value
      ) {
        UI.toast("اختر وجهة مختلفة عن المصدر.", "danger");
        return;
      }
      UI.confirm({
        title: "مراجعة حركة مخزون تجريبية",
        message: "لن تُحدث هذه المحاكاة رصيدًا دائمًا.",
        confirmText: "تأكيد المحاكاة",
      }).then(function (confirmed) {
        if (confirmed)
          UI.toast("تم تسجيل الحركة في سياق العرض فقط.", "success");
      });
    };
  }
  function manufacturing(screen, container, context) {
    var products = S.store.all("products"),
      state = context.state;
    state.output = state.output || products[0].id;
    state.quantity = state.quantity || 10;
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<div class="content-grid"><section class="card"><div class="card-head"><h2>مكونات التركيبة</h2><button class="button" data-add-component>+ مكون</button></div><div class="card-body"><div class="list"><div class="list-item"><span>' +
      U.escape(products[1].name) +
      "</span><strong>2 × " +
      U.money(products[1].purchasePrice) +
      '</strong></div><div class="list-item"><span>' +
      U.escape(products[7].name) +
      "</span><strong>3 × " +
      U.money(products[7].purchasePrice) +
      '</strong></div></div></div></section><aside class="card"><div class="card-head"><h2>ناتج التصنيع</h2></div><div class="card-body form-grid"><div class="field span-2"><label>الصنف الناتج</label><select class="select">' +
      products
        .map(function (product) {
          return "<option>" + U.escape(product.name) + "</option>";
        })
        .join("") +
      "</select></div>" +
      UI.field(
        { id: "manufactureQty", label: "الكمية", type: "number", min: 1 },
        state.quantity,
      ) +
      '<div class="span-2"><button class="button primary" data-manufacture>محاكاة أمر تصنيع</button></div></div></aside></div>';
    container.onclick = function (event) {
      if (event.target.closest("[data-add-component]"))
        UI.toast("أُضيف صف مكون مؤقت إلى المعاينة.", "success");
      if (event.target.closest("[data-manufacture]"))
        UI.toast("المحاكاة لا تخصم أو تضيف مخزونًا دائمًا.", "warning");
    };
  }
  function attendance(screen, container) {
    var rows = S.store.all("employees");
    container.innerHTML =
      UI.pageHeader(
        screen,
        '<button class="button primary" data-attendance-action="in">تسجيل حضور تجريبي</button><button class="button" data-attendance-action="out">تسجيل انصراف تجريبي</button>',
      ) +
      UI.mockBanner("<span>لا يوجد اتصال بجهاز بصمة.</span>") +
      rowsTable(schemaFor("employees").columns, rows, false);
    container.onclick = function (event) {
      var action = event.target.closest("[data-attendance-action]");
      if (action)
        UI.toast(
          action.getAttribute("data-attendance-action") === "in"
            ? "تمت محاكاة الحضور."
            : "تمت محاكاة الانصراف.",
          "success",
        );
    };
  }
  function permissions(screen, container) {
    var areas =
      screen.area === "sales"
        ? [
            "فتح نقطة البيع",
            "تطبيق خصم",
            "تعليق فاتورة",
            "تأكيد فاتورة",
            "إنشاء مرتجع",
            "عرض الأرباح",
          ]
        : screen.area === "settings"
          ? [
              "تعديل الإعدادات",
              "معاينة الطباعة",
              "تغيير الطابعة",
              "سياسة النسخ",
              "إدارة الفروع",
              "سجل النظام",
            ]
          : ["العملاء", "الموردون", "المخزن", "الخزنة", "الموظفون", "التقارير"];
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner("<span>هذه الاختيارات لا تغيّر تخويل API.</span>") +
      '<div class="filter-bar"><div class="field"><label>المستخدم</label><select class="select">' +
      S.store
        .all("users")
        .map(function (user) {
          return "<option>" + U.escape(user.displayName) + "</option>";
        })
        .join("") +
      '</select></div><button class="button primary" data-save-permissions>حفظ نموذج الصلاحيات</button></div><div class="permission-grid">' +
      areas
        .map(function (area, index) {
          return (
            '<label class="permission-card switch"><input type="checkbox"' +
            (index < 4 ? " checked" : "") +
            "><span>" +
            U.escape(area) +
            "</span></label>"
          );
        })
        .join("") +
      "</div>";
    container.querySelector("[data-save-permissions]").onclick = function () {
      UI.toast("حُفظ نموذج الواجهة فقط؛ تخويل الخادم لم يتغير.", "warning");
    };
  }
  function installments(screen, container, context) {
    function esc(value) { return U.escape(value); }
    function money(value) { return U.money(value); }
    function localDate() { var date = new Date(); return date.getFullYear() + "-" + String(date.getMonth() + 1).padStart(2, "0") + "-" + String(date.getDate()).padStart(2, "0"); }
    function showFailure(message) { container.innerHTML = UI.pageHeader(screen) + '<div class="empty-state"><strong>تعذر تحميل بيانات التقسيط</strong><span>' + esc(message || "أعد المحاولة.") + '</span><button class="button" data-installment-retry>إعادة المحاولة</button></div>'; container.querySelector("[data-installment-retry]").onclick = function () { installments(screen, container, context); }; }
    function renderSale(customers, products) {
      container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-head"><h2>بيانات البيع بالتقسيط</h2></div><div class="card-body form-grid">' +
        '<div class="field"><label for="installmentCustomer">العميل</label><select class="select" id="installmentCustomer"><option value="">اختر العميل</option>' + customers.map(function (c) { return '<option value="'+esc(c.id)+'">'+esc(c.name)+'</option>'; }).join("") + '</select></div>' +
        '<div class="field"><label for="installmentProduct">المنتج</label><select class="select" id="installmentProduct"><option value="">اختر المنتج</option>' + products.map(function (p) { return '<option value="'+esc(p.id)+'" data-price="'+Number(p.salePrice||0)+'">'+esc(p.name)+'</option>'; }).join("") + '</select></div>' +
        '<label class="field">سعر المنتج<input class="input" id="installmentPrice" type="number" readonly></label><label class="field">المقدم<input class="input" id="installmentDown" type="number" min="0" step="0.01" value="0"></label>' +
        '<label class="field">نسبة الفائدة (%)<input class="input" id="installmentRate" type="number" min="0" step="0.01" value="0"></label><label class="field">عدد الأقساط<input class="input" id="installmentCount" type="number" min="1" max="120" step="1" value="6"></label>' +
        '<div class="field"><label for="installmentEvery">تكرار القسط</label><select class="select" id="installmentEvery"><option value="1">شهريًا</option><option value="2">كل شهرين</option><option value="3">كل 3 أشهر</option></select></div><label class="field">أول استحقاق<input class="input" id="installmentFirstDue" type="date"></label>' +
        '<div class="field span-2"><strong>إجمالي الأقساط: <span id="installmentTotal">0.00</span></strong>　<strong>قيمة القسط: <span id="installmentEach">0.00</span></strong></div><div class="span-2"><button class="button primary" data-save-installment>تسجيل بيع التقسيط</button></div></div></section><section class="card"><div class="card-head"><h2>جدول الأقساط المتوقع</h2></div><div class="card-body" id="installmentPreview"></div></section>';
      var product = container.querySelector("#installmentProduct"), price = container.querySelector("#installmentPrice"), busy = false;
      container.querySelector("#installmentFirstDue").value = localDate();
      product.onchange = function () { var option=product.options[product.selectedIndex]; price.value=option&&option.dataset.price||""; recalc(); };
      ["installmentDown","installmentRate","installmentCount","installmentEvery","installmentFirstDue"].forEach(function(id){container.querySelector("#"+id).oninput=recalc;container.querySelector("#"+id).onchange=recalc;});
      function values(){return {customerId:Number(container.querySelector("#installmentCustomer").value),productId:Number(product.value),productPrice:Number(price.value)||0,downPayment:Number(container.querySelector("#installmentDown").value)||0,interestRate:Number(container.querySelector("#installmentRate").value)||0,installmentCount:parseInt(container.querySelector("#installmentCount").value,10),intervalMonths:Number(container.querySelector("#installmentEvery").value),firstDueOn:container.querySelector("#installmentFirstDue").value};}
      function recalc() { var v=values(), financed=Math.round(Math.max(0,v.productPrice-v.downPayment)*(1+v.interestRate/100)*100)/100, schedule=C.installments(financed,0,Math.max(1,v.installmentCount||1),v.firstDueOn||localDate()); schedule.forEach(function(item,index){var date=new Date((v.firstDueOn||localDate())+"T12:00:00");date.setMonth(date.getMonth()+index*v.intervalMonths);item.dueDate=date.toISOString().slice(0,10);}); container.querySelector("#installmentTotal").textContent=money(financed);container.querySelector("#installmentEach").textContent=money(schedule.length?schedule[0].amount:0);container.querySelector("#installmentPreview").innerHTML=rowsTable([{key:"number",label:"القسط",number:true},{key:"dueDate",label:"الاستحقاق",date:true},{key:"amount",label:"القيمة",money:true}],schedule.map(function(x){x.id=x.number;return x;}),false); }
      recalc();
      container.querySelector("[data-save-installment]").onclick=function(){ if(busy)return; var v=values();if(!v.customerId||!v.productId||v.productPrice<=0||v.downPayment<0||v.downPayment>=v.productPrice||!Number.isInteger(v.installmentCount)||v.installmentCount<1||v.interestRate<0){UI.toast("اختر العميل والمنتج وتأكد من صحة بيانات العقد.","warning");return;} var body={productId:v.productId,productPrice:v.productPrice,downPayment:v.downPayment,interestRate:v.interestRate,installmentCount:v.installmentCount,intervalMonths:v.intervalMonths,firstDueOn:v.firstDueOn}, identity=JSON.stringify(body), storageKey="alkhayal_installment_sale_"+v.customerId+"_"+btoa(unescape(encodeURIComponent(identity))), operationKey=sessionStorage.getItem(storageKey);if(!operationKey){operationKey="installment-"+Date.now().toString(36)+"-"+Math.random().toString(36).slice(2,10);sessionStorage.setItem(storageKey,operationKey);}body.operationKey=operationKey;busy=true;var button=this;button.disabled=true;Api.request("/api/customers/"+v.customerId+"/installments",{method:"POST",body:JSON.stringify(body)}).then(function(contract){sessionStorage.removeItem(storageKey);UI.toast("تم حفظ عقد "+contract.contractNumber+" وإثباته في كشف حساب العميل.","success");button.disabled=false;busy=false;}).catch(function(error){UI.toast(error.message||"تعذر حفظ العقد؛ أعد المحاولة بنفس البيانات.","danger");button.disabled=false;busy=false;}); };
    }
    function renderPayments(customers) {
      container.innerHTML=UI.pageHeader(screen)+'<div class="field" style="max-width:520px"><label for="installmentCustomerFilter">العميل</label><select class="select" id="installmentCustomerFilter"><option value="">اختر العميل</option>'+customers.map(function(c){return '<option value="'+esc(c.id)+'">'+esc(c.name)+'</option>';}).join("")+'</select></div><div id="customerInstallments"></div>';
      var select=container.querySelector("#installmentCustomerFilter"), host=container.querySelector("#customerInstallments");select.onchange=function(){context.state.installmentContractId=null;load();};if(context.state.installmentCustomerId){select.value=String(context.state.installmentCustomerId);load();}
      function load(){var customerId=select.value;if(!customerId){host.innerHTML='<div class="empty-state">اختر عميلًا لعرض عقود وأقساط العميل.</div>';return;}host.innerHTML='<div class="empty-state">جارٍ تحميل الأقساط...</div>';Api.request("/api/customers/"+encodeURIComponent(customerId)+"/installments").then(draw).catch(function(error){host.innerHTML='<div class="empty-state">'+esc(error.message||"تعذر تحميل الأقساط.")+'</div>';});}
      function draw(contracts){if(context.state.installmentContractId)contracts=contracts.filter(function(contract){return Number(contract.id)===Number(context.state.installmentContractId);});if(!contracts.length){host.innerHTML='<div class="empty-state">لا توجد عقود تقسيط لهذا العميل.</div>';return;}host.innerHTML=contracts.map(function(contract){return '<section class="card"><div class="card-head"><h2>'+esc(contract.contractNumber)+' — '+esc(contract.productName)+'</h2></div><div class="card-body"><p>إجمالي العقد '+money(contract.totalAmount)+' — المقدم '+money(contract.downPayment)+' — الفائدة '+esc(contract.interestRate)+'%</p>'+rowsTable([{key:"number",label:"القسط",number:true},{key:"dueDate",label:"الاستحقاق",date:true},{key:"amount",label:"القيمة",money:true},{key:"status",label:"الحالة"},{key:"action",label:"إجراء"}],contract.schedule.map(function(item){item.id=item.number;item.action=item.status==="مسدد"?"مسدد":'<button class="button" data-pay="'+contract.customerId+':'+contract.id+':'+item.number+'">تسديد القسط</button>';return item;}),false)+'</div></section>';}).join("");host.querySelectorAll("td").forEach(function(cell){var match=/^<button class="button" data-pay="(\d+:\d+:\d+)">تسديد القسط<\/button>$/.exec(cell.textContent);if(match){var button=container.ownerDocument.createElement("button");button.className="button";button.setAttribute("data-pay",match[1]);button.textContent="تسديد القسط";cell.textContent="";cell.appendChild(button);}});host.querySelectorAll("[data-pay]").forEach(function(button){button.onclick=function(){var parts=button.getAttribute("data-pay").split(":"),today=localDate(),keyName="alkhayal_installment_pay_"+parts[1]+"_"+parts[2],key=sessionStorage.getItem(keyName);if(!key){key="installment-pay-"+Date.now().toString(36)+"-"+Math.random().toString(36).slice(2,10);sessionStorage.setItem(keyName,key);}button.disabled=true;Api.request("/api/customers/"+parts[0]+"/installments/"+parts[1]+"/payments/"+parts[2],{method:"POST",body:JSON.stringify({paidOn:today,operationKey:key})}).then(function(){sessionStorage.removeItem(keyName);UI.toast("تم حفظ سداد القسط وتحديث كشف حساب العميل.","success");load();}).catch(function(error){button.disabled=false;UI.toast(error.message||"تعذر تسجيل السداد.","danger");});};});}
    }
    container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state">جارٍ تحميل البيانات...</div>';
    Promise.all([Api.request("/api/customers"),screen.mode==="create"?Api.request("/api/catalog/products?isActive=true&page=1&pageSize=100"):Promise.resolve(null)]).then(function(data){if(context.isActive&&!context.isActive())return;var customers=(data[0]||[]).filter(function(c){return c.status==="نشط" || (context.state && Number(c.id)===Number(context.state.installmentCustomerId));});if(screen.mode==="create"){var page=data[1]||{};renderSale(customers,(page.items||[]).filter(function(p){return p.isActive&&!p.isArchived;}));}else renderPayments(customers);}).catch(function(error){if(!context.isActive||context.isActive())showFailure(error.message);});
  }
  function maintenanceIntake(screen, container, context) {
    var fields = [
      { id: "phone", label: "الهاتف", required: true, ltr: true },
      { id: "received", label: "تاريخ الاستلام", type: "date", required: true },
      {
        id: "device",
        label: "نوع الجهاز",
        type: "select",
        options: ["هاتف محمول", "حاسب محمول", "طابعة", "جهاز لوحي"],
      },
      { id: "model", label: "الموديل", required: true, ltr: true },
      { id: "serial", label: "الرقم التسلسلي", required: true, ltr: true },
      { id: "accessories", label: "الملحقات المستلمة", span: true },
      {
        id: "technician",
        label: "الفني",
        type: "select",
        options: ["محمود علاء", "أحمد ربيع"],
      },
      {
        id: "expected",
        label: "التسليم المتوقع",
        type: "date",
        required: true,
      },
      {
        id: "issue",
        label: "العطل المبلغ عنه",
        type: "textarea",
        span: true,
        required: true,
      },
      { id: "estimatedCost", label: "تكلفة الصيانة التقديرية", type: "number", min: 0, step: "0.01" },
      { id: "notes", label: "ملاحظات الاستلام", type: "textarea", span: true },
    ];
    function localDate(){var date=new Date();return date.getFullYear()+"-"+String(date.getMonth()+1).padStart(2,"0")+"-"+String(date.getDate()).padStart(2,"0");}
    function isCurrent(){return container.isConnected&&document.title===screen.title+" | الخيال POS";}
    function loadCustomers(){container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state"><span>جارٍ تحميل العملاء...</span><button class="button" data-maintenance-customer-retry>إعادة المحاولة</button></div>';var loadingRetry=container.querySelector("[data-maintenance-customer-retry]");if(loadingRetry)loadingRetry.onclick=loadCustomers;var timer;var timedRequest=new Promise(function(_,reject){timer=global.setTimeout(function(){var timeoutError=new Error("تأخر الرد من الخادم أثناء تحميل العملاء.");timeoutError.code="request_timeout";reject(timeoutError);},15000);});Promise.race([Api.request("/api/customers"),timedRequest]).then(function(customers){global.clearTimeout(timer);if(!isCurrent())return;customers=(customers||[]).filter(function(customer){return customer.status==="نشط";});if(!customers.length){container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state"><strong>لا يوجد عملاء نشطون</strong><span>أضف عميلًا أو فعّل عميلًا من صفحة العملاء أولًا.</span></div>';return;}container.innerHTML=UI.pageHeader(screen)+'<section class="card"><div class="card-head"><h2>تسجيل جهاز وبيانات استلامه</h2></div><div class="card-body"><form id="maintenanceForm" class="form-grid"><div class="field"><label for="maintenanceCustomer">العميل</label><select class="select" id="maintenanceCustomer" required><option value="">اختر العميل</option>'+customers.map(function(customer){return '<option value="'+customer.id+'" data-phone="'+U.escape(customer.phone||"")+'">'+U.escape(customer.name)+'</option>';}).join("")+'</select></div>'+fields.map(function(field){return UI.field(field,"");}).join("")+'<label class="field"><span>هل دفع العميل مقدمًا؟</span><span class="check-row"><input type="checkbox" id="maintenanceHasAdvance"> نعم، دفع مقدمًا</span></label><div class="field" id="maintenanceAdvanceWrap" hidden>'+UI.field({id:"advancePaid",label:"قيمة المقدم",type:"number",min:0.01,step:"0.01"},"")+'</div><div class="span-2"><button class="button primary" type="submit">تسجيل الجهاز للصيانة</button></div></form></div></section>';
      var form=container.querySelector("#maintenanceForm"),customerSelect=container.querySelector("#maintenanceCustomer"),advanceCheck=container.querySelector("#maintenanceHasAdvance");container.querySelector("#received").value=localDate();container.querySelector("#expected").value=localDate();customerSelect.onchange=function(){var option=customerSelect.options[customerSelect.selectedIndex];container.querySelector("#phone").value=option&&option.dataset.phone||"";};advanceCheck.onchange=function(){container.querySelector("#maintenanceAdvanceWrap").hidden=!advanceCheck.checked;};
      form.onsubmit=function(event){event.preventDefault();var result=UI.collectForm(form,fields),customerId=Number(customerSelect.value),advance=advanceCheck.checked?Number(container.querySelector("#advancePaid").value)||0:0;if(!result.valid||!customerId){UI.toast("اختر العميل وأكمل البيانات المطلوبة.","warning");return;}if(advanceCheck.checked&&advance<=0){UI.toast("أدخل قيمة المقدم الذي دفعه العميل.","warning");return;}var body={customerId:customerId,phone:result.data.phone,device:result.data.device,model:result.data.model,serial:result.data.serial,accessories:result.data.accessories,issue:result.data.issue,technician:result.data.technician,estimatedCost:Number(result.data.estimatedCost)||0,advancePaid:advance,notes:result.data.notes,receivedOn:result.data.received,expectedOn:result.data.expected},identity=JSON.stringify(body),storageKey="alkhayal_maintenance_intake_"+btoa(unescape(encodeURIComponent(identity))),key=sessionStorage.getItem(storageKey);if(!key){key="maintenance-"+Date.now().toString(36)+"-"+Math.random().toString(36).slice(2,10);sessionStorage.setItem(storageKey,key);}body.operationKey=key;var button=form.querySelector('[type="submit"]');button.disabled=true;Api.request("/api/maintenance/tickets",{method:"POST",body:JSON.stringify(body)}).then(function(ticket){sessionStorage.removeItem(storageKey);UI.toast("تم حفظ تذكرة "+ticket.ticketNumber+" وقيمة المقدم في قاعدة البيانات.","success");form.reset();container.querySelector("#received").value=localDate();container.querySelector("#expected").value=localDate();container.querySelector("#maintenanceAdvanceWrap").hidden=true;button.disabled=false;}).catch(function(error){UI.toast(error.message||"تعذر حفظ تذكرة الصيانة.","danger");button.disabled=false;});};
    }).catch(function(error){global.clearTimeout(timer);if(isCurrent()){container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state"><strong>تعذر تحميل العملاء</strong><span>'+U.escape(error.message||"")+'</span><button class="button" data-maintenance-customer-retry>إعادة المحاولة</button></div>';var retry=container.querySelector("[data-maintenance-customer-retry]");if(retry)retry.onclick=loadCustomers;}});}
    loadCustomers();
  }
  function maintenanceTracking(screen, container, context) {
    var selectedCustomer=String(context.state.maintenanceCustomerId || "all"),statuses=["تم الاستلام","قيد الفحص","قيد الإصلاح","بانتظار قطعة","جاهز للتسليم"],customers=[];
    function localDate(){var date=new Date();return date.getFullYear()+"-"+String(date.getMonth()+1).padStart(2,"0")+"-"+String(date.getDate()).padStart(2,"0");}
    function draw(rows){if(context.state.maintenanceTicketId)rows=rows.filter(function(ticket){return Number(ticket.id)===Number(context.state.maintenanceTicketId);});var visible=selectedCustomer==="all"?rows:rows.filter(function(ticket){return String(ticket.customerId)===selectedCustomer;});container.innerHTML=UI.pageHeader(screen)+'<div class="toolbar"><div class="field" style="max-width:420px"><label for="maintenanceCustomerFilter">العميل</label><select class="select" id="maintenanceCustomerFilter"><option value="all">كل العملاء</option>'+customers.map(function(customer){return '<option value="'+customer.id+'"'+(String(customer.id)===selectedCustomer?' selected':'')+'>'+U.escape(customer.name)+'</option>';}).join("")+'</select></div><span class="muted">'+U.number(visible.length)+' تذكرة صيانة</span></div><div class="maintenance-ticket-list">'+visible.map(function(ticket){return '<section class="card maintenance-ticket" data-maintenance-id="'+ticket.id+'"><div class="card-head"><div><h2>'+U.escape(ticket.ticketNumber)+'</h2><p class="muted">'+U.escape(ticket.customerName)+' · '+U.escape(ticket.phone||"")+'</p></div>'+UI.badge(ticket.status)+'</div><div class="card-body form-grid"><div class="field"><label>الجهاز</label><strong>'+U.escape(ticket.device+" — "+ticket.model)+'</strong></div><div class="field"><label>الرقم التسلسلي</label><strong class="ltr">'+U.escape(ticket.serial||"—")+'</strong></div><div class="field span-2"><label>العطل</label><span>'+U.escape(ticket.issue||"—")+'</span></div><div class="field"><label>الفني</label><span>'+U.escape(ticket.technician||"—")+'</span></div><div class="field"><label>التسليم المتوقع</label><span>'+U.date(ticket.expectedOn)+'</span></div><div class="field"><label>الملحقات</label><span>'+U.escape(ticket.accessories||"—")+'</span></div><div class="field"><label>التكلفة التقديرية</label><span>'+U.money(ticket.estimatedCost)+'</span></div><div class="field"><label>المقدم</label><span>'+U.money(ticket.advancePaid)+'</span></div>'+(ticket.status==="تم التسليم"?'<div class="field"><label>التكلفة النهائية</label><span>'+U.money(ticket.finalCost||0)+' · '+(ticket.settlementType==="cash"?"نقدي":"آجل على حساب العميل")+'</span></div><div class="field"><label>تاريخ التسليم الفعلي</label><span>'+U.date(ticket.deliveredOn)+'</span></div>':'<div class="field"><label for="maintenanceStatus_'+ticket.id+'">الحالة</label><select class="select" id="maintenanceStatus_'+ticket.id+'" data-maintenance-status>'+statuses.map(function(status){return '<option value="'+status+'"'+(ticket.status===status?' selected':'')+'>'+status+'</option>';}).join("")+'</select></div><div class="row-actions"><button class="button" type="button" data-save-maintenance-status>حفظ الحالة</button>'+(ticket.status==="جاهز للتسليم"?'<label class="field">المبلغ النهائي<input class="input" type="number" min="0" step="0.01" data-final-cost value="'+Number(ticket.estimatedCost||0)+'"></label><label class="field">طريقة السداد<select class="select" data-settlement-type><option value="cash">نقدي</option><option value="installment">آجل على حساب العميل</option></select></label><button class="button primary" type="button" data-deliver-maintenance>تسليم للعميل</button>':'')+'</div>')+'</div></section>';}).join("")+(visible.length?"":'<div class="empty-state">لا توجد أجهزة مسجلة لهذا العميل.</div>')+'</div>';container.querySelector("#maintenanceCustomerFilter").onchange=function(){selectedCustomer=this.value;context.state.maintenanceTicketId=null;load();};
      container.querySelectorAll("[data-save-maintenance-status]").forEach(function(button){button.onclick=function(){var card=button.closest("[data-maintenance-id]"),id=card.getAttribute("data-maintenance-id"),status=card.querySelector("[data-maintenance-status]").value,key="maintenance-status-"+id+"-"+Date.now().toString(36);button.disabled=true;Api.request("/api/maintenance/tickets/"+id+"/status",{method:"PUT",body:JSON.stringify({status:status,operationKey:key})}).then(function(){UI.toast("تم حفظ حالة الصيانة.","success");load();}).catch(function(error){button.disabled=false;UI.toast(error.message||"تعذر تحديث الحالة.","danger");});};});
      container.querySelectorAll("[data-deliver-maintenance]").forEach(function(button){button.onclick=function(){var card=button.closest("[data-maintenance-id]"),id=card.getAttribute("data-maintenance-id"),body={finalCost:Number(card.querySelector("[data-final-cost]").value)||0,settlementType:card.querySelector("[data-settlement-type]").value,deliveredOn:localDate()},identity=JSON.stringify(body),storageKey="alkhayal_maintenance_delivery_"+id,stored;try{stored=JSON.parse(sessionStorage.getItem(storageKey)||"null");}catch(e){stored=null;}if(!stored||stored.identity!==identity){stored={identity:identity,key:"maintenance-delivery-"+Date.now().toString(36)+"-"+Math.random().toString(36).slice(2,10)};sessionStorage.setItem(storageKey,JSON.stringify(stored));}body.operationKey=stored.key;button.disabled=true;Api.request("/api/maintenance/tickets/"+id+"/deliver",{method:"POST",body:JSON.stringify(body)}).then(function(){sessionStorage.removeItem(storageKey);UI.toast(body.settlementType==="cash"?"تم تسجيل التسليم والسداد النقدي.":"تم تسجيل التسليم وإضافة المديونية إلى كشف حساب العميل.","success");load();}).catch(function(error){button.disabled=false;UI.toast(error.message||"تعذر تسجيل التسليم.","danger");});};});
    }
    function load(){container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state">جارٍ تحميل تذاكر الصيانة...</div>';Api.request("/api/maintenance/tickets"+(selectedCustomer==="all"?"":"?customerId="+encodeURIComponent(selectedCustomer))).then(function(rows){if(context.isActive&&!context.isActive())return;draw(rows||[]);}).catch(function(error){if(!context.isActive||context.isActive())container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state"><strong>تعذر تحميل تذاكر الصيانة</strong><span>'+U.escape(error.message||"")+'</span><button class="button" data-maintenance-retry>إعادة المحاولة</button></div>';var retry=container.querySelector("[data-maintenance-retry]");if(retry)retry.onclick=load;});}
    container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state">جارٍ تحميل العملاء...</div>';Api.request("/api/customers").then(function(rows){if(context.isActive&&!context.isActive())return;customers=(rows||[]).filter(function(customer){return customer.status==="نشط";});load();}).catch(function(error){if(!context.isActive||context.isActive())container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state"><strong>تعذر تحميل العملاء</strong><span>'+U.escape(error.message||"")+'</span></div>';});
  }
  function cashServices(screen, container) {
    var services = [
        { name: "شحن رصيد محمول", provider: "مزود غير متصل", fee: 2 },
        { name: "دفع فاتورة مرافق", provider: "مزود غير متصل", fee: 5 },
        { name: "تحصيل اشتراك", provider: "مزود غير متصل", fee: 3 },
      ],
      selected = 0;
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner("<span>لا يوجد اتصال بأي بوابة دفع أو مزود خدمات.</span>") +
      '<div class="stats-grid">' +
      services
        .map(function (service, index) {
          return (
            '<button class="card stat-card' +
            (index === 0 ? " active" : "") +
            '" data-cash-service="' +
            index +
            '" style="text-align:start"><span class="stat-label">' +
            U.escape(service.provider) +
            '</span><strong style="display:block;margin:12px 0">' +
            U.escape(service.name) +
            '</strong><span class="muted">رسوم عرض: ' +
            U.money(service.fee) +
            "</span></button>"
          );
        })
        .join("") +
      '</div><section class="card"><div class="card-head"><h2>بيانات العملية التجريبية</h2></div><div class="card-body form-grid">' +
      UI.field({ id: "serviceNumber", label: "رقم الخدمة", ltr: true }, "") +
      UI.field(
        { id: "serviceAmount", label: "القيمة", type: "number", min: 1, step: "0.01" },
        "",
      ) +
      '<div class="span-2"><button class="button primary" data-cash-preview>معاينة فقط</button></div></div></section>';
    container.onclick = function (event) {
      var card = event.target.closest("[data-cash-service]");
      if (card) {
        selected = Number(card.getAttribute("data-cash-service"));
        container
          .querySelectorAll("[data-cash-service]")
          .forEach(function (item) {
            item.classList.toggle("active", item === card);
          });
        UI.toast("تم اختيار خدمة " + services[selected].name + ".", "info");
      }
      if (event.target.closest("[data-cash-preview]"))
        UI.toast(
          "معاينة " +
            services[selected].name +
            " فقط؛ الخدمة تجريبية وغير متصلة.",
          "warning",
        );
    };
  }
  function underConstruction(screen, container) {
    container.innerHTML =
      UI.pageHeader(screen) +
      '<section class="card"><div class="empty-state under-construction" role="status"><strong>هذه الشاشة قيد الإنشاء</strong><span>ستتوفر هذه الوظيفة في تحديث لاحق. لم يتم تنفيذ أي عملية أو تغيير أي بيانات.</span></div></section>';
  }
  global.AlKhayalRenderers = {
    schemas: schemas,
    "entity-list": entityList,
    "entity-form": entityForm,
    settings: settings,
    statement: statement,
    payment: payment,
    export: exportScreen,
    report: report,
    "invoice-lookup": invoiceLookup,
    workflow: workflow,
    "invoice-editor": invoiceEditor,
    picker: picker,
    returns: returns,
    printer: printer,
    barcode: barcode,
    "expense-form": expenseForm,
    alerts: alerts,
    threshold: threshold,
    adjustment: function (screen, container) {
      operationForm(screen, container, "adjustment");
    },
    transfer: function (screen, container) {
      operationForm(screen, container, "transfer");
    },
    manufacturing: manufacturing,
    attendance: attendance,
    permissions: permissions,
    installments: installments,
    "maintenance-intake": maintenanceIntake,
    "maintenance-tracking": maintenanceTracking,
    "cash-services": cashServices,
    "under-construction": underConstruction,
  };
})(window);
