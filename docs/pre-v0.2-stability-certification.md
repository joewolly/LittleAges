# Pre-v0.2 stability certification

Status: **CERTIFIED — READY FOR POST-v0.1 FEATURE DEVELOPMENT**.

Completed on 2026-09-19. One BLOCKER and ten MUST FIX findings were identified;
all eleven are fixed. No known blocking or must-fix defect remains. This
conclusion includes the final 100-year acceptance comparison, the complete
required local suites, packaged application checks and copied-world recovery
evidence below. The accepted validation limits remain explicit.

Scope: the audited repository implementation and generated Windows package.
The existing installed deployment was inspected and backed up read-only, but
was not upgraded. Its original running service continued normal simulation;
the audit did not stop it, write its world, alter its configuration or ACLs,
or use it for fault injection. Deployment fixes require a later elevated upgrade.

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
Only further disposable copies were advanced or used for failure testing.

Local evidence: `artifacts/pre-v02/real-world-backup/evidence.json` and
`artifacts/pre-v02/installed-status-baseline.json`. The configuration SHA-256 is
`a88192b57aee35eb734f4d6615964006e1b85f23dd35918d3cf97b30e591c571`.
These ignored artifacts contain machine-specific evidence and are not shipped.

The final read-only preservation check at 2026-09-20 00:23 UTC found the same
service process Running and Healthy, at minute 306398, with zero consecutive
checkpoint failures. The configuration hash above and the protected backup
SHA-256 `422da4fbdd79af63b03c92ebb01d6e04c95bc9cbc0a972be85d2df3ef2bc588a`
were unchanged. The live world naturally advanced during the audit; its bytes
were not expected to remain static. Evidence: `real-install-preservation-final.json`.

## Validation ledger

| Gate | Result |
| --- | --- |
| Baseline locked solution restore | Passed |
| Baseline Release build | Passed, zero warnings/errors |
| Baseline backend non-Long suites | Passed: 432 tests (26 domain, 202 simulation, 138 persistence, 55 integration, 11 headless) |
| Baseline frontend install/lint/typecheck/tests/build | Passed: 139 tests |
| Corrected locked restore / full Release Rebuild | Passed, zero warnings/errors |
| Corrected frontend lint/typecheck/tests/build | Passed: 145 tests in 10 files |
| Corrected domain / simulation / persistence suites | Passed: 26 / 204 / 143 tests |
| Corrected integration / headless suites | Passed: 56 / 11 tests; total corrected backend non-Long 440 |
| Extended suites | Six Long tests passed; corrected M6 ten-year repeat also passed (5m58s) |
| Windows PowerShell 5.1 and PowerShell 7 installer/ACL fixtures | Passed, including real disposable NTFS ACL checks |
| Final self-contained Windows package | Built and extracted; installer and frontend hashes match source outputs |
| Final packaged browser checks | Eight scenarios passed; one expected injected model-load error, zero unexpected page errors |
| Renderer retention | 80 remounts: two retained contexts, bounded heap after warmup, no texture allocation errors |
| Seed-42 100-year acceptance, year-37 checkpoint | Passed: 23 invariant/equivalence checks; exact matching snapshots/history; no mismatches |
| Complete Long set on the final rebuild | Passed: four simulation Long tests (26m16s), two 250/500-citizen scale tests |
| Final implementation validation | Passed: fresh locked restore, full Release Rebuild, 440 non-Long + six Long backend tests, 145 frontend tests, package/browser/continuation gates |

The acceptance executable runs from a separate directory to avoid Windows
build-output locks. Its source includes P3; later implementation changes are
frontend-only. Comparison with the final full rebuild found identical method
bodies/signatures in Domain (876), Simulation (1449), Persistence (1171) and
Headless (625). File hashes differ across builds because assembly metadata
includes the Git revision. Evidence is recorded in the local `acceptance-*-il`
and runtime-identity artifacts; no running binary was replaced.

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

The final implementation repeated this test with the same exact fingerprint and database size. Its 120 cycles took 152.38 seconds, averaging 305 ms per checkpoint (maximum 1005 ms). Evidence: `artifacts/pre-v02/continuation-history-fixed/evidence.json`.

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

A final packaged-runtime probe passed 22 requests covering uppercase,
trailing-slash and encoded route aliases; foreign, opaque, malformed and
duplicate Origin headers; and successful same-origin controls. The disposable
world remained paused at minute 208200 with speed zero. Evidence:
`artifacts/pre-v02/control-route-probe.json`.

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

