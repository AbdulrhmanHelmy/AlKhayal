# QA Batch 2 — acceptance runner and scenario manifest report

Generated 2026-10-26 by the OpenCode QA Batch 2 worker (GLM-5.3-Flash). Scope: test
infrastructure only. Exactly three new files were created; no existing code, test, doc, or
pre-existing user file was edited, and no commit, push, or branch change was made:

- `scripts/run-acceptance-tests.ps1` — reusable PowerShell 5.1 acceptance runner
- `docs/qa/scenario-manifest.json` — data-only, extensible scenario manifest (QA-001..QA-100)
- `docs/qa/acceptance-test-report.md` — this report

`docs/qa/phase-1-coverage.md` (owned by Batch 1) was not modified. `seeded-database.db`, the
operator database under `%LOCALAPPDATA%\AlKhayal`, and all pre-existing working-tree changes
were untouched.

## Runner: `scripts/run-acceptance-tests.ps1`

PowerShell 5.1 compatible (`#requires -version 5.1`; verified on 5.1.22621.4249).

Parameters:

| Parameter | Meaning |
|---|---|
| `-All` | Complete .NET test suite (both test projects, unfiltered) plus the frontend registry suite as coarse checkpoints; per-scenario statuses are then attributed from per-test TRX results — a global suite pass is never treated as a per-scenario pass |
| `-Phase 1..5` | Only scenarios of that phase; executes only the union of their mapped tests via exact filters, never the full suite |
| `-Module <name>` | Only scenarios whose manifest module matches (combines with `-Phase`) |
| `-Scenario QA-NNN` | Only that exact scenario |
| `-NoBuild` | Skip the build step |
| `-OutputJson <path>` | Additionally write the JSON result to a UTF-8 file (JSON is always printed to stdout) |

Status semantics (enforced by the runner, documented in the manifest):

- **PASS** — every mapped test for the scenario ran in this run and passed, and the manifest records
  **no** manual/browser/desktop/hardware remainder for the scenario (`partial: false`).
- **PARTIAL** — every mapped test for the scenario ran and passed, but the scenario carries an
  unexecuted manual remainder (`partial: true`). PARTIAL is never converted to PASS; the open
  remainder stays visible in `manualRemainder`/`reason`. (See the addendum below for the batch that
  introduced this status; earlier table columns below that read "PASS" with `Partial = yes` now report
  PARTIAL under the updated runner.)
- **FAIL** — a mapped test failed, or a mapped test did not appear in the run results
  (selector mismatch or discovery failure). This makes a silently absent test loud.
- **SKIP** — mapped scenario outside the requested run scope.
- **BLOCKED** — unmapped placeholder, no automated test mapping (QA-016/QA-017), missing tool
  (dotnet/node), or a build failure. BLOCKED is never converted to PASS.

Exit codes: `0` no FAIL; `1` one or more FAIL (or a failed `-All` coarse checkpoint); `2` database
safety gate failed closed; `3` infrastructure failure (missing dotnet, empty scope, build failure).

Test selection uses exact VSTest filters (`FullyQualifiedName=<namespace.Class.Method>` joined with
` | `), one `dotnet test` invocation per test project for the union of in-scope mapped tests. TRX
results are parsed so each scenario's status comes from the outcome of exactly its own mapped tests
(xUnit theory cases are matched by FQN prefix + `(`). Shared tests (for example the large
`UserManagementApiTests` method used by QA-002/003/007/008/009/012/014/015) are executed once per
run and attributed to every scenario that maps them.

Build scope: only the two test projects are built (`tests/AlKhayal.UnitTests.csproj`,
`tests/AlKhayal.IntegrationTests.csproj`). A full `AlKhayal.slnx` build is deliberately not
attempted because the running desktop UI process locks solution build output (it was not killed).
Both test projects built with **0 warnings, 0 errors** before every run in this batch.

## Scenario manifest: `docs/qa/scenario-manifest.json`

