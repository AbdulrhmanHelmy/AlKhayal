# Pre-Phase 9 — selling prices and FIFO accounting invariants

Audit and verification: 2026-10-29. Scope: establish the pricing/cost facts required by Phase 9; no reporting module or Phase 9.1 implementation.

## 1. Approved pricing rule

The current applicable product/variant price defaults **new** sales of all remaining stock, irrespective of receipt date. FIFO determines acquisition cost, never selling price. Editing catalog sale/reference-purchase prices must not reprice posted invoices, original inventory layers, cost allocations, returns, or realized profit.

Persisted variant overrides take precedence over the product base price. There is no independent unit-price table: unit prices derive from the applicable base-unit variant price and the persisted rational unit conversion. User-entered sale prices are in piastres; internal money stays scale 4 and quantities scale 6.

## 2. Existing behavior found during the audit

| Area | Actual behavior |
| --- | --- |
| Catalog and quick-add | `CatalogService` saves sale and optional reference-purchase prices in `ProductDetails`. Quick-add uses the same create endpoint; full edit submits the product configuration aggregate. |
| Variant pricing | `COALESCE(ProductVariants.SellPriceOverrideScaled, ProductDetails.SellPriceScaled)` is the existing precedence. Purchase-reference overrides are separate metadata. |
| Standard Sales / Touch POS | Both use `sales.js`, retrieve real product detail on selection, and persist actual line prices through Sales drafts. Barcode lookup identifies the variant/unit; selecting a different variant must use that variant's price. |
| Draft save | Server validates active product/variant/sale unit and current applicable price. Saving explicitly recalculates totals and snapshots the current tax settings. |
| Draft posting / invoice reads | Posting consumes saved lines; it does not fetch current catalog prices or tax rates. Reads use `SalesInvoiceLines.UnitPriceScaled`, discounts, tax and totals. |
| Purchase posting | Purchase line cost and retail price are explicit inputs. `SqlitePurchaseRepository` invokes inventory posting inside the invoice/supplier transaction. The selected-unit cost creates the actual FIFO receipt. |
| Inventory receipt | `UnitCost` determines layer value; `SalePrice` updates the selected variant's current retail price. A simple sole variant also mirrors this price in `ProductDetails`. A new receipt can intentionally change the current retail price; retry cannot. |
| FIFO | Issues allocate oldest origin/layer first, retaining exact immutable quantity/value allocations. Final withdrawals take the remaining value residual. Unresolved cost basis blocks issues. |
| Transfers / counts / purchase returns | Transfers retain receipt origin and allocated cost; shortages consume FIFO; positive corrections require actual cost. Purchase returns restrict consumption to the original receipt origin, including transferred stock. |
| Sales returns / cancellation | Original sale allocations determine restored cost. Eligible cancellation appends reversing inventory/customer records. Original sale rows remain intact. |
| Valuation | On-demand API reconciles remaining layer totals against warehouse balances. It exposes cost, potential selling value and estimated margin, with completeness flags. |
| Profit screens | No persisted realized-profit reporting use case exists. Legacy sample calculations are not a source of financial facts for Phase 9. |

## 3. Reproduced bugs and gaps

1. **Simple product price shadowing:** after receipt at retail price 150, editing the product to 200 left the sole variant's receipt-created override at 150. Future sales and expected selling valuation continued using 150. The first six focused tests all failed before the correction, including the approved example and changed-price tax tests.
2. **Configuration round-trip cleared overrides:** the application editor omits override mutations when mapping configuration commands; the store nevertheless wrote null over existing variant overrides. Loading existing variant rows for update also omitted the price columns. The expanded two-variant tests reproduced both equal-to-base and distinct-price loss.
3. **Selected sale unit was not priced through its conversion:** both frontend and draft validation treated the base-unit price as the selected-unit price. A box of three could be sold for one base unit's price. The picker could also reuse a scanned variant's price after selecting another variant.
4. **Premature display rounding:** exact scale-4 base prices derived from receipt unit conversion were available in storage but only exposed as formatted two-decimal strings. Converting 3.3333 × 3 after formatting 3.33 produces the wrong default.

No defect in the FIFO allocation algorithm was found or assumed. No FIFO history was rewritten.

## 4. Exact corrections

