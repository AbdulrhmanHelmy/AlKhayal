# Design system

The product catalog is a real persisted slice. Its list and forms do not show the shared product mock records or a mock banner. Loading, empty, failure, retry, validation, success, pagination, status, and concurrency-conflict states use the existing visual system. Product Catalog is the visible operational entry and provides contextual add, quick-add, edit, category-management, and unit-management actions. Category and unit management use full-width table-first pages and shared compact centered RTL modals for create/edit, with inline errors, loading/duplicate-save protection, Escape/cancel/close handling, focus management, and active/inactive actions. The product form uses a compact two-column RTL basic grid, inline optional properties, and one sticky Save/Cancel footer. Size and color rows sit directly beneath their switches. It persists an optional reference purchase cost and offers the same category-add modal without unmounting the draft, refreshing options, and selecting the created category. The inventory sidebar contains Product Catalog and Warehouse Directory only; the latter is an honest Task 2.4 placeholder with no mock records. Product sizes, colors, variants, SKU, and barcodes are catalog metadata; quantities, warehouse balances, average cost, and stock movements remain non-operational.

The product name cell wraps subtle neutral badges for the first two saved sizes/colors, an optional `+N`, active combination count, and configured tracking indicators. The full saved value list is available through the badge title and accessible label. Edit shows a compact table of active saved combinations with size/color names and only populated identifier columns. Neither view adds inventory quantities or new catalog columns.

General Settings uses bounded two-column forms that collapse through the shared 560px/760px breakpoints. Company, general, appearance, and database information are real screens and never claim mock values are persisted; the maintenance screen explicitly labels backup/restore as unavailable. Appearance reuses `AlKhayalTheme` and applies Light, Dark, or System immediately without CSS zoom or a second theme system.

## Visual language

The product uses deep navy for structure and trust, muted gold for primary actions and identity, neutral data surfaces, restrained shadows, and compact desktop density. `tokens.css` is the semantic source for color, spacing, radii, elevation, and focus treatment. `themes.css` maps those semantics for Light, Dark, and System modes; modules must consume tokens instead of hard-coded theme colors.

The UI uses local system fonts only. It has no runtime CDN, icon font, remote image, or remote-font dependency. Motion is limited and disabled through the reduced-motion preference.

The compact transparent AlKhayal mark lives at `frontend/assets/alkhayal-mark.png`. It is the shared identity asset for the login lockup, authenticated shell, favicon, invoice preview, and printer preview. The Arabic name and slogan stay as live text beside it so they remain sharp, accessible, and easy to update. The supplied full-logo artwork is preserved locally at `frontend/assets/alkhayal-logo-reference.png` as the visual source of truth.

## RTL shell

The document is Arabic and `dir="rtl"`. The authenticated shell has a top bar, global screen search, a compact quick-access row, closable workspace tabs, a right-hand module sidebar, a scrollable content workspace, and a persistent status bar. The outer layout uses an explicit physical direction while text/content surfaces restore RTL so the sidebar remains on the right without reversing Arabic content.

At compact widths the sidebar collapses to icons. Selecting a module opens a keyboard-accessible route dialog rather than depending on hover. Long Arabic labels wrap or truncate with accessible names, and tables remain horizontally scrollable.

## Interaction model

`screens.json` registers each route's Arabic title, module, description, renderer, and optional data/workflow metadata. Navigation from the sidebar, Settings hub, quick-access row, global search, workspace tabs, and in-screen links resolves through the same registry. Administrative destinations use `settingsGroup`/`settingsOrder`; legacy aliases use hidden `redirectTo` metadata. No second hard-coded Settings route list is maintained.

Reusable components include page headers, explicit mock-data notices, cards, KPIs, tables, filters, pagination, forms, validation messages, badges, empty states, modal dialogs, toasts, chart-like bars, invoice editors, pickers, and print previews. Buttons either perform a session action, navigate, alter UI state, export a real CSV, or clearly identify preview-only behavior.

## Authentication and themes

The login screen provides username/password fields, password visibility, optional username remembrance, health status, loading/error states, theme selection, and native form submission. Passwords and bearer tokens are never stored in web storage. Theme and harmless navigation preferences persist locally; the CSRF token remains in session storage.

## Data honesty