### P3 — BLOCKER — Later same-minute partner death invalidates an existing memory (fixed)

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
The natural seed-42 M8 replay independently confirms the defect at minute **3,596,400**: citizen 19 retains a partner-death memory for event **220**, then dies in later event **222** in the same minute. Its ten-year snapshot passes corrected validation and preserves history fingerprint `fc3e706f0176f9973c0a338ba16abedf1a98406516ae8f54548b5becb57ffcb7`. Snapshots at years 10, 20, 30 and 37 all validated. Year 37 reached minute 19,180,800 with history fingerprint `b5d228ca57599aaa42fe295f82f45e8369ed060d2eec1ef7f9b6973aaecb67e5`. Evidence: `artifacts/pre-v02/memory-diagnostic.log`.

Both rules versions pass snapshot round trips and SQLite close/reopen/continuation
regressions. The corrected 100-year acceptance rerun passed all 23 checks and
exact canonical save/reload equivalence, with no mismatches.

### F4 — MUST FIX — Repeated 3D remounts retain retired WebGL contexts (fixed)

Heap snapshots after 40 ordinary 2D/3D toggles showed 41 native WebGL contexts
and 43 canvas objects, versus one context and three canvas objects initially.
Retaining paths led through cached GLTF textures/materials and Three r186's
global DFG lighting lookup texture. Calling renderer disposal alone did not
release those shared allocations; this was not merely DevTools/JIT retention.

The Canvas now tracks shared asset GPU resources and the late-bound lighting
uniform, and releases them on retirement. Decoded models, pixels and geometry
remain cached for retry; canonical data and art remain unchanged. Unit coverage
checks deduplication, idempotence, late uniform assignment and registry isolation.

With the fix, ten and eighty remounts both retain only two native contexts and
four canvas objects (the active scene and one retained predecessor), rather than
one additional context per toggle. After warmup, collected heap levels flatten:
20/40/60/80 remounts used 20.95/21.70/21.98/21.82 MB. No uncaught errors or
texture allocation errors occurred. Evidence: `renderer-80.json`, before/after
heap snapshots and `stable-comparison.txt` under `artifacts/pre-v02`.
All 145 frontend tests, lint, typecheck and production build pass.

A ten-minute continuous observer soak against the final package at 10 min/s advanced a disposable copied world from minute 208200 to 214180. Persistence remained Healthy throughout, with zero page errors and zero WebGL errors. After warmup, collected heap stayed near 20 MB (20.17 MB at two minutes; 20.25 MB after pause). DOM node and listener counts remained bounded. Evidence: `observer-soak.json`.
## Broader audit coverage

Finding totals: **one BLOCKER and ten MUST FIX, all eleven fixed**.
The final required local gates passed. No canonical goldens,
balances, migrations, emitted history or simulation transitions were changed.

| Finding | Changed implementation / regression evidence | Commit |
| --- | --- | --- |
| D1/D2 | `scripts/install-windows.ps1`, `scripts/uninstall-windows.ps1`; `scripts/test-install-windows.ps1` | `4581d0a` |
| P1/P2 | `src/LittleAges.Persistence/WorldCheckpointStore.cs`; `tests/LittleAges.Persistence.Tests/CheckpointSafetyTests.cs`, `M6HistoryPersistenceTests.cs` | `f2215b9` |
| F1/F2 | `src/LittleAges.Web/src/App.tsx`, `src/LittleAges.Web/src/world/SceneLoadBoundary.tsx`; component and failed-request browser checks | `2e9e61d` |
| S1 | `src/LittleAges.Server/Program.cs`; cross-origin control integration tests | `3c35786` |
| S2 | `scripts/install-windows.ps1`; `scripts/test-data-directory-acl.ps1` | `b1e069c` |
| F3 | `src/LittleAges.Web/src/world/terrainArt.ts`, `WorldViewport.tsx`; terrain lifecycle test and real browser console | `ba0c474` |
| P3 | Snapshot validation in `src/LittleAges.Simulation/SimulationEngine.cs`; `tests/LittleAges.Simulation.Tests/M6HistoryAcceptanceTests.cs`, `tests/LittleAges.Persistence.Tests/M6HistoryPersistenceTests.cs` | `58d53ca` |
| F4 | `src/LittleAges.Web/src/world/rendererResources.ts`, `WorldViewport.tsx`; resource lifecycle tests and before/after browser heaps | `fc12df1` |

