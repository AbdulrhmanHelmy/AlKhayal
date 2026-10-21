# User management

AlKhayal supports exactly two persisted account types: `administrator` and `cashier`. Multiple active accounts of either type are allowed. Administrators can create/edit accounts, change role or status, reset passwords, and configure cashier screen grants. Cashiers cannot invoke these endpoints or alter their own role or permissions.

`Users` remains the single identity table. Passwords use the existing salted PBKDF2-SHA256 implementation and no API contract exposes a password hash. New users require a non-empty password; existing passwords are never displayed. Password reset is separate and ends all sessions belonging to the target account. Role and activation changes also invalidate target sessions. Successful login records `LastLoginUtc`.

The data adapter enforces the last-active-administrator invariant inside the same SQLite transaction as a demotion or deactivation. Duplicate usernames use the existing case-insensitive unique constraint. Administrative actions are appended to `UserAuditLog` with actor, target, action, UTC time, and non-sensitive details only.

The Arabic RTL screen is the stable `users-list` route (`المستخدمون والصلاحيات`) with renderer key `user-management`. It is reached from `الإعدادات ← المستخدمون والصلاحيات`; no standalone user-permissions sidebar group remains. The Settings entry and its Admin-only classification are read from `screens.json`, not duplicated in the hub renderer.

The screen uses real API data and offers search, role/state filters, add/edit, activate/deactivate, password reset, and the cashier permission editor. Light, Dark, and System themes share the same semantic tokens. Other business records remain session-only demonstrations. Legacy route IDs `user-permissions`, `sales-permissions`, and `settings-permissions` are retained as hidden Admin-only redirects to `users-list`, preserving old links without exposing the obsolete permission placeholders.

Renderer availability is checked during shell startup. Static frontend responses are no-store and the WinForms host clears its WebView2 network cache before initial navigation, so a newly fetched registry cannot be paired with a stale HTML shell that omitted `user-management.js`.
