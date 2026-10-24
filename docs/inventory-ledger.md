# Inventory ledger — Phase 2 / Task 2.4

## Scope and invariant

Migration `011_inventory_ledger.sql` adds the first persisted warehouse quantities. All current quantity changes enter through `InventoryService.PostAsync`, `PostCountAsync`, or the restricted reversal path; each reaches one `SqliteInventoryStore.PostInTransactionAsync` coordinator. Future purchase receipts, sales, and returns must call that same posting boundary with a distinct document type and an idempotent operation key. They must never update `InventoryBalances` directly.

`StockMovements` is immutable. Each signed movement references a posted `InventoryDocuments` header, line, warehouse, variant, and optional batch. Transfers create negative and positive movements in one SQLite transaction. `InventoryBalances` is a warehouse/variant/batch projection, updated immediately after each movement in that transaction. Its insert/update triggers require exact equality with the ledger aggregate; delete is blocked. Posted document headers, lines, movements, and audit rows have update/delete guards. `GET /api/inventory/integrity` checks quantity and value aggregates; it does not repair them.

The transaction validates active warehouses, stock variants, exact product-unit conversion, batch and identifier state, current balance, and nonnegative stock before commit. It writes the document, movements, projection, physical-unit location, and audit event, then marks the document posted. A failure rolls back all rows. SQLite serializes writers and has a five-second bounded busy timeout. The caller should retry a busy or uncertain response using the **same** operation key. The key and request hash are unique in `InventoryDocuments`: a matching replay returns the original document; a changed payload returns `operation_key_conflict`.

## Precision and valuation

- Money and unit cost are signed 64-bit integers at scale 4. Quantity is signed 64-bit at scale 6. JavaScript submits decimal strings; the server parses invariant decimal text and rejects excess quantity precision or overflow.
- Product-unit conversion snapshots the entered quantity, `ProductUnitId`, numerator, denominator, and exact base quantity on each document line. A conversion that cannot produce an integral scale-6 base quantity is rejected.
- Receipts require an **actual acquisition cost** as a decimal string with at most two fractional digits, stored at scale 4. No catalog reference purchase price is substituted. When historical cost is unknown, the operator must obtain/approve an estimate and enter it explicitly; the UI cannot post an unknown-cost receipt. The source document retains that supplied cost.
- Inbound value is `round_away_from_zero(entered_unit_cost_scaled × entered_quantity_scaled / 1,000,000)` to the nearest scale-4 integer. The multiplication and division use C# `decimal`; conversion to `long` is checked.
- Migration 016 replaces outbound weighted-average costing with durable FIFO layers and movement allocations. Transfers carry each consumed layer fragment and its original ordering into the destination. Reference purchase-price edits never rewrite cost layers. Existing pre-FIFO balances remain unresolved until their opening cost basis is documented; see [inventory-fifo.md](inventory-fifo.md).
- Negative quantities or values in the projection are rejected. There is no production negative-stock switch in this phase. Physically tracked units cannot become negative.

## Physical tracking

Task 2.3 product assignments remain the source of tracking requirements even if a global capability is later disabled. A product with posted movements cannot turn off its batch, expiry, serial, or IMEI assignment. Batch identity is `(VariantId, normalized lot number)`; a lot retains its business-date expiration. Receipts require a lot and an expiration date where configured. Outbound operations require the specific lot; manual issue and transfer of an expired lot are rejected, while a documented negative adjustment remains available for disposal. FEFO auto-allocation is not implemented; the operator selects a lot. Expired lots remain visible in balances and history.

Serial tracking requires one distinct serial per whole base unit. IMEI tracking requires at least one valid IMEI per unit and stores any additional IMEIs on the **same** `InventoryPhysicalUnits` row. Identifiers are globally unique, including removed units. Outbound and transfer requests must name the source serial and every registered IMEI. The physical unit location changes in the same ledger transaction, and `StockMovementUnits` links it to both transfer movements. Identifier lookup is read-only. The administrative rebuild refuses to run if serial-unit location totals disagree with ledger totals.