- `SqliteCatalogConfigurationStore`: an explicit changed product price clears only an evidenced **receipt mirror** on a simple, sole default variant outside variant mode, where the override equals the old product price. It then inherits the edited product price. A distinct override, any configured variant product, unchanged price, or unproven override is preserved. This happens inside the existing catalog transaction/version check; no bulk repricing or migration runs at startup.
- The same store now loads exact existing variant prices and retains them when an aggregate update supplies no override. This applies to both regenerated size/color variants and existing explicit configurations. Direct internal store records may still provide an explicit non-null override; the HTTP catalog editor does not introduce a new override-edit use case.
- Application `SalesPricing.SelectedUnitPrice` converts scale-4 base price using numerator/denominator and rounds **once** to piastres, with decimal arithmetic and checked bounds. Draft validation uses this calculation; posted drafts retain their saved price.
- Product variant detail adds `effectiveSalePriceExact`, an invariant four-decimal **base-unit** string. Existing formatted fields and barcode lookup meanings remain compatible.
- `sales.js` uses that exact base string and BigInt rational arithmetic for new selected-unit defaults in both sales interfaces. The confirmed picker uses the selected variant, and an existing cart line keeps its saved unit price when quantity is increased.

No endpoint, package, schema migration, reporting module, historical correction, commit, push, or branch change was added.

## 5. FIFO layer behavior and valuation

Acquisition cost comes from the authoritative receipt, never `ProductDetails.PurchasePriceScaled`. Changing that reference price neither issues stock nor updates layers. New purchases may use a different explicit cost. Transfers carry actual allocated values and lineage; purchase returns continue using original-origin restrictions. Existing adjustment/reconciliation rules are unchanged.

Use `SUM(InventoryCostLayers.RemainingValueScaled)` for remaining cost, with the warehouse/variant/batch and completeness/integrity checks already used by valuation. Do not reconstruct layer value by multiplying displayed unit cost: exact partial allocations can contain rounding residuals.

The approved untaxed example is exercised through actual catalog, inventory and sales services:

| Fact | EGP |
| --- | ---: |
| Revenue: 12 × edited retail price 200 | 2400 |
| FIFO COGS: 10 × 100 + 2 × 120 | 1240 |
| Realized gross profit | 1160 |
| Remaining cost: 8 × 120 | 960 |
| Remaining potential selling value: 8 × 200 | 1600 |
| Expected gross margin amount | 640 |

The 640 expected amount is not earned revenue or realized profit.

## 6. Historical invoice price preservation

Posted line `UnitPriceScaled`, `DiscountScaled`, `InvoiceDiscountScaled`, `TaxScaled`, `LineTotalScaled` and invoice totals remain the financial facts. Product names are live labels in reads; this task does not add historical product-label snapshots. Identifiers and document links remain unchanged.

An existing saved draft can be read and posted at its original price after a catalog edit. Existing policy remains: an explicit draft save checks the current price and tax settings; an outdated price is rejected rather than silently replaced. The frontend performs a save before ordinary checkout, so an old-price draft may require resolving that conflict through the existing workflow. No automatic draft repricing was added.

## 7. Historical COGS preservation

Join `SalesInvoiceLines.InventoryDocumentLineId` to outgoing `StockMovements.DocumentLineId`, then sum positive `InventoryCostAllocations.ValueScaled` by `MovementId`. The outgoing movement's signed value is negative; do not negate both sources or join costs through today's catalog prices. Aggregate allocations before joining return/payment rows to avoid multiplying COGS.

Catalog edits and idempotent posting retries leave original allocation IDs, layer IDs, quantities and costs intact. The new tests compare persisted financial/cost facts before and after edits, returns and cancellation, and rerun migrations/reopen services on disposable files.

## 8. Returns and cancellations

Sales returns cap quantity at the original sold amount less previous returns, refund the original persisted line total proportionally, and give the final return its exact financial residual. Their cost restoration uses original `InventoryCostAllocations`, subtracting previous `SalesReturnCostAllocations`; the final allocation consumes the residual. A return spanning two source layers restores both original costs. Selected-unit conversions use the original inventory document conversion snapshot.

Credit returns follow the current debt-first `SalesRefundPolicy`: debt reduction is capped at current customer debt and any remainder follows the approved cash-refund path. Cash refunds use the sales-returns cashbox; they do not reprice the sale or draw from shift revenue. Electronic reversal remains blocked where unsupported.

