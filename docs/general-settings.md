# General settings

## Storage decisions

Company identity, date/time display policy, numeric grouping, currency, decimal precision, and configurable default tax behavior are business-wide and stored in SQLite. `CompanyProfile` and `ApplicationSettings` each use a constrained singleton row. The server validates every mutation and records a non-sensitive audit entry in the same transaction.

Theme is a user/device presentation preference. Light, Dark, and System continue to use the existing `alkhayal_theme` local-storage key and apply immediately through `AlKhayalTheme`; no business-critical value is stored there. Sidebar collapse remains device-local for the same reason. Language is fixed to Arabic because no complete second translation exists.

## Screens and behavior

The registry-driven Settings hub exposes:

- `settings-general`: date/time, number grouping, currency, decimal precision, and tax defaults.
- `settings-company`: company and optional registration/contact/footer fields.
- `settings-appearance`: Light, Dark, and System for the current device.
- `settings-backup`: read-only database path/status/migration information. Backup and restore are not implemented and the UI says so explicitly.

Company/general forms load persisted values, validate client-side for usability, retain failed edits, provide cancel behavior, disable duplicate submits, show success/error states, and ignore stale async results after navigation. The shared settings-state service deduplicates reads and updates the supported money/number/date/time formatters without recreating the active route.

Changing currency or precision affects presentation defaults only. It does not rewrite historical amounts. Tax settings are jurisdiction-neutral defaults and do not claim invoice compliance. Logo upload, document numbering, branches, warehouses, backup, restore, invoice generation, and business transactions remain outside this slice.

## Security

Authenticated users may read general/company values for future shared formatting and headers. Only administrators with a valid CSRF token may update them. Database information is administrator-only and exposes no arbitrary filesystem operation, SQL, connection string, credential, hash, cookie, or token.
