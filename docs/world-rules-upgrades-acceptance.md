# Automatic world upgrades acceptance — 2026-10-07

Implementation branch: `codex/existing-world-upgrades`. Validation used Windows,
.NET SDK 10.0.100, and disposable SQLite copies. The installed service and its
original databases were not upgraded. Publication and installation were not run.

## Completed checks

| Check | Result |
| --- | --- |
| Domain suite | 41 passed |
| Simulation suite, excluding long acceptance | 438 passed |
| Persistence suite | 209 passed; focused upgrade checks also passed |
| Integration suite | 96 passed, followed by the added uncertain-commit test passing |
| Headless suite, excluding long acceptance | 36 passed |
| Frontend | 275 tests passed; lint, typecheck, and production build passed |
| Release backend build | Passed with zero warnings or errors |
| Windows installer fixtures | Passed in PowerShell 7 and Windows PowerShell 5.1 |

Upgrade coverage includes every supported entry ruleset, skipped releases,
repeat startup, preservation mode, populated and extinct worlds, both settlement
counts, construction, active festivals, physical household migration and trade
cargo, and legacy farm returns. Failure checks cover backup and schema preparation,
conversion, transaction rollback, cancellation, verified committed targets, and
uncertain outcomes that must stop startup. Existing legacy and fresh-world golden
tests and ordinary-checkpoint guards passed. Older status responses remain valid
frontend input.

## Stored-world copies

Each activation retained time, map, creation metadata, citizens, buildings, and
history, then matched one day of continuation across different chunk sizes and
a SQLite checkpoint/reload. Backup integrity, foreign keys, and SHA-256 were
independently checked against the manifest and upgrade receipt.

| Source copy | Activation minute | Living residents | Buildings | History events | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| `m14-migration-20260924.db` | 273959 | 20 | 15 | 178 | M14 → M17; passed |
| `m15-roads-seed17.db` | 2851560 | 30 | 131 | 669 | M15 → M17; passed |
| `m17-newcomers-20261004-seed42.db` | 2216528 | 28 | 25 | 516 | Rules unchanged; pre-schema backup and continuation passed |

Verified backup SHA-256 values, in the same order:

```text
397b81543583b1039f3f13e8ba430b3b9783f44b66e4d411b2af11ee53a7e9b7
a2eadb38de7559ed266f0c65aafc5bdfc3c9f912c9821480764627be32c299bb
984dcba4a3ae9d0b94ee66d02ee7733bf09bc396e2f128e1d044b16d469b2265
```

## Migrated century acceptance

The long tests create an existing world under its source rules, advance to year
one, perform the registered conversion and transactional checkpoint, validate
headless invariants annually through year 100, and verify SQLite reloads at
years 50 and 100. The midpoint also compares one day of continuation across
chunk sizes. Each final checkpoint must be a conversion no-op.

| Seed | Source | Target | Result |
| --- | --- | --- | --- |
| 17 | M15 roads | M17 newcomers | Passed; 74 living residents at year 100 |
| 42 | M14 migration | M17 newcomers | Passed; 68 living residents at year 100 |

Year-100 checkpoint fingerprints, seed 17 followed by seed 42:

```text
045103ee2a0c9c78ca28b5194fda2a6101ec87f261a1f7ed0dc51e422c0a382a
782229297cf3dc790a17908a52d581d447abb8230a667fa900e03c7d4438b85f
```

Seed 42 completed its filtered test invocation in 56 minutes. The initial
sequential invocation recorded seed 17 passing after about two hours; its
redundant seed-42 case was then stopped because that seed had already passed
independently. That invocation reports an aborted run with one passed test and
zero failed tests. Both required cases completed all assertions before this
deliberate cancellation.

Run the cases separately from the repository root:

```powershell
dotnet test tests/LittleAges.Headless.Tests/LittleAges.Headless.Tests.csproj --configuration Release --filter 'FullyQualifiedName~WorldRulesUpgradeCenturyTests&DisplayName~17'
dotnet test tests/LittleAges.Headless.Tests/LittleAges.Headless.Tests.csproj --configuration Release --filter 'FullyQualifiedName~WorldRulesUpgradeCenturyTests&DisplayName~42'
```

## Observer QA

Browser automation used Playwright with installed Chrome because the Browser
plugin was unavailable. A disposable M14 world with an intentionally unavailable
backup directory produced the real server failure outcome while continuing its
old rules. No status-response mock was needed.

| Check | Result |
| --- | --- |
| Desktop, 1440 × 900 | Failure notice and rendered settlement visible |
| Mobile, 390 × 844 | Notice above map, usable map height, no horizontal overflow |
| Observer records | Opened and closed while the notice remained visible |
| Runtime diagnostics | No JavaScript errors; only the expected unavailable M14 roads response |

Session evidence is retained outside the checkout in
`%TEMP%\LittleAges-UpgradeValidation-20261007`: `upgrade-desktop.png`,
`upgrade-mobile.png`, `upgrade-records.png`, and `copied-worlds-validation.json`.
The two `LittleAges-UpgradeCentury-{seed}.log` files retain the century results.
Installing the Browser plugin would simplify repeated observer QA.

Linux CI, packaging, publication, and installed-service deployment were not run
as part of this implementation. Downgrade recovery still requires the matching
pre-upgrade database, as described in [backup and recovery](backup-and-recovery.md).
