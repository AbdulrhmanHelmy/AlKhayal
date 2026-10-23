# UI screen coverage

Originally generated from `frontend/data/screens.json` on 2026-10-20. Batch 1 added the nine missing navigation records, bringing the registry to 99 routes.

- **Browser smoke**: the route was opened and its primary UI behavior was exercised in the local browser.
- **Registry verified**: automated tests confirmed unique routing, navigation references, renderer support, and required metadata.
- The implementation details in the historical rows below describe the preserved future entry points. At runtime, routes outside the implemented customer, supplier, catalog, inventory, administration, and settings slices now display `هذه الشاشة قيد الإنشاء` and perform no session-only mutation.
- The dashboard is navigation-only and contains no sample totals, dates, alerts, or activity.
- Product creation and editing show only expiration tracking, colors, sizes, and a manual/generated barcode. Legacy batch, serial, IMEI, SKU, and product-type data remain preserved by the aggregate API but are not staff-facing controls.
- The customer sidebar exposes `إدارة العملاء`, `كشف حساب العميل`, `المعاملات المالية للعميل`, and `تصدير بيانات العملاء` only. `customer-add`, `customer-edit`, and `customer-subscriptions` stay registered but are hidden from navigation and search; creation and editing are reached from `إدارة العملاء` through the Arabic `+ إضافة عميل` and per-row `تعديل` actions.

