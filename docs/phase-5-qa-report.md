# Phase 5 independent QA report

Date: 2026-10-25 (Africa/Cairo)  
Repository commit inspected: `20496560e0f133ad5cac21e37276174febba95b6`  
Verdict: **ACCEPTED WITH KNOWN LIMITATIONS**

## Scope and evidence rules

This was an independent execution of the Phase 5 acceptance request. Existing progress notes were read for context but were not accepted as test evidence. Results below distinguish rendered-browser interaction, native WinForms observation, API/SQLite evidence, and automated tests. An unexecuted native or hardware scenario is never reported as passed.

The Windows application was launched and remained running as `AlKhayal.UI.exe`; its window title was `الخيال POS`, and its embedded OWIN host answered at `http://127.0.0.1:5137`. The available computer-use inventory did not expose native Windows windows, so it could not click the WinForms/WebView2 surface itself. The same production frontend and API were therefore exercised through a real Chromium browser against that exact localhost host. Native WinForms clicking is **BLOCKED**, not passed.

## Isolated environment

- Database: `C:\Users\user\AppData\Local\Temp\AlKhayal-Phase5-QA-e6fb81e61fd04e24873d5bf0ba109f4e\phase5-qa.db`
- WebView2 profile: `C:\Users\user\AppData\Local\Temp\AlKhayal-Phase5-QA-e6fb81e61fd04e24873d5bf0ba109f4e\WebView2`
- Frontend override: `D:\projects\AlKhayal\frontend`
- The database and profile did not exist before this run. Both were created only after launching with `ALKHAYAL_DATABASE_PATH` and `ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER` set on the child process.
- The default operator database/profile were not opened, reset, or deleted.
- The disposable database was left in place as audit evidence. All people, suppliers, products, identifiers, and financial records are fictional QA data.
- Fresh migration state contains 20 applied migrations; `PRAGMA foreign_key_check` returned no rows.

Test data included one administrator, one cashier, three customers, two suppliers, two warehouses, two categories, five stocked/unstocked products, barcodes, M/L variants, fractional stock, opening customer balance, and distinct FIFO receipts. Inventory was created through supported inventory receipt endpoints rather than direct inconsistent inserts.

## Manual startup, login, and permissions

| Case | Browser against actual host | Native WinForms | Evidence |
|---|---|---|---|
| A1-A4 clean start, database creation, migrations, first-run screen | PASS | BLOCKED | New isolated paths; first-run Admin form rendered; schema reached migration 020. |
| A5-A6 create and log in as first Admin | PASS | BLOCKED | `qa_admin_phase5` bootstrapped once and authenticated. |
| A7-A9 restart, re-login, no repeated bootstrap | PASS | BLOCKED | UI process restarted repeatedly on the same isolated database; invoices and accounts persisted; duplicate bootstrap returned HTTP 409. |
| A10 create Cashier through Admin UI | PASS | BLOCKED | Created `qa_cashier_phase5` from `المستخدمون والصلاحيات`. |
| A11-A12 Cashier login and permissions | PASS | BLOCKED | Granted only `الكاشير اللمسي`; after login only that quick action/module was visible and settings were absent. |
| A13 incorrect password | PASS | BLOCKED | Rendered message: `اسم المستخدم أو كلمة المرور غير صحيحة.` |

## Standard sales interface

All statuses in this section refer to rendered browser interaction against the real local API unless stated otherwise.

| Case | Status | Result |
|---|---|---|
| B1-B7 open, create, search, barcode, add, edit | PASS | Arabic RTL screen opened; name and barcode lookup worked; quantities changed. |
| B8 remove draft line | PASS | Unstocked test line was removed from the cart. |
| B9-B13 multiple products, warehouse/customer, totals | PASS | Multi-line invoice, warehouse/customer selection, subtotal/final total verified. |
| B12 line/invoice discount by rendered UI | NOT TESTED | Automated precision/validation exists, but the browser driver did not produce a trustworthy committed discount event during the first attempt. |
| B14-B16 save, reopen, edit, post | PASS | Draft `SAL-20261024-345E5909` was reopened, changed from one to two L shirts, and posted at EGP 500.00. |
| B17-B19 open/search/restart persistence | PASS | Posted details reopened; list search worked independently; process restart retained invoices. |
| B20-B21 posted immutability | PASS | Product, quantity, discount, date, warehouse, customer, and payment controls were disabled/read-only after posting; database triggers and integration tests also reject posted mutation. |

