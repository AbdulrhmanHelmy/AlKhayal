# AlKhayal POS — UI/UX Design System & WPF Implementation Contract

> **Project:** AlKhayal POS (الخيال)  
> **Platform:** Windows desktop, C# + WPF + XAML  
> **Product language:** Arabic-first, right-to-left (RTL)  
> **Specification language:** English; user-facing labels remain in Arabic  
> **Status:** Version 1.0 — implementation baseline, extensible  
> **Audience:** Developers, designers, Codex and other coding agents  
> **Source of truth:** This file governs all UI work unless an explicit project decision supersedes it.

---

## 0. How to use this contract

### 0.1 Mandatory rules

1. **Read this file before creating or changing a view, user control, shared style, window, dialog, or navigation entry.** Also read `AGENTS.md` if present.
2. Treat the defined design tokens, component contracts, shell layout, navigation conventions, RTL requirements, and theme behavior as **mandatory**.
3. **Do not reproduce the reference application's visual appearance.** Screenshots shared during discovery are only evidence of business modules, terminology, workflows, and UI patterns worth considering. AlKhayal must have its own visual identity.
4. Reuse shared WPF resources/styles and existing page templates. Do not invent a different color palette, typography scale, corner radius, tab system, sidebar, or dialog treatment for a new screen.
5. Every new/changed visual element must work in **Light, Dark, and System** theme selection without restarting the application.
6. Treat business rules, permissions, taxes, payment integrations, inventory costing, and invoice numbering as domain decisions; **never infer or implement them solely from screenshots**.
7. Preserve data, bindings, keyboard operations, accessibility, and responsive layout when polishing visuals.
8. If project code conflicts with this design contract, identify the conflict, implement the smallest consistent solution, and record any intentional departure in the change summary. Do not silently rewrite unrelated architecture.
9. **Unspecified future features are not existing requirements.** For new modules, use the template selection procedure in §15 and mark proposed behavior for product approval.

### 0.2 Priority and authority

Priority order: (1) explicit new product requirements and verified platform/security constraints; (2) this document's **Approved** decisions; (3) established shared styles and documented project conventions; (4) documented **Proposed** defaults; (5) agent discretion for local implementation details. Never turn a *Proposed* entry into a business rule without confirmation.

### 0.3 Approved vs proposed decisions

| Item | Status | Decision |
|---|---|---|
| Technology | **Approved** | C# WPF/XAML desktop UI. |
| Language and direction | **Approved** | Arabic-first RTL user interface; English allowed in technical documentation/code. |
| Identity | **Approved** | Independent AlKhayal brand inspired by supplied navy-and-gold logo. |
| Look and density | **Approved** | Modern Premium administration; Modern Compact grids and high-frequency POS. |
| Themes | **Approved** | Light, Dark, and System modes; consistent semantics across the app. |
| Sidebar | **Approved** | Right-docked, expandable/collapsible. |
| Workspace tabs | **Approved** | Top-mounted, closable tabs for open pages. |
| Quick access | **Approved** | Context-sensitive quick actions and shortcuts. |
| Exact color values, metrics, styling | **Proposed implementation baseline** | Use values in this file consistently until revised through a design decision. |
| Complete menu taxonomy / future modules | **Proposed information architecture** | Based on reference functionality; confirm actual scope as modules are implemented. |
| Backend workflows and financial calculations | **Not specified** | Implement only with separate product/business requirements. |

---

## 1. Brand personality and visual language

**Brand:** AlKhayal / الخيال. The provided logo combines a dark navy wordmark with warm gold accents and a geometric symbol. The product must feel **trustworthy, elegant, precise, fast, and distinctly its own**.

- Navy is the anchor for structure, navigation, identity, and (in Light mode) primary actions.
- Gold is a **controlled accent** for brand details, selected highlights, and (in Dark mode) primary-action emphasis. Do not scatter gold across every border or label.
- Content surfaces are calm and readable; avoid a pure-black wall of UI, neon blue highlights, aggressive red table headers, gigantic decorative buttons, and ornamental gradients.
- Favor clear hierarchy, subtle borders, generous admin layout spacing, and intentionally compact operational views.
- Admin pages use cards, clear section headings, summary statistics, and restrained elevation. Operational and data pages prioritize information density, focus indicators, and workflow speed.
- The logo must retain its original proportions and colors. Prefer a transparent, high-resolution official asset; do not stretch, recolor, rasterize repeatedly, or recreate the mark with text. Supply an icon-only variant for the collapsed sidebar when an approved asset exists. Until then, show a clean product icon/initials rather than cropping the logo arbitrarily.
- No visual element should imply a capability (e.g., cloud sync, card payment, tax compliance) before that capability exists.

### 1.1 Content tone

Arabic UI copy is brief, professional, and locally understandable. Prefer `إضافة منتج`, `حفظ`, `إلغاء`, `بحث`, `تأكيد الدفع`, `طباعة الفاتورة`, `لم يتم العثور على نتائج`. Avoid machine-translated awkwardness and ambiguous one-word actions. Confirmation text must identify the action and affected record. Success messages confirm the completed result, never a result that has not actually succeeded.

### 1.2 Egyptian localization rules

- Default locale: `ar-EG`; localized money label: `ج.م` (or `EGP` where constrained). Do not use `$` in Egyptian screens.
- Render financial values using the project's agreed number format, consistent decimal precision, and culture-aware formatting. **Currency precision, rounding, VAT treatment, and discount calculation are domain requirements, not design guesses.**
- Format numeric input and invoice identifiers predictably; isolate mixed Arabic/Latin sequences when necessary.
- Support barcode scanners behaving as keyboard input; avoid focus traps that block scanning.
- Thermal receipts, A4 reports, and barcode labels are separate print templates. Printer names come from the OS/application settings; never hardcode demo printers.
- Handle Arabic product/customer names, long names, empty values, Arabic/Latin search, dates, and Windows right-to-left text behavior.

---

## 2. Design tokens

**Use semantic theme resources instead of raw colors in screen XAML.** `Color.*` are color values; `Brush.*` are WPF brushes for binding. Colors below are **v1 initial values** extracted from the brand direction, not claims of exact color sampling from the logo.

### 2.1 Brand palette (identity, not arbitrary component assignment)

| Token | Hex | Use |
|---|---|---|
| `Color.Brand.Navy950` | `#081D49` | Primary navy identity. |
| `Color.Brand.Navy800` | `#142D57` | Supporting navy. |
| `Color.Brand.Navy650` | `#274875` | Dark-state/selection support. |
| `Color.Brand.Gold600` | `#B98A42` | Light-theme accent details. |
| `Color.Brand.Gold400` | `#DEB466` | Dark-theme readable gold accent. |
| `Color.Brand.White` | `#FFFFFF` | Clean neutral. |

### 2.2 Semantic theme palette (authoritative v1 implementation mapping)

For each row, expose a `SolidColorBrush` with the exact resource key given. A theme switch must replace the resolved brush values. UI views must reference these brushes via `DynamicResource` or theme-aware shared styles.