Authentication is real and SQLite-backed. Business screens use coherent deterministic Egyptian sample data copied into an in-memory session store. Every such screen displays a notice; checkout, invoice confirmation, inventory changes, financial posting, backups, driver installation, and physical printing are simulations or previews. This separation must remain obvious until corresponding Application/API operations are implemented.

Business activity profiles are also real and SQLite-backed. `الإعدادات ← نوع النشاط التجاري` uses shared cards, fields, badges, confirmation dialogs, and toasts. When no profile exists it becomes the first authenticated route; login remains unchanged. Activity cards support search and multi-selection, the primary activity is explicit, and recommendation deltas are shown before an administrator confirms saving. Non-administrators receive a read-only view. The layout collapses at the shared 1050px and 760px breakpoints and consumes the same Light, Dark, and System tokens.

Feature management is real and SQLite-backed at `الإعدادات ← الوحدات والميزات`. Cards show effective state, source, recommendation, and dependencies. Administrator changes require confirmation and update the shell only after persistence succeeds. The two-column grid collapses at 760px and shares the existing tokens and workspace scroller.

Settings is the single administration entry. Its compact searchable landing screen groups only existing routes under General Settings, Activity & Features, Users & Security, Operations, and Maintenance & Data. The group cards are derived from registry metadata, use existing local SVG/icon conventions and semantic tokens, and collapse to one column at 760px. Configuration-only sidebar groups are removed; daily shift approval remains an employee operation while shift configuration remains under Settings.

User management is real and SQLite-backed at `الإعدادات ← المستخدمون والصلاحيات`. The stable route is `users-list` and its renderer key is `user-management`. The table supports name/username search plus role and state filters. Add/edit and password reset use separate dialogs. Cashier permissions are grouped by Arabic module name, searchable, selectable in bulk, and show disabled-module grants as stored but inactive. The permission layout becomes one column at 760px and uses semantic theme tokens. Legacy routes `user-permissions`, `sales-permissions`, and `settings-permissions` are hidden administrator-only aliases that resolve to `users-list`.

## Accessibility and compatibility

All interactive controls are keyboard reachable, focus-visible, labelled in Arabic, and usable without color alone. Native semantic controls are preferred. The implementation avoids unsupported Chromium 109 APIs and transpilation assumptions. Visual verification covers both light/dark themes and compact RTL layout; actual Windows 7/WebView2 qualification remains pending.

## UI foundation repair contract

The shell owns one viewport: `.app-shell` fills the WebView2 viewport, `.app-main` owns the fixed application chrome rows, and `.workspace` owns page-level vertical scrolling. The sidebar navigation scrolls independently; the sidebar footer remains in the final grid row. Grid/flex boundaries must use `min-width: 0` and `min-height: 0` so long Arabic content can scroll instead of expanding the shell.

Do not add a second `100vh` or `calc(100vh - ...)` to a module. Use intrinsic responsive grids and bounded local scrolling only for genuinely wide tables, search results, product catalogs, cart lines, and modal bodies. The shared breakpoints are 1180px, 1080px, 1050px, 760px, and 560px. At 1080px the sidebar becomes a persisted 64px icon rail without overwriting the user's preference.

The WinForms host is Per-Monitor DPI aware through `src/AlKhayal.UI/app.manifest` and `AutoScaleMode.Dpi`. WebView2 starts at zoom factor 1.0, disables built-in zoom/pinch controls, and blocks only desktop-host browser zoom gestures. Standard browser accessibility zoom remains unrestricted. See `docs/ui-audit.md` for the confirmed causes and verification limits.

Sidebar headers keep the brand and collapse control in one adjacent flex cluster. The collapsed 64px rail centers every navigation, theme, settings, and logout SVG through `justify-content: center`, zero horizontal padding, and fixed 21–22px icon boxes; do not compensate with positional offsets or transforms.
# Warehouse Directory in Task 2.4

The real Warehouse Directory uses the current navy/gold RTL shell, themes, workspace scrolling, and existing modal components. Inventory navigation still contains only Product Catalog and Warehouse Directory. Contextual actions open compact multi-line forms; count and movement history render within the directory. Legacy stock routes redirect there. See [warehouse-management.md](warehouse-management.md) for workflows and [inventory-ledger.md](inventory-ledger.md) for what is persisted.
