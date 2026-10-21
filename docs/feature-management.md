# Feature management

## Resolution and overrides

The Application service resolves registry defaults, stored state, and recommendations into one `FeatureSnapshot`. Core state always wins. Missing optional rows use the registry default. Confirmed recommendations write source `recommended`; direct changes write `administrator`. Recommendation conflicts never overwrite administrator rows, and removed activities never disable a feature.

Activity application classifies Task 1 keys through the feature registry. It treats `maintenance-management` as a module, adds `barcode-label-printing` when `barcode-labels` is recommended, and includes dependency closure before one transactional apply.

## Persistence and audit

Migration `005_feature_management.sql` adds `ModuleConfiguration` and `FeatureAuditLog`; capabilities continue using migration 004's `CapabilityConfiguration`. State writes use parameterized values and short transactions. Each change records old/new state, source, user, and UTC time. Deactivation changes configuration only and never deletes business data.

## UI and navigation

`الإعدادات ← الوحدات والميزات` reads real API state and is read-only for non-administrators. A switch confirms, waits for persistence, then publishes the returned snapshot; errors retain the previous state.

Optional screens retain their definitions and carry `featureKey` in `screens.json`. The central service filters sidebar items, compact navigation, quick access, global search, and direct navigation. Disabling a feature closes its open tabs and activates the dashboard. Re-enabling restores navigation dynamically.

The effective-state client keeps at most one `/api/features/effective` request in flight. Screen-owned refreshes update the shared snapshot without broadcasting a shell navigation event; only a real enabled-state change broadcasts one. The shell reconciles navigation without recreating an unaffected active renderer, and renderer generations ignore stale asynchronous completions. This lifecycle prevents the feature screen from recursively loading and rendering itself.

## Boundaries

This task configures availability only. It does not implement full optional workflows or their business persistence. Existing prototypes remain session-only. Cashier screen permissions remain Task 3; this task only prevents non-administrators from mutating feature configuration.