## Counts, corrections, and rebuild

`POST /api/inventory/counts` creates a draft snapshot of existing warehouse balance rows and their versions. Posting compares each version and quantity with live balances; stale counts fail without changing stock. A posted count records each counted quantity. Zero differences create no movement. A draft can be canceled, leaving an audit event.

Posted movements are never edited. The current automatic reversal supports a positive opening, receipt, or positive adjustment **only when its warehouse/variant/batch balance and value are still exactly the original movement and it has no linked physical units**. It creates an opposing document and movement. Other cases return `reversal_unsupported` or `reversal_conflict`; the operator must use a new documented correction/adjustment. This restriction avoids fabricating historical cost or impossible serial state.

`POST /api/inventory/integrity/rebuild` is administrator-only and CSRF protected. It creates a SQLite backup under `InventoryBackups` beside the database, verifies `quick_check` and foreign keys, checks physical-unit totals against the ledger, then updates or inserts projection rows from ledger aggregates in one transaction. It leaves zero rows in place rather than deleting historical projection identities. The balance triggers remain active throughout. A posted `projection_rebuild` document and audit event retain the backup path and actor. Operators should quiesce normal stock operations and independently test restore before using it on a production database. The backup can precede a concurrent post, so it is a recovery copy, not a guarantee that intervening writes are present in a restored file.

## Schema

Migration 016 adds FIFO and entered-price snapshots as described in [inventory-fifo.md](inventory-fifo.md). Migration 011 adds `Warehouses`, `ProductBatches`, `InventoryDocuments`, `InventoryDocumentLines`, `StockMovements`, `InventoryBalances`, `InventoryPhysicalUnits`, `InventoryIdentifiers`, `StockMovementUnits`, and `InventoryAudit`. A single active default warehouse named `المخزن الرئيسي` is created during migration; no existing product, user, variant, unit, or capability row is changed. Unique indexes cover warehouse code/default, lot identity, operation key, one reversal per original, balance identities including null batch, and global physical identifiers. FK and immutability triggers protect history. Local timestamps are UTC ISO text; expiration is a `YYYY-MM-DD` business date.

## Idempotency and request hash policy

`InventoryDocuments.OperationKey` is unique; `RequestHash` is a content fingerprint used for idempotent replay. New inventory posts, counts, and reversals compute a versioned `v2:` canonical SHA-256 digest with length-prefixed fields, explicit list counts, and distinct `post`, `count`, and `reverse` request-type prefixes. This removes delimiter/newline ambiguity that existed in the earlier unversioned delimiter-based hashes.

Historical rows written before this policy contain unversioned 64-hex delimiter hashes (reversals may append `/documentId`). The original request identity cannot be recovered from those hashes. A same-key retry against any such legacy row therefore returns `operation_key_conflict` and is never reposted. Operators must treat legacy rows as already-finalized documents. Before recording any new stock movement that appears equivalent, inspect the posted document and reconcile it against physical stock and source records; otherwise duplicate stock is possible.

Identical new requests replay and return the original document identifier. Any material change to the payload under the same key returns `operation_key_conflict`. The operation key itself is not part of the hash.

## Expiry policy

Expiry enforcement uses the server's local calendar date (`DateTime.Today`) as the authoritative business date. A lot whose expiration date is before today is blocked for manual issue and transfer; a lot expiring today remains issuable and transferable. Receipts, adjustments, and documented disposals are not subject to this issue/transfer gate. Tests use stable relative dates; runs exactly at local midnight may race the date rollover.

## Current limits

The user interface offers manual lot selection, not FEFO allocation. Automatic reversal is intentionally limited as above. Whole-warehouse count snapshots existing balance rows; a selected unbatched variant with zero recorded stock may also be counted. A newly discovered batch must enter through manual receipt so its lot and expiry are recorded. Receipt cost must be supplied and documented by the operator. Native Windows 7/WebView2 behavior remains an explicit target-machine verification task.
