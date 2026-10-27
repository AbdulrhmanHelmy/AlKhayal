# Phase 2 QA coverage — Batch 3 (catalog/inventory mapping)

Generated 2026-10-26 by the OpenCode Batch 3 worker (GLM-5.3-Flash). Scope: QA-018..QA-039 only
(catalog QA-018..026, inventory QA-027..039). No production code, no runner code, and no pre-existing
tests were changed. Before mapping, `tests/AlKhayal.IntegrationTests/QaPhase2EdgeTests.cs` was considered
and deliberately **not** created: every concrete high-risk behavior below already has a direct verified
assertion, so a duplicate regression was avoided. The only files changed by this batch are the
classification data in `docs/qa/scenario-manifest.json` and this document.

## Phase/module range reclassification (owner-confirmed)

The user's explicit ranges supersede the old scaffold ranges. The manifest `scaffoldRanges` and every
placeholder entry were reclassified accordingly:

| Phase | Ids | Module |
|---|---|---|
| 2 | QA-018..QA-039 | catalog (018..026) / inventory (027..039) |
| 3 | QA-040..QA-055 | customers (040..047) / suppliers (048..055) |
| 4 | QA-056..QA-071 | purchasing: purchases/returns |
| 5 | QA-072..QA-093 | sales / sales returns / printing / frontend |
| 5 (cross-module) | QA-094..QA-100 | `module: "cross-module"` |

QA-094..100 are cross-module but carry numeric `phase: 5` because the acceptance runner validates
`-Phase 1..5` (`[ValidateRange(1,5)]` in `scripts/run-acceptance-tests.ps1`) and cannot address a phase 6.
This is documented both in `scaffoldRanges` and in each QA-094..100 note. IDs outside QA-018..QA-039
received a phase/module reclassification only — no titles, no test mappings, and Phase 1 entries are
untouched.

All 100 IDs remain unique and the JSON parses (`ConvertFrom-Json`; 100 scenarios, 100 unique IDs,
22 phase-2 entries all mapped).

## Mapping discipline

Every mapped test below was re-read line-by-line against its assertions before being cited; no scenario
was mapped "because a broad test exists". Test methods live in:

- `tests/AlKhayal.IntegrationTests/CatalogMigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/CatalogPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/CatalogVariantPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/CatalogApiTests.cs`
- `tests/AlKhayal.IntegrationTests/InventoryPersistenceTests.cs` — note that `InventoryFifoTests.cs` is a
  partial-class continuation of `InventoryPersistenceTests`, so all of its methods carry the
  `AlKhayal.IntegrationTests.InventoryPersistenceTests.*` FQN (confirmed by the QA-037 selector run).
- `tests/AlKhayal.IntegrationTests/InventoryApiTests.cs`

All catalog/inventory tests already allocate a disposable GUID SQLite path under `%TEMP%`
(`Fixture.Path` in `InventoryPersistenceTests`, `Temp()` helpers in the catalog files) and never touch
`%LOCALAPPDATA%\AlKhayal\alkhayal.db` or `seeded-database.db`, so **no new regression test file was
needed**; `tests/AlKhayal.IntegrationTests/QaPhase2EdgeTests.cs` was considered and deliberately not
created. Exhaustion of the requested expectation list against real existing assertions follows.

## Coverage map (22 scenarios)

