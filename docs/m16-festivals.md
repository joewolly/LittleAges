# M16 - Harvest festivals

`m16-rng1-festivals1` inherits M15 roads, trade, migration, and unified citizen
actions, plus the [M16 planned layout](m16-planned-layout.md) (districts,
storehouses, and shared household surplus). It adds a shared harvest afternoon, family visits timed for the
gathering, factual memories, and temporary observer decorations. Existing
worlds retain their recorded rules; this is a new-world feature.

Festivals were developed on `codex/m16-festivals` against M15 and then ported
onto the planned layout. `m16-rng1-festivals1` was the v0.5.0 new-world default; it was
promoted after the seed-17 and seed-42 century acceptance runs passed, including their
year-80 SQLite continuation and living residents at both settlements.
M17 builds on these rules and is the v0.6.0 new-world default. Existing M16
worlds retain their recorded behavior, and M16 remains explicitly selectable.

## Calendar and attendance

Settlement 1 celebrates on month 9, day 1, from noon to 18:00. Settlement 2
celebrates on month 9, day 8, at the same hours. Empty sites do not celebrate.
Each inhabited site keeps its current or latest festival in canonical Living
state; older occurrences remain in append-only history.

Residents aged six and older can walk to their settlement's center. Attendance
requires hunger below 3,500, rest below 5,000, and injury and illness below
7,000. Normal cargo and work claims must be released through the existing
action machinery. Emergency food work and personal needs take priority.
Children can attend without becoming ordinary workers.

Attendance is measured in physical time at the center, in short slices. Travel
does not count. After 60 accumulated minutes, each person receives, once per
occurrence:

- 1,000 less stress and 1,500 less social need;
- a Festival experience worth +400 mood, using the normal experience
  recalculation and retention rules (at most 30 days, subject to the 24-entry
  experience limit);
- a factual `FestivalAttended` event and personal memory;
- one opportunity to strengthen a nearby attendee relationship, preferring
  relatives and partners before stable citizen identity. Each unordered pair
  is strengthened at most once per festival.

Festival shifts produce no goods, work skill, or tool wear. Interruptions retain
accumulated attendance and cannot grant benefits or a feast portion twice.
Closing cancels remaining attendance work through the normal claim-release and
decision machinery.

## Feast or modest gathering

At opening, reserve 10 communal Food per eligible resident and expected visiting
relative only if doing so leaves at least 20 Food per living resident. Otherwise
the same gathering proceeds without special food. Existing meals remain normal
needs actions.

Reserved feast Food occupies site storage and remains in the conservation
ledger. A qualified attendee consumes at most one 10-Food portion, using the
normal food-consumption statistics and hunger reduction. Closing returns every
unused portion to the same communal store. The UI reports the number of people
who qualified and Food actually shared, which can differ from the reservation.

## Family journeys

The existing deterministic relative selection and one family visit per world
per year remain. M16 evaluates visits around the harvest dates rather than at
the year boundary. A selected visitor plans departure from the route's current
travel cost plus a one-day arrival buffer. Routes requiring more than 12 days
are skipped. Cargo obligations, provisions, missing routes, death, and an
unavailable relative retain the existing journey rules.

An early arrival dwells through closing. An arrival during the festival can
attend only for the remaining time; an arrival after closing takes the normal
one-hour visit. Visitors pause for food and rest, and those intervals do not
count as attendance. The same party returns home and deposits unused provisions.
No extra visiting population is created.

Closing accounts for the final attendance interval before a visitor starts
home. A 17:00 arrival can accumulate the required hour; a 17:01 arrival has
only 59 festival minutes, even if its ordinary one-hour dwell ends after 18:00.

## Saves, history, and observation

Festival schedules, food reservations, per-person attendance and reward flags,
normalized relationship pairs, and the selected visit plan are persisted in
optional Living JSON fields. Earlier rules omit those fields. M16 validation
checks canonical ordering, identities, times, attendance eligibility, work claims,
and feast conservation. New event values are appended as 29-31; the attendance
memory is value 7. Existing enum values stay unchanged.

The additive `M16FestivalHistory` SQLite migration expands history and memory
constraints without converting a world's simulation rules. Downgrading a
database containing festival history is rejected rather than deleting it.

`GET /api/v1/living` advertises the `festivals` capability and provides observed
state. There is no festival mutation endpoint. Living records show upcoming,
active, and remembered occurrences and link attendees to their citizen records.
An empty site's skipped occurrence is labeled "No gathering this year."
The painted village draws a temporary trestle table under bunting (with bowls
while a feast still has food); the map overview marks the same location. Decorations disappear at closing, while factual records remain.

## Verification

Focused checks cover lean gatherings, real attendance, one-time benefits and
food consumption, interrupted needs, visit timing, canonical corruption,
chunked advancement, and restored continuation. SQLite checks close and reopen
before opening, at opening, during attendance, and after closing, and verify
that migrating an M15 save preserves its rules, history, and fingerprints.
Journey checkpoints also reopen during outbound travel, early dwell, a meal,
festival attendance, and the return journey, each against its uninterrupted
continuation.

The acceptance evidence below was recorded before festivals were ported onto
the planned layout; the fingerprints and counts describe that earlier build and
should be re-run against the combined rules.

