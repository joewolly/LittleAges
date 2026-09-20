# Growing Settlement implementation evidence

This document records the three sequential local delivery gates. Publication,
installation, and modifications to the installed civilization are outside this work.

## Stage 1: sustainable generations

Candidate rules: `m9-rng1-growth1`. The explicit compatibility rules are
`m8-rng1-balance1`; the default remains M8 while development is in progress.
Fresh worlds can select growth with `--NewWorldRules m9-rng1-growth1`.
An existing database retains its own rules regardless of that option.

### Diagnosis and changes

The same generated maps, twenty founders, calendar, mortality, food consumption,
birth probability, health requirements, two-year birth spacing, and shelter sizes
are used in the baseline and growth comparisons. There is no immigration,
population floor, or resource injection. The `diagnose` headless command records
monthly age groups, mortality causes, partnership state, food, housing, and
overlapping reproduction blockers without changing simulation state.

M8's early losses come from exposure and deprivation. Fixed food targets do not
follow population; gathering into nearly full storage can leave workers waiting
to deposit instead of eating. Growth decision scoring prioritizes urgent meals,
rest, shelter construction, productive gathering, and social opportunities.
Prospective gathering accounts for workers already outbound and goods carried.
Citizens idle locally when needs and supplies are satisfied, avoiding needless
long trips. Food gathering targets 60 units per living citizen; ordinary wood and
stone reserves target 80 and 40, with construction deficits taking precedence.
Actual gathering still removes resources from finite regenerating nodes.

Every founder has a household. Unpartnered adults leave their parents' household;
new partnerships bring dependent children into a new household when the combined
size allows it. A surviving partner becomes eligible for a new relationship.
The deceased retains their partner-at-death reference, while immutable formation
and death records validate all transitions. Close kin means direct ancestry at
any depth or a shared ancestor within two generations of each person. First
cousins are excluded; second cousins can form partnerships. Legacy kinship and
partner-reference behavior remain unchanged.

The population outlook is available under Observer records → Citizens. It shows
births, deaths by cause, unpartnered adults, unhoused residents, food targets, and
the current blockers for active households. It is read-only and fetched only
while expanded. A ready household is eligible for the existing probabilistic
daily opportunity, not guaranteed a birth.

### Matched ten-year comparison

| Seed | M8 living | Growth living | M8 births | Growth births | M8 deprivation / exposure deaths | Growth deaths |
|---|---:|---:|---:|---:|---:|---:|
| 7 | 12 | 31 | 6 | 11 | 2 / 12 | 0 |
| 42 | 10 | 33 | 3 | 13 | 6 / 7 | 0 |
| 123 | 14 | 33 | 4 | 13 | 3 / 7 | 0 |
| 1001 | 11 | 29 | 1 | 9 | 7 / 3 | 0 |
| 20260918 | 9 | 36 | 2 | 16 | 10 / 3 | 0 |

All growth citizens are housed at year ten. Growth food holdings are respectively
1880, 2227, 2065, 1851, and 2168. Growth adult populations are twenty in each
world, with 20, 18, 18, 18, and 20 partnered adults. The remaining populations
are children. At that boundary, reproductive age and household/dwelling capacity
are the main blockers; no growth household is food-blocked. Eligibility varies
over time, and a zero ready count at this single boundary does not imply that
there were no opportunities during the preceding year. In M8, a partner reference
may point to a deceased person, so its raw partnered count is not a count of
currently viable pairs.

Raw monthly reports are under `artifacts/v02/baseline` and
`artifacts/v02/growth-trial4-ten`; the compact comparison is
`artifacts/v02/stage1-ten-year-comparison.json`. These are local ignored evidence.

### Validation status

Release build passes with zero warnings/errors. Backend non-long suite: 448
tests passed before the additional acceptance-validator regression; that new
regression also passes. Frontend lint, typecheck, 148 tests, and production build
pass. Existing Three deprecation and large-bundle notices remain.

The first seed-42 century comparison reached 40 living citizens, 54 births, 34
deaths, and ancestry depth 5. Uninterrupted and real SQLite reopen at year 37
were canonically identical. Its reporting gate exposed an old symmetric-partner
assumption for deceased citizens; the validator now distinguishes current living
partnerships from the historical partner-at-death reference. A focused regression
also proves that broken living partnerships are rejected. The corrected century
matrix passes every mandatory invariant:

| Seed | Living at 100 | Maximum ancestry depth |
|---|---:|---:|
| 7 | 27 | 5 |
| 42 | 40 | 5 |
| 123 | 70 | 5 |
| 1001 | 30 | 5 |
| 20260918 | 46 | 5 |