| ID | Expected behavior (specific acceptance expectation) | Test FQN (exact, asserts the behavior) | partial / manual remainder |
|---|---|---|---|
| QA-018 | Migrations 001..016 are idempotent and leave FK integrity ok, protected default unit `قطعة`, system SIZE/COLOR attrs at ids 1/2 | `CatalogMigrationTests.Fresh_and_repeated_migration_create_catalog_schema_and_default_unit` | No UI remainder (DB-level) |
| QA-019 | Legacy pre-008 upgrade preserves product IDs, exact scale-4 rounding (12.34567→123457), null unknown purchase cost, quarantines `EGP 8`/blank names to `review_required`, and reconciles late legacy inserts to `review_required` + 1/1 base unit without guessing | `CatalogMigrationTests.Upgrade_preserves_legacy_ids_prices_and_quarantines_invalid_rows`, `CatalogMigrationTests.Post_migration_legacy_rows_are_reconciled_on_next_start_without_guessing` | — |
| QA-020 | 009→010 backfill is an exact 1/1 base ProductUnits row (sale+purchase) with identifiers preserved; 010 preflight fails fast and rolls back (no `ProductUnits`, not in `SchemaVersion`) when a default variant is missing | `CatalogMigrationTests.Upgrade_from_009_backfills_base_product_units_and_preserves_identifiers`, `CatalogMigrationTests.Preflight_fails_fast_and_rolls_back_if_product_lacks_default_variant` | — |
| QA-021 | Create is idempotent by operation key and conflicts on a changed payload; search/filter; optimistic `version_conflict`; deactivate→archive; archived state/name/prices survive a new service instance; `IsDefault` variant + base ProductUnits row exists | `CatalogPersistenceTests.Catalog_create_update_search_state_archive_and_idempotency_persist` | Product screens in WebView2 (manual native) |
| QA-022 | Category cycle (`category_cycle`) and duplicates rejected; `protected_unit` deactivation blocked; `invalid_unit` product reference; `TryScaleMoney` rejects `-1` and `1,000` | `CatalogPersistenceTests.Category_cycles_duplicates_unit_constraints_and_invalid_product_references_are_rejected` | — |
| QA-023 | Catalog API: anonymous 401, missing CSRF 403, cashier write 403, screen-permission gating, 14.125/negative-cost 400, variant summary `sizeValues`/`colorValues`/`variantCount:4`/expiry flag, detail `isAssigned`, restart persistence on a second real server instance | `CatalogApiTests.Catalog_api_enforces_authentication_csrf_screen_permissions_and_restart_persistence` | Browser interaction with real screens |
| QA-024 | Global SKU/barcode uniqueness includes retired rows; `CatalogAuditLog` immutable by triggers (`catalog_audit_immutable` on UPDATE/DELETE) | `CatalogVariantPersistenceTests.Global_sku_and_barcode_uniqueness_includes_retired_rows`, `CatalogVariantPersistenceTests.Audit_log_triggers_abort_on_update_and_delete` | — |
| QA-025 | Case-insensitive barcode lookup returns exact identity/retired status/effective scale-4 price, null when unknown; barcode-referenced unit locks conversion (`conversion_locked`, `base_unit_locked`); simple→variant keeps the default variant id and locks mode (`variant_mode_locked`) | `CatalogVariantPersistenceTests.Barcode_lookup_returns_exact_identity_and_retired_status`, `CatalogVariantPersistenceTests.Conversion_locks_when_barcode_references_unit`, `CatalogVariantPersistenceTests.Transition_from_simple_to_variant_converts_default_row_and_locks_variant_mode` | Scanner-hardware input path (manual) |
| QA-026 | Configured variant aggregate (multi-unit + attributes + SKU/barcodes) persists identifiers, survives process restart, and full aggregate rollback on child write failure | `CatalogVariantPersistenceTests.Configured_variant_product_persists_and_survives_process_restart`, `CatalogVariantPersistenceTests.Transactionality_rolls_back_entire_aggregate_on_child_write_failure` | Variant editor screens in WebView2 |
| QA-027 | Receipt input validation leaves zero movements/zero layers (`invalid_quantity`, `invalid_cost`, `sale_price_required`, `cost_required`); exact 3/2 conversion snapshots 26667 entered-cost shadow 40000; `invalid_quantity_precision` and `numeric_overflow` rejected; catalog reference-price edits do not alter posted ledger values | `InventoryPersistenceTests.Invalid_receipt_never_posts_any_stock_or_cost`, `InventoryPersistenceTests.Exact_unit_conversion_cost_snapshot_and_overflow_are_enforced` | — |
| QA-028 | Receipt requires actual cost and sale price, never mutates the caller's request, replays/conflicts; converted unit prices and sale overrides touch only the selected variant; receiving a non-default variant never changes the default variant | `InventoryPersistenceTests.Receipt_requires_actual_purchase_and_sale_prices_and_never_mutates_the_request`, `InventoryPersistenceTests.Receipt_converts_selected_unit_prices_and_changes_only_selected_variant_atomically`, `InventoryPersistenceTests.Receiving_a_size_color_variant_does_not_change_the_default_variant` | — |
| QA-029 | Manual receipt/issue/transfer atomic+idempotent with exact per-warehouse balances; multiline failure rolls back everything; FKs on with immutable movements (delete/update abort); OWIN API path requires auth/CSRF/screen-permission, persists, replays (`replayed:true`) | `InventoryPersistenceTests.Manual_stock_and_transfer_are_atomic_idempotent_and_persist`, `InventoryPersistenceTests.Failed_multiline_post_rolls_back_every_effect`, `InventoryPersistenceTests.Fresh_migration_has_foreign_keys_and_immutable_movements`, `InventoryApiTests.Inventory_api_requires_auth_csrf_and_existing_screen_permission` | Warehouse screens in WebView2 |
| QA-030 | 010-schema upgrade preserves product/user IDs and one default warehouse; legacy pre-016 ledger keeps values/hash, flags `CostBasisComplete=false`, blocks issue with `unresolved_cost_basis`, requires exact legacy-value reconciliation (`legacy_value_mismatch`), idempotent then conflict on changed evidence, FKs clean at schema 20 | `InventoryPersistenceTests.Upgrade_from_existing_010_schema_preserves_product_and_user_ids`, `InventoryPersistenceTests.Fifo_migration_preserves_legacy_ledger_and_blocks_unknown_cost_until_documented_opening_basis` | — |
| QA-031 | Two concurrent issues of the last unit admit exactly one (never negative); projection rebuild creates a verified backup file, audits `projection_rebuild`, and verifies | `InventoryPersistenceTests.Concurrent_issues_never_make_stock_negative`, `InventoryPersistenceTests.Administrative_projection_rebuild_creates_verified_backup_and_audit` | — |
| QA-032 | Generated warehouse codes skip manual (`WH-0004`); edit normalizes to MAIN-CENTRAL, duplicate create/edit rejected, state survives reopen; second warehouse + transfers gated by effective `multiple-warehouses` module flag (`feature_disabled`) | `InventoryPersistenceTests.Generated_warehouse_codes_skip_existing_manual_codes`, `InventoryPersistenceTests.Warehouse_edit_persists_normalized_code_rejects_duplicate_and_survives_reopen`, `InventoryPersistenceTests.Additional_warehouse_and_transfer_follow_effective_module_flag` | Warehouse screens in WebView2 |
| QA-033 | FIFO consumes oldest layer with partial cross-receipt consumption (7000000 remainder; aggregate 8 units/9,600,000 after 12 units issued cost-exact), allocations immutable, replay, restart survival, projection verified | `InventoryPersistenceTests.Fifo_consumes_oldest_layers_partially_across_receipts_and_survives_restart_and_retry` | Valuation display in WebView2 |
| QA-034 | Transfer preserves origin order/cost into `Basis='transfer'` layers, one movement per side, replay idempotent, injected destination failure rolls back both sides without orphans, history names related warehouse, projection verified | `InventoryPersistenceTests.Fifo_transfer_preserves_origin_order_cost_and_allocations_and_rolls_back_both_sides` | — |
| QA-035 | Negative adjustment and count shortage consume FIFO layers (9600000), positive adjustment requires a cost (`cost_required`), provided cost posts exactly (12400000) with verified projection | `InventoryPersistenceTests.Fifo_adjustments_and_count_shortages_consume_layers_and_positive_counts_require_cost` | — |
| QA-036 | Count draft captures one line per stored variant; difference posted exactly (5→3, status `posted`); identical count replays/conflicts; `stale_count` rejected; already-reversed document rejected (`already_reversed`/`reversal_conflict`); zero-stock discovery with `BalanceVersion 0`; cancel leaves no movements | `InventoryPersistenceTests.Physical_count_posts_difference_and_zero_difference_without_extra_movement`, `InventoryPersistenceTests.Count_rejects_stale_capture_and_reversal_is_a_new_document`, `InventoryPersistenceTests.Selected_zero_stock_variant_can_be_counted_and_draft_can_be_canceled`, `InventoryPersistenceTests.Count_identical_replay_and_changed_payload_conflict` | — |
| QA-037 | Batch/expiry/serial/IMEI post through the same immutable ledger; duplicate tracked receipt → `inventory_conflict`; tracked transfer moves physical units to destination; unknown identifier → `identifier_not_in_stock`; batch without expiry issuable; yesterday-expiring lot blocked for issue and transfer, today-expiring allowed (`expired_batch`) | `InventoryPersistenceTests.Batch_expiry_and_serial_imei_follow_the_same_ledger`, `InventoryPersistenceTests.Tracked_fifo_transfer_and_issue_preserve_cost_and_rejected_identifiers_roll_back_consumption`, `InventoryPersistenceTests.Batch_without_expiry_can_be_issued`, `InventoryPersistenceTests.Server_local_date_blocks_yesterday_expiry_for_issue_and_transfer_allows_today` | — |
| QA-038 | Identical post replays/changed payload conflicts; canonical `v2:` hash distinguishes delimiter/newline payloads that collide in the legacy hash with balances unchanged; legacy unversioned hash conflicts without reposting | `InventoryPersistenceTests.Identical_post_replays_and_changed_payload_conflicts`, `InventoryPersistenceTests.Canonical_v2_hash_distinguishes_delimiter_and_newline_payloads`, `InventoryPersistenceTests.Legacy_unversioned_hash_conflicts_and_does_not_repost` | — |
| QA-039 | Reversal replay/changed-reason conflict; count hash covers cost and physical-unit identities (changed cost or missing IMEI conflicts; exactly 1 document/movement, 1 physical unit, 3 identifier rows) | `InventoryPersistenceTests.Reversal_identical_replay_and_changed_payload_conflict`, `InventoryPersistenceTests.Count_hash_includes_cost_and_physical_unit_identities` | — |

