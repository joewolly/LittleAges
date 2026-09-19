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
| Seed-42 100-year acceptance, year-37 checkpoint | Running from isolated runtime with P1/P2 |
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

### S1 — MUST FIX — Cross-site browser requests can change operational state (fixed)

The unauthenticated pause/resume routes accepted simple cross-site POSTs;
absence of CORS response headers prevents reading responses but does not
prevent those mutations. The regression reproduced HTTP 200 with a foreign
Origin. A narrow control-route check now rejects foreign, opaque, malformed,
or multiple origins with HTTP 403 before enqueueing commands. Same-origin
requests (including explicit default ports) and existing non-browser clients
without Origin remain compatible. Tests verify all three controls, rejection
without state changes, and successful same-origin/control requests. This is
not LAN authentication; the documented trusted-network boundary remains.

## Broader audit coverage

### S2 — MUST FIX — Inherited directory permission permits planted world files (fixed)

The installed database file was read-only to ordinary Users, but its directory
inherited ProgramData's Users Write grant, including create-file access.
This permits unwanted world or SQLite sidecar files despite the database's
own ACL. The installer now protects the directory ACL from parent inheritance,
preserves existing grants for other principals and ownership, replaces Users
grants with ReadAndExecute, and grants LocalService Modify by stable SID.
The directory must be dedicated to world data as documented.

`scripts/test-data-directory-acl.ps1` reproduces the inherited grant on a
disposable directory and failed before the correction. It runs the real
ACL helper/native utility, verifies removal of Users write rights, retained
LocalService access to the existing file, unchanged owner/content, and an
idempotent second invocation. It passes in PowerShell 7 and Windows PowerShell
5.1 and is included by the installer test script. No real installation ACL
was modified. This also avoids relying solely on mocked permission checks.

### F3 — MUST FIX — Worker terrain upload exceeds allocated GPU texture dimensions (fixed)

Real browser console capture during repeated scene remounts reported
`GL_INVALID_VALUE: glTexSubImage2DRobustANGLE: Offset overflows texture dimensions`.
The first-paint texture allocates one pixel per tile; the worker later supplies
four pixels per tile in each dimension. Changing its image without releasing
the earlier immutable WebGL allocation prevents that intended detailed upload.
`upgradeGroundTexture` now releases the previous GPU allocation before changing
the image and marking it for upload. The terrain also invalidates a demand
frame so reduced-motion mode displays the result without waiting for input.
Pixel generation and art assets are unchanged.

The new lifecycle regression fails before the correction and passes afterward.
All 142 frontend tests, lint, typecheck and production build pass. Ten real
browser remounts with delayed worker loading produce no allocation errors or
uncaught exceptions. Dependency Clock/shadow-mode deprecation warnings remain
separate diagnostics; the shadow implementation already falls back to PCF.

### P3 — MUST FIX — Later same-minute partner death invalidates an existing memory (fixed)

The 100-year acceptance attempt failed while constructing a persistence snapshot:
`M6 memory does not match its historical event`. A minimal M6/M8 reproduction
kills two partnered citizens sequentially within one minute. The second citizen
correctly remembers the first death, but timestamp-only snapshot validation then
incorrectly rejects that memory after the recipient's later death.

Validation now uses the existing append-only live death-event ordering to prove
that the recipient was alive when the memory formed. Equal timestamps alone do
not suffice. A forged memory attributed to the citizen who died first remains
rejected. Simulation decisions, emitted events/memories, IDs, and fingerprints
are unchanged; no migration, historical edits or golden regeneration is needed.
Both rules versions pass snapshot round trips and SQLite close/reopen/continuation
regressions. The 100-year acceptance rerun is in progress with this correction.

## Broader audit coverage
| Area | Review and evidence |
| --- | --- |
| Build/dependencies/CI | Pinned SDK, locked portable restores, warnings-as-errors, project boundaries, fast/Long workflow separation, Windows publish/package scripts reviewed. NuGet including transitives and npm including development dependencies report zero known vulnerabilities on 2026-09-19. |
| Determinism | Stateless domain/key-derived RNG, complete event-order tuple, integer path costs/ties, ordered social/housing/lifecycle transitions, invariant serialization, counters and locked fingerprint tests reviewed. Only snapshot validation changed (P3); no simulation transitions, goldens, balances, or rules versions changed. |
| M0–M8 simulation | Generation, decisions, resources, survival, construction/storage, shelter, household reconciliation, partnerships/birth/death cleanup, history/statistics and their snapshot invariants reviewed against existing adversarial and compatibility tests. The earlier M8 housing regression remains covered. |
| Persistence/migration | Transactions/rollback, retained history prefixes, migration chain, malformed/partial databases, retry/fault semantics and save/reload tests reviewed. P1/P2 close two concrete integrity gaps. All 141 persistence tests pass. |
| Host/lifecycle | Bounded 32-command single-reader queue; operational driver awaits its one outstanding advance; checkpoints capture on the same owner; immutable publication and final-checkpoint failure propagation reviewed. Integration tests cover cancellation, shutdown, retries and faulted health. |
| REST/SignalR | Observer-only hub, latest-only notifications, stream sequence/generation fences, reconnect resets, immutable projections, ID/query bounds and static/API route separation reviewed. Live browser and stream checks at 1/5/10/50 min/s verify ordered frames, operational controls, and stable paused time. |
| Frontend | Real packaged-server scenarios cover layouts, camera/follow, records, fallback/retry, reduced motion, failed map/chunk/model, context loss and reconnect. Existing movement/presentation-clock tests retain their locked authority boundary. |
| Deployment/security | Package contents and scripts reviewed; PowerShell 5.1/7 fixtures pass. Read-only installed inspection confirms LocalService, Automatic startup, Program Files write protection, database write access restricted to LocalService/admin/system, and firewall Private/LocalSubnet scope. D1/D2 and S1 address concrete issues. |
| Performance | Copied-world continuation/checkpoint timings, 250/500-citizen deterministic scale tests and live frame measurements executed. The stream averaged about 35 KB/frame for the 18-living-citizen copied world. Final 100-year evidence remains pending. |
| Test quality/architecture | The old service-quoting mock was a false assurance at the wrong boundary; replaced with structured OS-call assertions. New regressions reproduce each integrity/bootstrap/control defect. No skipped tests in completed gates. Canonical ownership remains in the engine/host, with persistence validation and observational frontend boundaries preserved. |

