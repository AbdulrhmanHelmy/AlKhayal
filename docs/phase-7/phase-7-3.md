# Phase 7.3 — Product barcodes and native product labels

Implemented in the existing checkout on 2026-10-29, preserving the uncommitted Phase 7.1/7.2 work. No external coding agent, delegation, commit, push, merge or branch change was used. Software verification and physical acceptance are separate; see the [final Phase 7 report](phase-7-final-report.md).

## Existing catalog audit and identity policy

The real catalog already has immutable `Products.Id`, `ProductDetails.Version`, `ProductVariants`, `ProductUnits`, `ProductSkus`, `ProductBarcodes`, size/color attribute values and `CatalogAuditLog`. Ordinary products already have one default variant; this phase reuses it and does not create artificial variants. The existing colors/sizes switches continue to control variant editing. Ordinary barcodes do not require those switches or the optional label-printing module.

There is no separate persisted product-code field. Labels expose `P-` plus the existing product ID (minimum six digits) as **مرجع المنتج**, and preserve SKU as a separate optional field. A barcode identifies its saved variant and sale unit. The existing POS lookup remains the scanner boundary for standard and Touch POS. It normalizes lookup identity with FormKC/uppercase and rejects unavailable records. Display/printed content retains the validated entered text; uniqueness follows that existing case-insensitive normalized identity, including retired codes.

`BarcodeService` and `IProductBarcodeStore` extend the existing catalog aggregate. Each command includes product/variant/unit IDs, the current aggregate version, optional barcode being replaced and a random operation key. A SQLite writer transaction verifies ownership/availability/version, checks uniqueness, retires the replaced code, inserts the replacement, stores symbology metadata, increments the aggregate version, records the operation result and writes the existing catalog audit. Failure of audit insertion rolls everything back. Repeating the same actor/command/key returns its original result; changing that command conflicts. Generation retries collisions under the same writer lock. No product, variant, invoice or historical line identity is rewritten; retired codes cannot be recycled.

Manual entry, display, copy, replacement and server-side internal generation are in the product-directory barcode dialog. Existing compact add/edit barcode fields remain and their generator refuses to overwrite entered text. Creation/edit identifiers also validate supported printable content. Existing stored identifiers are not silently migrated. Product editing retains the existing ordinary/variant relationships.

## Encoding and native output

Supported production symbologies:

- **Code 128**: printable ASCII 32–126, 1–64 characters, no leading/trailing whitespace. The encoder selects actual Code 128 patterns/checksum; Arabic is supported as label text, not as barcode content. Internal generation creates a ten-digit `29xxxxxxxx` Code 128 value. This is an internal identifier, not an assigned GS1 number.
- **EAN-13**: thirteen Western digits with verified modulo-10 check digit. Barcode-management commands explicitly complete twelve-digit input with the calculated check digit. Printing requires an already complete valid saved value. An arbitrary Code 128 value cannot silently become EAN-13.

