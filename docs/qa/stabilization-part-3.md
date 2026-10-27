# Stabilization Part 3 — directories and final QA

Date: 2026-10-27. Work performed in the existing uncommitted `main` checkout.

## Changes

- Customer creation no longer requires a code in either creation form. The server allocates the next available numeric `CUS-` code inside the SQLite write transaction, returns it, and the UI shows it on success. Existing codes are retained; explicit codes remain accepted for compatibility with existing API clients. SQLite serializes the write and the unique constraint remains in force. The sequence follows the highest currently present numeric code; deletion of the highest code may allow its later reuse.
- The warehouse balance view uses a 25-row server page with name, active SKU and barcode search. The warehouse predicate, variant/batch rows, quantities, FIFO completeness and valuation display are retained. Search and warehouse changes reset the page. Search keeps the input mounted and invalidates an earlier response immediately. The existing valuation button continues to calculate the selected warehouse as a whole, not the search result.
- The supplier directory uses a server page and searches all suppliers by name, code, contact or phone. Its page response includes the total and stable name/ID order. View, edit, add and delete remain available; ledger balances are read through the existing projection. Existing unpaged API callers retain their response contract.
- Added operation-level loading for these reads and customer creation. Existing POS, financial write, inventory adjustment and valuation guards remain. This is a targeted review of the affected screens, not a claim that every screen in the application has identical loading behavior.
- Removed the six specified sidebar entries. Their registered routes remain hidden, and ordinary sales invoice search remains available.

## Main files

`frontend/js/modules/{inventory,renderers}.js`, `frontend/data/screens.json`, `frontend/tests/run-tests.js`; `src/AlKhayal.Api/Controllers/{Inventory,Suppliers}Controller.cs`; customer, inventory and supplier application/data services and interfaces; `src/AlKhayal.Core/Models/SupplierPage.cs`; `src/AlKhayal.Data/Migrations/023_directory_query_indexes.sql`; focused integration tests and migration fixtures. API/database/testing documentation was updated.

## Verification

- Solution build `dotnet build AlKhayal.slnx --no-restore -v:q`: passed, zero warnings and errors.
- `node frontend/tests/run-tests.js`: passed, 99 registered screens.
- Targeted customer concurrency and warehouse name/SKU/barcode tests: passed 2/2 on disposable databases. The supplier page test also passed in a targeted run.
- `scripts/run-acceptance-tests.ps1 -All -NoBuild`: passed its coarse checks, 49/49 unit and 140/140 integration plus frontend. Scenario attribution: 21 PASS, 32 PARTIAL, 48 BLOCKED, 0 FAIL, 0 SKIP. QA-101 remains PARTIAL because its WebView2 screen check was not executed. The 48 BLOCKED scenarios include unmapped placeholders; they are not passes. The runner's JSON was written to `%TEMP%\alkhayal-part3-acceptance.json`.
- A subsequent targeted run passed 6/6, including authenticated OWIN requests for the new page endpoints and generated customer code, supplier legacy migration checks, and the 15,000-row benchmark. A selective inventory search across 15,000 zero-balance rows took **56.011 ms** on this host. This is not a benchmark of a large FIFO ledger or older hardware.
- Earlier full integration run reported 133/140: seven failures were historical migration fixtures that applied the new index before its tables or counted it in the wrong historical schema, and a customer validation test that still assumed a code was required. Those fixture and test assumptions were corrected without changing the ledger assertions.

## Database safety and limits

New write-capable tests use unique nonexistent files under the system temp directory, with explicit checks that the path is outside the operator directory. The acceptance runner applies its own fail-closed path gate. The operator database was inspected for metadata only: 121,712,640 bytes and 2026-10-25 14:41:42 UTC modification time, unchanged from the Part 2 record. No normal desktop launch or migration against that database was performed.

Native WebView2 interaction, physical scanner and printer checks remain unexecuted. POS idle navigation was not reproduced. Large-data measurements are local host timings, not legacy-hardware guarantees. Paid/cash/returned/tracked invoice cancellation remains blocked pending the policies described in Part 1.