Data-only JSON (no logic), version 1, 100 scenarios. QA-001..QA-015 carry exact
`FullyQualifiedName` mappings into the committed suite, following `docs/qa/phase-1-coverage.md`
(QA-004/QA-005 authoritative; the other IDs are marked *inferred* there, and the same caveat
applies here). QA-016 and QA-017 are known scenarios with **no automated coverage** and are
permanently BLOCKED until automation exists. QA-018..QA-100 are unmapped placeholders with the
manifest's documented phase/module scaffold ranges (phase 2 catalog, phase 3 inventory, phase 4
purchasing, phase 5 sales/customers/frontend/native); the runner reports them BLOCKED, never PASS.
Later batches extend the manifest by filling placeholders or appending scenarios — no runner
change is required.

## Database safety gate

Before any write-capable test process the runner:

1. generates a unique absolute SQLite path under the system temp directory
   (`%TEMP%\alkhayal-qa-<guid>\alkhayal-qa.db`); a pre-existing path is never touched and a
   candidate that exists is rejected,
2. sets the `ALKHAYAL_DATABASE_PATH` process environment variable for the runner and all child
   test processes (the previous value, when present, is recorded in the JSON),
3. prints the effective and canonical paths,
4. verifies the canonical path is outside `%LOCALAPPDATA%\AlKhayal`, differs from the normal
   `alkhayal.db`, and differs from the repository `seeded-database.db`; any violation fails closed
   (exit 2) before any test process starts.

No source database is copied and no unknown path is cleaned up. Existing test fixtures continue to
use their own disposable GUID SQLite paths, exactly as before.

Gate output observed in every executed run of this batch (paths varied per run; all checks `True`):

```text
[acceptance] SAFETY GATE PASSED
[acceptance]   effective ALKHAYAL_DATABASE_PATH = C:\Users\user\AppData\Local\Temp\alkhayal-qa-<guid>\alkhayal-qa.db
[acceptance]   DIFFERS_FROM_DEFAULT             = True (operator default: C:\Users\user\AppData\Local\AlKhayal\alkhayal.db)
[acceptance]   OUTSIDE_APPDATA_ALKHAYAL         = True (C:\Users\user\AppData\Local\AlKhayal)
[acceptance]   DIFFERS_FROM_SEEDED_DATABASE     = True
```

## Executed runs (exact recorded results)

Environment: Windows PowerShell 5.1.22621.4249, dotnet 10.0.402, node v22.14.0. All timestamps UTC.

### Run 1 — `-Scenario QA-004` (2026-10-26T10:50:25Z, exit code 0)

- Safety gate passed: `C:\Users\user\AppData\Local\Temp\alkhayal-qa-935b427e75fb4ebda75a00be46ed47de\alkhayal-qa.db`
- `integration-filtered`: exit=0, **total=2, passed=2, failed=0**, durationMs=18869
- QA-004 status: **PASS** (automated portion), durationMs=8215
  - `QA004_expired_session_is_rejected_removed_and_not_restored_by_restart` — Passed (2397 ms)
  - `QA004_csrf_token_is_invalid_for_an_expired_session` — Passed (5818 ms)
- Totals: PASS=1 FAIL=0 SKIP=99 BLOCKED=0
- Manual remainder still visible: real 8-hour wall-clock session; WebView2 cookie retention across restarts.

### Run 2 — `-Scenario QA-005` (2026-10-26T10:51:00Z, exit code 0)

- Safety gate passed: `C:\Users\user\AppData\Local\Temp\alkhayal-qa-c0e3a1cc94d0482385d6c0211016ef0d\alkhayal-qa.db`
- `integration-filtered`: exit=0, **total=1, passed=1, failed=0**, durationMs=14739
- QA-005 status: **PASS** (automated portion), durationMs=4986
  - `QA005_session_cookie_sets_HttpOnly_and_SameSite_and_login_me_responses_leak_no_secrets` — Passed
- Totals: PASS=1 FAIL=0 SKIP=99 BLOCKED=0
- Manual remainder still visible: `Secure` flag over a real HTTPS listener; WebView2 cookie storage inspection.

### Run 3 — `-Phase 1` (2026-10-26T10:51:25Z, exit code 0)