The seed-42 year-37 real SQLite reopen is exactly equivalent, with both canonical
snapshot fingerprints
`6555e10206f9d277872008280c09161e2ad7a3d7412d7fdba6c1142550f4c70b`.
Reports are in `artifacts/v02/growth-stage1-century/seed-<seed>`.
This completes Stage 1's survival/continuing-generations gate. The final v0.2
population target remains open: the current century median is 40, below 50–200.

A local self-contained Windows preview package was produced as
`artifacts/v02/stage1-package/LittleAges-v0.2.0-growth-preview-win-x64.zip`.
Its SHA-256 is
`f36eb48dad107bbe5e96b1005857d5983dc7edebd71d6d0dcc6173e2ca75f772`.
Packaged desktop (1440×1000) and mobile (390×844) checks pass for the population
panel, no horizontal overflow, pause/resume, supported speed changes, reduced
motion, 2D fallback, and recovery after a deliberately closed WebSocket and
offline interval. Screenshots were inspected. No application errors remained;
expected injected-offline errors and the pre-existing missing favicon are
recorded separately in `growth-packaged-qa.json` in the task visualization folder.
Browser plugin was unavailable; Playwright drove installed Chrome.

## Stage 2: farming and seasonal reserves

Implemented as `m10-rng1-agriculture1`; M9 remains frozen. One grain crop produces
Food. Suitable land is buildable, reachable, free of resource nodes, and has
fertility >= 3000 and water access >= 2000. Planting requires 1200 work and tending
2400, in shifts of 100. Autumn harvest shifts collect at most 40 Food and carry it
back to the communal depot. Winter closes the harvest; uncollected food is lost.
Potential yield is `3000 + (fertility + water) * 9000 / 20000`; actual yield scales
by planting completion and a tending multiplier from 25% to 100%, using integer
arithmetic. No weather, spontaneous food, or population additions are used.

A farm costs 60 Wood, 10 Stone, and 900 construction work. A granary costs 100 Wood,
60 Stone, and 1500 work and adds 10000 dedicated Food capacity. Storage is pooled
at the communal depot; granaries do not hold separate per-building inventories.
Non-food goods cannot occupy dedicated granary capacity. Construction targets
90 winter days at 9 Food per citizen per day and crop productive capacity of
1080 Food per citizen per year, leaving gathering useful throughout the year.
Food coverage is a projection, not a guarantee of adequate labor or harvests.

The ten-year seed-42 acceptance run has 32 living citizens, 12 births, and no
deaths. A real SQLite close/reopen at year 1 is canonically identical to the
uninterrupted result, including crops, work, harvest records, and pending hauling.
See `artifacts/v02/agriculture-acceptance/`. Focused reopen tests stop during actual
planting and harvest transport. A labor-loss fixture removes tending work before
autumn and demonstrates a smaller harvest followed by autonomous recovery the
next year; it adds no resources or citizens. Daily snapshot validation over a
year covers the full cycle. Harvest accounting requires yield = harvested + lost.

Release build passes with zero warnings. Backend checks pass: Domain 26,
Simulation 211, Persistence 148, Integration 58, Headless 12. The migration test's
expected schema list was updated and rechecked alongside both new reopen tests.
Frontend lint, typecheck, production build, and all 150 tests pass; one App test
timed out under concurrent backend load and its entire 23-test file passed on
recheck. A disposable copy of the M9 year-37 database opens under the M10 package
without changing any pre-existing table contents, adding agriculture state, or
violating foreign keys (`artifacts/v02/legacy-copy/result.json`). Its saved M9
rules remain authoritative even when the fresh-world option requests M10.

The local Windows package is
`artifacts/v02/stage2-package/LittleAges-v0.2.0-agriculture-preview-win-x64.zip`,
54116890 bytes, SHA-256
`c1d971add7dd8e5e51342634d231c8a208d416537f14f51605ee419f4700b5e7`.
Packaged desktop/mobile farming panels, production/consumption chart, harvest
records, controls, live updates, reconnect, reduced motion, and 2D fallback pass
with no horizontal overflow or application errors. Expected injected-offline
errors and the pre-existing favicon 404 are recorded separately. Screenshots and
browser results are in the task visualization directory under `agriculture-*`.

A new-rules-only fix releases citizens from WaitingForStorage after depositing
their last carried unit. The original behavior could strand an empty-handed
worker until death. M8 and M9 behavior remains unchanged.

## Stage 3: occupations, ownership, and physical barter

Pending the Stage 2 gate. Final century targets, conservation scenarios, focused
transaction reopen checks, and performance evidence remain required.
