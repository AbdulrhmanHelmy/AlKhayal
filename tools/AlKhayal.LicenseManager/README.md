# Owner License Manager

Standalone owner-only HTML/CSS/JavaScript dashboard, native Node.js crypto, built-in SQLite. No npm packages or customer-side dependencies.

Deploy with `install-desktop.ps1` using a verified Node 24 LTS runtime; it resolves the Windows Desktop Known Folder and bundles node.exe. Launch the Desktop shortcut. Private keys/registry are stored in the owner's LocalApplicationData, never under public/ or the installed tool.

See `docs/phase-10/phase-10-1-licensing.md` for actual installed paths, secure production key initialization, offline workflows, backup/restore, protocol compatibility and executed tests. Production key creation is always an explicit owner action. Never ship this tool to POS customers.