- Safety gate passed: `C:\Users\user\AppData\Local\Temp\alkhayal-qa-875a4307fbf043098c626c72388a1df6\alkhayal-qa.db`
- Only the union of the 25 unique mapped integration tests was executed (exact-FQN filter; the
  unit project had no in-scope mapped tests and was not run). Result: exit=0,
  **total=29 test cases (25 methods; two theories expand to 3 cases each), passed=29, failed=0**,
  durationMs=40877.
- Per-scenario statuses (scenario duration = sum of its mapped tests' durations):

| ID | Status | Tests (cases) | Scenario durationMs | Partial |
|---|---|---|---|---|
| QA-001 | PASS | 6 tests, 10 cases | 31507 | yes |
| QA-002 | PASS | 1 | 23681 | yes |
| QA-003 | PASS | 1 | 23681 | yes |
| QA-004 | PASS | 2 | 8435 | yes |
| QA-005 | PASS | 1 | 4986 | yes |
| QA-006 | PASS | 2 | 7864 | yes |
| QA-007 | PASS | 6 | 83669 | yes |
| QA-008 | PASS | 2 | 28538 | yes |
| QA-009 | PASS | 2 | 34326 | yes |
| QA-010 | PASS | 3 | 17725 | yes |
| QA-011 | PASS | 3 | 14328 | yes |
| QA-012 | PASS | 4 | 52143 | yes |
| QA-013 | PASS | 2 | 9400 | no |
| QA-014 | PASS | 4 | 50440 | yes |
| QA-015 | PASS | 2 | 28667 | yes |
| QA-016 | BLOCKED | — | 0 | no automated mapping |
| QA-017 | BLOCKED | — | 0 | no automated mapping |

- Totals: **PASS=15 FAIL=0 SKIP=83 BLOCKED=2**. Every PASS record carries its manual remainder
  (browser-only or native portions) in `manualRemainder`/`reason`, so the automated result is never
  presented as full browser/native coverage.

### Run 4 — `-All` (2026-10-26T10:52:18Z, exit code 0)

- Safety gate passed: `C:\Users\user\AppData\Local\Temp\alkhayal-qa-7f4d50afd0a94df299456af9a4d19280\alkhayal-qa.db`
- Coarse checkpoints (unfiltered):
  - unit-suite: exit=0, **total=49, passed=49, failed=0**, durationMs=16454
  - integration-suite: exit=0, **total=115, passed=115, failed=0**, durationMs=50840
  - frontend-suite (`node frontend/tests/run-tests.js`): exit=0, durationMs=212, output tail:
    `Frontend registry, settings/feature/access lifecycle, user management, customer management, and calculation tests passed (99 screens).`
- The suite counts match the independently verified full baseline (49 unit + 115 integration;
  frontend 99 screens).
- Per-scenario statuses were attributed from the per-test TRX results, not from the aggregate:
  **PASS=15 FAIL=0 SKIP=0 BLOCKED=85** — the 15 mapped automated portions of QA-001..QA-015 PASS;
  QA-016/QA-017 and QA-018..QA-100 are BLOCKED (85 total) exactly as designed. A green global
  suite did **not** turn any unmapped placeholder into PASS.

### Negative-semantics spot check — `-Scenario QA-016` (exit code 0)

QA-016 reported **BLOCKED** ("No automated test mapping..."), no test process was started and the
safety gate was correctly not invoked (nothing write-capable ran). Totals: PASS=0 FAIL=0 SKIP=99
BLOCKED=1. This confirms unmapped/no-automation scenarios can never silently PASS.

## Compatibility assumptions and scope

- Runs executed on this machine with dotnet 10.0.402 / node v22.14.0 / PowerShell 5.1; the runner
  fails closed (exit 2/3) with BLOCKED statuses when tools are missing.
- Test binaries were rebuilt from current sources for the two test projects only; the full solution
  build was skipped because the running desktop UI process locks solution build output.
- The frontend registry suite is used only as a coarse `-All` checkpoint; no scenario status is
  derived from it (no scenario maps to frontend tests yet).
- TRX artifacts for this batch remain under
  `%TEMP%\alkhayal-qa-results\20261026-*` (outside the repository; nothing was added to the repo
  except the three files listed above).

## Blockers / residual risks

- QA-016 (LAN/cloud hardening) and QA-017 (native WebView2 lifecycle) have no automated coverage
  and remain BLOCKED by design; QA-016 additionally requires deployment-review work per AGENTS.md.
- QA-018..QA-100 are scaffold placeholders; their phase/module ranges follow the manifest's
  documented convention because no canonical QA-001..QA-100 checklist exists in the repository
  (same numbering caveat as `docs/qa/phase-1-coverage.md`). A later batch owning the canonical
  list must confirm or replace the ranges.
- All PASS statuses in this report cover only the mapped automated assertions; every manual
  remainder is stated per scenario and remains open.

`git diff --check` was run against the three new files (via `--no-index` against an empty file, so
untracked files were covered): no whitespace errors.

## Addendum — PARTIAL status (reporting-only batch, 2026-10-26)

Scope: QA reporting only. No application code, no test code, no data, no commit/push, no operator
database. Files changed: `scripts/run-acceptance-tests.ps1` (status attribution + totals + synopsis
documentation), `docs/qa/scenario-manifest.json` (`statusSemantics`), `docs/qa/phase-3-coverage.md`
(NOT changed — its text was already accurate), and this addendum.

### Problem fixed

The runner previously reported **PASS** for a scenario whose mapped automated tests all passed even
when the manifest marked the scenario `partial: true` with an unexecuted manual/browser/desktop/
hardware remainder (for example QA-101). The full requirement — including the manual portion — was
never executed, so PASS overstated acceptance. Now such a scenario reports **PARTIAL**, and PASS is
reserved for scenarios with no recorded manual remainder (fully automated).

### What changed

- `scripts/run-acceptance-tests.ps1` — when every mapped test ran and passed and the manifest entry
  is `partial`, the status is now `PARTIAL` (with the `manualRemainder` restated in `reason`);
  a non-partial entry still reports `PASS`. `totals` gained a `partial` counter; the console summary
  prints `PASS=<n> PARTIAL=<n> FAIL=<n> SKIP=<n> BLOCKED=<n>`. Unchanged on purpose: FAIL (failed or
  absent mapped test) still exits 1 and is loud; the database safety gate, BLOCKED/SKIP semantics,
  exact-FQN test selection, and exit codes (0 = no FAIL, 2 = safety gate, 3 = infrastructure) are
  preserved. PARTIAL is not a FAIL and still exits 0.
- `docs/qa/scenario-manifest.json` — `statusSemantics.PASS`/`PARTIAL` now state the rule: PASS
  requires no manual remainder; PARTIAL = automated portion passed with an open unexecuted remainder.
- Manifest coverage review: all mapped scenarios with manual gaps are already marked `partial` with
  a concrete `manualRemainder` (screens in WebView2, browser-only UX, physical barcode scanner
  hardware in QA-025, WebView2 cookie/storage in phase 1); QA-016/QA-017 (fully manual) and QA-047
  (no automated mapping) stay unmapped and BLOCKED as before. QA-013 is recorded with no manual
  remainder and — per `docs/qa/phase-1-coverage.md`'s coverage map (no missing browser/native
  portion) — was treated as fully automated. No new gaps were invented and no execution was claimed
  beyond what is recorded below.

