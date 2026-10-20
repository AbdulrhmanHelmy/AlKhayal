# API contracts

All endpoints return `{ "success": true, "data": ... }` or `{ "success": false, "error": { "code": "...", "message": "..." } }`. Login, current-user restoration, and logout use the HttpOnly SameSite `alkhayal_session` cookie. JSON properties use camelCase.

- `GET /api/system/health` returns `{ status, utc, version }`.
- `POST /api/auth/login` accepts `{ username, password }`; success returns `{ id, username, displayName, role, csrfToken }` and sets the session cookie. Unknown users and invalid passwords return the same HTTP 401 credential error. A valid password for a disabled account returns the disabled-account response.
- `GET /api/auth/me` returns the same safe profile and current CSRF token for a valid, non-expired server-side session. Invalid or expired sessions return HTTP 401; expired records are deleted.
- `POST /api/auth/logout` requires the session cookie plus `X-CSRF-Token`. A missing or invalid token is rejected; success invalidates the session and expires the cookie.

Static frontend files, including `frontend/data/screens.json`, are served from the same OWIN origin. Business-screen operations do not yet have HTTP contracts: their UI state is explicitly in-memory and must not be interpreted as durable server behavior.
