# Phase 7.2 — Native printing, printer settings and template designer

Implemented individually in the existing working tree on 2026-10-29. The pre-existing Phase 7.1/workforce and other uncommitted changes were retained. No OpenCode, external coding worker, delegation, commit, push, merge or branch switch. Phase 7.3 is not implemented.

## Architecture and boundaries

`Application/Printing` owns `PrintingService`, validated structured contracts, default templates, document projection, persistent request identity and output ports. `IPrintDocumentProvider` reads existing Sales/Purchasing/Returns/Workforce/Settings use cases. `IPrintPaymentReader` provides targeted persisted receipt projections. The print request never accepts financial totals, HTML, executable templates, shell commands or filesystem paths.

`Data/Printing` implements `IPrintTemplateRepository` and `IPrintPaymentReader` with parameterized Dapper/SQLite commands. Additive migration **072_phase7_2_printing.sql** leaves Phase 6 numbering untouched. It creates `PrintingConfiguration`, `PrintingAudit` and `PrintJobs`; it changes no financial table or posting rule. The configuration is one versioned JSON aggregate of templates and per-document profiles, committed with its audit in a short writer transaction. Stale versions conflict; removing a referenced template is invalid. Posted sales/purchase DTOs additionally expose their existing `postedUtc` timestamp for the printed posting time, alongside the original invoice date. No posting timestamp is manufactured. The printed header identifies the configured currency. The persisted model is portable; JavaScriptSerializer is confined to the .NET Framework Data adapter. No package was added.

`AlKhayal.Printing` implements `IPrintRenderer` with `NativePrintRenderer` and `IPrinterBackend` with `WindowsPrinterBackend`. It references Application and System.Drawing, has no WebView2/WinForms/HTTP dependency, and can be used by a standalone local API host. The old unused text-only `IReceiptPrinter` interface remains for source compatibility; the new production path exclusively uses `IPrinterBackend`. Host composition constructs one printing service. An optional injected backend permits integration tests to run the same HTTP API with a recording printer.

Flow: frontend action → session/permission/CSRF/origin checks → authoritative persisted document → configured profile/template → durable job claim → native graphics renderer → `PrintDocument`/`StandardPrintController` → selected Windows driver/spooler → job diagnostic update. No browser printing, browser preview, PDF creation, PDF intermediary or browser/desktop print dialog is used by the application. `StandardPrintController` suppresses the .NET status dialog. Known interactive file/PDF/XPS/OneNote/Fax drivers and FILE/PORTPROMPT ports are rejected. Other third-party driver UI behavior still requires qualification on the target device.

## Settings, templates and designer

Arabic RTL entry: **الإعدادات ← الطابعات والطباعة** (`settings-printers`, renderer `native-printing`). `settings-invoice` and `receipt-printer` are hidden compatibility redirects. Barcode/label routes remain unfinished. Existing administration gating applies; all printing configuration, installed-device details, job diagnostics, previews, review and test-print endpoints require an administrator.

Installed printers and their registered forms are read from Windows. The operator explicitly assigns a printer, default template and 1–10 copies for each document type. The Windows default is descriptive only; the service never chooses it as an implicit fallback. Empty receipt-printer selection keeps automatic printing disabled; a removed selected device produces an error. Device/driver/spooler errors remain distinguishable from posting errors. Saving validates assigned printer/driver capabilities; an unavailable printer can therefore prevent saving until corrected or unassigned.

Eight safe default templates exist: sales invoice, sales return, purchase invoice, purchase return, customer collection, supplier payment, shift summary and shift closing. Initially these are produced by Application defaults; the first successful settings save persists the aggregate. Users can create, duplicate, rename, edit, reorder sections, delete unused templates, select per-document defaults and restore a selected design. Maximum 40 templates; optimistic versioning prevents one settings editor from silently overwriting another. Changes, including resets/deletes, take effect after explicit Save.