| WPF brush key | Light | Dark | Usage |
|---|---|---|---|
| `Brush.App.Background` | `#F5F7FB` | `#0B1325` | Window/workspace background. |
| `Brush.App.Surface` | `#FFFFFF` | `#172641` | Cards, forms, panels. |
| `Brush.App.SurfaceAlt` | `#F9FAFD` | `#101D36` | Inset surfaces, secondary panels. |
| `Brush.App.Input` | `#FFFFFF` | `#15233B` | Editable input field backgrounds. |
| `Brush.App.Sidebar` | `#081D49` | `#101D36` | Sidebar shell background. |
| `Brush.App.SidebarText` | `#EAF0FA` | `#E6EDF9` | Sidebar text/icons. |
| `Brush.App.SidebarSelected` | `#24416D` | `#274875` | Current navigation selection. |
| `Brush.App.TextPrimary` | `#172640` | `#F3F6FB` | Primary text. |
| `Brush.App.TextSecondary` | `#64748B` | `#A9BAD0` | Secondary metadata and labels. |
| `Brush.App.TextOnPrimary` | `#FFFFFF` | `#101827` | Text on primary-action fill. |
| `Brush.App.Border` | `#E1E7F0` | `#30405B` | General borders/dividers. |
| `Brush.App.BorderStrong` | `#CAD5E3` | `#485A73` | Emphasized borders. |
| `Brush.App.Accent` | `#B98A42` | `#DEB466` | Gold accents/icons/indicators. |
| `Brush.App.PrimaryAction` | `#081D49` | `#DEB466` | Primary CTA background. |
| `Brush.App.PrimaryActionHover` | `#16345F` | `#EAC581` | Primary CTA hover (keep on-primary text). |
| `Brush.App.PrimaryActionPressed` | `#254470` | `#CAA058` | Primary CTA pressed (keep on-primary text). |
| `Brush.App.SidebarHover` | `#173460` | `#203856` | Hovering over an unselected navigation item. |
| `Brush.App.Hover` | `#EDF2FA` | `#243854` | Neutral hover fill. |
| `Brush.App.Pressed` | `#DFE7F5` | `#365178` | Neutral pressed fill. |
| `Brush.App.Selected` | `#E7EDF7` | `#274166` | General selected fill. |
| `Brush.App.SelectedText` | `#081D49` | `#F3F6FB` | Text on selected fill. |
| `Brush.App.Focus` | `#2563EB` | `#79B9FF` | Focus outline, never only focus signal. |
| `Brush.App.DisabledBackground` | `#EEF1F5` | `#203047` | Disabled controls. |
| `Brush.App.DisabledText` | `#8996A8` | `#93A3B8` | Disabled text (visibly disabled). |
| `Brush.App.TableHeader` | `#EFF3F9` | `#1E304D` | DataGrid headers. |
| `Brush.App.TableAlternate` | `#FAFBFE` | `#152238` | Alternating data rows. |
| `Brush.App.TableHover` | `#F0F5FC` | `#223651` | Data-row hover. |
| `Brush.App.TableSelected` | `#E4EDF9` | `#28466A` | Selected table row. |
| `Brush.App.Overlay` | `#660B1325` | `#AA030710` | Modal backdrop. |
| `Brush.App.SuccessBackground` | `#E8F7EF` | `#123B2F` | Success chips and banners. |
| `Brush.App.SuccessForeground` | `#167347` | `#58DDA1` | Success text/icons. |
| `Brush.App.WarningBackground` | `#FFF5DD` | `#48330F` | Warning chips and banners. |
| `Brush.App.WarningForeground` | `#8B5A12` | `#F4C66E` | Warning text/icons. |
| `Brush.App.ErrorBackground` | `#FDEDED` | `#4A2028` | Error chips and banners. |
| `Brush.App.ErrorForeground` | `#B42332` | `#FF8995` | Error text/icons. |
| `Brush.App.InfoBackground` | `#EAF3FF` | `#183A5F` | Info chips and banners. |
| `Brush.App.InfoForeground` | `#1D5CA8` | `#8CC8FF` | Info text/icons. |

**Color usage contract:**

- Content text uses `Brush.App.TextPrimary` or `Brush.App.TextSecondary`, not the brand gold. Small gold-on-white text is not an acceptable substitute for readable neutral text.
- Light sidebar and dark sidebar both use navy backgrounds and light text. Maintain active state by a filled selection plus a slim gold leading indicator (on the **right** in RTL).
- In Dark mode the primary CTA is warm gold with **dark** text; in Light mode it is deep navy with **white** text. Do not retain white text on gold.
- Semantic success/warning/error/info colors carry meaning only; never use them as decorative random navigation colors.
- Native disabled appearance must remain readable enough to identify the label; disabled actions must not appear active.
- Aim for WCAG-style contrast checks: **at least 4.5:1 for normal informative text**, **3:1 for large text and meaningful non-text UI**, and an obvious keyboard focus indicator. Validate actual rendered pairs; the palette is a starting point, not proof of every pairing's compliance.

### 2.3 Typography

Target UI face: **Cairo** (provide appropriately licensed deployment or explicit install documentation). Fallback: **Segoe UI**, with Windows Arabic font fallback verified on supported devices. Do **not** bundle proprietary fonts without an explicit redistribution license. Keep font family configurable centrally via `Font.App.Body` and `Font.App.Heading` resources; do not set a new family per screen.

| Role | Size (WPF DIP) | Weight | Typical content |
|---|---:|---|---|
| Display | 28 | SemiBold/Bold | Significant page summary only. |
| Page title | 22 | SemiBold | `المبيعات`, `المنتجات`. |
| Section heading | 18 | SemiBold | Card/group headings. |
| Subheading | 16 | SemiBold | Form and list subsections. |
| Body | 14 | Normal | Default Arabic UI text. |
| Compact table | 13 | Normal | Dense data rows. |
| Form label | 13 | Medium | Field labels, captions. |
| Secondary/help | 12 | Normal | Hints, timestamps, metadata. |
| Button | 14 | SemiBold | Primary/secondary actions. |
| KPI value | 24–30 | Bold | Dashboard summaries. |

- Use 1.3–1.5× line height where practicable, especially for Arabic labels.
- Do not make operational table labels less than 12 DIP. Support OS text scaling and avoid fixed height that clips Arabic glyphs.
- Prefer localized readable number formatting and **tabular figures** where font support permits; right-align numeric values in RTL tables to the appropriate numeric edge consistently (usually aligned visually by digit position).

### 2.4 Spacing, shapes, and elevation

| Token | DIP | Typical use |
|---|---:|---|
| `Space.1` | 4 | Icon/label micro-gap. |
| `Space.2` | 8 | Tight row spacing. |
| `Space.3` | 12 | Compact control grouping. |
| `Space.4` | 16 | Default padding/gaps. |
| `Space.5` | 20 | Form section spacing. |
| `Space.6` | 24 | Admin page margins, card padding. |
| `Space.8` | 32 | Major section separation. |
| `Space.10` | 40 | Feature section separation. |

| Shape token | DIP | Applies to |
|---|---:|---|
| `Radius.Small` | 6 | Small buttons/chips. |
| `Radius.Control` | 8 | Inputs/buttons/toolbar surfaces. |
| `Radius.Card` | 12 | Cards and standard panels. |
| `Radius.Dialog` | 16 | Dialogs. |

- Border thickness: normally **1 DIP**, stronger focus outline **2 DIP**.
- Shadows: subtle on cards and floating surfaces in Light mode, softer and lower-opacity in Dark mode; borders define Dark mode surfaces. Do not apply a shadow to every table cell or row.
- Icon sizes: 16 compact, 18–20 standard controls, 24 emphasized buttons. Use one consistent vector icon family with licensing verified; do not mix emojis, multicolored glyphs, and unrelated icon packs in production UI.
- Icon-only buttons must have tooltips, `AutomationProperties.Name`, sufficient target size, and a text alternative when context is unclear.

---

## 3. Layout, density, and window behavior

### 3.1 Primary app shell (approved architecture)

The desktop shell has: **right-aligned collapsible Sidebar**, a **top utility/header row**, **top closable workspace tabs**, central page content, and a **bottom status strip**. This is **not** a replica of the reference application's crowded horizontal application menu.

```text
                 ALKHAYAL POS — MAIN SHELL (RTL)
┌────────────────────────────────────────────────┬─────────────────────┐
│   Global search | branch | notifications |     │  Logo / collapse   │
│   theme | user                             ←    │                     │
├────────────────────────────────────────────────┤  Right sidebar      │
│  [active tab ×] [another tab ×] [Home]         │  Dashboard          │
├────────────────────────────────────────────────┤  POS                │
│                                                │  Sales              │
│  Page header · actions                         │  Catalog            │
│                                                │  Purchases          │
│  Admin card(s) / compact table / work surface  │  Finance ...        │
│                                                │                     │
│                                                │  Settings (bottom)  │
├────────────────────────────────────────────────┴─────────────────────┤
│             Connection / active shift / user / branch / date-time     │
└──────────────────────────────────────────────────────────────────────┘
```

Schematic only: **actual visual positioning follows `FlowDirection=RightToLeft` and the approved layout**, not these ASCII character widths.

**Shell surface assignments:** Header uses `Brush.App.Surface` with a bottom `Brush.App.Border` divider; workspace tab rail uses `Brush.App.SurfaceAlt`; tab content uses `Brush.App.Background`; status strip uses `Brush.App.SurfaceAlt` and a top border. The sidebar remains `Brush.App.Sidebar` in both themes. Keep the top navigation chrome neutral and let one active tab be visually distinct.

### 3.2 Shell dimensions — WPF device-independent pixels (DIP)