Usability was straightforward, Arabic, and action-oriented. The final amount and primary action stayed visible. A product-search value originally leaked into invoice-history search and was fixed during this run.

## Touch cashier acceptance

| Case | Status | Result |
|---|---|---|
| C1-C10 load, add/re-add, +/- quantity, remove, name/barcode search, multiple products | PASS | Executed through the rendered touch interface. |
| C11-C15 customer, warehouse, cash and credit paths | PASS | Both warehouses were selectable; a credit sale for EGP 10.50 and cash sales were posted. |
| C16-C18 persistence, inventory decrease, matching totals | PASS | SQLite invoice/inventory values matched the standard screen. |
| C19 unavailable stock | PASS | Clear Arabic insufficient-stock error; draft remained intact. |
| C20 variants | PASS after fix | Picker now shows `M` and `L`; cart and persisted posted invoice show `L`. |
| Light/dark themes | PASS | Both were switched and visually inspected. |
| Old title removed | PASS | Visible title/navigation is exactly `الكاشير اللمسي`; the old title was not present. |

Rendered measurements (browser viewport intentionally reduced to account for desktop chrome):

| Viewport | Document | Workspace | Product pane | Cart | Checkout | Result |
|---|---|---|---|---|---|---|
| 1350×690 | 1350×690, no page scroll | 1136.4×484 | 639.6×408 | 472.8×408 | bottom 626 inside workspace bottom 660 | PASS |
| 900×600 | 900×600, no page scroll | 836×394 | 431.6×256 | 388.4×256 | bottom 543 inside workspace bottom 570 | PASS after fix |

At 900×600 the pre-fix layout stacked fixed-height panes, overlapped the search/header, and placed checkout below the workspace. The revised layout keeps bounded side-by-side panes, an auto-height two-row header, and internal scrolling only. Critical touch buttons measured 38-50 px high; no hover-only critical action was found.

## Financial reconciliation

SQLite uses scale 10,000 for money. The following values were read from the isolated database after the rendered workflows:

- Credit invoice `SAL-20261024-00A92D39`: total `105000` (EGP 10.50), two collections of `52500` each, paid `105000`, outstanding `0`, customer balance `0.00`.
- Collection methods were `card` then `transfer`; both persisted with local business date `2026-10-25`.
- Cash L-shirt invoice: total `5000000`, initial cash payment `5000000`; two EGP 250.00 refunds reduced paid value to zero after full return.
- Multi-line cash invoice: `5505000` total and paid (`EGP 550.50`).
- No unexplained discrepancy was found in the executed reconciliations.

| Financial case | Status | Evidence |
|---|---|---|
| D1 cash without customer | PASS | Rendered sale + SQLite payment row. |
| D2 cash with customer | PASS | Rendered multi-line invoice + database. |
| D3 credit with customer | PASS | Rendered sale + customer ledger increase. |
| D4-D5 partial/full payment | PASS | Rendered 5.25 + 5.25 collections; DB total/paid/outstanding reconciled. |
| D6 exact two-decimal prices | PASS | EGP 10.50 and EGP 5.25 paths. |
| D7 minimum EGP 0.01 | PASS (automated) | `MoneyPolicyTests`; not posted manually through GUI. |
| D8 fractional quantity | PASS (integration) | Real SQLite sale/inventory tests use quantity 2.5; not posted manually. |
| D9 multiple lines | PASS | Rendered and reconciled. |
| D10 multi-line discount | NOT TESTED | No trustworthy rendered execution. |
| D11 invoice discount | PASS (integration) | Sale integration test uses EGP 1.00 invoice discount. |
| D12 half-piastre boundary | PASS (unit) | Away-from-zero rounding test. |
| D13 large valid value | NOT TESTED | No dedicated boundary execution. |
| D14-D15 three-decimal/negative invalid input | PASS (unit/service) | Money policy rejects excess precision and negatives. |
| D16 repeated payment | PASS (integration) | Same operation key replays once. |
| D17 failed payment rollback | PASS (integration) | Invalid/excess payment does not mutate balances. |
| D18 restart balances | PASS | Process restart plus SQLite re-query. |