Templates contain typed paper options, a bounded PNG logo, custom header/footer/return policy, item-column percentages, dividers and an ordered whitelist of section settings. Each section controls visibility, font points, bold, alignment and spacing. Header/company/address/phone/tax identity, reference/date/cashier/party, item/variant/unit price/quantity/discount/tax/line total and persisted financial totals are supported. Item detail switches render under the product name; name/quantity/value column percentages determine the compact row. Quantity and amount text use explicit LTR alignment. Long exact numeric cells switch to a full-width stacked layout when necessary, keeping numbers intact. Barcode/QR content is intentionally absent until Phase 7.3.

Logo input is PNG data only, bounded to 130,000 decoded bytes and 1200×1200 pixels, with IHDR dimensions checked before decoding. No external asset is fetched. Logo size and alignment are configurable. Company text comes from existing company settings; the optional logo belongs to its template because CompanyProfile has no persisted logo contract.

The compact section editor shares existing forms, tables, tokens, themes, keyboard controls and workspace scrolling. It collapses at 760px; wide configuration tables scroll locally. Editor preview calls the C# renderer and displays a bounded PNG of the first sample page, clearly labelled **اختبار تصميم — غير مالي**. Preview does not require a printer and never submits output. It is opt-in inside settings and never appears during business printing. The test-print button explicitly submits physical output using the **saved** profile; it logs an idempotent nonfinancial test job.

## Physical units, sizes and native pagination

Presets: 58mm, 80mm, A4 (210×297mm), A5 (148×210mm), custom. Presets impose their documented physical dimensions; custom accepts width 30–420mm and height 30–3000mm, further restricted by driver capabilities. Four margins, portrait/landscape, 0.5–2 text/layout scale, 6–24pt section fonts and variable roll length are configurable. Scale changes native font/layout measurements rather than browser zoom or an OS setting.

All layout coordinates use millimetres; conversion to .NET paper/margin units uses 100ths of an inch. Drawing uses printer DPI through `GraphicsUnit.Millimeter`; native Tahoma font rendering supplies Windows Arabic shaping and bidirectional text. Numeric item cells use explicit LTR formats. Word wrapping and measured continuation rows preserve long item descriptions; combining marks/surrogates are kept with their character when wrapping. Unicode shaping still depends on the installed Windows font and driver graphics support.

Fixed paper must match an advertised driver form (within rounding tolerance), or fit the driver's `DC_MINEXTENT`/`DC_MAXEXTENT` bounds and survive a noninteractive `DocumentProperties` DEVMODE round trip. Unicode DEVMODE dimensions are in tenths of a millimetre. HGLOBAL memory is locked/unlocked and released with GlobalFree. Landscape must be advertised by the driver. Custom-size support is conservative: a driver that does not advertise usable limits is restricted to its registered forms. Register the form in Windows if necessary. This is not a promise that every driver accepts custom stock.

Continuous mode computes content height, clamps it to the configured maximum page length and advertised driver maximum, and respects the driver minimum. Content beyond a driver's page length is paginated. The actual PrintPage callback intersects requested margins with `PrintableArea`/hard margins, recalculates wrapping for the actual printable width, and paginates measured rows without clipping at the bottom. An element taller than a page or a numeric value that cannot fit even a full-width field fails clearly. Pages continue the content; repeated page headers, cutter commands, cash-drawer pulses and ESC/POS are not implemented. Continuous stock with unusually large hardware margins can require an extra continuation page; verify stock feeding/cutting on the real device.

## Document integration and financial invariants

Supported identities and sources:

| Type | Persisted identity / boundary | UI |
|---|---|---|
| `sale` | SalesInvoices.Id; posted only; existing scale-4 amounts and saved lines | Posted invoice/POS Print button; automatic checkout hook |
| `sales-return` | SalesReturns.Id via ReturnService | Unified saved return details |
| `purchase` | PurchaseInvoices.Id; posted only | Posted purchase editor |
| `purchase-return` | PurchaseReturns.Id via ReturnService; posted only | Unified saved return details |
| `customer-payment` | **CustomerTransactions.Id**; collection/legacy_payment only | Collection row in customer statement |
| `supplier-payment` | **SupplierLedgerEntries.Id**; payment/legacy_payment only | Saved-payment chooser in Supplier Accounts; successful payment dialog |
| `shift-summary` | CashierShifts.Id, live persisted projection | Shift details/current-shift modal |
| `shift-closing` | CashierShifts.Id, closed only | Closed shift details |

