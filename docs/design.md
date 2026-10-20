# Design system

## Visual language

The product uses deep navy for structure and trust, muted gold for primary actions and identity, neutral data surfaces, restrained shadows, and compact desktop density. `tokens.css` is the semantic source for color, spacing, radii, elevation, and focus treatment. `themes.css` maps those semantics for Light, Dark, and System modes; modules must consume tokens instead of hard-coded theme colors.

The UI uses local system fonts only. It has no runtime CDN, icon font, remote image, or remote-font dependency. Motion is limited and disabled through the reduced-motion preference.

The compact transparent AlKhayal mark lives at `frontend/assets/alkhayal-mark.png`. It is the shared identity asset for the login lockup, authenticated shell, favicon, invoice preview, and printer preview. The Arabic name and slogan stay as live text beside it so they remain sharp, accessible, and easy to update. The supplied full-logo artwork is preserved locally at `frontend/assets/alkhayal-logo-reference.png` as the visual source of truth.

## RTL shell

The document is Arabic and `dir="rtl"`. The authenticated shell has a top bar, global screen search, a compact quick-access row, closable workspace tabs, a right-hand module sidebar, a scrollable content workspace, and a persistent status bar. The outer layout uses an explicit physical direction while text/content surfaces restore RTL so the sidebar remains on the right without reversing Arabic content.

At compact widths the sidebar collapses to icons. Selecting a module opens a keyboard-accessible route dialog rather than depending on hover. Long Arabic labels wrap or truncate with accessible names, and tables remain horizontally scrollable.

## Interaction model

`screens.json` registers each route's Arabic title, module, description, renderer, and optional data/workflow metadata. Navigation from the sidebar, quick-access row, global search, workspace tabs, and in-screen links resolves through the same registry.

Reusable components include page headers, explicit mock-data notices, cards, KPIs, tables, filters, pagination, forms, validation messages, badges, empty states, modal dialogs, toasts, chart-like bars, invoice editors, pickers, and print previews. Buttons either perform a session action, navigate, alter UI state, export a real CSV, or clearly identify preview-only behavior.

## Authentication and themes

The login screen provides username/password fields, password visibility, optional username remembrance, health status, loading/error states, theme selection, and native form submission. Passwords and bearer tokens are never stored in web storage. Theme and harmless navigation preferences persist locally; the CSRF token remains in session storage.

## Data honesty

Authentication is real and SQLite-backed. Business screens use coherent deterministic Egyptian sample data copied into an in-memory session store. Every such screen displays a notice; checkout, invoice confirmation, inventory changes, financial posting, backups, driver installation, and physical printing are simulations or previews. This separation must remain obvious until corresponding Application/API operations are implemented.

## Accessibility and compatibility

All interactive controls are keyboard reachable, focus-visible, labelled in Arabic, and usable without color alone. Native semantic controls are preferred. The implementation avoids unsupported Chromium 109 APIs and transpilation assumptions. Visual verification covers both light/dark themes and compact RTL layout; actual Windows 7/WebView2 qualification remains pending.

## UI foundation repair contract

The shell owns one viewport: `.app-shell` fills the WebView2 viewport, `.app-main` owns the fixed application chrome rows, and `.workspace` owns page-level vertical scrolling. The sidebar navigation scrolls independently; the sidebar footer remains in the final grid row. Grid/flex boundaries must use `min-width: 0` and `min-height: 0` so long Arabic content can scroll instead of expanding the shell.

Do not add a second `100vh` or `calc(100vh - ...)` to a module. Use intrinsic responsive grids and bounded local scrolling only for genuinely wide tables, search results, product catalogs, cart lines, and modal bodies. The shared breakpoints are 1180px, 1080px, 1050px, 760px, and 560px. At 1080px the sidebar becomes a persisted 64px icon rail without overwriting the user's preference.

The WinForms host is Per-Monitor DPI aware through `src/AlKhayal.UI/app.manifest` and `AutoScaleMode.Dpi`. WebView2 starts at zoom factor 1.0, disables built-in zoom/pinch controls, and blocks only desktop-host browser zoom gestures. Standard browser accessibility zoom remains unrestricted. See `docs/ui-audit.md` for the confirmed causes and verification limits.

Sidebar headers keep the brand and collapse control in one adjacent flex cluster. The collapsed 64px rail centers every navigation, theme, settings, and logout SVG through `justify-content: center`, zero horizontal padding, and fixed 21–22px icon boxes; do not compensate with positional offsets or transforms.
