# Pre-v0.2 stability certification

Status: **audit in progress; certification has not been granted**.

## Baseline

- Started on clean `main`, SHA `af35d568ce8a008ce1a57c22c6336ff2d9b33e04`.
- Fetched `origin/main` on 2026-09-19; it matches the supplied baseline.
- Audit branch: `codex/pre-v02-stability-certification`.
- Windows, PowerShell 7.4.20, .NET SDK 10.0.100 (explicit Codex runtime),
  Node 26.8.1, npm 11.19.0, Python 3.12 / SQLite 3.49.1.
- Installed `Little Ages` service was Running / Automatic / LocalService,
  PID 37876, at `C:\Program Files\LittleAges\LittleAges.Server.exe`.
- Installed configuration: data `C:\ProgramData\LittleAges\worlds`,
  world `default-world`, seed `20260918`, LAN `http://0.0.0.0:5274`, 10 min/s.
- No installed binaries, configuration, service settings, or canonical data
  were changed to establish this baseline.

### Protected real-world evidence

A read-only SQLite connection and SQLite's online backup API captured the live
committed world at minute **208200**, rules `m8-rng1-balance1`, on
2026-09-19 at 21:40 UTC. Source and backup `PRAGMA integrity_check` returned
`ok`; backup `foreign_key_check` returned no violations. The service remained
running. Configuration and ownership marker were copied alongside the backup.
Only further disposable copies will be advanced or used for failure testing.

Local evidence: `artifacts/pre-v02/real-world-backup/evidence.json` and
`artifacts/pre-v02/installed-status-baseline.json`. The configuration SHA-256 is
`a88192b57aee35eb734f4d6615964006e1b85f23dd35918d3cf97b30e591c571`.
These ignored artifacts contain machine-specific evidence and are not shipped.

## Validation ledger

| Gate | Result |
| --- | --- |
| Baseline locked solution restore | Passed |
| Baseline Release build | Passed, zero warnings/errors |
| Baseline backend non-Long suites | Passed: 432 tests (26 domain, 202 simulation, 138 persistence, 55 integration, 11 headless) |
| Baseline frontend install/lint/typecheck/tests/build | Passed: 139 tests |
| Seed-42 100-year acceptance, year-37 checkpoint | Running |
| Final committed-state validation | Pending |

## Findings

### D1 — MUST FIX — Service executable quoting (fixed)

Windows PowerShell 5.1 removes embedded quotes when the installer invokes
`sc.exe` with a quoted executable string. A native argument probe reproduced
the loss; the real installation's registry ImagePath also has no quotes.
The old test intercepted the PowerShell argument array before native conversion
and therefore could not detect this failure.

`scripts/install-windows.ps1` now sends the command line as a structured
`Win32_Service.Change` argument, checks its return code, and preserves the
complete original command line during rollback (including arguments).
`scripts/test-install-windows.ps1` checks the OS API boundary, account,
startup mode, rollback arguments, and failure propagation. Both PowerShell
7.4.20 and Windows PowerShell 5.1 fixtures pass. No canonical contracts change.

The real service was not reconfigured. This session is not elevated; actual
privileged service registration remains a separate validation limitation.

### D2 — MUST FIX — Service/installation ownership mismatch (fixed)

The fixed service name and directory marker checks did not establish that the
registered executable belongs to the chosen installation directory. Selecting
another missing/owned directory could stop/reconfigure or uninstall the real
service. Both deployment scripts now reject a mismatched or unreadable
registration before service mutation. Tests cover quoted/unquoted legacy paths,
arguments, wrong installations, executable suffix tricks, and missing metadata.
PowerShell 7 and 5.1 fixtures pass. The runbook also now accurately describes
the existing LAN-preservation behavior on upgrade.

Audit coverage and final validation remain in progress. An ordinary green test
suite alone is not a certification decision.

### P1 — MUST FIX — Orphaned history mistaken for an empty world (fixed)

Empty-world detection checked M0–M5 tables but omitted all six M6 history
tables. A database containing only history state returned `HasCheckpoint=false`
and could have its state overwritten by fresh-world initialization. Opening
and empty detection now reject every canonical row group without metadata.
The disposable history-state regression failed before the fix and passes after it.

### P2 — MUST FIX — Checkpoint success despite changed immutable rows (fixed)

Incremental M6/M8 checkpoints retained immutable map/resource rows and checked
only their counts. Changing a valid-range elevation or regeneration value
between open and checkpoint produced a successful commit that failed on reload.
Checkpoint now compares every retained immutable field and rejects missing
rows before commit. Both injected-corruption regressions failed before the fix
and pass afterward, with previous checkpoint metadata retained and corruption
left intact for investigation. These changes reject invalid data; they do not
change simulation decisions, valid saves, migrations, or goldens.

All 141 persistence tests pass after P1/P2. A disposable copy of the protected
real-world backup also passed 120 advance/checkpoint/close/reopen cycles of
360 minutes each, reaching minute 251400. Its history fingerprint exactly
matches an uninterrupted 30-day continuation:
`881b315bcc65a6f0ab964e39d252aab595f4446af77d2ecc816e35120645c907`.
Average checkpoint time was 262 ms, maximum 978 ms; final database 1,363,968
bytes. Evidence: `artifacts/pre-v02/continuation/evidence.json`.

### F1 — MUST FIX — Initial map failure never retried (fixed)

If the first map request failed but SignalR connected, the observer could
remain without a scene indefinitely. Stream frames omit the immutable map;
the fallback timer was suppressed by a healthy connection. `App.tsx` now
retries until a map succeeds, independently of stream health, then stops
fetching it. The new component regression failed before the fix and passes
afterward. A packaged-server browser test injected a first-request HTTP 503,
observed the retry, and verified a usable scene and cleared error.

### F2 — MUST FIX — Failed scene chunk blanks the observer (fixed)

A failed lazy scene import escaped Suspense and removed the entire app,
including records and operational controls; its preload also produced an
unhandled rejection. `SceneLoadBoundary.tsx` retains a 2D map and the rest
of the observer, and preloading now handles rejection. A lazy-rejection
regression and a browser test that aborts the scene chunk both pass.

Frontend lint, typecheck, all 141 tests, and production build pass after both
fixes. Browser checks cover desktop 1440x900, tablet 1024x768, mobile 390x844,
reduced motion, selection/follow, 2D/3D switching, all records tabs, map/chunk
failures, model failure, and WebGL context loss/retry. Model failure correctly
falls back to 2D; React Three Fiber deliberately reports its caught error as
a browser error event. That exact injected diagnostic is recorded, not hidden.
Other scenarios produced no uncaught browser errors. Screenshots were captured
and desktop/mobile inspected. These headless measurements do not certify a
foreground hardware 45 FPS target.
