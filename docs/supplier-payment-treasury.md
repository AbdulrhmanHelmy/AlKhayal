# Supplier settlement, treasury and invoice allocation

Every new HTTP supplier payment requires an explicit `treasuryAccountId`. The dialog loads saved cashboxes/banks and their exact balances, supports loading/empty/error/retry states, and disables Save until accounts are available and one is selected. The payment amount is sent as decimal text. The source selection and amount stay locked during posting and uncertain-response retries; the same operation key and command are retained. Account lookup and payment require effective `supplier-payment` access (administrators retain access); this does not grant manual treasury withdrawals.

`SupplierLedgerService.AddTreasuryPaymentAsync` validates the source and authenticated actor. The payment's version-3 hash uses length-prefixed canonical fields including source account and actor. The existing account-only Application method and its old hashes remain for historical service callers, but the HTTP endpoint always uses the integrated method. Old entries are neither retroactively deducted nor reallocated. A changed account, actor, amount or invoice with the same key conflicts; an identical replay returns the original ledger entry and movement IDs.

One SQLite writer transaction validates supplier/invoice outstanding, builds allocations from current invoice balances, appends the supplier payment, writes allocations, withdraws the exact amount through the shared `SqliteTreasuryStore.PostWithinTransactionAsync`, links the movement and updates the supplier projection. No nested transaction or separate treasury commit exists. Missing/deleted sources, insufficient funds, overpayment or any late failure roll everything back. Immutable source links and allocation rows preserve the financial trail. Concurrent payments cannot spend the same funds or invoice remaining balance twice.

## On-account priority

For a payment without a selected invoice, Application `SupplierPaymentAllocator` distributes it among all the supplier's unpaid posted invoices:

1. Earliest due date, falling back to invoice date when no due date exists. Overdue dates come first.
2. Earliest original creation timestamp for equal priority dates.
3. Stable source ID/family order if both timestamps also match.

Each invoice receives up to its remaining amount; the remainder proceeds to the next invoice, including partial payment of the final one. Account-debt invoices and integrated purchase invoices participate together; drafts are excluded. An explicitly selected invoice receives only its own allocation. Any payment beyond the sum of eligible invoices, while still within supplier payable, remains on account against the supplier's opening/uninvoiced debt rather than inventing an invoice.

Invoice `paidAmount` and `remaining` include automatic allocations. Direct invoice-linked ledger payments remain counted once; their audit allocations are excluded from the extra account-allocation sum. Posted returns also reduce invoice remaining. Subsequent invoice-payment validation uses this same allocation-aware projection, so another outstanding invoice cannot hide an overpayment of an already allocated one. The UI reloads supplier summaries and invoice tables after success.

## Additive migrations

- `026_supplier_payment_treasury.sql` creates immutable `SupplierPaymentTreasuryLinks`, linking a supplier ledger entry to exactly one treasury withdrawal and source account.
- `027_supplier_payment_allocations.sql` creates immutable `SupplierPaymentAllocations`, linking each portion to one legacy or purchase invoice, with indexed foreign keys and per-payment/source uniqueness. Amounts are exact scale-4 integers; historical sub-piastre invoice residuals can be allocated without rounding, while the overall user payment/treasury withdrawal remains limited to piastres.

No existing financial row, posted invoice or original migration is edited. Existing deployments apply both at normal startup. Historical fixture builders exclude these dependent scripts until their prerequisite schemas are introduced; final migration-count expectations are 27.

## Verification

`SupplierPaymentTreasuryTests` covers cash/bank selection, on-account/explicit-invoice settlement, exact balances, source links/history, concurrent replay, changed-account conflicts, missing source, insufficient funds, injected late-projection rollback/retry, immutable links and concurrent payments across suppliers sharing one account. `SupplierPaymentAllocationTests` verifies due-date priority, creation-time tie-breaking independent of ID order, mixed purchase/debt sources, date fallback, partial allocation, replay, paid/remaining refresh and overpayment rejection after allocation. Supplier HTTP tests verify CSRF, required selection, real movement IDs/balances, source screen permissions, cashier manual-withdrawal denial and automatic on-account allocation.

`frontend/tests/supplier-accounts-tests.js` covers source submission for both invoice families, explicit account selection, in-flight duplicate protection, locked uncertain retries, stable payload identity and refreshed server-projected invoice balances. The frontend suite and `node --check frontend/js/modules/renderers.js` pass.

On 2026-10-28, `dotnet test AlKhayal.slnx --no-restore -m:1` passed 175 integration and 50 unit tests. The ordinary solution build could not replace the running desktop's locked DLLs (`MSB3021/MSB3027`), so the complete UI/dependency build was verified with `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj --no-restore -m:1 -p:OutDir=D:/k/AlKhayal/.tmp/supplier-payment-validation/`, which passed with zero warnings/errors. The operator must close the running desktop and run the normal project again to build/copy the updated binaries. No process was forcibly closed or operator database mutated for testing. NuGet vulnerability metadata remained unavailable during the ordinary test build (`NU1900`). Native visual/DPI and Windows 7/Chromium 109 verification remain target-machine checks; no dependency or runtime target changed.

A read-only SQLite backup of the actual local database also migrated through 026 and 027 twice successfully and passed integrity/foreign-key checks. Its initially empty source-link/allocation tables confirmed there was no retroactive deduction or allocation. Creation-time comparisons normalize UTC instants, including timestamps with different fractional-second precision; the unit regression covers this tie-break edge case.