| Module | Arabic screen | Route | Implementation | Layout/renderer | Mock source | Main interactions | Verification |
|---|---|---|---|---|---|---|---|
| الرئيسية | الرئيسية | `dashboard` | `frontend/js/modules/dashboard.js` | dashboard | Aggregated mock store | KPIs, charts, alerts and navigation | Browser smoke |
| الإعدادات | إعدادات البرنامج | `settings-program` | `frontend/js/modules/renderers.js` | settings | UI/session state | Edit settings and save in session | Browser smoke |
| الإعدادات | نوع النشاط التجاري | `business-activities` | `frontend/js/modules/business-activities.js` | business-activities | SQLite/API | Search, multi-select, primary activity, preview and confirmed persistence | Automated integration |
| الإعدادات | الوحدات والميزات | `feature-management` | `frontend/js/modules/feature-management.js` | feature-management | SQLite/API | Core/optional groups, capability switches, dependencies, recommendations | Automated integration + browser smoke |
| الإعدادات | إعدادات النظام | `settings-system` | `frontend/js/app.js` | legacy-redirect | Compatibility | Redirect to real General Settings | Automated registry |
| الإعدادات | الإعدادات العامة | `settings-general` | `frontend/js/modules/general-settings.js` | general-settings | SQLite/API | Edit validated date/time/currency/tax defaults | Automated integration |
| الإعدادات | بيانات المنشأة | `settings-company` | `frontend/js/modules/general-settings.js` | company-settings | SQLite/API | Edit validated reusable company profile | Automated integration |
| الإعدادات | المظهر | `settings-appearance` | `frontend/js/modules/general-settings.js` | appearance-settings | Device preference | Apply Light/Dark/System immediately | Native smoke |
| الإعدادات | إعدادات الفاتورة | `settings-invoice` | `frontend/js/modules/renderers.js` | settings | UI/session state | Edit settings and save in session | Registry verified |
| الإعدادات | إعدادات الطابعات | `settings-printers` | `frontend/js/modules/renderers.js` | printer | UI/session state | Configuration and print preview only | Registry verified |
| الإعدادات | البيانات والصيانة | `settings-backup` | `frontend/js/modules/general-settings.js` | database-settings | SQLite/API read | Inspect database status; backup/restore explicitly unavailable | Automated integration |
| العملاء | إدارة العملاء | `customers-list` | `frontend/js/modules/renderers.js` | entity-list | customers | Search, filter, sort, paginate, inspect, add, and edit persisted customers | Browser smoke |
| العملاء | إضافة عميل | `customer-add` | `frontend/js/modules/renderers.js` | entity-form | customers | Hidden contextual route; validate and save to SQLite | Registry verified |
| العملاء | تعديل بيانات عميل | `customer-edit` | `frontend/js/modules/renderers.js` | entity-form | customers | Hidden contextual route; pick a customer and save to SQLite | Registry verified |
| العملاء | كشف حساب العميل | `customer-statement` | `frontend/js/modules/renderers.js` | statement | customers | Party selection and running-balance review | Registry verified |
| العملاء | دفع آجل للعميل | `customer-credit-payment` | `frontend/js/modules/renderers.js` | payment | customers | Validate and simulate a party payment (المعاملات المالية للعميل) | Registry verified |
| العملاء | تصدير بيانات العملاء | `customers-export` | `frontend/js/modules/renderers.js` | export | customers | Generate a UTF-8 CSV download | Registry verified |
| العملاء | اشتراكات العملاء | `customer-subscriptions` | `frontend/js/modules/renderers.js` | under-construction | customers | Hidden placeholder; no session-only mutation | Registry verified |
| المبيعات | نقطة بيع سريعة | `pos` | `frontend/js/modules/pos.js` | pos | products | Product search, cart, totals, keyboard shortcuts and mock checkout | Browser smoke |
| المبيعات | إدارة المبيعات | `sales-management` | `frontend/js/modules/renderers.js` | entity-list | sales | Search, filter, sort, paginate, inspect and session actions | Browser smoke |
| المبيعات | البحث السريع برقم الفاتورة للتعديل | `invoice-lookup` | `frontend/js/modules/renderers.js` | invoice-lookup | sales | Interactive session workflow or preview | Registry verified |
| المبيعات | قائمة المبيعات | `sales-list` | `frontend/js/modules/renderers.js` | entity-list | sales | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| المبيعات | تقارير المبيعات | `sales-reports` | `frontend/js/modules/renderers.js` | report | sales | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| المبيعات | تعديل أو تأكيد الفواتير | `sales-confirm` | `frontend/js/modules/renderers.js` | workflow | sales | Review and simulated confirmation | Registry verified |
| الموردين | قائمة الموردين | `suppliers-list` | `frontend/js/modules/renderers.js` | entity-list | suppliers | Search, filter, sort, paginate, inspect and session actions | Browser smoke |
| الموردين | مشتريات المورد | `supplier-purchases` | `frontend/js/modules/renderers.js` | report | purchases | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الموردين | حسابات الموردين آجلاً | `supplier-credit` | `frontend/js/modules/renderers.js` | statement | suppliers | Party selection and running-balance review | Registry verified |
| الموردين | رصيد المورد | `supplier-balance` | `frontend/js/modules/renderers.js` | entity-list | suppliers | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| الموردين | سداد حساب المورد | `supplier-payment` | `frontend/js/modules/renderers.js` | payment | suppliers | Validate and simulate a party payment | Registry verified |
| الموردين | تأكيد سداد المورد | `supplier-payment-confirm` | `frontend/js/modules/renderers.js` | workflow | suppliers | Review and simulated confirmation | Registry verified |
| الموردين | تقارير الموردين | `supplier-reports` | `frontend/js/modules/renderers.js` | report | suppliers | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الموردين | تقرير المبالغ المسددة للموردين | `supplier-paid-report` | `frontend/js/modules/renderers.js` | report | suppliers | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| المشتريات | إدارة المشتريات | `purchase-management` | `frontend/js/modules/renderers.js` | invoice-editor | purchases | Line items, quantities, dates and calculated totals | Browser smoke |
| المشتريات | إدارة الموردين أثناء الشراء | `purchase-supplier` | `frontend/js/modules/renderers.js` | picker | suppliers | Interactive session workflow or preview | Registry verified |
| المشتريات | قائمة المشتريات | `purchases-list` | `frontend/js/modules/renderers.js` | entity-list | purchases | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| المشتريات | تقارير المشتريات | `purchase-reports` | `frontend/js/modules/renderers.js` | report | purchases | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| المشتريات | تعيين تاريخ صلاحية المنتجات | `purchase-expiry` | `frontend/js/modules/renderers.js` | workflow | products | Review and simulated confirmation | Registry verified |
| المشتريات | تأكيد عملية الشراء | `purchase-confirm` | `frontend/js/modules/renderers.js` | workflow | purchases | Review and simulated confirmation | Registry verified |
| المرتجعات | نظرة عامة على المرتجعات | `returns-overview` | `frontend/js/modules/renderers.js` | entity-list | returns | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| المرتجعات | إدارة مرتجعات المبيعات | `sales-returns` | `frontend/js/modules/renderers.js` | returns | sales | Interactive session workflow or preview | Registry verified |
| المرتجعات | إدارة مرتجعات المشتريات | `purchase-returns` | `frontend/js/modules/renderers.js` | returns | purchases | Interactive session workflow or preview | Registry verified |
| التسطيب والطباعة | تسطيب برنتر الفواتير | `receipt-printer` | `frontend/js/modules/renderers.js` | printer | UI/session state | Configuration and print preview only | Registry verified |
| التسطيب والطباعة | تسطيب برنتر الباركود | `barcode-printer` | `frontend/js/modules/renderers.js` | printer | UI/session state | Configuration and print preview only | Registry verified |
| التسطيب والطباعة | تصميم وطباعة الباركود | `barcode-designer` | `frontend/js/modules/renderers.js` | barcode | UI/session state | Interactive label design and print preview | Registry verified |
| التسطيب والطباعة | إعدادات التصميم | `print-design` | `frontend/js/modules/renderers.js` | barcode | UI/session state | Interactive label design and print preview | Registry verified |
| التسطيب والطباعة | التنسيق المتقدم | `print-advanced` | `frontend/js/modules/renderers.js` | barcode | UI/session state | Interactive label design and print preview | Registry verified |
| التسطيب والطباعة | تعديل السعر والكمية والباركود | `barcode-product-edit` | `frontend/js/modules/renderers.js` | entity-form | products | Validate and save to the session store | Registry verified |
| المصروفات والسندات | قائمة المصروفات والسندات | `expenses-list` | `frontend/js/modules/renderers.js` | entity-list | expenses | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| المصروفات والسندات | أنواع المصروفات | `expense-categories` | `frontend/js/modules/renderers.js` | workflow | expenses | Review and simulated confirmation | Registry verified |
| المصروفات والسندات | إضافة مصروف | `expense-add` | `frontend/js/modules/renderers.js` | expense-form | expenses | Interactive session workflow or preview | Registry verified |
| المصروفات والسندات | حركة مالية | `financial-movement` | `frontend/js/modules/renderers.js` | entity-list | transactions | Search, filter, sort, paginate, inspect and session actions | Browser smoke |
| الخزنة والبنك | قائمة الخزنة والبنك | `accounts-list` | `frontend/js/modules/renderers.js` | entity-list | accounts | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| الخزنة والبنك | إضافة خزنة جديدة | `account-add` | `frontend/js/modules/renderers.js` | entity-form | accounts | Validate and save to the session store | Registry verified |
| الخزنة والبنك | الرصيد الحالي للخزنة والبنك | `accounts-balance` | `frontend/js/modules/renderers.js` | report | accounts | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الخزنة والبنك | حركة الخزنة والبنك | `account-movements` | `frontend/js/modules/renderers.js` | entity-list | transactions | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| التنبيهات | قائمة التنبيهات | `alerts-list` | `frontend/js/modules/renderers.js` | alerts | notifications | Interactive session workflow or preview | Registry verified |
| التنبيهات | تنبيه حد الطلب | `reorder-alert` | `frontend/js/modules/renderers.js` | threshold | products | Interactive session workflow or preview | Registry verified |
| التنبيهات | تنبيه نقص الأصناف | `low-stock-alert` | `frontend/js/modules/renderers.js` | entity-list | products | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| المخزن | إضافة مخزن جديد | `warehouse-add` | `frontend/js/modules/renderers.js` | entity-form | warehouses | Validate and save to the session store | Registry verified |
| المخزن | إعدادات المخزن | `warehouse-settings` | `frontend/js/modules/renderers.js` | settings | UI/session state | Edit settings and save in session | Registry verified |
| المخزن | تسوية المخزون | `stock-adjustment` | `frontend/js/modules/renderers.js` | adjustment | products | Interactive session workflow or preview | Registry verified |
| المخزن | نقل الأصناف بين المخازن | `warehouse-transfer` | `frontend/js/modules/renderers.js` | transfer | products | Interactive session workflow or preview | Registry verified |
| المخزن | حركة الأصناف | `stock-movements` | `frontend/js/modules/renderers.js` | entity-list | movements | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| المخزن | الأصناف الناقصة | `low-stock` | `frontend/js/modules/renderers.js` | entity-list | products | Search, filter, sort, paginate, inspect and session actions | Browser smoke |
| المخزن | إضافة وحدة | `unit-add` | `frontend/js/modules/renderers.js` | workflow | products | Review and simulated confirmation | Registry verified |
| المخزن | تصنيع | `manufacturing` | `frontend/js/modules/renderers.js` | manufacturing | products | Interactive session workflow or preview | Registry verified |
| المخزن | دليل المنتجات | `products-list` | `frontend/js/modules/renderers.js` | entity-list | products | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| المخزن | إضافة المنتجات | `product-add` | `frontend/js/modules/renderers.js` | entity-form | products | Validate and save to the session store | Registry verified |
| المخزن | إضافة سريعة للأصناف | `product-quick-add` | `frontend/js/modules/renderers.js` | entity-form | products | Validate and save to the session store | Registry verified |
| الموظفين | قائمة إدارة الموظفين | `employees-list` | `frontend/js/modules/renderers.js` | entity-list | employees | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| الموظفين | إضافة موظف جديد | `employee-add` | `frontend/js/modules/renderers.js` | entity-form | employees | Validate and save to the session store | Registry verified |
| الموظفين | إدارة الورديات | `shifts-list` | `frontend/js/modules/renderers.js` | entity-list | shifts | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| الموظفين | إضافة وردية | `shift-add` | `frontend/js/modules/renderers.js` | entity-form | shifts | Validate and save to the session store | Registry verified |
| الموظفين | تقارير الورديات | `shift-reports` | `frontend/js/modules/renderers.js` | report | shifts | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الموظفين | الحضور والانصراف | `attendance` | `frontend/js/modules/renderers.js` | attendance | employees | Interactive session workflow or preview | Registry verified |
| صلاحيات المستخدم | المستخدمون | `users-list` | `frontend/js/modules/renderers.js` | entity-list | users | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| صلاحيات المستخدم | صلاحيات المستخدمين | `user-permissions` | `frontend/js/modules/renderers.js` | permissions | UI/session state | Toggle a session-only permission matrix | Registry verified |
| صلاحيات المستخدم | صلاحيات إدارة المبيعات | `sales-permissions` | `frontend/js/modules/renderers.js` | permissions | UI/session state | Toggle a session-only permission matrix | Registry verified |
| صلاحيات المستخدم | صلاحيات الإعدادات | `settings-permissions` | `frontend/js/modules/renderers.js` | permissions | UI/session state | Toggle a session-only permission matrix | Registry verified |
| صلاحيات المستخدم | إضافة ورديات وتأكيدها | `shift-approval` | `frontend/js/modules/renderers.js` | workflow | shifts | Review and simulated confirmation | Registry verified |
| الإحصائيات والأرباح | تقارير أرباح المبيعات | `profit-sales-report` | `frontend/js/modules/renderers.js` | report | sales | Date/branch filters, summary, chart, CSV/print preview | Browser smoke |
| الإحصائيات والأرباح | الأرباح | `profits` | `frontend/js/modules/renderers.js` | report | sales | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الإحصائيات والأرباح | تقارير المبيعات | `analytics-sales` | `frontend/js/modules/renderers.js` | report | sales | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الإحصائيات والأرباح | تقارير المشتريات | `analytics-purchases` | `frontend/js/modules/renderers.js` | report | purchases | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الإحصائيات والأرباح | تقارير الموردين | `analytics-suppliers` | `frontend/js/modules/renderers.js` | report | suppliers | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الإحصائيات والأرباح | تقارير الورديات | `analytics-shifts` | `frontend/js/modules/renderers.js` | report | shifts | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| الإحصائيات والأرباح | تقارير التتبع | `tracking-reports` | `frontend/js/modules/renderers.js` | report | maintenance | Date/branch filters, summary, chart, CSV/print preview | Registry verified |
| البيع بالتقسيط | نظام التقسيط | `installment-system` | `frontend/js/modules/renderers.js` | installments | installments | Build and review a calculated schedule | Registry verified |
| البيع بالتقسيط | سداد التقسيط | `installment-payment` | `frontend/js/modules/renderers.js` | installments | installments | Build and review a calculated schedule | Registry verified |
| البيع بالتقسيط | تأكيد سداد التقسيط | `installment-confirm` | `frontend/js/modules/renderers.js` | installments | installments | Build and review a calculated schedule | Registry verified |
| الصيانة والفروع | استلام جهاز صيانة | `maintenance-intake` | `frontend/js/modules/renderers.js` | maintenance-intake | maintenance | Interactive session workflow or preview | Registry verified |
| الصيانة والفروع | تتبع الصيانة | `maintenance-tracking` | `frontend/js/modules/renderers.js` | maintenance-tracking | maintenance | Interactive session workflow or preview | Browser smoke |
| الصيانة والفروع | الفروع والفرعيات | `branches-list` | `frontend/js/modules/renderers.js` | entity-list | branches | Search, filter, sort, paginate, inspect and session actions | Registry verified |
| الصيانة والفروع | حركة الأصناف بين الفروع | `branch-stock-transfer` | `frontend/js/modules/renderers.js` | transfer | products | Interactive session workflow or preview | Registry verified |
| الصيانة والفروع | حركة مالية للفروع | `branch-finance` | `frontend/js/modules/renderers.js` | workflow | branches | Review and simulated confirmation | Registry verified |
| الصيانة والفروع | خدمات كاش | `cash-services` | `frontend/js/modules/renderers.js` | cash-services | UI/session state | Interactive session workflow or preview | Registry verified |

Total distinct screens: **99**.