An eligible unpaid credit cancellation appends `SalesCancellations` and its reversing inventory document; original invoice/cost allocations remain immutable. Current exclusions (cash, payments, returns, tracked physical units or ambiguous downstream collections) remain blocked. Cancellation restores exact original allocated quantity/value; it does not use current catalog cost. Retries create one reversal only.

## 9. Realized profit contract

For a posted sale before returns/cancellation:

```text
net recognized sales revenue = SalesInvoices.TotalScaled - SalesInvoices.TaxScaled
actual FIFO COGS = sum(original sale InventoryCostAllocations.ValueScaled)
realized gross profit = net recognized sales revenue - actual FIFO COGS
```

`SubtotalScaled` already deducts line discounts; invoice discount is separate. Equivalently, line net revenue is `LineTotalScaled - TaxScaled`. Do not deduct discounts again from final totals. Expenses, customer collections, treasury deposits and supplier payments are not additional sales revenue or COGS.

For return-adjusted reporting, subtract the recognized original-price **net-of-tax** return revenue and subtract the original restored FIFO cost from COGS. Cancellation must reverse applicable revenue and cost once. A lifetime report can exclude canceled sales and their reversal effects; a period/event report needs original posting and cancellation events in their respective periods. Do not combine both treatments.

## 10. Expected margin contract

```text
potential selling value = sum(remaining base quantity × current applicable base-unit sale price)
expected gross margin amount = potential selling value - remaining actual FIFO cost
```

The existing valuation endpoint rounds each warehouse/variant/batch selling amount to piastres and returns four-decimal strings. It is a raw current-retail estimate, without hypothetical discounts, future tax changes, expenses, or assumptions about which sale unit will be chosen. It is not a margin percentage or a tax-adjusted realized-profit forecast. Null/incomplete costs must not be substituted with zero.

## 11. Tax and discount assumptions

Existing inclusive/exclusive tax calculations, rate snapshots, piastre rounding, line and invoice discount allocation, proportional refund/final residual rules are unchanged. Tests cover both 15% inclusive and exclusive settings with line plus invoice discounts; changing tax settings after posting leaves the original amounts unchanged.

Return rows persist refund totals and original-line links, but **do not separately persist returned tax/discount components**. Phase 9 must define and approve a deterministic allocation/reconciliation of the original saved components for partial returns before presenting net-of-tax returned revenue. This task does not invent a tax/refund policy or claim those components already exist. Original invoice tax must never be recomputed using today's tax settings. For no-tax returns, the existing persisted refund and FIFO restoration facts are sufficient directly.

## 12. Verification

All new integration fixtures create randomly named databases under the OS temporary directory, explicitly outside the normal operator data directory, and clean up their own files. Test child processes additionally receive `ALKHAYAL_DATABASE_PATH` pointing inside `.tmp`. The running desktop and its normal AppData database were not used. Isolated SDK artifacts are under `artifacts/pre-phase-9`.

Executed from `D:\projects\AlKhayal`:

```powershell
$env:ALKHAYAL_DATABASE_PATH=Join-Path $PWD '.tmp\pre-phase-9-guard.db'
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/pre-phase-9 --no-restore --filter FullyQualifiedName~PricingFifoInvariantTests --logger 'trx;LogFileName=pricing-final.trx' --results-directory artifacts/pre-phase-9/results -m:1
dotnet test AlKhayal.slnx --artifacts-path artifacts/pre-phase-9 --logger 'trx;LogFileName=full-suite.trx' --results-directory artifacts/pre-phase-9/results -m:1
node frontend/tests/pricing-fifo-tests.js
node frontend/tests/run-tests.js
$files=Get-ChildItem frontend/js -Recurse -Filter *.js
foreach($file in $files){node --check $file.FullName; if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}}
node --check frontend/tests/pricing-fifo-tests.js
dotnet build AlKhayal.slnx --artifacts-path artifacts/pre-phase-9 --no-restore -m:1
git diff --check
```

