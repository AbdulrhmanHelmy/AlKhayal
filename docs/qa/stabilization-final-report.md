# Stabilization final report — Parts 1–3

Date: 2026-10-27. This report distinguishes automated results from checks requiring a person, desktop or hardware.

## Part 1 — financial integrity

Draft deletion is an auditable tombstone; posted unpaid credit invoices without downstream activity can be canceled through an atomic reversal of inventory FIFO and customer receivables. Posted history remains visible. Cash, paid, returned, and physically tracked invoices are explicitly blocked because refund, collection allocation, or physical-return policies are unresolved. Sales and purchase posting operation-key conflicts were clarified and the existing financial idempotency tests retained. On-demand warehouse valuation uses remaining FIFO cost and current selling prices, and labels the margin potential rather than realized. See [Part 1](stabilization-part-1.md) for detailed scope, exclusions and actual previous test evidence.

## Part 2 — input and Touch POS

Search fields retain their mounted inputs and discard stale results. Touch POS loads a server-paged product browse, supports categories, warehouse and customer selection, scanner input, and fixed or percentage discounts. Tab route persistence was corrected; the reported supplier-list idle jump itself was not reproduced. The recorded Part 2 checks passed: 49 unit, 137 integration, 99-screen frontend and UI build; WebView2 and physical scanner acceptance remained open. See [Part 2](stabilization-part-2.md).

## Part 3 — directories and loading

Automatic customer code allocation, server-side warehouse and supplier pagination/search, targeted loading states, and the six requested sidebar removals are implemented. The supplier CSV action fetches all matching pages only on request. See [Part 3](stabilization-part-3.md) for changed files, tests and limitations.

## Final automated results

- Complete solution build: passed with zero warnings and errors.
- `scripts/run-acceptance-tests.ps1 -All -NoBuild`: unit **49/49**, integration **140/140**, and frontend **99 screens** passed. Scenario results: **21 PASS, 32 PARTIAL, 48 BLOCKED, 0 FAIL**. PARTIAL retains a manual remainder; BLOCKED includes unmapped scenarios. QA-101 is PARTIAL for its unexecuted WebView2 portion. Runner result: `%TEMP%\alkhayal-part3-acceptance.json`.
- After that run, a focused 6/6 test run passed the authenticated new endpoints and customer creation, historical supplier migrations and the 15,000-row query check. The selective inventory query took **56.011 ms** on this host over 15,000 zero-balance rows. The frontend suite was rerun after the supplier export and stale-response regression changes and passed.

## Manual checklist — all unexecuted

- In a disposable WebView2 profile and database, type Arabic and English, paste, backspace and use composition in customer, supplier and inventory searches; check focus and caret.
- Browse Touch POS categories and pages, select customer and warehouse, then test a physical scanner with and without an Enter suffix.
- Post and cancel an eligible unpaid credit invoice; confirm history, quantity, FIFO value and customer statement. Confirm paid/cash/return cases explain their block.
- Run warehouse valuation, inspect incomplete-cost messages, switch warehouses and verify displayed values.
- Check light and dark themes at 1366×768 and a smaller window. Restart and verify settings, route and persisted business data.
- Exercise a physical printer when available; verify output and failure/retry behavior without using an operator database.

## Safety, open work and next steps

Automated writes used disposable SQLite paths. Operator database metadata remained 121,712,640 bytes and last modified 2026-10-25 14:41:42 UTC. No commit, push or branch switch was made. Migration 023 must be applied through the normal migrator when the user next starts the application; it adds indexes and does not change balances.

Agree cash refund/cash-drawer, payment allocation and exact physical return policies before expanding cancellation. Run the checklist on real WebView2 and hardware, investigate the unreproduced supplier-list navigation if it recurs, benchmark realistic large sales history and FIFO data on target hardware, and review remaining loading states.
