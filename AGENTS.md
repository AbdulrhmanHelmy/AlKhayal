# AlKhayal POS — Agent Instructions

Applies to this repository's C# WPF desktop UI and all visual changes.

## Read first

1. **Read [`design.md`](design.md) in full before changing any UI, shell, navigation, dialog, resource, or theme.** Treat it as the single design source of truth.
2. Follow verified project architecture and explicit user requirements. If something in `design.md` is **Proposed**, use it as a UI default, but do not treat it as approved domain/business logic.
3. The application is **Arabic-first RTL**, using **AlKhayal's navy-and-gold identity**; modern premium administration, modern compact DataGrids/POS, light/dark/system themes, right-hand collapsible sidebar, and closable workspace tabs.
4. Reference screenshots are **for functional discovery only**. Do not recreate the reference product's layout, colors, or branding.

## Non-negotiable implementation rules

- Use shared `Brush.App.*` dynamic theme resources and `AlKhayal.*Style` shared WPF styles. No ad-hoc screen-local HEX values for theme-sensitive UI.
- Implement new UI in WPF/XAML + C# with MVVM/project conventions, **not web/CSS UI**.
- Support live Light/Dark/System switching without recreating open tabs or losing user edits.
- Include hover, pressed, selected, focus, disabled, loading, validation, and error states as relevant.
- Respect Arabic RTL while giving barcodes, invoice IDs, phone numbers, amounts, and other mixed-direction values appropriate LTR rendering where needed.
- Preserve accessible focus and controls, keyboard/scanner behavior, data-grid virtualization, responsiveness, and DPI safety.
- Don't invent back-end workflow rules, taxes, payments, permissions, stock effects, or future features from screenshots.
- Use existing page templates (T01–T10 in `design.md`) and a consistent app shell; do not redesign navigation for a new feature.

## Before finishing UI work

- Verify both themes + System mode resolution; include dark dialogs, menus, dropdowns, and tooltips.
- Verify collapsed/expanded sidebar, tab close/unsaved-edit behavior, Arabic RTL, and sizes including 1366×768 and 125–150% DPI if testing environment allows.
- Confirm that visual states (empty/loading/error/success) and user-facing Arabic labels are meaningful.
- Run the project build/tests if tooling is available, and say clearly what couldn't be tested.
- Summarize added/changed views, shared tokens/styles used, and any intentional deviations requiring an update to `design.md`.

## If the task is underspecified

Implement only the approved UI structure for the requested feature. Ask for missing consequential product decisions rather than guessing. Record new approved design choices in `design.md`.