| Check | Actual result |
| --- | --- |
| First focused reproduction, before fixes | 6 failed / 0 passed, matching stale retail-price behavior; `pricing-before.trx` |
| Expanded override reproduction | 2 failed / 7 passed; reproduced configuration loss of persisted variant prices |
| Final focused pricing/FIFO suite | **11 passed**, 0 failed/skipped; approximately 14 seconds; `pricing-final.trx` |
| Full integration suite | **261 passed**, 0 failed/skipped; approximately 2 minutes 48 seconds |
| Full unit suite | **111 passed**, 0 failed/skipped; approximately 9 seconds |
| Focused frontend | Passed for both sales interfaces |
| Full frontend suite | Passed, **108 registered screens**, including all existing handler suites |
| JavaScript syntax | Passed for all **30 application files** and the new test |
| Complete solution build | Passed, **0 warnings / 0 errors**, including WinForms UI |
| Diff whitespace | Passed; Git also printed existing LF/CRLF normalization notices on unrelated working files |

The full solution test command used one explicit TRX filename for both assemblies, so the unit run overwrote `full-suite.trx`; the integration count above is the completed runner output, not a claim that its TRX remains separately available. Use unique/default TRX filenames for future multi-project runs. Focused before/after TRX files remain under the isolated results directory.

Existing FIFO, purchase-return, serial tracking, unsupported cancellation, integrity, concurrency and API tests passed as part of the full integration run. No Windows 7/native-device result is inferred from these automated checks.

Task-specific changed/new files (pre-existing unrelated work is retained):

- Application: `Catalog/CatalogService.cs`, `Catalog/CatalogVariantContracts.cs`, `Sales/SalesPricing.cs`.
- Data: `Catalog/SqliteCatalogConfigurationStore.cs`, `Sales/SqliteSalesStore.cs`.
- Frontend: `frontend/js/modules/sales.js`, `frontend/tests/pricing-fifo-tests.js`, `frontend/tests/run-tests.js`.
- Tests: `tests/AlKhayal.IntegrationTests/PricingFifoInvariantTests.cs`, `tests/AlKhayal.UnitTests/SalesPricingTests.cs`.
- Documentation: this handoff and `docs/architecture.md`, `docs/design.md`, `docs/database.md`, `docs/api-contracts.md`, `docs/testing.md`.

## 13. Remaining limitations

- No retrospective repair of posted invoices, old incorrect selected-unit prices, or cleared historical overrides. Existing IDs and cost history are preserved.
- Unproven or genuinely distinct overrides retain precedence; changing only the product base is not an instruction to overwrite them. A historical override already lost before this task cannot be reconstructed from the catalog alone.
- Missing legacy acquisition bases still block complete valuation/issues under existing rules. This work does not revalue legacy stock.
- Potential valuation uses current prices, not historical/as-of retail prices. It is not itself a Phase 9 report.
- Partial-return tax component attribution and period/event reporting contracts require Phase 9 design approval as described above.
- Tests run on the installed Windows/.NET environment. Native WebView2 UI acceptance, low-resource benchmarks and Windows 7 / Chromium 109 runtime validation were not performed. Added frontend arithmetic uses BigInt supported by Chromium 109; no framework, network asset or browser API dependency was introduced.

## 14. Required Phase 9 calculation contracts

1. Read persisted sale/line financial facts and actual allocation/return/cancellation links. Current catalog prices are only inputs to current potential valuations.
2. Define report grain and dates explicitly: business invoice date versus UTC posting/return/cancellation event timestamps, local display boundaries, and reproducible period/as-of rules.
3. Return gross-before-discount, line discount, invoice discount, tax, net recognized revenue, original COGS, restored cost, net COGS and realized gross profit as separate reconciled fields.
4. Define partial-return tax/discount component allocation from original snapshots, including repeated partial returns and final residuals; reconcile it to persisted refunds without changing history.
5. Apply cancellation once and independently of live invoice-status projections for historical periods. Preserve blocked unsupported refund scenarios.
6. Aggregate each one-to-many financial/cost source independently before joining. Do not double-count multiple FIFO layers, payments, returns or warehouses.
7. Sum exact scale-4 costs and scale-6 quantities with checked decimal/integer arithmetic; only format at presentation. Never calculate money using floating point or unsafe JavaScript JSON integers.
8. Keep remaining inventory cost, raw potential selling value, expected margin and realized profit separate, with explicit completeness and valuation scope.
9. Reconcile report outputs to original invoices, inventory movements/allocations, return allocations and cancellation documents on disposable regression datasets, including retry/restart paths, before implementing Phase 9.1.