| Region | Suggested measurement | Behavior |
|---|---:|---|
| Expanded sidebar | 256 DIP | Right; full icon + Arabic label. |
| Collapsed sidebar | 72 DIP | Icon-only + tooltips; do not make navigation inaccessible. |
| Top header | 60 DIP | Global context, user, settings, theme, notifications. |
| Workspace tab row | 44 DIP | Horizontally scrollable when many tabs exist. |
| Bottom status bar | 30 DIP | Compact, secondary information. |
| Admin page horizontal margin | 24 DIP | Reduce to 16 in narrower windows. |
| Admin card padding | 20–24 DIP | Premium spacing. |
| Standard control height | 40 DIP | Forms and toolbars. |
| Compact control height | 34–36 DIP | Filter bars and dense workspaces. |
| POS primary action target | ≥44 DIP | Optimize for repeated use and touch. |
| Compact DataGrid row | 36 DIP | Test for Arabic glyph clipping. |
| Standard DataGrid row | 42 DIP | More readable admin lists. |
| Standard dialog width | 440–560 DIP | Expand for forms; clamp to usable window. |

Suggested supported baseline: **1366×768 desktop**; test 1024×720 and 1920×1080 at 100%, 125%, 150%, and 200% scaling. Do not hardcode `Window.Width` as the only responsive strategy. At constrained widths: collapse sidebar, wrap/move secondary toolbar actions into an overflow menu, preserve POS totals and payment CTA, and let tables scroll horizontally rather than crush columns into unreadable text. For small windows, use sensible minimum dimensions or scrolling rather than clipped controls.

### 3.3 Sidebar contract

- Dock on the **right**. Expanded state shows icon + short Arabic label; optional nested children via accessible disclosure. Collapsed state shows icons with tooltip / flyout labels.
- Keep selection after navigation and while workspace tabs change. Current section has a subtly lighter navy filled background and a gold edge indicator on the **right**.
- Sections are grouped logically, with restrained dividers and scrollable content. Bottom fixed area is reserved for Settings, Help (if supported), and profile/log out.
- Expansion state persists per local user/device preference. Collapsing never closes workspace tabs.
- Keyboard access: Tab focuses nav controls; Enter/Space opens; arrow-key interaction should follow the actual control type (do not fake standard tree/list semantics).
- Do not display hidden modules, fake counters, or shortcuts the user cannot access. Permissions filter navigation and also independently authorize actions at the command/service layer.

### 3.4 Workspace tabs contract

- Home/Dashboard is a **pinned tab** and cannot be closed by accident. Other workspace views open as closable tabs above the content.
- Activating an existing module instance should focus its tab rather than duplicate it, except when the domain explicitly permits multiple entities (e.g., two separate invoice drafts).
- Active tab: `Brush.App.Surface` (or visibly selected equivalent), primary text, gold 2-DIP bottom/RTL-leading accent. Inactive tabs: surface-alt, secondary text. Hover changes surface only.
- Close button appears consistently and supports tooltip/automation label; closing an unsaved form prompts for **Save / Discard / Cancel** if supported by state. Do not silently discard input.
- Limit visible tab width; use a tab scroller/overflow dropdown and prevent the entire header from stretching off-screen. Middle-click close can be an enhancement, not the only close method.
- Workspace tabs must not create an uncontrolled number of view instances or leak event subscriptions. Prefer view model–driven navigation.

### 3.5 Header and status bar

Header content: optional global search/command box, current branch/store, quick-access trigger, notification center, theme switcher (`Light / Dark / System`), and active user menu. Do not show features until implemented. Sidebar logo provides a home affordance.

Status bar content when available: currently selected branch, operator, current shift, sync/connection state, local time, and concise operational indicator. Avoid meaningless permanent decorative icons. Error or offline state must include accessible text, not color alone.

### 3.6 Split pane layouts

For desktop workspaces, use `Grid` with resizable `GridSplitter` and sensible minimum widths; never require precise pixel-perfect positions to complete an operation. In Arabic RTL, field labels and logical order follow RTL, while specialized barcode and numeric fields may use LTR internally. A collapsed side pane must remain recoverable.

---

## 4. Theme behavior (Light / Dark / System)

### 4.1 Theme selector semantics

- `Light`: always use the Light dictionary.
- `Dark`: always use the Dark dictionary.
- `System`: resolve current Windows application theme preference, watch for changes, and apply without restart. On OS versions where this cannot be detected reliably, fall back to Light and document behavior.
- Persist **preference**, not just last resolved theme. Example: persist `System`, not `Dark` when System currently resolves to Dark.
- Switching must update existing sidebar, all open tabs, data grids, dialogs, popups, tooltips, autocomplete, contextual menus, status bar, progress bars, notifications, and newly opened windows.
- Theme transitions should be fast and predictable; avoid long animated crossfades, flashing whites, and restarting/navigating away to apply the new theme.
- The official multi-color logo stays unchanged; choose a suitable transparent/contrast-safe asset variant for background changes if brand assets support it.

### 4.2 Component state design matrix

| Component/state | Light | Dark |
|---|---|---|
| Primary button/default | Navy fill + white text | Gold fill + dark text |
| Primary button/hover | Slightly lighter navy | Slightly deeper warm gold; readable dark text |
| Secondary button/default | White surface + border + primary text | Dark surface + border + light text |
| Neutral button/hover | `Brush.App.Hover` | `Brush.App.Hover` |
| TextBox/default | White field + soft border | Deep navy field + visible border |
| TextBox/focus | 2-DIP `Brush.App.Focus` ring | 2-DIP `Brush.App.Focus` ring |
| Sidebar/current page | Navy + lighter navy selected item + gold edge | Navy + lighter navy selected item + gold edge |
| Tab/active | Surface + navy label + gold indicator | Surface + bright label + gold indicator |
| DataGrid/header | Cool very-light-blue-gray | Desaturated navy |
| DataGrid/alternate rows | White / soft neutral | Surface / darker navy |
| DataGrid/selected | Cool-blue selected | Slate-blue selected |
| Dialog | White elevated surface + dim overlay | Navy elevated surface + deeper overlay |
| Toast/success | Pale green + green text/icon | Deep green + light green text/icon |
| Error field | Error-colored border + explanatory text | Error-colored border + explanatory text |
| Disabled | Neutral disabled tokens | Navy disabled tokens |

**Do not** generate component-specific hexadecimal exceptions unless a new semantic role is added to this document and both themes. Avoid changing a screen's colors when the user merely navigates to a different module.

### 4.3 Motion, feedback, and accessibility

- Hover/focus transitions: brief (roughly 100–150 ms) where WPF implementation is reasonable; no obligatory motion for completing transactions.
- Loading operations show a spinner/skeleton or progress indicator with helpful text, keep the UI responsive, and prevent duplicate submits.
- Success Toast: dismiss automatically after roughly 3–5 seconds **only for noncritical informational success**. Errors needing action must remain visible or provide durable detail.
- Modal dialog: accessible focus containment, initial focus on appropriate non-destructive control, Escape cancels when safe, Enter follows defined default action. Disable background interactions while open.
- Never use success green alone to convey that a payment was committed: show receipt/reference or clear text when confirmed by the application service.

---

## 5. Shared reusable WPF component library

All concrete Style keys below are **required public design-system contracts**; implement their XAML in shared dictionaries before building individual screens. Templates must respect dynamic brushes, text direction, hit targets, and focus visuals.

