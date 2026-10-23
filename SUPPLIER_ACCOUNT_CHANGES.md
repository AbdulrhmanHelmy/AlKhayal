# ربط الموردين وحسابات الموردين الآجلة بقاعدة البيانات

## التغييرات
- ترحيل جديد `012_suppliers.sql`: جداول `Suppliers` و`SupplierInvoices` و`SupplierPayments` (علاقات `RESTRICT`، و`PaidAmount <= Total`).
- طبقات جديدة بنفس تقسيم العملاء: `Core` (نماذج + `ISupplierRepository` + `ISupplierAccountRepository`)، `Application` (`SupplierService` + `SupplierAccountService` للتحقق والتقريب)، `Data` (`SqliteSupplierRepository` + `SqliteSupplierAccountRepository`)، `Api` (`SuppliersController`).
- نقاط API: `GET/POST/PUT/DELETE /api/suppliers`، `GET/POST /api/suppliers/{id}/invoices`، `POST /api/suppliers/{id}/payments`، `GET /api/suppliers/{id}/statement`.
- إضافة فاتورة تزيد مستحق المورد، والدفع على فاتورة يخفض مستحق المورد والمتبقي على الفاتورة، وكلاهما داخل معاملة SQLite واحدة مع تراجع كامل عند الفشل.
- الواجهة: «قائمة الموردين» و«رصيد المورد» أصبحتا من قاعدة البيانات (إضافة/تعديل/حذف/عرض)، و«حسابات الموردين آجلاً» تعرض فواتير المورد المختار تحته مع زر «دفع آجل» لكل فاتورة وزر «+ فاتورة شراء آجلة» وكشف الحساب، و«سداد حساب المورد» يحفظ فعليًا (على فاتورة أو على الحساب).
- `UI.field` يدعم الآن `step` (كان يتجاهله، فكانت الحقول العشرية تُرفض في نماذج HTML العادية).
- «عرض» في القوائم أصبح يفتح نافذة تفاصيل للقراءة فقط بدل نموذج الإضافة.

## حدود مهمة
- فواتير الموردين تُنشأ من شاشة «حسابات الموردين آجلاً». شاشة «إدارة المشتريات» (محرر فاتورة الشراء) ما زالت تجريبية ولم تُربط بهذه الجداول.
- لا يوجد تعديل أو حذف للفواتير والدفعات (سجل مالي ثابت)؛ التصحيح يكون بقيد عكسي لاحقًا.
- لم يُضف مفتاح عملية (idempotency key) للدفعات؛ نفس سلوك دفعات العملاء الحالي.
- شاشات تقارير الموردين وتحليلات الموردين وإدارة الموردين أثناء الشراء ما زالت تقرأ بيانات العرض.
- قيمة `Payable` في تعديل المورد لا تُعدّل مباشرة؛ تتغير بالفواتير والدفعات فقط.

## البناء والاختبار
لم يتوفر `dotnet` في بيئة العمل، لذلك **لم يُبنَ كود C# ولم تُشغَّل اختبارات xUnit**. شغّل على Windows:
`dotnet build AlKhayal.slnx` ثم `dotnet test` (الاختبارات الجديدة: `SupplierAccountServiceTests` و`SupplierPersistenceTests`).
اختبار الواجهة `node frontend/tests/run-tests.js` يفشل في النسخة الأصلية أيضًا بسبب مسارات ناقصة في `screens.json` (`customer-add`, `customer-edit`, `customers-export`, `customer-subscriptions`, `supplier-purchases`, `supplier-payment-confirm`)؛ وعند تجاوز هذه النقطة تمر بقية الاختبارات بما فيها اختبارات الموردين الجديدة.
