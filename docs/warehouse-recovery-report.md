# Warehouse recovery report — 23 October 2026

Implemented and reviewed in Codex only. No OpenCode, external coding agent, delegation, commit, push, reset, checkout, clean or stash was used. Tests use temporary databases; the normal operator database was not tested against or modified.

## Recovery baseline

All initially modified/untracked files were copied outside the repository to `D:\AlKhayal-recovery-20261023-205529`, including the original working diff and status. Existing unrelated changes were preserved. Original line endings were retained on unchanged source lines. The original build succeeded with zero warnings/errors. Baseline frontend tests failed on the interrupted receipt assertion; baseline .NET results were 26 passing unit tests and 66/68 passing integration tests. The two baseline failures were the receipt's mutated request hash and an HTTP test's randomly selected occupied port.

## Task 1 — customer navigation verification

Verified the existing cleanup: `customer-add`, `customer-edit` and `customer-subscriptions` remain registered and navigation-hidden, with no sidebar entries. `إدارة العملاء`, `كشف حساب العميل`, and `المعاملات المالية للعميل` remain accessible. The existing export entry remains unchanged. Customer management retains `+ إضافة عميل` and per-row `تعديل`; executable frontend VM tests open both forms, retain saved customer values and verify their POST/PUT API calls, including the contextual creation route. The registry test validates all 99 routes. No customer financial redesign was added by this recovery.

## Task 2 — stock receipt

The compact RTL form uses the selected warehouse, product name/barcode search, quantity, explicit `سعر الشراء الجديد` and `سعر البيع الجديد`, and direct `حفظ`. One active variant/unit is automatic; multiple active choices are shown. Expiry/lot/serial/IMEI fields follow persisted requirements, including legacy tracking. Actual identifiers are never generated or bypassed. When warehouse context is absent, active warehouse selection/defaulting is available.

Purchase cost is never populated from the catalog reference. Selected-unit quantity and prices are converted with the Application fixed-scale policy. The selected variant's retail override, versions and catalog audit save with the receipt, balance, movement, tracking and FIFO layer in one transaction. A single-variant product also updates its base catalog selling price and legacy mirror so its ordinary list price stays consistent; multi-variant base prices, reference purchase costs, other variant prices and historical movements remain intact. Pending submissions prevent duplicate dispatch, disable editing/Save and block close. Failure retains input and re-enables saving. Unchanged lost-response retries retain their operation key; changed material fields get another key. Server-side unit conversion does not mutate the hashed input.

## Task 3 — warehouse workflows

Adjustment, issue and transfer reuse the same direct-save engine with operation-specific fields. Acquisition cost appears for incoming adjustments; negative corrections use FIFO. Transfers use the selected source, destination and quantity with optional notes. Counts show recorded/count/difference, lot/expiry identity, conditional increase cost and changed serial identifiers; save retains version protections and request-bound retries. Counts and balances show current purchase value, expected retail value and explicitly estimated gross profit. Incomplete valuation never becomes an apparently complete total.

History uses local Arabic date/time, descriptive movement types, signed quantities, warehouse, related transfer warehouse and optional notes. No operation hashes/keys are shown. Warehouse forms have no reason selector or separate review step; the backend derives a distinguishable audit reason when omitted. Light/Dark use shared tokens. Recent success toasts cannot cover a newly opened warehouse modal's Save button. Compact forms, normal focus behavior and bounded local table scrolling reuse the existing shell.

## FIFO and migration impact

See [inventory-fifo.md](inventory-fifo.md) for the maintained technical contract. Migration 016 is additive. It adds durable layers, immutable source-layer/movement allocations, immutable legacy-opening reconciliation records and entered-unit purchase/retail snapshots. Old migrations were not edited. Receipt order follows original acquisition movement sequence; transfers preserve it. Checked fixed-scale allocation conserves residual layer value across partial withdrawals. Positive corrections create acquisition layers, and shortages consume them. Idempotent replay creates no additional layer/allocation/movement or price update. Integrity/rebuild checks never manufacture missing FIFO layers.

Pre-016 balances keep their exact quantity, ledger value and historical identities, with an unresolved FIFO basis. Cost-sensitive withdrawals and confirmed valuation stay blocked until documented. The administrator reconciliation path establishes an explicitly labeled opening basis only when its evidenced total agrees with the untouched historical ledger. It does not claim recovered historical FIFO. A different total or a required multi-layer historical reconstruction remains unresolved and needs a separately designed valuation reconciliation; no historical value is silently rewritten. This conservative legacy limitation is intentional.

## Confirmed repairs