## Unsupported / mocked boundary

The frontend inventory/catalog support in `frontend/js` executes against `frontend/js/services/mock-services.js`
whenever no real use case is wired; compact notices must remain the deterministic
`وضع تجريبي — البيانات غير محفوظة فعليًا`. No scenario above is mapped to any mock-only assertion:
the `node frontend/tests/run-tests.js` registry tests (catalog renderers, inventory module/validation/UI
fields, variant visibility VM, warehouse renderer) are **not** part of the per-scenario runner scope —
they run only as the `-All` coarse frontend checkpoint and their status is never attributed to an
individual QA PASS. Mock-only UI behavior (inline draft creation, contextual quick actions, client-side
hints) must not be presented as persisted inventory/catalog operations by any scenario claim.

## Manual / native remainder

Not covered by automation anywhere in this phase (kept out of PASS semantics):

- Authenticated WebView2 interaction for product/category/unit/variant/warehouse screens, focus at
  minimum sizes, Light/Dark/System visual pass, per-Monitor-DPI verification at 100%/125%/150%/175%.
- Physical barcode-scanner input path and barcode label printing hardware.
- Windows 7 / Chromium 109 runtime qualification and printer drivers (deployment work).
- LAN multi-terminal inventory behavior needs deliberate server-side concurrency review before trusting
  the single-process concurrency test in QA-031 for terminal deployments.

## Known gaps (precise, not blocking the mapped scenarios)

- QA-025: the retired-barcode branch of `LookupBarcodeAsync` is asserted indirectly through the
  uniqueness/retirement test rather than by a direct lookup-after-retirement call; behavior is covered,
  the dedicated direct lookup assertion does not exist.
- QA-036: the `reversal_conflict` assertion lives inside the stale-count test by construction; the
  reversal-owning scenario is QA-039's replay/conflict pair.
- No automated coverage exists for warehouse-delete protections or recorded-days UI display; both docs
  claims elsewhere (`docs/testing.md`) list them only as manual.

## Execution evidence (this batch)

Environment: `ALKHAYAL_DATABASE_PATH` was set to a new absolute GUID temp path before any test process;
the runner's own fail-closed safety gate independently creates and prints its effective path and asserts
that it differs from `%LOCALAPPDATA%\AlKhayal\alkhayal.db` and `seeded-database.db` (recorded verbatim
below). No seeded database, no operator AppData database, and no pre-existing working-tree edit was
read or modified.

Commands and results are listed in the final report of this batch message; the runner emitted per-scenario
PASS/SKIP with exact counts, statuses, and durations, and any first-run failure is recorded verbatim below.