Customer statement movements now expose nullable `transactionId` for the print action; this adds projection metadata, not a posting mutation. CustomerPayments.Id and SupplierPayments.Id are **not** interchangeable with the receipt identities above. Supplier receipt browsing reuses the existing ledger-read endpoint only when the operator opens the chooser; actual job submission reads the selected receipt directly. Purchase receipts display the persisted purchase totals; unsupported supplier settlement totals are omitted. Returns show saved original reference, cash and account adjustments. Shift closing uses existing reconciliation; labels distinguish expected/counted/difference from sale totals. Cashiers can print only their own shifts; administrators retain existing cross-cashier scope. Open shifts cannot produce a closing report. Draft/canceled invoices cannot produce final posted print output.

`PrintDocumentProvider` never writes a sale, return, stock movement, collection, treasury event or shift. Print failure cannot roll back a posted document. Tests compare financial table counts/totals before and after failure and 20 reprints, and exercise actual posted sales, partial returns, purchases, purchase returns, customer collections, supplier payments and shifts.

## Auto-print, retries and diagnostics

**طباعة الفاتورة تلقائيًا بعد البيع** defaults off. After posting succeeds, the frontend uses the returned persisted invoice ID and requests auto-print through the same endpoint. It does not issue another financial command. If off, the service returns `disabled`. If on, the database request key `auto:type:id` ensures repeated callbacks, refreshes or host restarts never submit a second automatic job for that invoice. Print errors are separate warning messages; the saved sale remains posted and its manual Print button remains available.

All screen actions share `AlKhayalPrinting.submit`. An in-flight map suppresses duplicate clicks. Manual requests have a random operation identity; a lost HTTP response retains that identity for retry in the mounted session so the API replays the existing job. The next deliberate reprint after a confirmed result uses a new identity. API idempotency validates document/reprint identity; a reused manual key for another document conflicts. A database unique index also prevents simultaneous accepted jobs for the same document, including from different service instances. No automatic submission retry occurs, including after offline/partial/uncertain failures. An intentional manual action is marked as a reprint request.

Statuses:

- `accepted`: durable request claim; submission may still be running or may have been interrupted.
- `submitted_unknown`: PrintDocument returned after submitting; **physical paper completion is unknown**.
- `failed`: a known validation/device/layout rejection, with an error code and Arabic message.
- `uncertain`: an unexpected error during submission; some pages/copies may already exist.

Jobs store document reference/type/id, actor ID, selected printer/template, copies, UTC submission timestamp, status, reprint flag and safe diagnostic text. They do not log document bodies. The settings UI reads the latest 100 jobs without polling. Persistent history is retained in SQLite; no automatic retention deletion is introduced.

After a process interruption, an accepted row is deliberately not resubmitted or silently unlocked. An administrator must check the Windows queue and explicitly review an accepted job older than ten minutes. Active in-process jobs cannot be reviewed. Review records its actor/time, changes the status to uncertain and releases the document for **manual** reprint. It neither cancels a spooler job nor confirms physical output. Automatic identity remains consumed. If diagnostic finalization itself fails, the row stays accepted and requires this same review.

## API and security

All endpoints use `{ success, data, error }` and existing same-origin session cookies. JSON POST/PUT requests require `X-CSRF-Token`; supplied Origin must equal the request authority (including port). No CORS permission or remote print bridge was added. Print routes enforce effective operational screen access by type; shift printing additionally requires `advanced-shifts` and server-side owner scope. Configuration/preview/test/review/history/device APIs are administrator-only. Default host binding remains loopback; production LAN authentication/rate-limit/TLS policies remain deployment work. There is no licensing bypass or new licensing implementation.