| Component | Required style/control contract | Requirements |
|---|---|---|
| Primary button | `AlKhayal.PrimaryButtonStyle` | Navy/white in Light, gold/dark in Dark; states and busy indicator support. |
| Secondary button | `AlKhayal.SecondaryButtonStyle` | Bordered neutral, semantic hover/focus. |
| Quiet/icon button | `AlKhayal.IconButtonStyle` | Label/tooltip, 36-DIP target minimum; ≥44 DIP for POS-critical actions. |
| Destructive button | `AlKhayal.DangerButtonStyle` | Explicit text, requires confirmation when irreversible. |
| Text entry | `AlKhayal.TextBoxStyle` | Clear focus, empty, read-only, disabled, validation, optional leading/trailing icon. |
| Selection | `AlKhayal.ComboBoxStyle` | Styled dropdown content, keyboard and RTL. |
| Dates | `AlKhayal.DatePickerStyle` | Localized display/calendar, no clipped popups. |
| Numeric/money | `AlKhayal.NumericInputStyle` | Direction/format handling, no free-form financial rounding rules. |
| Toggle/check/radio | `AlKhayal.ToggleStyle`, `AlKhayal.CheckBoxStyle`, `AlKhayal.RadioButtonStyle` | Consistent checked/un-checked/focus/disabled states. |
| Compact data grid | `AlKhayal.CompactDataGridStyle` | Dense operational tables, row 36 DIP. |
| Standard data grid | `AlKhayal.DataGridStyle` | Standard admin table, row 42 DIP. |
| Grid header/row/cell | `AlKhayal.DataGridColumnHeaderStyle`, `AlKhayal.DataGridRowStyle`, `AlKhayal.DataGridCellStyle` | Sorting, selection, hover, edit mode, keyboard. |
| Search box | `AlKhayal.SearchBoxStyle` | Search icon, clear action, optional keyboard shortcut. |
| Form field | `AlKhayal.FormFieldStyle` | Label, optional required indicator, validation/helper line. |
| Card | `AlKhayal.CardStyle` | Calm border, radius, section padding, optional header/action. |
| KPI card | `AlKhayal.KpiCardStyle` | Metric, comparison/label, optional icon; no unjustified trends. |
| Workspace tab | `AlKhayal.WorkspaceTabStyle` | Open, active, close, overflow, unsaved indicator. |
| Sidebar item | `AlKhayal.SidebarItemStyle` | Expanded/collapsed/selected states and tooltips. |
| Modal/dialog | `AlKhayal.DialogStyle` | Focus management, clear primary/secondary actions. |
| Toast | `AlKhayal.ToastStyle` | Success/info/error/warning semantic styles. |
| Section toolbar | `AlKhayal.ToolbarStyle` | Search, filters, primary CTA, overflow layout. |
| Status chip | `AlKhayal.StatusBadgeStyle` | Semantic backgrounds + visible text labels. |
| Empty state | `AlKhayal.EmptyStateStyle` | Plain-language explanation and optional action. |

### 5.1 Buttons and command hierarchy

1. **Primary**: one strongest action in each local task context (`حفظ`, `إتمام البيع`, `إضافة`). In a complex screen there may be one primary action per clearly separated region, not five competing primary buttons in the same toolbar.
2. **Secondary**: cancel, preview, export, supplementary task.
3. **Quiet**: filter clear, row details, navigation, low emphasis.
4. **Danger**: delete, void, or other destructive action; always explicit and permission-aware.
5. Icon-only buttons are limited to familiar or tooltip-backed operations. Avoid colorful symbol-only toolbars like the reference.
6. Use `ICommand`/MVVM actions and `CanExecute` to express availability; don't imitate disabled state with opacity alone.

### 5.2 Forms

- Group related fields into titled sections. Arabic labels appear logically to the right/above controls in RTL reading order; use a stable layout across the form.
- Default admin form uses a one- or two-column `Grid` according to available width, not a crowded fixed-width wall of fields. A single-column layout is preferred for narrow dialogs.
- Mark required fields clearly and show validation feedback near the control, not just a topmost generic popup.
- Sensitive operations such as save/payment stay explicit and confirm only after success. Unsaved changes are tracked when leaving/closing.
- Use placeholders as hints, **not** substitutes for labels. Help text uses `Brush.App.TextSecondary` and readable size.
- Do not bind a password to ordinary plain-text view-model properties without evaluating secure input handling.

### 5.3 Data grids — modern compact

- Use a shared `DataGrid` base style. Prefer native WPF `DataGrid` with sorting and suitable virtualization, unless the existing app intentionally chooses a supported grid component.
- Support search, filters, and (when applicable) date range above the grid; keep batch actions in a distinct toolbar. Row actions should appear in a consistent actions column or context menu, never drift across screens.
- Default: restrained header background, 13-DIP text, 36-DIP compact row height, 1-DIP light dividers, subtle alternate fills, distinct selected/focus states.
- Align quantities/prices and localized numeric values consistently; ensure full precision or a tooltip/detail view for truncated values.
- Resizable columns and logical minimum widths; long Arabic text truncates with a tooltip or opens detail. Horizontal scrolling is legitimate for wide finance reports.
- Sorting state and filter count are visible. Column grouping is **optional**, not mandatory because it was present in a reference screenshot.
- Show `Loading`, `Empty`, `No search results`, `Error`, and `Permission denied` states rather than an unexplained dark/white blank rectangle.
- Pagination or incremental loading depends on dataset/source; don't load millions of finance rows at once.
- In WPF: favor `EnableRowVirtualization=True`, `EnableColumnVirtualization=True`, `VirtualizingPanel.IsVirtualizing=True`, `VirtualizingPanel.VirtualizationMode=Recycling`, and a `ScrollViewer` configuration that does not accidentally disable virtualization. Avoid surrounding a large DataGrid with another unbounded `ScrollViewer`.

### 5.4 Dialogs and notifications

| Type | Use | Action order |
|---|---|---|
| Confirm | Irreversible delete, void, posting important operations | Clear primary verb + `إلغاء`; default focus to safer choice. |
| Form dialog | Create/edit a compact record | `حفظ` and `إلغاء` with validation. |
| Payment dialog | Amounts, tender method, completion details | Prominent total and payment CTA; never confirm before service succeeds. |
| Info | Non-blocking explanation | One accessible acknowledge action. |
| Toast | Transient result | Semantic icon, text; optional details action. |

Don't chain unnecessary dialogs for every step. Never stack 3 nested modals as a normal workflow. Prefer dedicated editor pages or a single guided flow for complex operations.

### 5.5 Icons and charts

Use one coherent vector icon set across modules (library choice must be approved and license-compatible). Charts belong primarily on Dashboard/reports, not every CRUD page. Graph series should use navy, gold, and a small supporting accessible palette; convey data through legend/labels as well as color. No fake/sample performance data in production screens.

---

## 6. Navigation and proposed information architecture

These sections are **a proposed AlKhayal grouping derived from functional references**, not a mandate to implement all future features immediately. Agent: **only show routes the application actually implements and the current user may access.** Arabic labels are proposed user-facing nomenclature and may be revised by product owner.

| Sidebar group | Proposed subpages (Arabic labels) | Functional status |
|---|---|---|
| الرئيسية | `لوحة التحكم` | Page-template baseline. |
| نقطة البيع | `بيع سريع`, `الفواتير المعلقة` (if supported), `فتح/إغلاق وردية` (if supported) | POS workflow; optional actions conditional. |
| المبيعات | `فواتير المبيعات`, `سجل المبيعات`, `مرتجعات المبيعات` | Functional references; scope as implemented. |
| المنتجات والمخزون | `المنتجات`, `التصنيفات`, `الوحدات`, `المخازن`, `حركة المخزون`, `جرد المخزون`, `الباركود والملصقات` | Some future features. |
| المشتريات والموردون | `فواتير المشتريات`, `الموردون`, `حسابات الموردين`, `سداد الموردين`, `مرتجعات المشتريات` | Strong workflow reference. |
| العملاء | `العملاء`, `حسابات العملاء`, `كشف حساب عميل` | Future/additive as needed. |
| الخزنة والمالية | `الخزن`, `الخزنة والبنوك`, `المصروفات`, `سند قبض`, `سند صرف`, `الحركات المالية` | Financial workflow references. |
| التقسيط | `عقود التقسيط`, `الأقساط المستحقة`, `سداد الأقساط` | Installment workflow reference. |
| التقارير | `تقارير المبيعات`, `أرباح المبيعات`, `تقارير المشتريات`, `تقارير المخزون`, `تقارير الموردين` | Report template baseline. |
| الموظفون والصلاحيات | `الموظفون`, `المستخدمون`, `الأدوار والصلاحيات`, `الورديات` | User and employee reference. |
| الإعدادات | `إعدادات عامة`, `إعدادات الفواتير`, `الطابعات`, `النسخ الاحتياطي` | Settings reference. |

**Navigation design policy:** Keep the sidebar's top-level groups fewer and cleaner than the screenshots' long horizontal menu. Secondary children appear in expandable groups or are reached from a section landing page; avoid deeply nesting more than two levels. A frequently used operation such as `بيع سريع` may appear as a top-level favorite quick action as well as under its logical group. Do not show the same route twice in the sidebar by default.

### 6.1 Quick access