- Inverted variant visibility; single variants now stay automatic and multiple variants are visible.
- First-variant price guidance; guidance and valuation now use the selected/effective variant and unit.
- Reference-cost substitution and receipt-request mutation; actual entered cost is mandatory and hash identity stays stable.
- Receipt tests scoped to the whole inventory file; assertions are now receipt-scoped and production handlers also execute in the test DOM.
- Review-only operation posting and mandatory reason controls; direct Save now handles receipt, issue, adjustment and transfer.
- Cost/identifier fields shown to ordinary products; conditional product requirements now control them.
- Quantity conversion ignored by serial-count UI validation; base-unit counts are now validated.
- Count retry key reused after changed data; keys now follow the complete count payload.
- Stale asynchronous product detail applied to a newly selected product; stale responses are ignored and Save requires the loaded selection.
- Partial stock/price changes on failure; multi-line, tracking and destination-failure tests assert rollback.
- Generated warehouse code collision with existing manually assigned codes; generation now probes for an unused code.
- HTTP test selected a port from a small occupied range; it now asks the OS for an available loopback port.
- Historical migration fixtures inadvertently applied FIFO before inventory tables; fixtures now apply it at the correct boundary.
- Success notifications covering the next warehouse form's Save; scoped overlay ordering fixes this without field-hiding timers.

## Executed validation

Final result entries below are recorded only after the commands finish. TRX files, native harness source/binaries, screenshots and isolated smoke databases are outside the repository in the recovery directory.

```powershell
dotnet build AlKhayal.slnx --no-restore
dotnet test AlKhayal.slnx --no-restore --no-build --filter 'FullyQualifiedName~InventoryPersistenceTests|FullyQualifiedName~InventoryCostingTests|FullyQualifiedName~InventoryApiTests' --logger 'trx;LogFilePrefix=acceptance-focused' --results-directory D:\AlKhayal-recovery-20261023-205529\tests
dotnet test AlKhayal.slnx --no-restore --no-build --logger 'trx;LogFilePrefix=acceptance-full' --results-directory D:\AlKhayal-recovery-20261023-205529\tests
node frontend/tests/run-tests.js
Get-ChildItem frontend/js,frontend/tests -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName; if($LASTEXITCODE -ne 0){throw ('Syntax failed: '+$_.FullName)} }
git -c core.safecrlf=false diff --check
```

| Check | Confirmed result |
| --- | --- |
| Full solution build | PASS — 0 warnings, 0 errors |
| Focused inventory/costing/API tests | PASS — 2 unit and 38 integration tests, 0 skipped |
| Full unit suite | PASS — 28/28, 0 skipped |
| Full integration suite | PASS — 82/82, 0 skipped |
| Frontend registry and executed warehouse/customer handlers | PASS — all 99 registered screens plus receipt/count/transfer/adjustment/retry/valuation cases |
| JavaScript syntax (`frontend/js` and `frontend/tests`) | PASS |
| `git diff --check` | PASS |
| Isolated native WinForms/WebView2 smoke | PASS for the paths described below; target-machine limits remain explicit |

Latest full-suite records: `acceptance-full_net48_20261023212550.trx` (unit) and `acceptance-full_net48_20261023212621.trx` (integration). Focused records: `acceptance-focused_net48_20261023212130.trx` and `acceptance-focused_net48_20261023212149.trx`. All are under `D:\AlKhayal-recovery-20261023-205529\tests`.

The latest native output is under `D:\AlKhayal-recovery-20261023-205529\native-acceptance`: `result.txt`, `ordinary-fields.json`, `fifo-balance.json`, and the Light/Dark/compact PNG captures. The temporary harness source is `D:\AlKhayal-recovery-20261023-205529\NativeSmoke.cs`. The initial timing-only native attempt clicked before login script initialization; waiting for document/script readiness fixed the harness, and subsequent native runs passed.

The inventory tests cover different receipt costs, partial and across-layer FIFO withdrawals, exact residual values, transfer order/atomic rollback, adjustment/count shortages, tracked expiry/serial/IMEI transfer and rejected-identity rollback, unit conversions, selected variant price isolation, missing/invalid inputs, immutable allocations, pre-016 migration, unresolved historical cost, documented opening reconciliation, replay, restart, repeatable migrations and balance preservation. The full suites retain existing unrelated regressions.

## Native verification and limits

A dedicated x86 WinForms/WebView2 smoke harness used the actual self-hosted frontend with an isolated SQLite database and its own browser profile. Runtime: `155.0.4283.62`. It authenticated, searched the real product endpoint, directly received 10 units at 100 and 10 at 120 with selling price 150, attempted repeated Save, then issued 12. The persisted result was 8 units, FIFO cost 960, estimated sale value 1,200 and estimated gross profit 240. Ordinary variant/unit/expiry/batch/serial controls were all hidden, `حفظ` was direct, and `dir` was `rtl`. Light/Dark receipt/balance and desktop/compact movement screenshots were captured and visually inspected. The Save/toast overlap found in the first capture was repaired and checked again.

