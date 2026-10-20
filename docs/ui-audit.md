# AlKhayal POS UI audit

Last audited: 2026-10-20

## Confirmed root causes

- The authenticated shell used `100vh` with a hidden document body, so every descendant had to participate in a fixed-height grid. That is safe only when each grid item has `min-height: 0`; the old module layouts did not consistently provide that contract.
- The POS renderer added `height: calc(100vh - 274px)` and `min-height: 440px` inside the shell's own content scroller. This created a second viewport ownership model and caused clipped or double-scrolling content at shorter window heights.
- The sidebar already had a scrollable navigation region, but collapsed navigation was represented by letter/emoji-like text glyphs and child routes were hidden without a clear icon-rail affordance. Compact mode fell back to a modal route picker.
- Table and form components correctly use local overflow for genuinely wide data, but fixed minimums (`720px` tables and `150px` fields) needed a bounded parent and `min-width: 0` on every shell/grid boundary to prevent document-level overflow.
- Modal scrolling was attached to the complete modal, including its actions. Long dialogs could move important actions below the visible viewport.
- Workspace tabs rendered as a growing flex row without active-tab reveal or explicit per-tab scroll restoration on direct tab selection.
- The WinForms shell had no application manifest DPI declaration, no explicit `AutoScaleMode`, no controlled WebView2 zoom factor, and no host-side zoom suppression. The browser settings therefore inherited runtime defaults.
- The UI had no external font dependency, which is appropriate for offline deployment, but the shared font fallback and type scale needed explicit documentation. Inline text symbols were also used for navigation and quick actions.

## Implemented repair boundaries

- `html`, `body`, and the shell now have an explicit full-viewport/minimum-size contract. `.app-main`, `.workspace`, and the sidebar navigation use `min-width: 0`/`min-height: 0` where grid and flex sizing requires it.
- The workspace is the single page-level vertical scroller. The sidebar navigation is an independent scroller. Tables, search results, POS panes, and modal bodies retain bounded local scrolling only where the content itself is wider or independently useful.
- POS no longer owns a second viewport height. It uses responsive intrinsic grid sizing and stacks below the compact breakpoint; purchase editing uses the same responsive content grid with a bounded wide-table region.
- Sidebar collapse remains persisted in local storage, retains a 64px icon rail, keeps footer actions available, and uses an accessible route modal at compact widths. Navigation and quick-access icons are inline SVGs.
- Tabs preserve `scrollTop` per route, reveal the active tab, and keep the dashboard as the pinned singleton route.
- The WinForms host declares Per-Monitor DPI awareness in `src/AlKhayal.UI/app.manifest`, uses DPI scaling in the form, fills WebView2 through docking, sets `ZoomFactor` to `1.0`, disables built-in zoom/pinch controls, and injects desktop-only prevention for Ctrl+wheel/Ctrl+plus/Ctrl+minus/Ctrl+0. Ctrl+F/S/P are not blocked.
- Mock-data notices are compact and consistently identify the session-only boundary as `وضع تجريبي — البيانات غير محفوظة فعليًا`.
- Sidebar header toggle placement now uses one adjacent logo/toggle cluster with state-specific RTL panel icons and Arabic tooltips. Collapsed rail controls remove expanded padding/gaps at the layout level, keeping 21px SVG icons centered across the full 64px rail; footer controls use the same centering contract.

## Responsive contract

The maintained CSS breakpoints are 1180px for POS column compression, 1080px for the automatic icon rail, 1050px for summary-grid stacking, 760px for compact top-bar/content density, and 560px for single-column secondary grids. The target verification matrix is 1920×1080, 1600×900, 1366×768, 1280×720, 1024×768, and 900×600.

## Verification limits

Source, build, frontend registry/calculation checks, JavaScript syntax checks, and a real WinForms/WebView2 login-window smoke inspection were performed. The current environment does not provide an authenticated test session without entering credentials, so the authenticated shell, DPI changes at 100/125/150/175%, multi-monitor transitions, and Windows 7/Chromium 109 qualification remain deployment verification work. No visual pass is claimed for those unperformed cases.
