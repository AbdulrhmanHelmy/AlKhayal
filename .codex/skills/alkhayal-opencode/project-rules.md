# AlKhayal high-risk rules

- Preserve the approved C# .NET Framework 4.8 / .NET Standard 2.0 modular monolith, SQLite, Dapper or parameterized ADO.NET, and HTTP boundary. Do not add an ORM or move business logic into UI/controllers.
- Sales, purchases, returns, treasury, inventory, and any operation changing money, stock, balances, or documents must be one atomic database transaction with full rollback on failure. Require tests for calculations, deterministic rounding, rollback, and prevention of negative or inconsistent stock/balances. Preserve immutable audit/source-document trails and idempotent operation keys where applicable.
- Never run a destructive migration, reset, or delete a database without user approval. Keep SQLite on local disk. Follow the release-sensitive migration rule in `AGENTS.md`.
- Never create Git commits or push; never change billing, credentials, or privacy settings.
- For UI work, read `docs/design.md` and `docs/ui-audit.md` as relevant; preserve Arabic RTL conventions, Chromium 109 compatibility, and the shared shell. Do not imply mock mutations persist.
- Follow `AGENTS.md` for security, dependency direction, documentation, testing, and Windows 7 compatibility caveats. This file supplements it rather than replacing it.
