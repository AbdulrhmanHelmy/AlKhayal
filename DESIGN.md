# AlKhayal POS design contract

The canonical, maintained design specification is [docs/design.md](docs/design.md). It defines the Arabic RTL shell, semantic navy/gold tokens, themes, density, responsive behavior, reusable components, route-driven screen model, accessibility expectations, and the explicit boundary between real authentication and session-only business demonstrations.

Implementation sources:

- `frontend/css/tokens.css`, `themes.css`, `base.css`, `layout.css`, `components.css`, and `modules.css`
- `frontend/data/screens.json`
- `frontend/js/components/ui.js`
- `frontend/js/modules/`
- `frontend/assets/alkhayal-mark.png` and `alkhayal-logo-reference.png`

Any visual change must preserve Chromium 109 compatibility, work without remote assets or fonts, and be verified in both RTL desktop and compact layouts.

The shell scroll ownership and DPI/zoom contract are maintained in [docs/ui-audit.md](docs/ui-audit.md) and [docs/design.md](docs/design.md). New modules must not introduce nested viewport calculations, document-level horizontal overflow, emoji navigation icons, or large repeated mock-data banners.