The frontend tax-inclusive calculation was independently checked at EGP 115.00 with 15% included tax: tax EGP 15.00 and total EGP 115.00. A regression test now enforces this formula.

## Inventory and FIFO

The existing real-SQLite FIFO test was rerun, not merely inspected. It receives 10 units @ EGP 100 and 10 units @ EGP 120, consumes 12 total, and verifies:

- Allocated quantities: 3 + 7 + 2 units across the two issue documents.
- Allocated values: EGP 300 + EGP 700 + EGP 240 = **EGP 1,240**.
- Remaining quantity: **8 units** from the EGP 120 layer.
- Remaining value: **EGP 960** (`9600000` scaled).
- Layer remaining quantities: `[0, 8000000]`; values: `[0, 9600000]`.
- Projection verification passed after reopening the SQLite store.

| FIFO case | Status | Evidence |
|---|---|---|
| E1 single layer | PASS | Sale/inventory integration and manual cash sale. |
| E2 two layers / exact 1240 & 960 example | PASS | Exact DB allocation assertions above. |
| E3 fractional | PASS (integration) | Fractional sale and inventory tests. |
| E4 exact depletion | PASS (integration) | FIFO layer assertions. |
| E5 insufficient stock | PASS | Rendered error plus atomic integration assertion. |
| E6 warehouse-specific availability | PASS (integration); UI selection PASS | Multi-warehouse tests and rendered selector. |
| E7 transfer then sale | PASS (integration) | Cost-preserving transfer tests. |
| E8-E9 repeat key / changed payload | PASS (integration) | Replay and conflict assertions. |
| E10 concurrency | PASS (integration) | Only one concurrent sale consumes the last unit. |
| E11 posting rollback | PASS (integration) | Forced audit failure leaves stock/document unchanged. |
| E12 restart/reconciliation | PASS | Store reopen and projection verification. |

The manual dataset also reconciled variant 6 back to quantity `5000000`, value `7500000` after selling and fully returning two units at cost EGP 150 each.

## Sales returns

| Case | Status | Result |
|---|---|---|
| F1-F3 open original, partial return, remaining full return | PASS | Rendered returns `RET-20261024-2D0680A6` and `RET-20261024-106554F6`. |
| F4-F7 stock/cost/account/refund | PASS | Each return restored `1000000` quantity and `1500000` cost; each cash refund was `2500000`. |
| F8-F10 excessive/duplicate/double refund prevention | PASS (integration) | Quantity cap and idempotent replay tests. |
| F11 return sale spanning multiple FIFO layers | NOT TESTED | FIFO sales and returns are separately covered, not this combined path. |
| F12 fractional return | PASS (integration) | 0.5 quantity return with exact cost. |
| F13 after partial payment | PASS (integration) | Refund/credit-reduction split reconciled. |
| F14 after full payment | PASS | Fully paid cash sale returned through rendered UI. |
| F15 after restart | PASS (DB/API); native UI BLOCKED | Persisted rows re-read; native click path unavailable. |
| F16 original sale auditability | PASS | Sale stays posted; two immutable return rows reference it. |

## Printing

| Case | Status | Result |
|---|---|---|
| G1 posted invoice and print action | PASS | Posted invoice exposed `طباعة / إعادة طباعة الإيصال`. |
| G2 trigger print | PASS | Click reached `window.print()` and opened a modal print state that blocked the browser automation channel until Escape/reload. |
| G3 preview/output inspection | BLOCKED | In-app browser did not expose the system print-preview surface. |
| G4-G10 receipt identity/content/amount visual verification | BLOCKED | Cannot truthfully verify system preview; receipt generation source includes these fields. |
| G11 reprint | BLOCKED | Preview surface unavailable. |
| G12 no duplicate sale | PASS | Invoice/payment counts were unchanged after print invocation. |
| G13 unavailable printer | BLOCKED | No safe virtual/unavailable printer surface exposed. |
| G14 print failure does not undo sale | PASS (database) | Posted sale remained posted after the blocked/cancelled print path. |
| G15 failed-print retry | BLOCKED | Requires an exposed Windows print surface or configured virtual printer. |
| Physical receipt | BLOCKED | No known physical printer was used. |

## Regression coverage