| Area | Review and evidence |
| --- | --- |
| Build/dependencies/CI | Pinned SDK, locked portable restores, warnings-as-errors, project boundaries, fast/Long workflow separation, Windows publish/package scripts reviewed. NuGet including transitives and npm including development dependencies report zero known vulnerabilities on 2026-09-19. |
| Determinism | Stateless domain/key-derived RNG, complete event-order tuple, integer path costs/ties, ordered social/housing/lifecycle transitions, invariant serialization, counters and locked fingerprint tests reviewed. Only snapshot validation changed (P3); no simulation transitions, goldens, balances, or rules versions changed. |
| M0–M8 simulation | Generation, decisions, resources, survival, construction/storage, shelter, household reconciliation, partnerships/birth/death cleanup, history/statistics and their snapshot invariants reviewed against existing adversarial and compatibility tests. The earlier M8 housing regression remains covered. |
| Persistence/migration | Transactions/rollback, retained history prefixes, migration chain, malformed/partial databases, retry/fault semantics and save/reload tests reviewed. P1/P2 close two concrete integrity gaps. All 143 final persistence tests pass. |
| Host/lifecycle | Bounded 32-command single-reader queue; operational driver awaits its one outstanding advance; checkpoints capture on the same owner; immutable publication and final-checkpoint failure propagation reviewed. Integration tests cover cancellation, shutdown, retries and faulted health. |
| REST/SignalR | Observer-only hub, latest-only notifications, stream sequence/generation fences, reconnect resets, immutable projections, ID/query bounds and static/API route separation reviewed. Live browser and stream checks at 1/5/10/50 min/s verify ordered frames, operational controls, and stable paused time. |
| Frontend | Real packaged-server scenarios cover layouts, camera/follow, records, fallback/retry, reduced motion, failed map/chunk/model, context loss and reconnect. Existing movement/presentation-clock tests retain their locked authority boundary. |
| Deployment/security | Package contents and scripts reviewed; PowerShell 5.1/7 fixtures pass. Read-only installed inspection confirms LocalService, Automatic startup, Program Files write protection, database write access restricted to LocalService/admin/system, and firewall Private/LocalSubnet scope. D1/D2 and S1 address concrete issues. |
| Performance | Copied-world continuation/checkpoint timings, 250/500-citizen deterministic scale tests and live frame measurements executed. The stream averaged about 35 KB/frame for the 18-living-citizen copied world. The completed 100-year run processed 26,567,156 events at 9,076 events/second for Run A. |
| Test quality/architecture | The old service-quoting mock was a false assurance at the wrong boundary; replaced with structured OS-call assertions. New regressions reproduce each integrity/bootstrap/control defect. No skipped tests in completed gates. Canonical ownership remains in the engine/host, with persistence validation and observational frontend boundaries preserved. |

## Packaged runtime and failure evidence

The final implementation package from `fc12df1` contains the self-contained
server, built frontend/assets and deployment scripts. Size **54,076,272 bytes**;
SHA-256 `979b8d2d089beb50192b86e88444b97673d3cc56876571161c1d1624088029f8`.
Packaging first tries copied checked-in RID lock
graphs, then transparently generates disposable win-x64 graphs when missing
and revalidates those in locked mode. Checked-in lockfiles did not change.

The final package is
`artifacts/pre-v02/stability-package/LittleAges-v0.1.0-pre-v02-stability-win-x64.zip`.
Fresh extraction on port 5379 passed all eight browser scenarios against a
disposable copy paused at minute 208200. Its installer and frontend entrypoint
hashes match the final source/build outputs. Final desktop/mobile screenshots
were visually inspected. `browser-stability-results.json` records the results.

An earlier package extraction was run on loopback port 5375 against another
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

## Final 100-year acceptance and performance evidence

The corrected acceptance process exited successfully after approximately 95
minutes for both trajectories and verification. Seed 42, M8 rules and a
year-37 SQLite checkpoint reached minute **51,840,000** in both runs. All
**23** invariant/equivalence checks passed, `mandatoryInvariantsPassed` and
`acceptance.equivalent` are true, and `mismatches` is empty.

