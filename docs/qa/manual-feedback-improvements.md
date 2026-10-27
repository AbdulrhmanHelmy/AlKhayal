# دليل تحسينات ملاحظات المستخدم — سجل تقدم قابل للاستئناف

> Resumable progress record for the six-batch user-feedback stabilization plan
> (QA Architect plan, six batches, issue IDs UX-001..UX-017).
> This file records actual status only. Do not treat any item as VERIFIED without real evidence.

**Last updated:** 2026-10-27 (Stabilization Parts 1–3 cross-reference)
**Part 2 execution:** sole Codex engineer; no delegated worker.
**Database safety:** Part 2 regressions used only disposable temp SQLite files; the operator database was unchanged. No commits or pushes.

## Overall batch status

| Batch | Scope | Status |
|---|---|---|
| 1 | Global focus, search, navigation | **IMPLEMENTED — awaiting manual verification** |
| 2 | Touch POS browsing/usability | **IMPLEMENTED — awaiting manual WebView2 verification** |
| 3 | Sales invoice cancellation | PARTIALLY IMPLEMENTED — see stabilization Part 1 |
| 4 | Customers, inventory, suppliers | IMPLEMENTED — automated verification and manual checks in Part 3 |
| 5 | Inventory valuation, loading states | PARTIALLY IMPLEMENTED — see stabilization Part 1 |
| 6 | Sidebar cleanup, final QA | IMPLEMENTED — see Part 3 and final report |

## Batch 1 — Global focus, search & navigation

### UX-001 — Search inputs lose focus / characters after one letter (entity lists)
- **Status:** FIXED (frontend implementation; pending manual verification)
- **Root cause:** `entityList` in `frontend/js/modules/renderers.js` re-invoked the full renderer on the debounced `input` event of `#listSearch`, replacing `container.innerHTML` (including the focused input). Fast typing therefore lost both focus and the caret/IME composition context. In-place update now writes only the `[data-table-host]` table, the `[data-list-count]` count, and `[data-list-pagination]`, so the input element is never recreated.
- **Fix:** in-place list update path inside `entityList` (renderers.js) + regression tests `verifyEntityListSearchKeepsFocus` and the search-focus suite in `frontend/tests/run-tests.js`.
- **Evidence:** `node frontend/tests/run-tests.js` — passed (99 screens), including the entity-list focus-survival test.

### UX-002 — Same focus-loss pattern in Settings hub / Business activities / Catalog screens
- **Status:** FIXED (frontend implementation; pending manual verification)
- **Root cause:** the same full-redraw-on-input pattern existed in `settings-hub.js` (`[data-settings-search]`), `business-activities.js` (`#activitySearch`), and `catalog.js` (`#catalogSearch` product list plus `#categoryManagementSearch` / `#unitManagementSearch`), each destroying the focused input after its debounce; business-activities additionally patched focus back afterwards (`focus()` + `setSelectionRange`), which is brittle for Arabic IME composition and selection.
- **Fix:** each screen now keeps the search input mounted and repaints only its results region: `[data-settings-results]` (settings-hub), `#activityGrid` (business-activities), `#catalogResults` (product list), `#categoryManagementResults` / `#unitManagementResults` (management tables). The initial load bug where the catalog management screens never painted their first rows (`render()` was invoked without `paintRows()`) was also fixed.
- **Evidence:** `verifySearchInputFocusSurvival` in run-tests.js (behavioral, fake DOM): typing mutates only the results region, container write count stays constant, and the same input node survives; suite passed.

### UX-003 — Touch POS / sales editor search loses focus and stale results override newer ones
- **Status:** FIXED (frontend implementation; pending manual verification)
- **Root cause:** in `frontend/js/modules/sales.js`, `searchProducts()` called `draw()` (full container rewrite incl. the search input) after each debounced response, then re-focused the recreated input — focus/IME still broke at typing speed, and two overlapping searches had no ordering: a slower older response could overwrite a newer result set.
- **Fix:** `searchProducts()` now uses a monotonic `searchToken`; responses that are not the latest, whose target input was unmounted, or whose screen is inactive are discarded; results repaint only into the mounted product grid (`.touch-product-grid` or `.sale-product-grid`). Same pattern applied to the invoice-history `#salesSearch` (dedicated `#salesListResults` region) and the sales-returns `#data-return-search` (dedicated `returnListHtml()` region).
- **Evidence:** new regression test `verifySalesSearchMountedInput` in run-tests.js: renders POS in a virtual DOM, starts two searches, resolves them out of order, asserts the stale payload is dropped, the grid region alone is repainted, and the identical input node is retained; invoice-history typing asserts container writes stay constant. Suite passed.

### UX-004 — Enter-only search fields (barcode scanner behavior in user-facing searches)
- **Status:** FIXED (human live search everywhere; scanner behavior preserved)
- **Root cause:** typing was only visually "live" through the refocus/redraw path (UX-001..003); a slow search effectively required pressing Enter or pausing to avoid character loss. No screen required Enter for normal text search — Enter handling on POS exists solely for barcode scans and was retained.
- **Fix:** with the results regions decoupled from the input, every covered search field performs incremental live search without Enter. Part 2 also adds rapid per-character scanner detection for exact barcode lookup without an Enter suffix, retains `keydown → Enter → scanBarcode()`, and prevents a late suffix from adding the item twice.
- **Evidence:** run-tests.js executes the no-suffix and late-Enter paths with a virtual DOM; manual verification of a physical scanner is still required.

