# Feature management

The Phase 2.1 catalog uses the existing core `product-catalog` module. Product catalog routes declare that `featureKey` in `screens.json`; no catalog-specific feature store was added. Future capabilities such as variants, sizes, colors, barcodes, batches, expiry, serials, IMEI, multiple units, and inventory do not block creation of a basic product and do not make their future fields operational.

The effective global capability snapshot controls Product Add/Edit visibility. Enabled capabilities appear as product-specific switches, initially off for a new product. Disabled unassigned capabilities are absent; existing assignments remain readable and are preserved during edits. Sizes and colors depend on global `product-variants` in FeatureRegistry, but the technical prerequisite is assigned automatically by the aggregate product command and has no product-form switch. Product-level IMEI and expiry selection also requires serial/batch tracking respectively. A product switch configures only supported product metadata; physical tracking values belong to receipt/inventory workflows.

## Resolution and overrides

The Application service resolves registry defaults, stored state, and recommendations into one `FeatureSnapshot`. Core state always wins. Missing optional rows use the registry default. Confirmed recommendations write source `recommended`; direct changes write `administrator`. Recommendation conflicts never overwrite administrator rows, and removed activities never disable a feature.

Activity application classifies Task 1 keys through the feature registry. It treats `maintenance-management` as a module, adds `barcode-label-printing` when `barcode-labels` is recommended, and includes dependency closure before one transactional apply.

## Persistence and audit

Migration `005_feature_management.sql` adds `ModuleConfiguration` and `FeatureAuditLog`; capabilities continue using migration 004's `CapabilityConfiguration`. State writes use parameterized values and short transactions. Each change records old/new state, source, user, and UTC time. Deactivation changes configuration only and never deletes business data.

Product forms consume the same effective feature snapshot immediately when opened. Global capability availability controls whether a product switch is available; it never auto-assigns a capability to a new product and never clears an existing product assignment or its variants, identifiers, or conversions. Product-level dependencies are validated again in Application: sizes/colors require product variants, IMEI requires serial tracking, and expiry dates require batch tracking. The SQLite catalog assignment store retains tracking configuration keys without creating stock records; lifecycle capture remains a later inventory boundary.

## UI and navigation

`الإعدادات ← الوحدات والميزات` reads real API state and is read-only for non-administrators. A switch confirms, waits for persistence, then publishes the returned snapshot; errors retain the previous state.

Optional screens retain their definitions and carry `featureKey` in `screens.json`. The central service filters sidebar items, compact navigation, quick access, global search, and direct navigation. Disabling a feature closes its open tabs and activates the dashboard. Re-enabling restores navigation dynamically.

The effective-state client keeps at most one `/api/features/effective` request in flight. Screen-owned refreshes update the shared snapshot without broadcasting a shell navigation event; only a real enabled-state change broadcasts one. The shell reconciles navigation without recreating an unaffected active renderer, and renderer generations ignore stale asynchronous completions. This lifecycle prevents the feature screen from recursively loading and rendering itself.

## Boundaries

This task configures availability only. It does not implement full optional workflows or their business persistence. Existing prototypes remain session-only. Cashier screen permissions remain Task 3; this task only prevents non-administrators from mutating feature configuration.

# Inventory feature behavior in Task 2.4

The existing `basic-inventory` module keeps the Warehouse Directory available. The existing effective `multiple-warehouses` module gates creation of additional warehouses and transfer posting. Product-level batch, expiry, serial, and IMEI assignments remain binding for existing stock even after global availability is disabled. A product with posted movements cannot drop one of these tracking assignments. No second feature registry was introduced.