The harness exercises a dedicated WinForms/WebView2 wrapper, not the production `MainForm` startup/profile/DPI/cache-clear lifecycle. Windows 7, Chromium 109 runtime distribution, legacy hardware performance, actual printers, and complete manual operator acceptance of expiry/IMEI/count/transfer forms remain **UNVERIFIED**. Automated temporary-database tests cover those inventory contracts; they do not substitute for native operator acceptance. BigInt display math and the scoped CSS `:has()` rule are supported by Chromium 109, but that target runtime was not executed here. No statement is made about the exact FIFO history of any real operator database. Preexisting customer-accounting and other unrelated edits remain preserved and were not given a new specialized financial audit by this task.

## قائمة قبول يدوية بالعربية

نفّذ الخطوات التالية في قاعدة اختبار مستقلة مع مستخدم اختبار. لا تستخدم قاعدة المحل المعتادة.

1. **إضافة بضاعة:** افتح `المخزن ← دليل المخازن` واختر المخزن، ثم `إضافة بضاعة`. ابحث عن منتج بالاسم، ثم جرّب باركوده. أدخل كمية 10 وسعر شراء 100 وسعر بيع 150 واضغط `حفظ`. تحقق من إغلاق النموذج، ورسالة النجاح، وزيادة الرصيد 10، وتحديث سعر البيع الفعلي للمتغير المختار دون تغيير المتغيرات الأخرى. المنتج العادي لا يطلب تشغيلة أو سيريال أو IMEI. إذا كان له اختيار واحد فلا يظهر اختيار اللون/المقاس أو الوحدة.
2. **الصلاحية والتتبع:** اختر منتجًا يتتبع الصلاحية. تحقق من ظهور تاريخ الصلاحية، ومنع الحفظ عند نقصه، وإدخال رقم التشغيلة الحقيقي إذا كانت إعدادات المنتج تتطلبه. جرّب منتجًا قديمًا يتتبع السيريال/IMEI وتأكد أن عدد الأرقام يطابق الوحدات الأساسية، وأن أرقامًا ناقصة أو مكررة لا تغيّر الرصيد.
3. **تكلفتان مختلفتان:** لنفس المنتج والمتغير والمخزن والتشغيلة عند الحاجة، أضف 10 أخرى بسعر شراء 120 وسعر بيع 150. يجب أن يصبح الرصيد 20 وتكلفة الشراء الإجمالية 2,200. لا تستخدم سعر الشراء المرجعي بدل السعر الفعلي.
4. **صرف FIFO:** اضغط `إخراج بضاعة` واصرف 12. المتبقي 8 وتكلفته 960، والبيع المتوقع 1,200، والربح الإجمالي المتوقع **تقديري** 240. أعد تشغيل التطبيق وتحقق من بقاء هذه القيم. عند استعمال الكرتونة أو وحدة أخرى، تأكد أن الكمية والأسعار تخص الوحدة المختارة وأن التحويل صحيح.
5. **الجرد والتسوية:** ابدأ جردًا وأدخل كمية أقل بوحدة واحدة. يجب أن يظهر الفرق وأن تستهلك التسوية أقدم تكلفة متاحة. جرّب زيادة كمية وتأكد من طلب سعر شراء فعلي. افتح جردًا جديدًا، ثم غيّر المخزون من عملية أخرى؛ يجب أن يرفض حفظ الجرد القديم دون حركة جزئية. جرّب `تسوية مخزون` زيادة ونقصًا وتحقق من عرض الرصيد المتوقع ومن ظهور حركة لكل تسوية.
6. **نقل بضاعة:** فعّل تعدد المخازن في قاعدة الاختبار وأضف مخزنًا ثانيًا. انقل كمية من المصدر إلى الوجهة. يجب أن ينخفض المصدر وتزيد الوجهة بنفس الكمية والقيمة، وأن تبقى تكلفة الشركة الإجمالية كما هي. النقل إلى المخزن نفسه أو بكمية أكبر من المتاح يجب أن يُرفض دون تعديل أي جانب.
7. **حركة المخزون:** راجع الإضافة والصرف والجرد والتسوية والنقل في السجل. تحقق من التاريخ والوقت المحليين، ونوع الحركة بالعربية، وإشارة الكمية، والمخزن الآخر واتجاه النقل، والملاحظات. لا تظهر مفاتيح طلب أو hashes. كل نقل يظهر في المخزنين مرة واحدة لكل جانب.
8. **التكرار والخطأ:** اضغط `حفظ` سريعًا أكثر من مرة؛ يجب أن تُسجل عملية واحدة. في بيئة الاختبار اقطع الرد بعد وصول الطلب للخادم ثم أعد الحفظ دون تعديل؛ يجب ألا يزيد المخزون مرة ثانية. غيّر سعرًا أو كمية بعد طلب فاشل وتحقق من قبول محاولة جديدة. أخطاء الإدخال/الخادم يجب أن تُبقي القيم وتعيد تفعيل الحفظ.
9. **المظهر والرصيد القديم:** بدّل بين الفاتح والداكن، وضيّق النافذة، وتنقل بلوحة المفاتيح. تحقق من RTL، ووضوح التركيز، وإمكان الوصول إلى الحفظ وتمرير الجداول دون تمدد الصفحة. في نسخة اختبار لبيانات قبل FIFO تحقق من `تكلفة غير مؤكدة` ومنع الصرف/النقل. لا تعتمد تكلفة افتتاحية إلا بمستند فعلي؛ اختلاف القيمة التاريخية يجب أن يُرفض دون تغيير التاريخ.

