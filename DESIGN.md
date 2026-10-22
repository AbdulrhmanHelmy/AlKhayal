# AlKhayal POS design contract

The canonical, maintained design specification is [docs/design.md](docs/design.md). It defines the Arabic RTL shell, semantic navy/gold tokens, themes, density, responsive behavior, reusable components, route-driven screen model, accessibility expectations, and the explicit boundary between real authentication and session-only business demonstrations.

Implementation sources:

- `frontend/css/tokens.css`, `themes.css`, `base.css`, `layout.css`, `components.css`, and `modules.css`
- `frontend/data/screens.json`
- `frontend/js/components/ui.js`
- `frontend/js/modules/`
- `frontend/assets/alkhayal-mark.png` and `alkhayal-logo-reference.png`

Any visual change must preserve Chromium 109 compatibility, work without remote assets or fonts, and be verified in both RTL desktop and compact layouts.

The real `business-activities` settings route uses the shared cards, fields, badges, confirmation dialog, and toast system. On first authenticated launch with no saved selection it opens automatically. Its activity/recommendation layout collapses intrinsically at 1050px and its activity cards become one column at 760px; it inherits Light, Dark, and System tokens without a separate theme.

The real `feature-management` route uses the same primitives for core modules, optional modules, product capabilities, dependencies, recommendations, and administrator-only switches. Its feature grid collapses to one column at 760px. Navigation consumes the effective server snapshot and never infers module state from visual controls.

The real `settings-program` route is the single administration entry. Its compact, searchable `settings-hub` renderer builds General, Activity & Features, Users & Security, Operations, and Maintenance & Data groups from `screens.json` metadata rather than a second navigation list. Only implemented entries appear, disabled feature entries are identified without becoming navigable, and administrator-only access is enforced by the shared access pipeline.

The real `users-list` route is reached through `الإعدادات ← المستخدمون والصلاحيات` and provides Arabic RTL administrator and cashier account management. Its API-backed table, filters, secure password reset, and grouped cashier screen-permission editor use the shared modal, field, table, badge, confirmation, and toast primitives. Disabled-module grants remain visible as inactive selections, and the shell consumes centralized effective access for every navigation surface. Legacy permission route IDs remain hidden compatibility redirects to `users-list`; they are not separate permission models.

The shell scroll ownership and DPI/zoom contract are maintained in [docs/ui-audit.md](docs/ui-audit.md) and [docs/design.md](docs/design.md). New modules must not introduce nested viewport calculations, document-level horizontal overflow, emoji navigation icons, or large repeated mock-data banners.

The real General Settings, Company Information, Appearance, and Data & Maintenance screens use the same registry-driven Settings hub and shared responsive form primitives. Business-wide values are SQLite-backed; appearance is an explicitly device-local preference that reuses the existing Light/Dark/System theme controller. See [docs/general-settings.md](docs/general-settings.md).

The real product catalog preserves the existing card, toolbar, table, modal, form, workspace-tab, RTL, and theme primitives. `products-list` is the primary visible product destination; add/edit/quick-add stay permission-checked contextual routes. Category and unit management remain administrator routes and are discoverable from the catalog. Their management pages are full-width table-first views with compact 540px add/edit modals, inline validation, guarded close behavior, focus return, and status actions; no permanent management forms or unsafe delete action is shown. The form persists an optional scale-4 reference purchase cost and can create/select a category in the same modal pattern without unmounting the unsaved draft. The inventory sidebar exposes only Product Catalog and a no-fake-data Warehouse Directory placeholder; actual warehouses, quantities, movements, and average cost remain Task 2.4 work.
# Task 2.4 warehouse implementation

Warehouse Directory now hosts the persisted inventory workflows inside the approved RTL shell. Keep the Inventory sidebar limited to Product Catalog and Warehouse Directory. Use the existing compact forms, modal focus behavior, navy/gold tokens, and light/dark/system themes. Stock values shown here come from the inventory API only. Implementation details and operator flows are in [docs/warehouse-management.md](docs/warehouse-management.md).