### UX-005 — Stale async screen repaints (screen-left-in-flight requests)
- **Status:** FIXED (frontend implementation; pending manual verification)
- **Root cause:** several persisting-data flows in `renderers.js` (entity list fetch, customer entity form, customer/supplier statement initial fetch, supplier statement shell) and `sales.js` had no `context.isActive()` (render-generation) guard, so an in-flight request that resolved after switching screens/tabs repainted `workspace` with the old screen — including suppliers-list content appearing over the current screen.
- **Fix:** added `context.isActive()` guards to those fetch callbacks in renderers.js; clear the workspace-level delegated handlers (`workspace.onclick/oninput/onchange = null`) in `app.js` `renderActive` before mounting a new screen so late callbacks from a previous screen can no longer drive navigation or repaint; stale in-screen searches additionally use per-flow tokens (UX-003).
- **Evidence:** run-tests.js includes catalog stale-token checks (`request !== state.request`, `data-catalog-retry` retries) and passes.

### UX-006 — Unexpected navigation to Supplier List (user finds "قائمة الموردين" open)
- **Status:** asynchronous repaint guards and tab-route persistence implemented; reported idle jump not reproduced
- **Investigation result:** no timer, interval, hashchange listener, poller, or autonomous navigation path to `suppliers-list` exists (`frontend/js/app.js` only reads `location.hash`/`alkhayal_last_route` once at startup; the only interval is the topbar clock; quickbar/global-search/sidebar all require directed clicks).
- **Possible mechanisms:** stale async repaint (UX-005) could render suppliers-list HTML after a tab switch. Part 2 also found that activating an existing tab did not persist the actual active route; a later shell reload could restore an older Supplier List route. Neither event sequence has been captured in a live WebView2 session.
- **Mitigation:** `isActive()` guards on fetch-repaint paths, workspace handler reset per render, and Part 2 active-tab route persistence. Runtime WebView2 lifecycle itself could not be reproduced and remains for manual verification.
- **Manual verification needed:** leave POS/other screens idle and switch tabs during a slow suppliers-list load; confirm never lands on suppliers-list.

### Batch acceptance (Batch 1)
- Input focus works in all major shared search/list screens (entity lists, catalog, settings hub, business activities, POS, invoice history, returns). [automated: yes; manual: pending]
- Human typing requires no Enter across screens. [automated: yes]; scanner Enter behavior preserved [manual hardware test pending].
- No autonomous Supplier List navigation path was found; delayed repaint and stale restored-route gaps were addressed. The idle report remains unreproduced. [manual verification pending]
- Relevant frontend tests pass: `node frontend/tests/run-tests.js` → **PASS (99 screens)**, run 2026-10-26.
- No backend, schema, or financial behavior touched. No commits/pushes. No DB writes.

## Batch 2 — Touch POS (IMPLEMENTED; MANUAL VERIFICATION PENDING)

UX-007..UX-011 were implemented in [stabilization-part-2.md](stabilization-part-2.md): paged best-selling/default product browse, category controls and stable colors, live name/SKU/barcode search, bounded customer lookup, feature-aware warehouse selection, fixed/percentage discount, and compact CSS. Automated frontend and disposable SQLite tests passed. Physical scanner and authenticated WebView2 visual/input acceptance remain open.

## Batch 3 — Sales invoice cancellation (PARTIALLY IMPLEMENTED)

Issues UX-012..UX-013: draft deletion and guarded cancellation of unpaid credit invoices were implemented and tested in `stabilization-part-1.md`. Cash sales, paid invoices, linked returns, and serial-tracked cases remain blocked pending explicit financial/physical return policy and manual WebView2 verification.

## Batch 4 — Customers, inventory, suppliers

Issues UX-014..UX-016: customer code allocation and server-paged inventory and supplier directories are implemented. See stabilization-part-3.md for test results and manual limits.

## Batch 5 — Inventory valuation & loading (PARTIALLY IMPLEMENTED)

Issue UX-017: on-demand server-side FIFO valuation and an operation-specific loading state were implemented and tested in `stabilization-part-1.md`. Large-dataset benchmarking and manual WebView2 verification remain open; other unrelated loading screens are outside this stabilization part.

## Batch 6 — Sidebar cleanup & final regression

The requested six sidebar routes remain registered but are hidden from navigation. See stabilization-final-report.md for final regression results and outstanding manual work.

## Process notes

- Historical Batch 1 notes recorded `opencode-go/glm-5.3-flash`; Part 2 was completed directly by Codex without delegation.
- Batch 1 was executed in incremental sub-attempts (entity list first, then shared screens, then POS/sales + shell fixes + tests + docs). Workflow change that helped: keep each subtask to one file family plus one regression test; reuse the fake-DOM `makeContainer` harness in run-tests.js.
- Nothing committed or pushed. Part 2 applied migration 022 only to disposable test databases; no operator DB cleanup or migration was run manually.

## Safety constraints that remain binding

- Real DB `%LOCALAPPDATA%\AlKhayal\alkhayal.db` must never be reset, reseeded, or written to by tests. Write-capable tests require an isolated disposable SQLite database with explicit path verification before executing.
- No commits, pushes, or branch switches during this plan.
- Do not claim intermediate completion without independently executed tests that confirm the specific acceptance criteria.

## Manual verification needed (Batch 1)

- Type full Arabic/English queries in: shared entity lists, product catalog, category/unit management, Settings hub, Business activities, POS product search, invoice history, sales returns — confirm focus, caret position, backspace/selection, and Arabic composition survive; no character loss at fast typing.
- Verify a physical/keyboard-emulated barcode scanner still completes scans with the optional Enter suffix and never triggers duplicate additions.
- Idle/tab-switch stress during slow supplier loads to confirm no unexpected suppliers-list repaint.
- Light and dark themes on the affected screens; 1366×768 and 1024×768 layouts.
- Part 2 additionally needs authenticated Touch POS visual/input acceptance at about 900×600, including category scrolling, customer/warehouse selection, percentage discount, and rapid scans without Enter.