- Dashboard supports up to ~4–6 pinned shortcut tiles (e.g., Quick Sale, New Purchase, Products, Customers, Reports), chosen from available functions. Present them as **compact rounded rectangular tiles**, not oversized circles.
- Header may include a Quick Actions menu. Per-page toolbars expose frequent actions relevant to that page.
- Favorites and last-used pages can be optional later; do not invent saved user preferences without persistence.
- Keyboard shortcut definitions must be centralized and discoverable, never silently conflict with native DataGrid editing or barcode scanner Enter keys.

### 6.2 Workspace navigation states

- Clicking a sidebar module activates/opens its tab.
- Newly created record editors may open in a dialog for simple records or a distinct workspace tab for complex ones; follow §7 templates.
- If a module is not yet implemented, exclude it or label it explicitly as unavailable in a separate roadmap/demo context. Never route to empty mock pages in the shipping UI.
- Provide breadcrumbs only for multi-level workflows; avoid redundant breadcrumb + tab + oversized heading for the same title.

---

## 7. Page templates (agent must choose before building a view)

### T01 — Premium Dashboard / Home

**Use for:** executive/admin overview; not the high-speed till screen.

Layout: page header (`لوحة التحكم`, active branch/context, last-updated indicator), 3–5 KPI cards with responsive wrap, compact chart/insight card only when backed by real data, an activity/alerts section, and **approved quick-action tiles**. Light mode uses white cards on cool light background; Dark uses navy cards with borders. KPI numbers are prominent; explanatory labels remain readable. Avoid large empty areas and decorative circles.

States: loading, no-sales-yet/empty, permission-limited metrics, API/db error, offline (if applicable). Do not fake revenue/profit statistics.

### T02 — POS / Rapid Checkout (Modern Compact + touch-safe)

**Use for:** `بيع سريع`, cashier transaction. This layout is intentionally different from admin.

RTL desktop split: **right/center product search and selection area** and **left persistent invoice/cart summary**, with clear hierarchy; app sidebar may automatically collapse for maximum workspace while remaining accessible. Suggested proportion on wide monitors: ~60–65% search/product area and ~35–40% cart/payment area, subject to testing. On constrained widths, prioritize cart visibility, product search, and payment access. Maintain visual logical order in RTL.

Required UI regions:

1. **Transaction header:** selected customer (default generic cash customer only if business workflow defines it), location/register/shift context if available, transaction state/identifier.
2. **Fast entry:** autofocus-capable barcode field, product/name/code search, optional category chips, keyboard-first selection, clear scanned-product feedback.
3. **Products:** virtualized search results or product tiles depending on catalog volume/stock requirements; title, variant/unit, price, availability where provided.
4. **Cart:** item name, unit/variant, quantity, unit price, line total, safe edit/remove controls; readable compact rows and subtotal. No confusing tiny buttons.
5. **Totals:** subtotal, discount/tax/fees only as supported, final **amount due** visibly differentiated, EGP label.
6. **Fixed action region:** `الدفع` primary; optional suspend, new invoice, print/preview, return as implemented. Keep dangerous actions secondary and confirm as needed.

Keyboard: scanner handling, search focus, quantity editing, payment activation, Esc semantics, and post-sale focus reset must be documented in the actual implementation's command map. **Suggested, not approved:** F1 search/barcode, F4 pay, F8 suspend, F9 new transaction; validate collisions and cashier preferences before finalizing.

States: idle, search results/no match, item out-of-stock (if enforced), customer change, editing item, pending payment, payment error, sale successful, offline restrictions, unsaved cart close warning. Do not auto-clear the cart on failed payment. Payment flow and fiscal behavior require separate domain specs.

#### POS blueprint (proportions are suggested, not domain rules)

```text
RTL SCREEN: [ Right sidebar, often collapsed ]
┌───────────────────────────────────────────────────────────────┐
│ Context / customer         │ Search + barcode + scan status   │
├───────────────────────────┬───────────────────────────────────┤
│    Cart / editable lines  │ Product filters and results        │
│    unit / qty / price     │ Search list / product tiles        │
│    scrolls independently  │ Results scroll independently      │
├───────────────────────────┤                                   │
│    Subtotal & breakdown   │                                   │
│    FINAL AMOUNT DUE       │                                   │
│    [ PAY — primary ]      │                                   │
│    Secondary quick acts   │                                   │
└───────────────────────────┴───────────────────────────────────┘
  LEFT: cart 35–40%                 RIGHT: product area 60–65%
```

With RTL app flow, place the **product/search pane on the right and cart on the left** even if the `Grid.Column` indices depend on `FlowDirection`. Keep the product results and cart separately scrollable; the final amount and Pay action remain visible at the bottom of the cart. Avoid making the desktop app sidebar permanently occupy full width during checkout.

### T03 — Management List (Premium header + Compact grid)

**Use for:** Products, Customers, Suppliers, Warehouses, Users, Employees, Cash Drawers.

Anatomy:

- Page header: concise title, optional count/help, one primary `إضافة ...` CTA.
- Filter/search toolbar: search, relevant filters, clear filters, optional export.
- Main virtualized compact DataGrid: appropriate columns, accessible sorting and selection, action menu at consistent edge.
- Row selection opens a right-side detail drawer **or** a dedicated editor tab/dialog; choose one pattern per entity category and keep it consistent.
- Footer: pagination/data count if needed; never arbitrary giant blank whitespace.

For uncomplicated entity master data, use a modal or contained side panel with form sections. For complex record editing, use T04.

#### Management list blueprint

```text
┌───────────────────────────────────────────────────────────────┐
│ [إضافة عنصر]                       Page title / help / count   │
├───────────────────────────────────────────────────────────────┤
│ [More ⋯] [Filters] [Search.................................] │
├───────────────────────────────────────────────────────────────┤
│ [Action] [Status] [Column C] [Column B] [Name/ID]            │
│ ───────────────────────────────────────────────────────────── │
│                Virtualized compact DataGrid                    │
│                Row hover / selection / keyboard               │
│                                                               │
├───────────────────────────────────────────────────────────────┤
│ [Page controls]                                Results count   │
└───────────────────────────────────────────────────────────────┘
```

In actual RTL UI, labels, controls and column order follow the right-to-left reading sequence; `Grid` indices and ASCII ordering are illustrative. If a record editor is open alongside the grid, allocate a 360–440 DIP side drawer with its own scrolling form and a sticky save/cancel footer. Do not leave a permanently empty editor rail on every list.

### T04 — Full Editor / Transaction Workspace

**Use for:** Purchase invoice, sales invoice, installment contract, financial record requiring multiple sections.

Anatomy: header (`رقم الفاتورة`, state, business context) and `Save`/secondary operations; guided form sections for party/date/warehouse/etc.; line-item table where appropriate; totals/settlement sidebar or bottom summary; sticky task actions. Never use an enormous one-column scatter of unlabeled inputs. Use genuine field validation and guard unsaved edits.

Purchase UI reference functions (not copied visuals): supplier selector/add, invoice/date, barcode/product picker, unit/quantity/buy price/sell price (when domain says so), destination warehouse, discount/shipping if supported, invoice total, payment terms and printing. Data contracts/rules remain separate.

### T05 — Analytics / Reports (Modern Compact)

**Use for:** sales profit report, purchase/supplier reporting, stock movement, financial summaries.

Anatomy: report title + description; compact filter row (date range, entity/category filters, `بحث`/`تطبيق`); export/print actions; large virtualized DataGrid with meaningful formatting, sorting; a sticky totals summary footer; optional detail drill-down in pane/tab. Users can tell whether filters are active and what period the report covers.

- No hardcoded export-to-Excel icon implementation unless actual export exists.
- Support wide tables via horizontal scrolling and column sizing; don't reduce type to illegible 9 DIP.
- Profit reports must never calculate profitability with fabricated cost logic. Show values only from approved report services.
- Loading, error, no results, stale data/last updated if relevant.

#### Report blueprint

```text
┌───────────────────────────────────────────────────────────────┐
│ [Export] [Print]                     Report title + timeframe │
├───────────────────────────────────────────────────────────────┤
│ [Apply] [Clear] [Entity] [To] [From]     Compact filter bar    │
├───────────────────────────────────────────────────────────────┤
│                                                               │
│               Wide scrollable report DataGrid                  │
│                                                               │
├───────────────────────────────────────────────────────────────┤
│ [Total net] [Total cost] [Total sales]   Filtered rows count  │
└───────────────────────────────────────────────────────────────┘
```