## Changed files

Files added or edited by this recovery (relative to the saved arrival snapshot):

- `docs/api-contracts.md`
- `docs/architecture.md`
- `docs/database.md`
- `docs/design.md`
- `docs/inventory-fifo.md`
- `docs/inventory-ledger.md`
- `docs/product-catalog.md`
- `docs/testing.md`
- `docs/warehouse-management.md`
- `docs/warehouse-recovery-report.md`
- `frontend/css/modules.css`
- `frontend/js/modules/inventory.js`
- `frontend/tests/inventory-tests.js`
- `frontend/tests/run-tests.js`
- `src/AlKhayal.Api/Controllers/InventoryController.cs`
- `src/AlKhayal.Application/Inventory/InventoryContracts.cs`
- `src/AlKhayal.Application/Inventory/InventoryCosting.cs`
- `src/AlKhayal.Application/Inventory/InventoryService.cs`
- `src/AlKhayal.Data/Inventory/SqliteInventoryStore.Fifo.cs`
- `src/AlKhayal.Data/Inventory/SqliteInventoryStore.cs`
- `src/AlKhayal.Data/Migrations/016_inventory_fifo.sql`
- `tests/AlKhayal.IntegrationTests/ApiEndpointTests.cs`
- `tests/AlKhayal.IntegrationTests/BusinessActivityPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/CatalogMigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/FeaturePersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/InventoryApiTests.cs`
- `tests/AlKhayal.IntegrationTests/InventoryFifoTests.cs`
- `tests/AlKhayal.IntegrationTests/InventoryPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/MigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/SettingsPersistenceTests.cs`
- `tests/AlKhayal.UnitTests/InventoryCostingTests.cs`

Preexisting changed/untracked files retained byte-for-byte:

- `DESIGN.md`
- `docs/ui-screen-coverage.md`
- `frontend/css/layout.css`
- `frontend/css/themes.css`
- `frontend/css/tokens.css`
- `frontend/data/screens.json`
- `frontend/js/api-client.js`
- `frontend/js/app.js`
- `frontend/js/components/ui.js`
- `frontend/js/modules/business-activities.js`
- `frontend/js/modules/catalog.js`
- `frontend/js/modules/dashboard.js`
- `frontend/js/modules/feature-management.js`
- `frontend/js/modules/renderers.js`
- `src/AlKhayal.Api/Contracts/CustomerTransactionRequest.cs`
- `src/AlKhayal.Api/Controllers/CustomersController.cs`
- `src/AlKhayal.Application/Catalog/CatalogService.cs`
- `src/AlKhayal.Application/Customers/CustomerAccountService.cs`
- `src/AlKhayal.Core/Interfaces/ICustomerAccountRepository.cs`
- `src/AlKhayal.Core/Models/CustomerStatement.cs`
- `src/AlKhayal.Core/Models/CustomerStatementTransaction.cs`
- `src/AlKhayal.Data/Customers/SqliteCustomerAccountRepository.cs`
- `src/AlKhayal.Data/Customers/SqliteCustomerRepository.cs`
- `src/AlKhayal.Data/Database/CustomerLedgerMigrationPreflight.cs`
- `src/AlKhayal.Data/Database/Migrator.cs`
- `src/AlKhayal.Data/Migrations/015_customer_transactions.sql`
- `tests/AlKhayal.UnitTests/CatalogServiceTests.cs`
- `.test-results-batch5/` (existing test-result artifacts, retained)