Acceptance uses seeds 17 and 42, first for ten years and then for a century,
comparing uninterrupted execution with an SQLite checkpoint continuation.
Per-site living residents must be reported at year 100; global population alone
does not prove both settlements survived. Browser checks use disposable worlds
at 1440 x 1000 and 390 x 844, including attendee navigation and the 2D fallback.
Page identity, content, error overlays, and navigation passed; the mobile layout
had no horizontal overflow. Chromium reported existing Three.js Clock/shadow-map
deprecation warnings. Software WebGL was used, so this check does not establish
GPU performance.

The completed runs passed every mandatory invariant and matched their SQLite
continuation exactly. Ten-year runs checkpointed at year 8; century runs
checkpointed at year 80:

| Seed | Years | Site 1 living residents | Site 2 living residents | Qualified attendances | SQLite continuation |
| --- | --- | --- | --- | --- | --- |
| 17 | 10 | 29 | 7 | 213 | Exact match |
| 42 | 10 | 29 | 4 | 214 | Exact match |
| 17 | 100 | 26 | 38 | 3,691 | Exact match |
| 42 | 100 | 20 | 7 | 2,331 | Exact match |

The history fingerprints are
`3b887ab961ad4dc607b58b8a0df2fac287694eecd4ec93b2df17574509db9607`
(seed 17) and
`441e99c4f88eb25c123e395194652ad3e9f49be34a69765bb8007bd571891d17`
(seed 42). The JSON and Markdown reports are in
`artifacts/m16-acceptance/seed-17-10-final` and
`artifacts/m16-acceptance/seed-42-10-final`.

Seed 42 also passed the century run and year-80 SQLite continuation: settlement
1 had 20 living residents and settlement 2 had 7. All mandatory invariants
passed, with 2,331 qualified attendances and 64 completed family visits. Its
history fingerprint is
`bbfd52bf4e945a0cefce7746aab5571849162bd5467194245ed49121def38138`;
both final snapshots have fingerprint
`74713e0699d27ea7e2b16974d81f24b304a123f02b665f58fb3d0ab09bdf5072`.
Reports are in `artifacts/m16-acceptance/seed-42-100-final`.

Seed 17 finished with 26 living residents at settlement 1 and 38 at settlement
2, 3,691 qualified attendances, and 80 completed family visits. Its history
fingerprint is
`f3ce6fe40d4769eda954e1f849ad7d8f84b72caa536886430704f2e62bc93e63`;
both final snapshots have fingerprint
`e901eaf9954d8f7bde5eb2b30ba273a469e44097fba135aaa7eef6cb488cfea4`.
Reports are in `artifacts/m16-acceptance/seed-17-100-validated`.

Reference/reloaded elapsed times were 119.6/118.6 minutes for seed 42 and
226.3/221.4 minutes for seed 17. The reference and reload engines ran
concurrently, and the seed-42 run also overlapped the initial seed-17 attempt.
These are acceptance timings, not an isolated performance benchmark.

The first seed-17 century attempt exposed an inherited validation mismatch
around year 74: the facility planner permits up to 18 facilities per settlement,
but snapshot validation still capped the whole world at 18. Validation now
applies the limit per recorded settlement, retaining the original single-site
limit. Four regression cases cover M14, M15, M16, snapshot round trips, and
rejection above the local limit. This changes snapshot validation only; it does
not change engine behavior or saved canonical state. The corrected seed-17
century run passed in `artifacts/m16-acceptance/seed-17-100-validated`.

To reproduce the century acceptance in a disposable directory:

```powershell
dotnet run --project .\src\LittleAges.Headless\LittleAges.Headless.csproj `
  --configuration Release --no-build -- acceptance `
  --seed 42 --years 100 --rules m16-rng1-festivals1 `
  --checkpoint-year 80 --database .\artifacts\m16-seed-42\world.db `
  --output .\artifacts\m16-seed-42
```

Repeat with seed 17 and a separate output/database directory. Reusing a
directory containing a different world is not a rules upgrade.

Local focused validation on 2026-09-29 passed 16 festival/legacy-visit simulation
checks, three SQLite festival checks, and two observer API cases. The full
simulation suite passed 376 tests before the final visitor cadence correction;
the affected visit tests were then rerun. Domain (38), integration (74), and
headless (28) regressions passed. Two persistence schema assertions needed the
new migration/enum ceiling; both passed on recheck alongside the existing M16
save tests. Frontend tests passed 193 cases, with lint, type checks, and the
production build passing. Build warnings remain limited to the existing large
Three.js bundle.

After both century runs passed, the new-world default was promoted to M16 on
2026-09-30. The Release solution build passed with zero warnings and errors.
All nine affected simulation/default checks, four API/configuration checks,
and all 28 headless regression checks passed after the default switch.

| Browser check | Environment | Result |
| --- | --- | --- |
| Festival records and attendee links | Chromium, 1440 x 1000 | Passed; factual memory opens |
| Mobile records | Chromium, 390 x 844 | Passed; no horizontal overflow |
| 2D fallback and closing | Disposable paused festival worlds | Passed; scenery ends and records remain |

Screenshots and run reports are saved locally under
`artifacts/m16-acceptance`. The installed civilization was not changed.