For report pages, the filter and totals strips remain visible where possible while the result rows scroll. Display report period/filters in the visual header and exported report metadata. The summary footer shows only metrics genuinely supplied/calculated by the approved report service.

### T06 — Finance / Settlement Workspace

**Use for:** supplier balances and settlements, cashbox transactions, installment collections, vouchers.

Anatomy: account/entity selection; due/current balances with neutral KPI summary; obligations/transaction list; explicit amount-to-pay field and payment source/method where enabled; **review/confirm step** with exact amount and entity name; transaction outcome with reference; accessible receipt action after success. Distinguish posted vs draft records visually and textually.

Important: color is not a substitute for financial status; never claim a payment settled until back-end confirmation. Reversal/void actions are permission-aware and require explicit confirmation.

### T07 — Settings / Preferences

**Use for:** General, Invoice, Printing, Barcode printer, Theme (if present).

Use a sectioned preferences page with internal category navigation or tabs, not a huge form with dozens of unrelated rows. Each setting has a visible label, explanation if needed, current state, and save/apply behavior.

Printing subpage: **receipt 80 mm**, A4 report, barcode/sticker printer selectors (only show supported printers and paper profiles); copies count; print-preview vs direct-print options where supported; a **Test Print** action if implemented; visible validation and a clear Save control. Do not assume the reference's demo printer `Microsoft Print to PDF` is the production printer.

Theme preference is global (`Light`, `Dark`, `System`) and applies immediately. Keep global preferences separate from branch/device-specific printing or cash drawer settings.

#### Settings blueprint

```text
┌───────────────────────────────────────────────────────────────┐
│ [Save changes]                       Settings / current group │
├───────────────────┬───────────────────────────────────────────┤
│ Section navigation│ [Section heading + short description]      │
│ General           │ [Label] [Input / selection / toggle]       │
│ Invoice           │ [Label] [Input / selection / toggle]       │
│ Printing          │ [Label] [Input / selection / toggle]       │
│ Theme             │ [Optional validation / helper text]       │
│ ...               │                                           │
└───────────────────┴───────────────────────────────────────────┘
```

The inner settings navigation is conceptually separate from the application's primary sidebar; it is not a second full app sidebar. On narrower windows, replace the section list with an accessible dropdown/tab strip above settings content. Do not let a save button silently overwrite unrelated settings from another device/branch.

### T08 — Accounts, Roles, Employees

**Use for:** User management, employee profiles, permissions, shifts.

Use T03 list combined with a structured role/user editor (T04 if complex). Fields have labels, readable validation, secure password affordances, assigned role(s), branch/store scope where supported, and a separate permissions view. Never assume an admin is allowed to change their own role or remove the last admin: such rules belong to backend requirements.

### T09 — Dialog/Drawer workflow

**Use for:** Add supplier from purchase flow, initial-balance prompt, confirmation, receipt preview, lightweight selection.

Use a clearly titled dialog; size to its content; trap focus accessibly; avoid nested modal chains; offer appropriate action labels rather than generic `نعم/لا` where verbs explain the effect better. For supplier opening balance, treat optional collection as a follow-up step, not a surprise forced popup.

### T10 — Empty and onboarding states

**Use for:** New installation, no products yet, no report results, missing printer, inactive shift, insufficient permissions.

Use a concise explanation, a semantic icon or restrained illustration, a safe next action, and a link to the relevant settings when available. This template shares surface, type, and button tokens; never use a theme-incompatible placeholder splash.

---

## 8. Initial screen specifications & feature-ready blueprints

These describe **UI anatomy and expected interaction patterns**, not commitments that all workflows already exist. A developer implementing a future feature uses the corresponding blueprint after the product owner approves actual capabilities.

| Screen | Template | Key visible elements | Primary behavior |
|---|---|---|---|
| Dashboard (`لوحة التحكم`) | T01 | KPIs, recent activity, shortcuts, alerts | Navigate to real module/record. |
| Quick Sale (`بيع سريع`) | T02 | Barcode/search, product selection, cart, totals, payment | Cashier-first sale flow. |
| Purchase Invoices (`فواتير المشتريات`) | T04 | Supplier, invoice info, lines, warehouse, totals | Create/edit only according to confirmed rules. |
| Products (`المنتجات`) | T03 + T04 | Search, category, barcode/SKU, name, unit, price, status | List/edit/detail. |
| Sales History (`سجل المبيعات`) | T05 or T03 | Date filter, invoice/customer, amount, status, drill-down | Locate/inspect transaction. |
| Sales Returns (`مرتجعات المبيعات`) | T04 + T06 | Original invoice, returnable lines, qty, refund summary | Controlled return flow. |
| Purchase Returns (`مرتجعات المشتريات`) | T04 + T06 | Vendor/invoice, returned items, credit/payment treatment | Controlled vendor return. |
| Supplier Management (`الموردون`) | T03 + T09 | Company, contact, balance summary, actions | Add/edit/detail. |
| Supplier Accounts (`حسابات الموردين`) | T06 + T05 | Supplier, invoices, paid, due, history, settlement | Review and post payment. |
| Treasury (`الخزن`) | T03 + T06 | Cashboxes, balances, linked branch, transactions | Manage and inspect cashbox. |
| Sales Profit Report (`أرباح المبيعات`) | T05 | Date/filter row, results grid, profit summary, export | Filter/inspect/export. |
| Installment Contracts (`عقود التقسيط`) | T04 | Customer, product, deposit, schedule preview, totals | Create/inspect per domain specification. |
| Installment Due (`الأقساط المستحقة`) | T05 + T06 | Due list, customer, amount/date, pay action | Review and collect due payment. |
| Employees (`الموظفون`) | T03 + T08 | Person, job/role, contact, optional payroll data | Manage employee records. |
| Users & Permissions (`المستخدمون`) | T08 | User, roles, branch access, permissions | Permission-aware management. |
| Printer Settings (`إعدادات الطابعات`) | T07 | Receipt/A4/label printer settings | Save/test device configuration. |
| Warehouse Management (`المخازن`) | T03 | Warehouse list/editor, name/status | Master data. |
| Customers (`العملاء`) | T03 | Name, contact, balances if supported | Future feature-ready pattern. |
| Inventory/Stock (`حركة المخزون`) | T05/T04 | Filters, stock ledger, quantities, adjustments | Future feature-ready pattern. |
| Shifts (`الورديات`) | T03 + T06 | Shift operator, start/end, cash movement summary | Only after explicit workflow defined. |
| Expense/Voucher (`المصروفات والسندات`) | T04 + T06 | Category, cashbox, amount, date, reference | Approval and posting per business logic. |

### 8.1 Per-screen design acceptance template

Before merging a view, the implementing agent records:

```text
Screen ID / Arabic title:
Module and route:
Chosen template (T01–T10):
Data states: loading / empty / populated / error / permission denied
Header / toolbar / grid / editor / action areas:
Primary action + guards:
Theme tokens/styles used:
RTL and LTR field exceptions:
Keyboard navigation and shortcuts:
Resize / scrolling / 125%+ DPI behavior:
Unsaved changes / transaction-safety handling:
Accessibility (name, focus, contrast):
Open product questions (do not invent logic):
```

---

## 9. WPF resource architecture and examples

### 9.1 Recommended design-system project structure

Adapt namespaces/folder placement to the **existing** solution. Do not restructure application layers just to match this diagram.

```text
AlKhayal.POS/
├── design.md                      # This contract
├── AGENTS.md                      # Short coding-agent instruction file
├── App.xaml
├── Resources/
│   ├── Branding/                 # Approved logos/assets, properly licensed
│   ├── Themes/
│   │   ├── LightTheme.xaml
│   │   ├── DarkTheme.xaml
│   │   ├── Typography.xaml
│   │   ├── Metrics.xaml
│   │   ├── Controls.xaml
│   │   ├── DataGrid.xaml          # Optional split dictionary
│   │   └── Navigation.xaml        # Optional split dictionary
│   └── Icons/                    # Coherent vector icon set
├── Views/
│   ├── Shell/
│   ├── Dashboard/
│   ├── POS/
│   ├── Management/
│   ├── Reports/
│   └── Settings/
└── ViewModels/
    ├── Shell/
    └── ...
```