### Executed checks (real output, this batch)

Environment: Windows PowerShell 5.1, dotnet 10.0.402. Each run printed
`SAFETY GATE PASSED` with `DIFFERS_FROM_DEFAULT=True` (operator default
`C:\Users\user\AppData\Local\AlKhayal\alkhayal.db`), `OUTSIDE_APPDATA_ALKHAYAL=True`,
`DIFFERS_FROM_SEEDED_DATABASE=True`, and a fresh non-existing temp path under
`C:\Users\user\AppData\Local\Temp\alkhayal-qa-<guid>\alkhayal-qa.db`.

1. `powershell -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -Scenario QA-101`
   — integration-filtered: exit=0, total=11 (5 facts + 6 theory cases), passed=11, failed=0.
   QA-101 status: **PARTIAL** (never PASS), reason restates the unexecuted WebView2 manual remainder.
   Totals: PASS=0 PARTIAL=1 FAIL=0 SKIP=100 BLOCKED=0; exit code 0.
   Full JSON verified: `record.status == "PARTIAL"`, `record.reason` contains the manual remainder.
2. `… -Scenario QA-047` — no tests, no build, no safety-gate DB write attempted (nothing
   write-capable planned). QA-047 status: **BLOCKED** ("No automated test mapping…").
   Totals: PASS=0 PARTIAL=0 FAIL=0 SKIP=100 BLOCKED=1; exit code 0; runner process exit 0.