| Method / path under `/api/printing` | Payload / result |
|---|---|
| GET `settings` / `defaults` | Saved or safe initial PrintingSettings / fresh built-ins |
| PUT `settings` | Whole PrintingSettings including current `version`; returns incremented version |
| GET `printers` | Names, default flag, registered paper sizes, custom-capability note |
| POST `preview` | One structured PrintTemplate → `{pngBase64,message}`; sample first page |
| POST `jobs` | `{documentType,documentId,operationKey,automatic,reprint}` → PrintJob |
| GET `jobs` | Latest 100 PrintJobs |
| POST `test/{type}` | `{operationKey}` → logged sample PrintJob, saved profile |
| POST `jobs/{id}/review` | Empty JSON; explicit administrator queue-review acknowledgement |

`disabled`, known job failure and uncertain submission are job outcomes in a successful API envelope, not failed financial requests. HTTP 400 covers invalid contracts; 401 missing session; 403 role/screen/CSRF/origin/module rejection; 404 unavailable/unposted/other-owner documents; 409 version/key/busy/device configuration conflicts; 503 unavailable infrastructure or diagnostic persistence. Unexpected exception details are traced as exception types, never sent as raw paths or secret content.

## Verification and measured results

All write-capable tests use unique temporary SQLite files owned by their fixtures. The normal/seeded operator database was not opened or modified. No automated test submitted a real print job.

- Focused PrintingPersistenceTests/PrintingApiTests: 6 passed, including configuration/template restart, stale version, referenced-template deletion, invalid dimensions, missing printer, audit rollback, durable manual/automatic replay, concurrent distinct clicks, orphan review, failure/uncertain paths, persisted source projections, shift owner scope and HTTP authorization/CSRF/origin. Recording backend only.
- Fake-backend measurement (focused run, 20 persisted-sale submissions): **909.42ms total, 45.47ms mean**. Includes SQLite/source projection/claim/finalization, excludes Windows driver/spooler/device time. This is a local measured sample, not a throughput guarantee.
- Native layout/raster tests exercise 58mm, 80mm, A4, A5 and custom stock; Arabic/English/long unbroken names, exact amounts/quantities, bounds, multi-page row conservation, invalid templates/logos, oversized elements and physical-unit conversion. PNGs are generated by the native renderer without any printer submission. A4 and 58mm PNGs were visually inspected; this does not prove a physical printer's behavior.
- Frontend VM tests execute production transport/editor handlers: concurrent click suppression, network retry identity, deliberate reprint, failed/disabled auto outcome, draft edits, copying/deleting templates, duplicate-save suppression and stale preview rejection. Full registry remains 104 routes and validates the new renderer/redirects in script load order. All frontend JavaScript syntax checks run separately.
- Read-only real Windows enumeration returned Send To OneNote 2016 and Microsoft Print to PDF, each with ten advertised forms. Validating the PDF profile returned `interactive_printer` without submitting output. There is **no physical printer available** in this environment.