Keep views separate from business/domain layers. A shared component/style belongs in `Resources` or a UI library, not copied into multiple screens.

### 9.2 Resource naming conventions

- Theme brushes: `Brush.App.<Role>` (e.g., `Brush.App.Surface`).
- Brand colors: `Color.Brand.<Hue><Level>`.
- Fonts: `Font.App.Body`, `Font.App.Heading`.
- Layout measurements: `Space.*`, `Radius.*`, `Size.*` as numeric resource keys (or strongly typed design constants where XAML needs it).
- Public control styles: `AlKhayal.<Component>Style`.
- Shared control templates: `AlKhayal.<Component>Template`.
- View files: `<Feature>View.xaml` / `<Feature>ViewModel.cs`, matching existing project conventions.
- Localization resources: use existing localization architecture; UI labels must not be buried in shared styling XAML.

### 9.3 Example theme brush dictionary (illustrative, compilable WPF syntax)

In `LightTheme.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.App.Background" Color="#F5F7FB" />
    <SolidColorBrush x:Key="Brush.App.Surface" Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.App.TextPrimary" Color="#172640" />
    <SolidColorBrush x:Key="Brush.App.PrimaryAction" Color="#081D49" />
    <SolidColorBrush x:Key="Brush.App.TextOnPrimary" Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.App.Border" Color="#E1E7F0" />
    <!-- Fill in ALL semantic keys from §2.2, consistently. -->
</ResourceDictionary>
```

In `DarkTheme.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.App.Background" Color="#0B1325" />
    <SolidColorBrush x:Key="Brush.App.Surface" Color="#172641" />
    <SolidColorBrush x:Key="Brush.App.TextPrimary" Color="#F3F6FB" />
    <SolidColorBrush x:Key="Brush.App.PrimaryAction" Color="#DEB466" />
    <SolidColorBrush x:Key="Brush.App.TextOnPrimary" Color="#101827" />
    <SolidColorBrush x:Key="Brush.App.Border" Color="#30405B" />
    <!-- Fill in ALL semantic keys from §2.2, consistently. -->
</ResourceDictionary>
```

A view must use:

```xml
<Border Background="{DynamicResource Brush.App.Surface}"
        BorderBrush="{DynamicResource Brush.App.Border}"
        BorderThickness="1"
        CornerRadius="12">
    <TextBlock Text="إدارة المنتجات"
               Foreground="{DynamicResource Brush.App.TextPrimary}" />
</Border>
```

Do **not** do this in screen views:

```xml
<!-- Forbidden in feature views: hardcoded theme-sensitive color -->
<Border Background="#202020" />
```

### 9.4 Example shared button style

```xml
<Style x:Key="AlKhayal.PrimaryButtonStyle" TargetType="Button">
    <Setter Property="MinHeight" Value="40" />
    <Setter Property="Padding" Value="16,8" />
    <Setter Property="FontSize" Value="14" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="Background" Value="{DynamicResource Brush.App.PrimaryAction}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.App.TextOnPrimary}" />
    <Setter Property="BorderThickness" Value="0" />
    <!-- The complete design-system ControlTemplate MUST implement
         hover, pressed, disabled, focus, corner radius, and content layout.
         WPF's default Button template may not honor desired visuals. -->
</Style>
```

For custom templates, use `TemplateBinding` or appropriate relative-source bindings to expose control properties. Apply dynamic references at the style level or in the template so a live theme change propagates. A code-only change to `Background` is insufficient if a custom template ignores that property.

### 9.5 Runtime theme switching

**Required implementation approach:** `ThemeService` stores the user's `Light|Dark|System` preference, resolves current mode, and replaces the specific theme `ResourceDictionary` in `Application.Current.Resources.MergedDictionaries` on the UI dispatcher. `LightTheme.xaml` and `DarkTheme.xaml` define **the exact same semantic brush keys**. Do not append duplicate theme dictionaries forever. Do not replace unrelated merged dictionaries.

Recommended merge order: typography/metrics, then the active theme, then shared component styles that use **dynamic** semantic brushes. This order permits style lookup while dynamic bindings update when a theme dictionary is replaced.