## Packaged runtime and failure evidence

The final implementation package from `3c35786` contains the self-contained
server, built frontend/assets and deployment scripts. Size **54,074,947 bytes**;
SHA-256 `f8725fd81a094f46f44bb6144560721ab08261a863c544537c08dbcb2f033e86`.
The local filename contains `pre-v02-certified`; that filename is not itself
a certification result. Packaging first tries copied checked-in RID lock
graphs, then transparently generates disposable win-x64 graphs when missing
and revalidates those in locked mode. Checked-in lockfiles did not change.

Fresh package extraction was run on loopback port 5375 against another
disposable copy of the protected world. All eight browser scenarios were
repeated against that package. At 1/5/10/50 min/s, five-second observations
advanced 5/25/48/200 minutes respectively; this fixture intentionally used a
much more frequent 20-minute checkpoint interval. Slower processing did not
create queued catch-up after pause. Stream sequence/revision/time remained
ordered and there were no unexpected browser errors.

A separate packaged process on port 5376 was killed 0, 50 and 180 ms after
the checkpoint-start log. Each attempt retained the previous committed
minute 208200, passed SQLite integrity and foreign-key checks, reopened
Healthy/paused at exactly that minute and reconnected the existing browser.
This proves those interrupted transactions recovered; it does not claim
every possible OS/power-loss timing was exercised.

A fresh seed-42 packaged world on port 5377 initialized successfully,
advanced, and wrote its final checkpoint at minute 301 after console Ctrl+C.
Reopening paused resumed exactly at minute 301. The PTY wrapper reports exit
1 for Ctrl+C, so success is based on the final-checkpoint log, persisted
integrity and restart observation, not that wrapper exit code.

Local evidence includes `browser-final-results.json`, `live-controls.json`,
`crash-restart.json`, `fresh-shutdown-integrity.json`,
`fresh-status-after-restart.json` and the test TRX files under
`artifacts/pre-v02`. Screenshots are under this task's Codex visualization
directory and are machine-local, not fabricated application mockups.

## Validation limits and accepted boundaries

- **ACCEPTED — privileged deployment tests:** this session is not elevated.
  Actual install/upgrade/rollback/firewall/installed-directory ACL mutation and uninstall were not
  executed. Fixtures, source review, packaging, disposable executable runs and
  real disposable-directory ACL tests and read-only inspection were executed. The real service still has its earlier
  unquoted registration; a future elevated upgrade with the fixed installer
  will correct it. No claim is made that this session repaired the installation.
- **ACCEPTED — hardware/disruptive checks:** foreground hardware 45 FPS,
  reboot, suspend/resume, and physical power loss were not executed. Headless
  Chrome measured about 32 FPS under the audit workload. No hardware target
  is certified from that result; manual disruptive operations were excluded
  to protect the user's active machine.
- **ACCEPTED — trust and growth:** v0.1 remains a local/trusted-LAN application
  without authentication or TLS. History intentionally grows append-only;
  this pass does not add retention or change historical facts. Canonical
  versioning/goldens must remain explicit boundaries for future development.

### Superseded or unsuccessful validation attempts

- The first 100-year run was interrupted before completion because it used
  build outputs needed by later compilation. It is not counted as passed;
  the replacement uses a separate copied runtime containing P1/P2.
- Two solution build attempts encountered Windows copy locks held by the
  running acceptance/test process. These were harness scheduling failures;
  final build must be retried after those tests exit.
- Browser tooling used installed Chrome with the bundled Playwright/Node
  runtime after the cached Chromium launch failed. No browser plugin was
  available. The live-control harness initially requested an unsupported
  UI speed and later read status before its pause response completed; both
  harness mistakes were corrected before the successful 1/5/10/50 run.
- An exploratory console restart used the repository working directory and
  correctly warned that its `wwwroot` was absent there. Packaged UI checks
  use the package directory; Windows service hosting sets its content root.

