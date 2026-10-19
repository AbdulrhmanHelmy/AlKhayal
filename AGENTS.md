# AlKhayal (الخيال) - Agent Rules
- Egyptian Arabic RTL desktop POS. Replicate the original (DoCashier) exactly from docs/MASTER_SPEC.md. No extra features.
- Target: .NET Framework 4.8 (net48), must run on Windows 7 SP1+. Do NOT use APIs or packages that need .NET 5+.
- Modules: src/AlKhayal.Core (models, services, interfaces; references nothing), src/AlKhayal.Data (SQLite, repositories; references Core), src/AlKhayal.UI (WPF, MVVM; references Core + Data).
- No business logic in code-behind. UI talks to Core services only.
- The SQLite file lives in %LocalAppData%\AlKhayal, never in the repo.
- Build: dotnet build | Test: dotnet test
- Every feature must be built fully complete before moving on.
- Design rules: see DESIGN.md