```xml
<!-- App.xaml; file paths may vary with solution structure. -->
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Resources/Themes/Typography.xaml" />
            <ResourceDictionary Source="Resources/Themes/Metrics.xaml" />
            <ResourceDictionary Source="Resources/Themes/LightTheme.xaml" />
            <ResourceDictionary Source="Resources/Themes/Controls.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

**Theme-switching pseudocode, not a drop-in service implementation:**

```csharp
// 1. Persist user preference: Light / Dark / System.
// 2. Resolve System from Windows app-theme preference.
// 3. On the WPF UI dispatcher, find the active theme dictionary by its
//    known URI/reference or a tracked dictionary instance.
// 4. Replace that dictionary AT ITS INDEX with the newly loaded theme dictionary.
// 5. Keep all other dictionaries and open view models unchanged.
// 6. Raise theme-changed notification only if consumers actually need it.
// 7. When preference is System, subscribe/unsubscribe safely to OS theme changes.
```

If the app uses another theme library, keep these semantic keys as the adapter contract and avoid directly binding feature views to vendor-specific color names. Theme selection must not require a `Window` reload and should not reset text entered into an active form.

### 9.6 MVVM and navigation implementation rules

- Business state and commands live in view models/services; no business logic inside XAML code-behind, except UI-specific behavior that cannot be cleanly expressed otherwise.
- Bind actions to `ICommand`; respect `CanExecute`, async execution, cancellation, and duplicate-click prevention.
- Keep navigation tab state in a shell/workspace view model; keep page view model lifetime intentional and dispose subscriptions when closing tabs.
- Use `ContentControl`/`DataTemplate` or the project's chosen view resolution convention rather than building many arbitrary windows for ordinary modules.
- A transaction editor must maintain an unsaved state and safe close behavior.
- Keyboard focus and scanner use must be integrated with input commands; no global key handler that hijacks every TextBox/DataGrid input.
- All long-running data fetches, report generation, device I/O, printing, or payment calls are asynchronous when feasible; UI thread remains responsive.
- Use WPF DIPs; test `PerMonitorV2` DPI awareness where supported by the target .NET/application manifest and Windows environment.

### 9.7 RTL and mixed-direction WPF guidance

Set the shell/root content logical direction to `FlowDirection="RightToLeft"`. Allow inner numeric, phone, barcode, URL, serial number, invoice code, and date strings to opt into `FlowDirection="LeftToRight"` where necessary to avoid reversed sequences. **Do not set the entire app LTR** to fix one number display. Test DataGrid column ordering and horizontal navigation deliberately: don't rely blindly on automatic mirroring. Directional arrows implying Back/Next may need logical mirroring; fixed semantics like Play, Print, Check, Currency, Barcode do not automatically mirror.

Windows native dialogs, third-party popups, printer dialogs, and tooltips may need separate direction settings. Verify mixed Arabic + Latin labels, caret movement, number formatting, and keyboard focus order in actual builds.

---

## 10. Interaction, validation, and safety patterns

### 10.1 Operation feedback state machine

For operations changing business records (sale, payment, save, stock move, refund):

1. `Idle`: fields editable, action available if valid.
2. `Validating`: show inline errors, avoid phantom request.
3. `Submitting`: lock duplicate submission; progress indicator + actionable cancel only if safe.
4. `Succeeded`: confirm **after** commit/service confirmation; reveal reference/receipt/next steps.
5. `Failed`: keep form/cart data intact, show clear error and recovery path; no false success toast.

If operation status is unknown after a network/power interruption, do not offer an unguarded retry that might duplicate payment/sale. Surface an appropriate unknown/reconciliation state according to backend capabilities.

### 10.2 Unsaved and destructive actions

- Unsaved editor tab close: `حفظ التغييرات`, `تجاهل التغييرات`, `إلغاء` (only offer Save if an actual working save action exists).
- Delete/void: show record identifier and consequence; use explicit action text; the primary destructive button is danger-styled, not green.
- High-risk financial posting: present entity, amount, cashbox/payment method, and confirmation; enforce permissions/service-side rules.
- Do not disable Escape globally. Dialogs should have consistent cancellation semantics.

### 10.3 Search and filtering

- Search uses Arabic-friendly text normalization appropriate to the data layer, without silently modifying stored names. Clear button visible when nonempty.
- Filters show current selections and reset action. When a report is filtered, its headline/summary uses the **filtered** dataset.
- Optional search debounce belongs in the view model/service layer; barcode scanning should not be delayed by generic list-search debounce where rapid input is required.

### 10.4 Print and export

- Print Preview and Print are distinct commands. Provide printer/paper/margins options in settings when those capabilities exist.
- Receipt layout is not necessarily the same as WPF main theme. Thermal print should be high-contrast and legible even if the app is in Dark mode.
- Exports reflect active report filters, handle large data asynchronously, and include readable Arabic text. Export is not a substitute for an in-app accessible report.

---

## 11. Responsive behavior and performance

### 11.1 Density by context

| Context | Density | Rule |
|---|---|---|
| Dashboard / overview | Premium | Larger gutters, cards, summary, breathable content. |
| Admin list | Hybrid | Premium header and toolbar + compact readable grid. |
| Data-heavy report | Compact | Dense table, compact filters, summary footer, horizontal overflow. |
| POS checkout | Compact/touch-safe | Dense information but **large critical targets**, fixed totals/payment. |
| Full editor | Balanced | Grouped fields and stable CTA; not overcompressed. |
| Dialog | Focused | Single task, minimal distractions. |

### 11.2 Scaling tests

For every new page, verify:

- 1366×768 (baseline), 1920×1080, and a constrained 1024×720 window.
- DPI at 100%, 125%, 150%, and 200% where available.
- Expanded and collapsed sidebar.
- Three or more open tabs; long tab titles; overflow behavior.
- Light, Dark, and System theme resolution.
- At least one long Arabic field, an Arabic/Latin mixed name, a very large monetary value, and an empty dataset.
- Mouse + keyboard navigation; critical POS targets usable on touch setups when touch is in scope.
- RTL without incorrectly mirrored amounts/barcodes.

### 11.3 Performance rules

Avoid large visual trees for products and reports: use virtualization, lazy loading, and asynchronous query execution. Avoid excessive drop shadows, animated gradients, heavy blur, and expensive converters on each grid cell. Maintain focus and state while theme-switching; don't recreate all open pages just to recolor them.

---

## 12. Empty, loading, and error design language

| State | Presentation | Example Arabic copy |
|---|---|---|
| Initial loading | Inline skeleton/progress and stable layout | `جارٍ تحميل البيانات...` |
| No items yet | Short explanation + allowed create action | `لا توجد منتجات بعد` |
| No search matches | Query-specific helpful text + clear | `لا توجد نتائج مطابقة للبحث` |
| Failed request | Error text + retry if safe | `تعذر تحميل البيانات. حاول مرة أخرى.` |
| Saved | Success toast after confirmation | `تم الحفظ بنجاح` |
| Payment complete | Confirmed result details and receipt action | `تم تسجيل الدفع بنجاح` |
| Permission denied | Plain-language restriction | `ليس لديك صلاحية لتنفيذ هذا الإجراء` |
| Device unavailable | Informative error + settings/retry action | `الطابعة غير متاحة حاليًا` |

Use consistent semantic icons/chips and tokenized backgrounds, not oversaturated full-screen error fills. Error messages should help recovery but must not leak sensitive technical internals.

---

## 13. Information boundaries and reference evidence

The provided reference screenshots displayed functional examples of: rapid POS, purchases, sales/profit reports, treasury/cashboxes, users, settings, returns, supplier balances/settlements, printer configuration, installments, employees, and modal confirmations. They also showed menu category ideas (customers, sales, suppliers, purchases, returns, installments, expenses/vouchers, treasury/banks, alerts, employees, backups).

**Permitted extraction:** functional vocabulary, module categories, form field types, cashier/financial workflow checkpoints, existence of tabs/grids/print controls, loading and confirmation states.

**Not permitted as design authority:** original charcoal-only aesthetic, top horizontal menu layout, saturated red headers, multi-colored image icons, modal styling, exact column density, oversized circular home shortcuts, bottom status icons, or the screenshots' UI component dimensions. AlKhayal's brand and this contract take precedence.

Future areas such as full customer account statements, inventory counting, product variants, advanced taxation, and online sync are **extension points**, not present feature commitments merely because they appeared in a reference menu.

---

## 14. Design review & release acceptance checklist

A UI task is not complete until these checks pass:

- [ ] Uses one approved page template (T01–T10) and shares app shell/sidebar/tabs.
- [ ] No page-local theme-sensitive HEX color, unless explicitly part of an approved brand asset/data visualization and documented.
- [ ] Reuses `AlKhayal.*Style` and `Brush.App.*` resources; no parallel style system introduced.
- [ ] Live Light↔Dark switch works without resetting the view; System preference persists correctly.
- [ ] Buttons, inputs, menus, popups, grids, dialogs, validation, and toasts support both themes.
- [ ] Arabic RTL layout and sensible local LTR exceptions verified.
- [ ] Focus ring, meaningful automation names, keyboard operation, and contrast tested.
- [ ] Sidebar works collapsed/expanded; workspace tabs activate/close/guard unsaved work.
- [ ] No actions shown for absent/unimplemented features; permissions hide/disable appropriate UI.
- [ ] Loading, empty, no results, failed, and populated states are handled.
- [ ] Critical transaction requests avoid duplicate submission and never show premature success.
- [ ] Layout works at baseline and constrained window sizes and increased DPI.
- [ ] Grid/list performance and virtualization verified where applicable.
- [ ] Project build passes; relevant UI smoke tests/manual screenshots reviewed in both themes.
- [ ] New semantic design decisions are reflected in this document, not just hidden in XAML.

---

## 15. Rules for future screens and features

Whenever a new feature is requested:

1. Identify its domain, user role, and concrete workflow from product requirements.
2. Choose an existing template from §7; prefer composition of existing components over inventing another layout.
3. Add its route to the appropriate sidebar group **only when the feature is implemented and authorized**.
4. Decide whether record editing is a dialog, drawer, or closable workspace tab based on complexity and potential unsaved state.
5. Use shared semantic brushes and styles; if a genuinely new state is needed, add **both** Light/Dark tokens with usage rules before use.
6. Provide page-level UI anatomy in the §8 specification format.
7. Test the sidebar in both states, tab overflow, Arabic RTL, all themes, keyboard focus, and smaller desktop sizes.
8. Record unresolved business logic as explicit questions, not invented defaults.
9. Keep existing visual language stable as modules are added. A feature addition must not trigger an unrequested shell redesign.
10. Update `design.md` when approved patterns change; update `AGENTS.md` only if agent operating rules change.

### 15.1 Change log template

```text
Date:
Decision:
Status: Approved / Proposed / Deprecated
Reason:
Affected tokens/components/templates/screens:
Migration/test implications:
Owner/approval reference:
```

### 15.2 Architectural anti-patterns (do not do these)

- Building each view with isolated hardcoded colors/styles.
- Implementing Dark mode by a root `Opacity` change, color inversion, or a second duplicated set of entire views.
- Rebuilding the UI in HTML/CSS styles and imitating them loosely in WPF without actual reusable XAML resource contracts.
- Designing a cramped administrative view because operational grids happen to be compact.
- Making POS control targets tiny merely to fit additional columns.
- Creating an unrelated layout for every management page.
- Using mock data to imply posted sales, inventory, payment confirmation, or profit.
- Copying the reference screenshots' navigation architecture or visuals verbatim.
- Embedding sensitive business rules in button Click handlers or presentation-only converters.
- Changing the theme by replacing user controls and losing open tabs, input, or keyboard focus.

---

## 16. Build order for the visual foundation

**Recommended sequence, not a claim that these features are already implemented:**

1. Establish `Resources/Themes` and complete the Light/Dark semantic keys from §2.2.
2. Implement `ThemeService` with persistence and System mode fallback/listening.
3. Implement global Cairo/fallback typography, metrics, and shared styles for buttons, fields, grids, cards, sidebar, tabs, and dialogs.
4. Implement the WPF shell: right collapsible sidebar, header, tabs, central content, bottom status bar.
5. Implement T03 management listing and T05 reporting templates; they exercise grid styling and responsiveness.
6. Implement T02 POS layout with keyboard/scanner focus handling and real transaction commands when available.
7. Fill feature views as backend requirements are confirmed; use §14 acceptance checks on every one.

**End of specification.** All future visual changes should preserve these reusable contracts or update this document through an explicit design decision.