| Evidence | Final result |
| --- | --- |
| Run A / Run B snapshot fingerprint | `1328ac598f6f57e889f9a71fb396119638d145ab33c9b7cd27830a4defef8e6f` |
| Run A / Run B history fingerprint | `d55bb95a886911899519b9d6d1ed1ab40751028457ac252edfc498f4b1981eb2` |
| Living / peak / total citizens | 6 / 20 / 30 |
| Births / deaths / ancestry depth | 10 / 24 / 3 |
| Historical events / statistics / memories | 1144 / 1200 / 526 |
| Run A elapsed / scheduled events | 2,927.339 seconds / 26,567,156 |
| Run B elapsed / post-reload event counter | 2,793.550 seconds / 15,677,962 |
| Year-37 database | 1,449,984 bytes; integrity `ok`, no foreign-key violations |

Run B's event counter resets on reload; its reported elapsed time includes the
pre-checkpoint work, so its throughput is not directly comparable to Run A's.
Operational timing and report-context fingerprints are not canonical state.
The process peak working set was **1,859,637,248 bytes (1.73 GiB)** while holding
two trajectories and serializing/reloading the checkpoint. Sampled working set
later returned to about 833 MiB; this is a process-level measurement, not a
single-world memory budget or proof of unbounded history retention safety.

The older `v0.1-acceptance-report.md` records a different, earlier candidate
and different hashes. In particular, the starting `af35d56` already contains
the documented M8 household reconciliation correction. This audit preserves
that starting implementation's simulation transitions; its only simulation
source change is P3 snapshot validation. The old report remains historical
evidence, and no locked test golden was regenerated to match this run.

Exact JSON/Markdown reports and the disposable checkpoint are under
`artifacts/pre-v02/acceptance-history-fixed`. The separately checked summary is
`acceptance-verified-summary.json`; memory sampling is recorded in
`acceptance-memory-summary.json`. The runtime/final-build IL comparison above
establishes the acceptance code's provenance despite assembly revision metadata.

The final one-year seed-42 M8 benchmark completed 324,024 scheduled events in
35.862 seconds (9,035 events/second), reaching minute 518400 with all mandatory
invariants passing. Its history fingerprint is
`8c7735f587f4319eca4f7759ebc1143b4acf6aa181387232de74780421409caf`.
The result had 13 living citizens, 20 total and a peak of 20; it is a measurement
of this deterministic workload, not a claim about every population or machine.
Evidence: `artifacts/pre-v02/benchmark-one-year`.

## Validation limits and accepted boundaries

- **ACCEPTED — privileged deployment tests:** this session is not elevated.
  Actual install/upgrade/rollback/firewall/installed-directory ACL mutation and uninstall were not
  executed. Fixtures, source review, packaging, disposable executable runs and
  real disposable-directory ACL tests and read-only inspection were executed. The real service still has its earlier
  unquoted registration and inherited Users write grant on its data directory;
  these need a later elevated upgrade with the fixed installer. No claim is
  made that this session repaired the installation.
- **ACCEPTED — hardware/disruptive checks:** foreground hardware 45 FPS,
  reboot, suspend/resume, and physical power loss were not executed. Headless
  Chrome measured about 32 FPS under the audit workload. No hardware target
  is certified from that result; manual disruptive operations were excluded
  to protect the user's active machine.
- **ACCEPTED — trust and growth:** v0.1 remains a local/trusted-LAN application
  without authentication or TLS. History intentionally grows append-only;
  this pass does not add retention or change historical facts. Canonical
  versioning/goldens must remain explicit boundaries for future development.
- **ACCEPTED — validation scope:** the 500-year path was not run because no
  finding requires that horizon; the requested 100-year path and focused Long
  suites are the proportional long-horizon gates. Tablet/mobile checks use
  browser viewport emulation, not physical devices. Hosted CI was not triggered
  and no commits were pushed; all validation reported here was local.

### Superseded or unsuccessful validation attempts

- The first 100-year run was interrupted before completion because it used
  build outputs needed by later compilation. It is not counted as passed;
  the replacement uses a separate copied runtime containing P1/P2.
- That replacement failed the real P3 memory invariant and is explicitly not
  counted as passed. A further isolated run contains the P3 correction.
- Two solution build attempts encountered Windows copy locks held by the
  running acceptance/test process. These were harness scheduling failures;
  the later complete Release build passed with zero warnings/errors.
- Browser tooling used installed Chrome with the bundled Playwright/Node
  runtime after the cached Chromium launch failed. No browser plugin was
  available. The live-control harness initially requested an unsupported
  UI speed and later read status before its pause response completed; both
  harness mistakes were corrected before the successful 1/5/10/50 run.
- An exploratory console restart used the repository working directory and
  correctly warned that its `wwwroot` was absent there. Packaged UI checks
  use the package directory; Windows service hosting sets its content root.