- PASS: authentication, first Admin, Cashier creation, screen permissions, feature/activity configuration, general settings, Arabic RTL, light/dark theme, navigation, and workspace tabs.
- PASS: product/category/unit/variant/barcode/warehouse APIs and rendered product search.
- PASS: inventory ledger, FIFO valuation, customer/supplier persistence, purchasing, supplier payment, and purchase-return automated integration suites.
- PASS: registry validates 99 routes and all active renderer/redirect targets.
- NOT TESTED manually in this run: a fresh rendered Phase 4 purchase invoice/payment/return workflow. It remains covered by the full integration suite.
- No unrelated operator data was accessed or mutated.

## Confirmed bugs and fixes

| Severity | Reproduction/root cause | Minimal fix and regression |
|---|---|---|
| High | Posting an already-saved draft posted the old server copy; current editor changes were never PUT. | Persist current payload before POST; frontend assertion plus rendered save→reopen→change→post replay. |
| High | A saved draft could not be updated: repository returned unit price as `250.0000`, while the application accepts at most two decimal places. | Return user money with `MoneyPolicy.FormatForUser`; new SQLite integration test proves resubmit/change/post. |
| High | M/L variants had identical labels and the touch cart omitted the selected value. | Derive labels from attribute values and persist/read them; frontend test plus rendered M/L scenario. |
| Medium | Product query leaked into invoice-history search and hid saved drafts. | Split `productQuery` and `listQuery`; frontend regression assertion and rendered retest. |
| Medium | 900×600 touch layout overlapped the header and cropped checkout. | Preserve two bounded panes, auto-size header, compact heights; repeated rectangle measurements pass. |
| Medium | `toISOString().slice(0,10)` used UTC for invoice/payment dates and displayed 2026-10-24 in Cairo on 2026-10-25. | Local-date helper used for invoices and collections; source test and rendered/DB verification. |
| Medium | Tax-inclusive frontend formula used exclusive-tax multiplication. | Denominator changed to `100 + rate`; exact EGP 115/15% regression test. |
| Medium | Posted invoice controls remained locally editable, allowing misleading on-screen/print values. | Lock all business fields and line actions after posting; rendered verification. |
| Low | Negative sub-unit parser lost the sign (`-0.50`). | Preserve regex sign; exact BigInt regression assertion. |

## Automated validation

Final commands are rerun after all fixes:

```text
dotnet build AlKhayal.slnx --no-restore
dotnet test AlKhayal.slnx --no-build --no-restore --logger "console;verbosity=minimal"
node frontend/tests/run-tests.js
node --check <every frontend/**/*.js file>
git diff --check
```

Final executed counts: **49/49 unit tests passed** and **112/112 integration tests passed**, with zero skipped or failed tests. The focused new draft-resubmission test passed independently and again inside the final full run. The executable frontend registry/calculation test passed for all **99 screens**; syntax checks passed for **25 JavaScript files**; `git diff --check` passed.

## Remaining limitations and owner checklist

The following compact checklist must be run on a machine where native Windows UI and printer surfaces are controllable:

1. Set `ALKHAYAL_DATABASE_PATH` and `ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER` to new disposable absolute paths; launch `AlKhayal.UI.exe`; verify the title `الخيال POS` and first-run screen.
2. At physical display 1366×768 and Windows scaling settings used in production, log in as the QA Cashier and open `الكاشير اللمسي`. Verify search, cart, quantity, payment, total, clear, and checkout are simultaneously reachable without page scrolling.
3. Repeat the cash sale, credit sale, insufficient-stock, and M/L variant workflows in WebView2; restart the process and reopen the invoices.
4. Open the posted receipt, select a known safe PDF printer, inspect Arabic identity, invoice number, L variant, quantity, prices, discounts, total, paid and remaining; cancel once, retry once, and verify no duplicate sale/payment.
5. If a supported thermal printer is available, print and reprint one QA receipt, disconnect it for one attempt, then reconnect and retry. Confirm the posted sale never rolls back or duplicates.

Native WebView2 acceptance, print preview, virtual PDF output, and physical printer output remain **BLOCKED**. No critical financial, inventory, customer-ledger, or return discrepancy remains in the executed browser/API/SQLite and automated coverage, so the release is **ACCEPTED WITH KNOWN LIMITATIONS**, not fully accepted.