The pinned dependency is [ZXing.Net 0.16.11](https://www.nuget.org/packages/ZXing.Net/0.16.11), Apache 2.0; footprint, framework asset selection and notices are recorded in [third-party/README.md](../third-party/README.md). Only `zxing.dll` is deployed, not its optional WPF presentation assembly.

`NativeBarcode` takes the encoder's boolean modules and adds quiet zones: ten modules on either side for Code 128, eleven for EAN-13. Module width rounds **up** to whole actual printer dots, minimum two dots, at the configured minimum physical module width. Bars are solid black/white integer rectangles without smoothing, interpolation or font dependence. The whole symbol including quiet zones must fit the element; insufficient width or height fails explicitly. Software decoder round trips cover numeric/mixed Code 128 and EAN-13; physical scanning remains unverified.

Labels extend the **same** Phase 7.2 `WindowsPrinterBackend` and `PrintingService` job lifecycle. `LabelBatch`/`ProductLabel`/`ILabelRenderer`/`ILabelPrinterBackend` describe nonfinancial catalog output independently from invoice DTOs. The backend reuses installed-device enumeration, explicit printer selection, custom paper capability/DEVMODE negotiation, status checking and standard direct `PrintDocument` submission. There is no browser print, WebView2 print, PDF, network print bridge or second queue subsystem.

The renderer preflights every item and cell against actual printer DPI and printable area before calling `.Print()`. It compensates hardware-origin offsets when drawing. Text uses native Tahoma, Arabic shaping/RTL and explicit LTR identifiers; it wraps/shrinks down to five points, then rejects overflow instead of clipping or hiding text. A failure after submission begins becomes `uncertain`, since some pages might exist.

## Settings, paper and designer

The sole administration entry remains **الإعدادات ← الطابعات والطباعة**. A dedicated label section stores its own versioned configuration/audit, independent from receipt profiles and auto-print. Up to twenty named label profiles select an installed Windows printer, template and 1–10 copies. The default profile chooses the default template through its assignment. An empty printer is an unconfigured state, never an instruction to choose another device. Saving an assigned profile validates its driver; missing/interactive/unsupported devices return an explicit error.

Up to forty structured templates support creation, copying, rename, deletion when unused, reset, assignment and default selection. Elements are product name, company, product reference, SKU, barcode, readable barcode, persisted sale price, unit, category, size, color, static text and optional PNG logo. Controls provide visibility, X/Y/width/height in mm, font size, bold, alignment, barcode module width/height, stock bounds, margins and gaps. Logo validation reuses native bounded PNG validation. A selected-element outline overlays a native PNG preview at actual page proportions. Preview uses sample data, first page only, 300 DPI and a bounded 16-million-pixel raster; it never submits a job. Debouncing and response tokens reject stale previews. Layout changes preserve the local settings draft until save/reload.

Presets are 30×20, 40×30, 50×25, 50×30, 60×40, 100×50mm and A4; custom dimensions remain editable. Presets do not guarantee printer support. Label bounds are 10–420mm wide and 10–1000mm high; page bounds are 10–420mm by 10–3000mm before orientation. Sheets allow 1–20 columns, 1–100 rows, at most 500 cells. Horizontal/vertical pitch equals label size plus gap. Margins and all cells must fit the page, and visible elements must fit both label and driver printable area. The barcode rectangle includes its quiet zones; overlapping it with other visible elements is rejected.

For continuous rolls, one cell is one driver page/feed step. Set the page height to include label height, top/bottom margins and vertical inter-label gap. The driver must support that exact form/pitch and be configured for the stock's gap/black-mark sensing. The continuous flag validates this model; it does not send proprietary feed/cut commands. It requires one row/column and portrait orientation. Sheet layout starts **top-left, then right**, regardless of Arabic UI direction, and can skip used cells on the first sheet. Later sheets start at cell zero. Individual element rotation and freehand drag/drop are not implemented; numeric positioning is the intended editor.

## Individual and bulk workflows

From the directory, choose **باركود** to manage codes or **طباعة ملصقات** to open the contextual batch with that product's active codes. Add/edit/details surfaces provide a collapsed inline print panel for an already saved product. It selects a saved barcode/unit, profile, optional template override and count. It does not replace the product modal or read unsaved fields, so an unsaved draft remains intact. A new product must be saved before production label output. Print controls follow the optional `barcode-label-printing` feature; ordinary barcode management remains available independently.

**دفعة ملصقات** opens `product-labels`, a hidden contextual route requiring catalog permission. Search uses the existing server-side product endpoint with twenty results per page, then fetches details only for a selected product. Choose active variant/unit barcodes, set quantity/symbology, remove rows, select profile/template and choose the first sheet cell. The batch is capped at 200 distinct barcode IDs, 1–1000 per item and 2000 total labels including profile copies. No full-catalog request or polling is introduced. Stale searches cannot repaint newer results and the search input is kept mounted.

The client submits only saved barcode IDs, quantities and symbology plus layout/profile selection. `SqliteLabelRepository` reads product, variant price override/base price, existing POS unit ratio/rounding policy, SKU, unit, category, attributes and company/currency in one database snapshot. No client-submitted price is trusted. Output, success, failure and retry do not post stock/FIFO/ledger/treasury/sale/return/shift mutations.

An in-flight guard suppresses duplicate clicks. An unknown HTTP result freezes the pending payload and retains its key for retry. An identical server request replays the durable job **before rereading changed catalog data**. Changed parameters with a consumed key conflict. A deliberate subsequent reprint gets a new key and reprint flag. At most one active label batch per actor is accepted, including across service instances. Shared job statuses/review are the Phase 7.2 `accepted`, `submitted_unknown`, `failed`, `uncertain` semantics. `submitted_unknown` is spooler submission, never proof of paper completion. No automatic output retry occurs.

PrintJobs records printer/template/copies/actor/time/status, request hash and a compact batch summary (settings version, starting cell, barcode IDs/counts/formats). It does not store product price bodies or rendered images. Administrator history shows the summary. Persistent barcode audit records replacements; configuration audit records version/actor/time, not full historic template bodies.

## API, persistence and security

Migration **073_phase7_3_labels.sql** adds LabelConfiguration, LabelConfigurationAudit, BarcodeOperations and BarcodeSymbologies; it adds nullable RequestHash/BatchSummaryJson to PrintJobs. Existing 071/072 and financial schema are preserved. The current embedded migration count is 34. Historical upgrade fixtures exclude 073 until their upgrade stage.

All routes use the existing `{success,data,error}` envelope, same-origin session cookies and cancellation conventions:

| `/api/labels` route | Contract / authorization |
|---|---|
| GET `settings`, `defaults` | Full configuration / administrator |
| PUT `settings` | Versioned complete configuration / administrator + CSRF/origin |
| POST `preview` | Structured LabelTemplate → sample `{pngBase64}` / administrator + CSRF/origin |
| GET `options` | Names/IDs/copies only / effective catalog permission + label module |
| POST `jobs` | `{operationKey,profileId,templateId,startCell,reprint,items:[{barcodeId,quantity,format}]}` / catalog permission + module + CSRF/origin |
| POST `test/{profileId}` | `{operationKey}`; saved profile, logged sample output / administrator + module + CSRF/origin |
| POST `barcodes` | BarcodeChange / product list/edit permission + CSRF/origin; no label/variant switch required |

Cashiers cannot configure templates/devices or read full settings/diagnostics. Output accepts effective products-list/product-edit/product-add/product-quick-add access; barcode changes require list/edit access. The shared print-history/review/device endpoints remain administrator-only. Supplied Origin must match the request authority including port; writes require the existing session CSRF token. Feature switches are rechecked server-side. Existing deployment limits for LAN/TLS/rate limiting still apply.

## Validation evidence and limits

Reproducible commands and final totals are in the [final report](phase-7-final-report.md). New label tests execute native raster/geometry/decoder behavior, temporary SQLite barcode uniqueness/retirement/version/replay/audit rollback, variant mapping, persisted price fields, configuration restart, independent receipts, bulk job concurrency/uncertain failures, financial nonmutation and real OWIN authorization/CSRF/origin/feature gates. Frontend VM tests run production transport/editor/search handlers and preserve a mounted unsaved product field. No automated test uses the normal operator database or submits physical output.

Native preview PNGs for all six presets plus 73×37 custom and A4 are generated under local `test-artifacts/phase73/unit/`. The 30×20 and multi-column A4 images were visually inspected; Arabic/mixed text, readable values, bars and empty starting cells were visible. This is software raster evidence only. Full authenticated WinForms/WebView2 theme/DPI interaction, target printer drivers, paper feed and actual scanner acceptance require the manual matrix in the final report. Optional receipt-reference barcodes were not added; receipt rendering is preserved.
