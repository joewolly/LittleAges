# Performance and saved-world recovery pass — 2026-09-19

This records local validation of the changes made after `a802d9a`. It is not a
new release or a replacement for the historical acceptance reports.

## Findings and changes

- The installed service repeatedly failed while checkpointing at world minute
  177600: `Unhoused M5 household members cannot retain individual homes.` The
  saved world uses M8 rules. Settlement demand was assigning individual homes
  after household reconciliation; M8 now reconciles the complete household at
  that boundary. M5/M6 retain their locked replay behavior and goldens.
- Operational advancement ran in one-second bursts. The driver now accumulates
  time every 100 ms, with at most one outstanding operational advance. Commands,
  checkpoint retries, pause semantics, and canonical ownership remain on the
  existing single-writer path; there is no wall-time catch-up.
- A refreshed route replaced the first segment's departure with the observation
  time. The additive observer field `segmentStartedMinute` preserves the actual
  departure. A shared, bounded presentation clock drives interpolation and
  camera following; React no longer overwrites animated positions each frame.
- Resting residents use a presentation-only doorway endpoint and hide indoors.
  Selection and records remain available. Canonical locations are unchanged.
- Scene code and model downloads start alongside bootstrap requests. Secondary
  ledger requests do not hold up the scene. The full terrain texture is produced
  in a worker after a small initial texture. Static scene objects, immutable map
  data, and world summaries are reused; history link projection uses lookups.
- Responses use compression; hashed JavaScript/CSS receive immutable caching.
  HTML and unversioned model files revalidate. Stream subscriptions reject old
  callbacks and duplicate frames, and accept reset revisions after reconnect.

## Executed validation

- Locked solution restore and Release build: zero warnings and errors.
- Backend `Category!=Long`: Domain 26, Simulation 202, Persistence 138,
  Integration 55, Headless 11 passed (432 total). Older deterministic goldens
  were not regenerated.
- Frontend: typecheck, lint, build, and all 139 tests passed.
- The actual saved-world copy advanced from minute 177240 to 188040, checkpointing
  and reopening every 360 minutes. All 30 cycles passed. Its final history
  fingerprint matched uninterrupted continuation:
  `f465ce01cc66e42ba76084a3b63461660a719f6b3a826e4a22047359a5298d9b`.
- The new M8 regression was also run against the original installed simulation
  binary; it reproduced the same checkpoint exception. It passes with the fix.
- A self-contained Windows package was tested against a separate saved-world
  copy. Packaging used the existing disposable RID-lock fallback, then validated
  those locks. Checked-in package locks were unchanged.
- Real SignalR samples at 1, 5, and 10 min/s each delivered 40 frames in four
  seconds. Observed advances were 4, 20, and 39 minutes, with no jump larger than
  one minute; median advance intervals were 1001, 202, and 95 ms. Pause held the
  world minute constant.
- Browser checks covered loading, connection status, rotation, follow, records,
  the `Resting indoors` state, and recovery after an intentional test-server
  restart. Only expected disconnect errors appeared during that restart.

## Delivery measurements

Measured HTTP body sizes on the same machine with `Accept-Encoding: br,gzip`:

| Resource | Previous installation | Updated installation | Reduction |
| --- | ---: | ---: | ---: |
| Scene JavaScript | 1,019,400 bytes | 357,560 bytes | 65% |
| Main JavaScript | 320,285 bytes | 117,783 bytes | 63% |
| Map JSON | 594,290 bytes | 131,393 bytes | 78% |
| Villager model | 103,140 bytes | 74,922 bytes | 27% |

The `?diagnostics` overlay reports frame p95 and the time to the first
frame after model loading. One embedded-browser load reported 1.27 seconds.
This is a single local sample, not a cold-load benchmark.

Screenshot capture was unavailable in the embedded browser. It reported roughly
one frame per second even while marked visible, so this session does **not**
establish a foreground-browser FPS target or visually certify smooth animation.
Route continuity is covered by regressions and real server-stream measurements;
the user subsequently confirmed **movement looks smooth** in their usual browser
against the updated installation. Numeric foreground GPU/frame-time acceptance
was not measured.

## Installed service

The existing installation and complete stopped world directory were backed up
before replacement. The backup's SQLite integrity check passed and its world
minute was 177240. Configuration was preserved, including the original seed,
active world, LAN binding, and 10 min/s speed. No world reset or migration was
performed. The installed service resumed and checkpointed past minute 177600.

An online backup of live checkpoint 181920 passed SQLite integrity validation
and matched deterministic continuation from the original saved world:
`2f7d37099959fde8a4c3956bba55784d2207198aee38f2d0ce81e013874f422a`.
All 380 packaged files checked against the installed copies matched; the
original configuration hash was unchanged.

The live service passed a 16.4-minute observation window with 31 healthy samples,
zero checkpoint failures, and the same process throughout. The final read was
minute 186989 with checkpoint 186960. Windows recorded no new Little Ages .NET
Runtime or Application Error crash events during that window. The service
remains running automatically at the original LAN address and configured speed.