3. `… -Scenario QA-013` — integration-filtered total=2 passed=2 failed=0
   (`Bootstrap_api_is_public_once_then_permanently_conflicts`,
   `Health_login_me_and_logout_use_real_owin_api`).
   QA-013 status: **PASS** (fully automated; no manual remainder recorded).
   Totals: PASS=1 PARTIAL=0 FAIL=0 SKIP=100 BLOCKED=0; exit code 0.

The previously recorded per-scenario tables in this report mean "automated portion passed"; under the
updated runner those same passing-with-manual-remainder scenarios report PARTIAL (and those tables
already carried a `Partial` column distinguishing them from QA-013).

## Addendum 2 — latest independently executed result (2026-10-26)

Reporting-only addendum; this report is the only file changed. No application or test code was
modified, no test was authored, and no commit or push was made.

Command executed independently and freshly (not copied from a previous batch record):

```
powershell -ExecutionPolicy Bypass -File scripts\run-acceptance-tests.ps1 -All -NoBuild
```

- Started 2026-10-26T12:06:25.929Z, finished 2026-10-26T12:07:37.530Z, runner
  `scripts/run-acceptance-tests.ps1`, tools dotnet 10.0.402 / node v22.14.0.
- **Database safety gate passed** (all checks True):
  `DIFFERS_FROM_DEFAULT=True`, `OUTSIDE_APPDATA_ALKHAYAL=True`, `DIFFERS_FROM_SEEDED_DATABASE=True`;
  fresh writable temp path
  `C:\Users\user\AppData\Local\Temp\alkhayal-qa-ffdb9349aa5e495fb803b53f6d4309fa\alkhayal-qa.db`.
- Build: **skipped by `-NoBuild`** — and the full solution build was **not rerun** in any form because
  the live desktop UI process locks solution build output; running it was not attempted.
- Coarse checkpoints (exit 0 each): **unit 49/49 passed** (9532 ms), **integration 127/127 passed**
  (60680 ms, grew from the 115 recorded in Run 4 above), and the frontend checkpoint
  **passed** (187 ms) with output tail `Frontend registry, settings/feature/access lifecycle, user
  management, customer management, and calculation tests passed (99 screens).`
- Per-scenario attribution (from per-test TRX results, not the aggregate):
  **PASS 21, PARTIAL 32, FAIL 0, SKIP 0, BLOCKED 48**. Runner exit code 0 (no FAIL).
- **QA-056..QA-100 remain unmapped placeholders and are reported BLOCKED** (phase 4 purchasing
  QA-056..071, phase 5 sales/printing/frontend/cross-module QA-072..100); none was converted to
  PASS or PARTIAL by the green suites.
- **QA-016, QA-017 and QA-047 remain BLOCKED** (no automated test mapping; QA-016 also requires
  deployment-review work per AGENTS.md).
- All PASS/PARTIAL statuses cover only the mapped automated assertions. The **desktop/native
  (WebView2), browser, and physical-hardware portions were not verified** in this run; every
  scenario carrying such a remainder stays PARTIAL with the remainder stated in
  `manualRemainder`/`reason`.
- The purchase-mapping worker was stopped without making any changes; phase-4 purchasing mappings
  are unchanged (still unmapped) and no purchase-scenario evidence was produced here.
- TRX artifacts for this run:
  `C:\Users\user\AppData\Local\Temp\alkhayal-qa-results\20261026-120625-1cf00f94`
  (`unit-full.trx`, `integration-full.trx`; outside the repository).
