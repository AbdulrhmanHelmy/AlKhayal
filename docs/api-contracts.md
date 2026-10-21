# API contracts

## General settings endpoints

- `GET /api/settings/company`: authenticated company profile read.
- `PUT /api/settings/company`: administrator + CSRF protected company profile update.
- `GET /api/settings/general`: authenticated effective language/date/time/currency/tax settings.
- `PUT /api/settings/general`: administrator + CSRF protected validated update.
- `GET /api/settings/database-info`: administrator-only local path, size, migration count/latest name, journal mode, and foreign-key state. It cannot modify files.

All responses use the standard `{ success, data, error }` envelope. The only supported `languageCode` is `ar`; invalid formats, currency codes, precision, tax rates, email addresses, and inconsistent tax flags return `400 validation`. Database information returns no connection string or credentials.

All endpoints return `{ "success": true, "data": ... }` or `{ "success": false, "error": { "code": "...", "message": "..." } }`. Login, current-user restoration, and logout use the HttpOnly SameSite `alkhayal_session` cookie. JSON properties use camelCase.

- `GET /api/system/health` returns `{ status, utc, version }`.
- `POST /api/auth/login` accepts `{ username, password }`; success returns `{ id, username, displayName, role, csrfToken }` and sets the session cookie. Unknown users and invalid passwords return the same HTTP 401 credential error. A valid password for a disabled account returns the disabled-account response.
- `GET /api/auth/me` returns the same safe profile and current CSRF token for a valid, non-expired server-side session. Invalid or expired sessions return HTTP 401; expired records are deleted.
- `GET /api/auth/me/screen-access` returns `{ role, screenIds, generatedUtc }` after combining authentication, role, explicit cashier grants, administrator-only registry metadata, and effective feature state.
- `POST /api/auth/logout` requires the session cookie plus `X-CSRF-Token`. A missing or invalid token is rejected; success invalidates the session and expires the cookie.
- `GET /api/business-activities` requires an authenticated session and returns the active registry ordered by default priority. Each item includes `key`, `arabicName`, `description`, `defaultPriority`, `isActive`, and `suggestedCapabilities`.
- `GET /api/business-activities/selected` requires an authenticated session and returns `{ isConfigured, primaryActivityKey, activityKeys }`.
- `GET /api/business-activities/recommendations` requires an authenticated session. With no query it evaluates the saved selection; `?activityKeys=clothing,footwear` previews an unsaved selection. It returns deduplicated recommendations, source activity keys, Arabic reasons, newly recommended keys, recommendations that no longer apply, and preserved administrator override keys.
- `PUT /api/business-activities/selected` requires an authenticated `administrator` session and `X-CSRF-Token`. The body is `{ activityKeys, primaryActivityKey, applyRecommendations }`. It rejects unknown/inactive keys and a primary key outside the selection. Saving replaces only selection metadata; it does not delete business data. Recommendations are applied only when explicitly requested and never overwrite `administrator` capability rows.

Feature endpoints require authentication:

- `GET /api/features` returns stable catalog metadata for all core/optional modules and product capabilities.
- `GET /api/features/effective` returns resolved `modules`, `capabilities`, source/recommendation metadata, dependencies, and `generatedUtc`.
- `PUT /api/features/modules/{key}` and `PUT /api/features/capabilities/{key}` accept `{ isEnabled }`; administrator role and `X-CSRF-Token` are required. Unknown/core changes return 400 and dependency conflicts return 409.
- `POST /api/features/recommendations/preview` accepts `{ activityKeys }` and returns a read-only effective preview.
- `POST /api/features/recommendations/apply` accepts `{ activityKeys }`, requires administrator plus CSRF, applies dependency-complete recommendations, and preserves administrator overrides.

User-management endpoints require an authenticated administrator. Mutations additionally require `X-CSRF-Token`: `GET/POST /api/users`, `GET/PUT /api/users/{id}`, `PUT /api/users/{id}/status`, `POST /api/users/{id}/reset-password`, and `GET/PUT /api/users/{id}/screen-permissions`. They return 400 for validation/role/screen errors, 401 for missing sessions, 403 for cashier or CSRF failures, 404 for missing users, and 409 for duplicate usernames or last-active-administrator protection. Passwords and hashes are never returned.

Static frontend files, including `frontend/data/screens.json`, are served from the same OWIN origin with `Cache-Control: no-store, no-cache, must-revalidate`, `Pragma: no-cache`, and an expired response. This keeps the shell scripts and route registry on one deployment version in persistent WebView2 profiles. Other business-screen operations do not yet have HTTP contracts: their UI state is explicitly in-memory and must not be interpreted as durable server behavior.