Reproducible commands (separate output avoids touching the running desktop's binaries):

```powershell
dotnet build AlKhayal.slnx --no-restore -p:OutDir=D:\projects\AlKhayal\test-artifacts\phase72\solution\ -v:q
dotnet test tests/AlKhayal.UnitTests --no-restore -p:OutDir=D:\projects\AlKhayal\test-artifacts\phase72\unit\
dotnet test tests/AlKhayal.IntegrationTests --no-restore -p:OutDir=D:\projects\AlKhayal\test-artifacts\phase72\integration\
node frontend/tests/run-tests.js
Get-ChildItem frontend/js -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName }
git diff --check
```

Final full-suite results: **83 unit tests passed; 218 integration tests passed; zero failed/skipped.** Frontend behavior/registry tests passed for 104 routes and all frontend JavaScript syntax checks passed. The solution build succeeded with zero warnings/errors; the final build after renderer review also succeeded with zero warnings/errors (4.70s in the last recorded build). A newly added posting-time assertion also caught the missing PostedUtc field in the explicit sales header SELECT; that read projection was corrected. Initial broader integration run found two old migration-count assertions still expecting 32; these were updated to 33 for additive 072. Historic migration builders exclude 072 until their upgrade step. A first native-layout run had a testhost startup abort; its immediate diagnostic rerun and subsequent suite completed. Test logs, TRX reports, build outputs and PNGs are local under `test-artifacts/phase72/` (excluded locally from Git).

## Compatibility, manual acceptance and Phase 7.3 handoff

Targets remain .NET Framework 4.8 for backend/printing and .NET Standard 2.0 for Application/Core. No Windows 10-only print API was introduced. The existing desktop remains x86; standalone backend/test code is AnyCPU with pointer-size-aware printer structure reads and Unicode P/Invoke. The source-level API choice is compatible with legacy Windows, but **Windows 7 SP1 runtime, fonts, actual printer drivers, x86/x64 device behavior and Chromium 109 UI are UNVERIFIED**. Readiness is not a hardware compatibility guarantee. `DocumentProperties`, device capabilities and printer graphics contracts were checked against Microsoft documentation: [DocumentProperties](https://learn.microsoft.com/en-us/windows/win32/printdocs/documentproperties), [DeviceCapabilities](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-devicecapabilitiesw), [graphics origin/margins](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.printing.printdocument.originatmargins), [GetHdevmode lifetime](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.printing.printersettings.gethdevmode), [printer status](https://learn.microsoft.com/en-us/windows/win32/printdocs/printer-info-6). Normal busy/printing/processing flags are accepted; offline and intervention/error flags have distinct validation outcomes, covered by a deterministic status test.

Remaining target-machine checks: authorize a physical test print; select/install the intended thermal or office driver; confirm 58/80/A4/A5/custom forms, variable length, actual margins, orientation, copies, feed/cut behavior, multi-page continuation, Arabic/mixed text and long values; test offline/paper-out/spooler restart and intentional reprint; verify settings survive an actual desktop restart; visually inspect Light/Dark, minimum desktop size, keyboard controls and DPI scaling in the real authenticated WinForms/WebView2 UI. Perform these on an isolated database/profile. Browser-mode HTTP behavior is covered by OWIN integration tests without WebView2; interactive browser-mode/physical printing acceptance remains manual.

Phase 7.3 can reuse `PrintDocumentData` → `IPrintRenderer` → `IPrinterBackend`, paper units, validated profiles/templates, PNG preview and job identity/diagnostics. Introduce a separately validated label document/layout contract and new document type when that phase starts; do not reuse sale callbacks for bulk labels. Barcode generation, product barcode management, label templates, label-printer profiles and bulk label submission are not part of this change.

## Changed-file map

New implementation: `Application/Printing/{PrintContracts,PrintingService,PrintDocumentProvider}.cs`; `Data/Printing/{SqlitePrintRepository,SqlitePrintPaymentReader}.cs`; migration 072; `Printing/{NativePrintRenderer,WindowsPrinterBackend}.cs`; `Api/Controllers/PrintingController.cs`; `frontend/js/modules/printing.js`; `frontend/css/printing.css`.

Integration edits: ApiBootstrap/AlKhayalServer and Data/Printing/Host project references; CustomerStatement movement projection and SqliteCustomerAccountRepository; frontend index/registry/app, sales, purchasing, returns, workforce, shared customer/supplier renderer and removal of old browser-receipt CSS. New tests: PrintingLayoutTests, PrintingPersistenceTests, PrintingApiTests, printing-tests.js. Existing migration fixtures/counts, unit project references and frontend test load order are updated. SalesContracts and PurchaseInvoice add the existing persisted posting timestamp to their read projections; the sales header SELECT explicitly includes PostedUtc. Architecture, API, database, design and testing documentation link here; the screen coverage matrix records the new printing renderer and compatibility redirects. Pre-existing workforce/features/sales documentation changes are preserved.
