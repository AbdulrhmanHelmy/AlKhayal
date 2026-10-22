(function (global) {
  "use strict";
  var U = global.AlKhayalUtils,
    UI = global.AlKhayalUI,
    S = global.AlKhayalServices,
    C = global.AlKhayalCalculations;
  var schemas = {
    customers: {
      columns: [
        { key: "code", label: "الكود", ltr: true },
        { key: "name", label: "اسم العميل" },
        { key: "phone", label: "الهاتف", ltr: true },
        { key: "address", label: "العنوان" },
        { key: "balance", label: "الرصيد", money: true },
        { key: "creditLimit", label: "حد الائتمان", money: true },
        { key: "status", label: "الحالة", badge: true },
      ],
      fields: [
        { id: "code", label: "كود العميل", required: true, ltr: true },
        { id: "name", label: "اسم العميل", required: true },
        { id: "phone", label: "رقم الهاتف", required: true, ltr: true },
        { id: "address", label: "العنوان" },
        { id: "balance", label: "الرصيد الافتتاحي", type: "number", min: 0 },
        { id: "creditLimit", label: "حد الائتمان", type: "number", min: 0 },
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
        { id: "payable", label: "الرصيد المستحق", type: "number", min: 0 },
        { id: "paid", label: "المدفوع", type: "number", min: 0 },
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
        },
        {
          id: "purchasePrice",
          label: "سعر الشراء",
          type: "number",
          required: true,
          min: 0,
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
        { id: "balance", label: "الرصيد الافتتاحي", type: "number", min: 0 },
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
    if (!fields.length) {
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
      persistent = entity === "customers";
    var layer = UI.modal({
      title: isEdit
        ? persistent
          ? "تعديل بيانات العميل"
          : "تعديل سجل تجريبي"
        : persistent
          ? "إضافة عميل"
          : "إضافة سجل تجريبي",
      body:
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
      button.disabled = true;
      try {
        if (persistent) {
          await AlKhayalApi.request(
            isEdit
              ? "/api/customers/" + encodeURIComponent(record.id)
              : "/api/customers",
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
            ? "تم حفظ بيانات العميل في قاعدة البيانات."
            : "تم حفظ التغيير مؤقتًا داخل جلسة العرض.",
          "success",
        );
        if (afterSave) afterSave();
      } catch (error) {
        button.disabled = false;
        UI.toast(error.message || "تعذر حفظ بيانات العميل.", "danger");
      }
    };
  }
  function entityList(screen, container, context, dataLoaded) {
    var entity = screen.data,
      schema = schemaFor(entity),
      state = context.state;
    if (entity === "customers" && !dataLoaded) {
      container.innerHTML =
        UI.pageHeader(screen) +
        '<section class="card"><div class="card-body">جارٍ تحميل بيانات العملاء من قاعدة البيانات...</div></section>';
      AlKhayalApi.request("/api/customers")
        .then(function (rows) {
          S.store.replace("customers", rows);
          entityList(screen, container, context, true);
        })
        .catch(function (error) {
          container.innerHTML =
            UI.pageHeader(screen) +
            '<section class="card"><div class="card-body"><strong>تعذر تحميل العملاء من قاعدة البيانات</strong><p class="muted">' +
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
    var pages = Math.max(1, Math.ceil(allRows.length / state.pageSize));
    state.page = Math.min(state.page, pages);
    var rows = allRows.slice(
        (state.page - 1) * state.pageSize,
        state.page * state.pageSize,
      ),
      canAdd = !!schema.fields;
    container.innerHTML =
      UI.pageHeader(
        screen,
        (canAdd
          ? '<button class="button primary" data-list-add>+ إضافة</button>'
          : "") + '<button class="button" data-list-export>تصدير CSV</button>',
      ) +
      (entity === "customers"
        ? ""
        : UI.mockBanner(
            '<button class="button ghost" data-reset-mocks>إعادة بيانات العرض</button>',
          )) +
      '<div class="toolbar"><div class="field grow"><label for="listSearch">بحث</label><input class="input" id="listSearch" value="' +
      U.escape(state.query) +
      '" placeholder="ابحث في السجلات…"></div><span class="muted">' +
      U.number(allRows.length) +
      " سجل</span></div><div data-table-host>" +
      rowsTable(schema.columns, rows, true) +
      '</div><div class="pagination"><span>صفحة ' +
      state.page +
      " من " +
      pages +
      '</span><div class="pagination-buttons"><button class="button" data-page="prev"' +
      (state.page <= 1 ? " disabled" : "") +
      '>السابق</button><button class="button" data-page="next"' +
      (state.page >= pages ? " disabled" : "") +
      ">التالي</button></div></div>";
    var search = container.querySelector("#listSearch");
    search.addEventListener(
      "input",
      U.debounce(function () {
        state.query = search.value;
        state.page = 1;
        entityList(screen, container, context, true);
      }, 180),
    );
    container.onclick = async function (event) {
      var sort = event.target.closest("[data-sort]"),
        page = event.target.closest("[data-page]"),
        action = event.target.closest("[data-row-action]");
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
          entityList(
            screen,
            container,
            context,
            entity === "customers" ? false : true,
          );
        });
        return;
      }
      if (event.target.closest("[data-list-export]")) {
        U.downloadCsv(entity + ".csv", schema.columns, allRows);
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
            entityList(
              screen,
              container,
              context,
              entity === "customers" ? false : true,
            );
          });
        else if (
          operation === "delete" &&
          (await UI.confirm({
            title: entity === "customers" ? "حذف عميل" : "حذف سجل تجريبي",
            message:
              entity === "customers"
                ? "سيتم حذف العميل نهائيًا من قاعدة البيانات."
                : "سيُحذف هذا السجل من جلسة العرض الحالية فقط.",
            confirmText: "حذف",
            danger: true,
          }))
        ) {
          if (entity === "customers") {
            try {
              await AlKhayalApi.request(
                "/api/customers/" + encodeURIComponent(row.id),
                { method: "DELETE" },
              );
              UI.toast("تم حذف العميل من قاعدة البيانات.", "success");
              entityList(screen, container, context, false);
            } catch (error) {
              UI.toast(error.message || "تعذر حذف العميل.", "danger");
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
          S.store.replace("customers", rows);
          entityForm(screen, container, context, true);
        })
        .catch(function (error) {
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
      try {
        if (entity === "customers") {
          await AlKhayalApi.request(
            screen.mode === "edit"
              ? "/api/customers/" + encodeURIComponent(record.id)
              : "/api/customers",
            {
              method: screen.mode === "edit" ? "PUT" : "POST",
              body: JSON.stringify(result.data),
            },
          );
          UI.toast("تم حفظ بيانات العميل في قاعدة البيانات.", "success");
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
        options: ["الرئيسية", "نقطة بيع سريعة", "المبيعات"],
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
  function customerStatement(screen, container) {
    var state = { id: null, from: "", to: "" },
      seq = 0;
    var columns = [
      { key: "date", label: "التاريخ", date: true },
      { key: "reference", label: "المرجع", ltr: true },
      { key: "description", label: "البيان" },
      { key: "debit", label: "مدين", money: true },
      { key: "credit", label: "دائن", money: true },
      { key: "balance", label: "الرصيد", money: true },
    ];
    function failure(error, retry) {
      container.innerHTML =
        UI.pageHeader(screen) +
        '<section class="card"><div class="card-body"><strong>تعذر تحميل كشف الحساب من قاعدة البيانات</strong><p class="muted">' +
        U.escape(error.message || "تأكد من تسجيل الدخول وتشغيل الخادم.") +
        '</p><button class="button" data-retry-statement>إعادة المحاولة</button></div></section>';
      container.querySelector("[data-retry-statement]").onclick = retry;
    }
    function shell(customers) {
      container.innerHTML =
        UI.pageHeader(
          screen,
          '<button class="button" data-print-preview>معاينة الطباعة</button>',
        ) +
        '<div class="filter-bar"><div class="field grow"><label for="statementCustomer">العميل</label><select class="select" id="statementCustomer">' +
        customers
          .map(function (item) {
            return (
              '<option value="' +
              U.escape(item.id) +
              '"' +
              (Number(item.id) === Number(state.id) ? " selected" : "") +
              ">" +
              U.escape(item.name) +
              "</option>"
            );
          })
          .join("") +
        '</select></div><div class="field"><label for="statementFrom">من</label><input class="input" type="date" id="statementFrom" value="' +
        U.escape(state.from) +
        '"></div><div class="field"><label for="statementTo">إلى</label><input class="input" type="date" id="statementTo" value="' +
        U.escape(state.to) +
        '"></div><button class="button" type="button" id="statementReset">كل الفترات</button></div><div id="statementBody"><div class="card"><div class="card-body">جارٍ تحميل كشف الحساب...</div></div></div>';
      container.querySelector("#statementCustomer").onchange = function () {
        state.id = Number(this.value);
        load();
      };
      container.querySelector("#statementFrom").onchange = function () {
        state.from = this.value;
        applyDates();
      };
      container.querySelector("#statementTo").onchange = function () {
        state.to = this.value;
        applyDates();
      };
      container.querySelector("#statementReset").onclick = function () {
        state.from = "";
        state.to = "";
        container.querySelector("#statementFrom").value = "";
        container.querySelector("#statementTo").value = "";
        load();
      };
      container.onclick = function (event) {
        if (event.target.closest("[data-print-preview]"))
          UI.toast("معاينة فقط؛ لم يُرسل شيء إلى طابعة.", "warning");
      };
    }
    function applyDates() {
      if (state.from && state.to && state.from > state.to) {
        UI.toast("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.", "danger");
        return;
      }
      load();
    }
    function draw(data) {
      var host = container.querySelector("#statementBody");
      if (!host) return;
      var movements = data.movements || [];
      var rows = movements.map(function (item, index) {
        var isOpening = item.reference === "OPEN";
        return {
          id: index,
          date: isOpening && !data.from ? "—" : item.date,
          reference: item.reference,
          description:
            isOpening && !data.from ? "الرصيد قبل التحصيلات" : item.description,
          debit: item.debit,
          credit: item.credit,
          balance: item.balance,
        };
      });
      host.innerHTML =
        '<div class="stats-grid"><article class="card stat-card"><span class="stat-label">الإجمالي</span><strong class="stat-value">' +
        U.money(data.openingBalance) +
        '</strong></article><article class="card stat-card"><span class="stat-label">المبلغ المدفوع</span><strong class="stat-value">' +
        U.money(data.totalCredit) +
        '</strong></article><article class="card stat-card"><span class="stat-label">المبلغ المستحق</span><strong class="stat-value">' +
        U.money(data.closingBalance) +
        '</strong></article><article class="card stat-card"><span class="stat-label">حد الائتمان</span><strong class="stat-value">' +
        U.money(data.creditLimit) +
        "</strong></article></div>" +
        rowsTable(columns, rows, false) +
        (rows.length <= 1
          ? '<p class="muted" style="margin-top:12px">لا توجد تحصيلات في الفترة المحددة.</p>'
          : "");
    }
    function load() {
      var current = ++seq,
        query = [];
      if (state.from) query.push("from=" + encodeURIComponent(state.from));
      if (state.to) query.push("to=" + encodeURIComponent(state.to));
      var host = container.querySelector("#statementBody");
      if (host)
        host.innerHTML =
          '<div class="card"><div class="card-body">جارٍ تحميل كشف الحساب...</div></div>';
      AlKhayalApi.request(
        "/api/customers/" +
          encodeURIComponent(state.id) +
          "/statement" +
          (query.length ? "?" + query.join("&") : ""),
      )
        .then(function (data) {
          if (current === seq) draw(data);
        })
        .catch(function (error) {
          if (current !== seq) return;
          if (host)
            host.innerHTML =
              '<div class="card"><div class="card-body"><strong>تعذر تحميل كشف الحساب</strong><p class="muted">' +
              U.escape(error.message || "حاول مرة أخرى.") +
              "</p></div></div>";
        });
    }
    container.innerHTML =
      UI.pageHeader(screen) +
      '<section class="card"><div class="card-body">جارٍ تحميل العملاء من قاعدة البيانات...</div></section>';
    AlKhayalApi.request("/api/customers")
      .then(function (customers) {
        S.store.replace("customers", customers);
        if (!customers.length) {
          container.innerHTML =
            UI.pageHeader(screen) +
            '<section class="card"><div class="card-body">لا يوجد عملاء. أضف عميلًا أولًا ثم افتح كشف الحساب.</div></section>';
          return;
        }
        state.id = Number(customers[0].id);
        shell(customers);
        load();
      })
      .catch(function (error) {
        failure(error, function () {
          customerStatement(screen, container);
        });
      });
  }
  function statement(screen, container) {
    if (screen.party !== "supplier")
      return customerStatement(screen, container);
    var isSupplier = screen.party === "supplier",
      entity = isSupplier ? "suppliers" : "customers",
      parties = S.store.all(entity),
      party = parties[0],
      amount = isSupplier ? party.payable : party.balance,
      movements = [
        {
          date: "2026-10-20",
          reference: "INV-1008",
          description: isSupplier ? "فاتورة شراء" : "فاتورة مبيعات",
          debit: amount + 1250,
          credit: 0,
        },
        {
          date: "2026-10-18",
          reference: "PAY-443",
          description: "دفعة مسجلة",
          debit: 0,
          credit: 1250,
        },
        {
          date: "2026-10-15",
          reference: "OPEN",
          description: "رصيد سابق",
          debit: 1250,
          credit: 0,
        },
      ];
    function draw(selected) {
      party = S.store.find(entity, selected) || party;
      amount = isSupplier ? party.payable : party.balance;
      container.innerHTML =
        UI.pageHeader(
          screen,
          '<button class="button" data-print-preview>معاينة الطباعة</button>',
        ) +
        UI.mockBanner() +
        '<div class="filter-bar"><div class="field grow"><label>الطرف</label><select class="select" id="partySelect">' +
        parties
          .map(function (item) {
            return (
              '<option value="' +
              item.id +
              '"' +
              (item.id === party.id ? " selected" : "") +
              ">" +
              U.escape(item.name) +
              "</option>"
            );
          })
          .join("") +
        '</select></div><div class="field"><label>من</label><input class="input" type="date" value="2026-10-01"></div><div class="field"><label>إلى</label><input class="input" type="date" value="2026-10-20"></div></div><div class="stats-grid"><article class="card stat-card"><span class="stat-label">الرصيد الحالي</span><strong class="stat-value">' +
        U.money(amount) +
        '</strong></article><article class="card stat-card"><span class="stat-label">إجمالي المدين</span><strong class="stat-value">' +
        U.money(amount + 1250) +
        '</strong></article><article class="card stat-card"><span class="stat-label">إجمالي الدائن</span><strong class="stat-value">' +
        U.money(1250) +
        '</strong></article><article class="card stat-card"><span class="stat-label">حد الائتمان</span><strong class="stat-value">' +
        U.money(party.creditLimit || 0) +
        "</strong></article></div>" +
        rowsTable(
          [
            { key: "date", label: "التاريخ", date: true },
            { key: "reference", label: "المرجع", ltr: true },
            { key: "description", label: "البيان" },
            { key: "debit", label: "مدين", money: true },
            { key: "credit", label: "دائن", money: true },
          ],
          movements,
          false,
        );
      container.querySelector("#partySelect").onchange = function () {
        draw(this.value);
      };
      container.onclick = function (event) {
        if (event.target.closest("[data-print-preview]"))
          UI.toast("معاينة فقط؛ لم يُرسل شيء إلى طابعة.", "warning");
      };
    }
    draw(party.id);
  }
  function payment(screen, container) {
    var isSupplier = screen.party === "supplier";
    if (isSupplier) {
      container.innerHTML =
        UI.pageHeader(screen) +
        UI.mockBanner() +
        '<section class="card"><div class="card-body">حفظ دفعات الموردين في قاعدة البيانات غير متاح في هذا المسار.</div></section>';
      return;
    }
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
        '<section class="card"><div class="card-head"><h2>تسجيل تحصيل من العميل</h2></div><div class="card-body"><form id="paymentForm" class="form-grid"><div class="field"><label for="paymentParty">العميل</label><select class="select" id="paymentParty" required>' +
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
        UI.field(
          {
            id: "amount",
            label: "قيمة التحصيل",
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
            label: "تاريخ التحصيل",
            type: "date",
            required: true,
          },
          today,
        ) +
        UI.field(
          {
            id: "account",
            label: "حساب السداد",
            type: "select",
            options: ["الخزنة الرئيسية", "بنك مصر — حساب جاري"],
            required: true,
          },
          "الخزنة الرئيسية",
        ) +
        UI.field(
          {
            id: "method",
            label: "طريقة السداد",
            type: "select",
            options: ["نقدي", "تحويل", "بطاقة"],
            required: true,
          },
          "نقدي",
        ) +
        UI.field(
          { id: "notes", label: "ملاحظات", type: "textarea", span: true },
          "",
        ) +
        '<div class="span-2"><button class="button primary" type="submit">حفظ التحصيل في قاعدة البيانات</button></div></form><div id="paymentResult" class="muted" style="margin-top:12px">سيتم حفظ التحصيل وتحديث مديونية العميل عند نجاح العملية.</div></div></section>';
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
          UI.toast("أدخل قيمة تحصيل أكبر من صفر.", "danger");
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
            "/api/customers/" + encodeURIComponent(customerId) + "/payments",
            {
              method: "POST",
              body: JSON.stringify({
                amount: amount,
                paymentDate: form.querySelector("#paymentDate").value,
                method: form.querySelector("#method").value,
                account: form.querySelector("#account").value,
                notes: form.querySelector("#notes").value,
              }),
            },
          );
          var updatedRows = await AlKhayalApi.request("/api/customers");
          S.store.replace("customers", updatedRows);
          UI.toast(
            "تم حفظ التحصيل في قاعدة البيانات وتحديث رصيد العميل.",
            "success",
          );
          render(updatedRows);
          var result = container.querySelector("#paymentResult");
          if (result && saved)
            result.textContent =
              "رقم مرجع التحصيل: " + (saved.reference || "تم الحفظ");
        } catch (error) {
          button.disabled = false;
          button.textContent = "حفظ التحصيل في قاعدة البيانات";
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
              '"></td><td><input class="input ltr" type="number" min="0" data-line-field="price" value="' +
              line.price +
              '"></td><td><input class="input" type="date" value="' +
              (product.expiry === "—" ? "" : product.expiry) +
              '"></td><td>' +
              U.money(line.quantity * line.price) +
              '</td><td><button class="table-action danger-text" data-remove-line>حذف</button></td></tr>'
            );
          })
          .join("") +
        '</tbody></table></div></div></section><aside class="card"><div class="card-head"><h2>ملخص الفاتورة</h2></div><div class="card-body"><div class="field"><label>خصم الفاتورة</label><input class="input ltr" id="purchaseDiscount" type="number" min="0" value="' +
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
    var contracts = S.store.all("installments");
    if (screen.mode !== "create") {
      container.innerHTML =
        UI.pageHeader(screen) +
        UI.mockBanner() +
        '<div class="filter-bar"><div class="field grow"><label>العقد</label><select class="select" id="contractSelect">' +
        contracts
          .map(function (contract) {
            return (
              '<option value="' +
              contract.id +
              '">' +
              U.escape(contract.contract + " — " + contract.customer) +
              "</option>"
            );
          })
          .join("") +
        '</select></div><button class="button primary" data-installment-action>' +
        (screen.mode === "confirm" ? "تأكيد تجريبي" : "تسجيل دفعة تجريبية") +
        "</button></div>" +
        rowsTable(schemaFor("installments").columns, contracts, false);
      container.querySelector("[data-installment-action]").onclick =
        function () {
          UI.confirm({
            title: "مراجعة دفعة قسط",
            message: "سيتم تحديث حالة العرض فقط دون حركة خزنة حقيقية.",
            confirmText: "تأكيد المحاكاة",
          }).then(function (confirmed) {
            if (confirmed) UI.toast("تم تحديث جدول العرض مؤقتًا.", "success");
          });
        };
      return;
    }
    var state = context.state;
    state.amount = state.amount || 12000;
    state.down = state.down || 3000;
    state.count = state.count || 6;
    function drawSchedule() {
      var schedule = C.installments(
        state.amount,
        state.down,
        state.count,
        "2026-11-01",
      );
      var host = container.querySelector("#scheduleHost");
      if (host)
        host.innerHTML = rowsTable(
          [
            { key: "number", label: "القسط", number: true },
            { key: "dueDate", label: "الاستحقاق", date: true },
            { key: "amount", label: "القيمة", money: true },
            { key: "status", label: "الحالة", badge: true },
          ],
          schedule.map(function (item) {
            item.id = item.number;
            return item;
          }),
          false,
        );
    }
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<div class="content-grid"><section class="card"><div class="card-head"><h2>بيانات العقد</h2></div><div class="card-body form-grid"><div class="field span-2"><label>العميل</label><select class="select">' +
      S.store
        .all("customers")
        .filter(function (customer) {
          return customer.name !== "عميل نقدي";
        })
        .map(function (customer) {
          return "<option>" + U.escape(customer.name) + "</option>";
        })
        .join("") +
      "</select></div>" +
      UI.field(
        { id: "contractAmount", label: "قيمة العقد", type: "number", min: 1 },
        state.amount,
      ) +
      UI.field(
        { id: "downPayment", label: "المقدم", type: "number", min: 0 },
        state.down,
      ) +
      UI.field(
        {
          id: "installmentCount",
          label: "عدد الأقساط",
          type: "number",
          min: 1,
        },
        state.count,
      ) +
      UI.field(
        { id: "firstDue", label: "أول استحقاق", type: "date" },
        "2026-11-01",
      ) +
      '<div class="span-2"><button class="button primary" data-save-contract>حفظ عقد تجريبي</button></div></div></section><section class="card"><div class="card-head"><h2>جدول الأقساط</h2></div><div class="card-body" id="scheduleHost"></div></section></div>';
    drawSchedule();
    ["contractAmount", "downPayment", "installmentCount"].forEach(
      function (id) {
        container.querySelector("#" + id).onchange = function () {
          state.amount = Number(
            container.querySelector("#contractAmount").value,
          );
          state.down = Number(container.querySelector("#downPayment").value);
          state.count = Number(
            container.querySelector("#installmentCount").value,
          );
          drawSchedule();
        };
      },
    );
    container.querySelector("[data-save-contract]").onclick = function () {
      UI.toast("تم حفظ العقد وجدوله في ذاكرة الجلسة فقط.", "success");
    };
  }
  function maintenanceIntake(screen, container) {
    var fields = [
      { id: "customer", label: "اسم العميل", required: true },
      { id: "phone", label: "الهاتف", required: true, ltr: true },
      {
        id: "device",
        label: "نوع الجهاز",
        type: "select",
        options: ["هاتف محمول", "حاسب محمول", "طابعة", "جهاز لوحي"],
      },
      { id: "model", label: "الموديل", required: true, ltr: true },
      { id: "serial", label: "الرقم التسلسلي", required: true, ltr: true },
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
    ];
    container.innerHTML =
      UI.pageHeader(screen) +
      UI.mockBanner() +
      '<section class="card"><div class="card-head"><h2>بيانات الاستلام</h2></div><div class="card-body"><form id="maintenanceForm" class="form-grid">' +
      fields
        .map(function (field) {
          return UI.field(field, "");
        })
        .join("") +
      '<div class="span-2"><button class="button primary" type="submit">إنشاء تذكرة تجريبية</button></div></form></div></section>';
    container.querySelector("#maintenanceForm").onsubmit = function (event) {
      event.preventDefault();
      var result = UI.collectForm(this, fields);
      if (!result.valid) return;
      result.data.ticket = "MNT-DEMO-" + Date.now().toString().slice(-4);
      result.data.received = "2026-10-20";
      result.data.status = "تم الاستلام";
      S.createMock("maintenance", result.data);
      UI.toast("أُنشئت تذكرة مؤقتة: " + result.data.ticket, "success");
      this.reset();
    };
  }
  function maintenanceTracking(screen, container) {
    var tickets = S.store.all("maintenance");
    function draw(id) {
      var ticket = S.store.find("maintenance", id) || tickets[0];
      container.innerHTML =
        UI.pageHeader(screen) +
        UI.mockBanner() +
        '<div class="filter-bar"><div class="field grow"><label>رقم التذكرة</label><select class="select" id="ticketSelect">' +
        tickets
          .map(function (item) {
            return (
              '<option value="' +
              item.id +
              '"' +
              (item.id === ticket.id ? " selected" : "") +
              ">" +
              U.escape(item.ticket + " — " + item.customer) +
              "</option>"
            );
          })
          .join("") +
        '</select></div><button class="button" data-advance-status>تقدم الحالة تجريبيًا</button></div><div class="content-grid"><section class="card"><div class="card-head"><h2>' +
        U.escape(ticket.ticket) +
        "</h2>" +
        UI.badge(ticket.status) +
        '</div><div class="card-body form-grid"><div class="field"><label>العميل</label><strong>' +
        U.escape(ticket.customer) +
        '</strong></div><div class="field"><label>الجهاز</label><strong>' +
        U.escape(ticket.device + " — " + ticket.model) +
        '</strong></div><div class="field"><label>العطل</label><strong>' +
        U.escape(ticket.issue) +
        '</strong></div><div class="field"><label>الفني</label><strong>' +
        U.escape(ticket.technician) +
        '</strong></div></div></section><aside class="card"><div class="card-head"><h2>خط التتبع</h2></div><div class="card-body timeline"><div class="timeline-item"><strong>تم الاستلام</strong><p class="muted">' +
        U.date(ticket.received) +
        '</p></div><div class="timeline-item"><strong>الفحص الفني</strong><p class="muted">تم تسجيل التشخيص الأولي.</p></div><div class="timeline-item"><strong>' +
        U.escape(ticket.status) +
        '</strong><p class="muted">آخر حالة في بيانات العرض.</p></div></div></aside></div>';
      container.querySelector("#ticketSelect").onchange = function () {
        draw(this.value);
      };
      container.querySelector("[data-advance-status]").onclick = function () {
        UI.toast("تقدمت الحالة داخل جلسة العرض فقط.", "success");
      };
    }
    draw(tickets[0].id);
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
        { id: "serviceAmount", label: "القيمة", type: "number", min: 1 },
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
  };
})(window);
