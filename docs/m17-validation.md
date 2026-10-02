# M17 local validation

Baseline main: `796c7306e4f92915b6e7a222e94b489a8be92871` (published v0.5.0).
Validation uses Windows, .NET SDK 10.0.401, Release backend binaries and the
locked frontend dependencies. M16 festivals remain the new-world default;
`m17-rng1-newcomers1` is explicit opt-in. Existing saves retain their rules.

The definitive version-five frozen simulation SHA256 is
`6A8C0395DEF5E15D9A9E485F35A111F88C2D8396D282DE6DC2A1D57D823E1A9A`.
`artifacts/m17-acceptance/frozen-v5/manifest.json` records 289 source files and
754 binary files. The manifest SHA256 is
`3C5AA88A2F192E085516C181457D9645085C7557BBC91A38CFC99675CC4CB282`.
All nine horizons started fresh on this reviewed build. All six decades and
three centuries are complete with invariant and canonical equivalence passes. After freezing,
only trailing blank lines in a test file were removed; the original file and
before/after hashes are retained in `v5/test-formatting/`. Production source and
frozen binaries are unchanged. Raw reports, SQLite
checkpoints, TRX files and interrupted attempts are
local ignored artifacts, separate from this checked-in evidence summary.

## Backend and frontend checks

The Release non-Long cases have passing results across all five projects:

| Project | Passing cases |
| --- | ---: |
| Domain | 41 |
| Simulation | 421 |
| Persistence | 200 |
| Integration | 77 |
| Headless | 31 |

These 770 unique passing cases combine completed project runs with rebuilt
changed scopes; this does not claim that every project ran again in one final
suite. `tests/test-summary-final-v5.json` records the selected TRX files. The
initial sandbox integration attempt could not write the existing Windows
EventLog and stalled in fault-path tests; the unchanged suite passed 77/77 in
the existing owner context. No permissions or logging settings changed. An
initial daughter-host fixture used unfinished shelters; the corrected fixture
passed with the final runtime tests. Original results and these replacements
are retained in `artifacts/m17-acceptance/tests/`.

Final rebuilt Release checks pass all 28 Simulation M17 cases and 33 focused
legacy rule cases together (61/61), plus all 13 SQLite M17 cases. The unchanged
observer/API scope has three passing cases. They cover actual sixty-minute contacts,
off-boundary admission, host-2 account/residence ownership, native births during
a visit, canonical citizen ordering, population milestones without a birth,
external ancestry and native descendants, physical departure, guest death,
dead social-target cancellation, loss of support, unreachable routes, no
daughter settlement and extinction without rescue. Active and archived phases
are checked through snapshot/SQLite reconstruction and canonical continuation.
The native partnership regression completes an ordinary social action, keeps
four residents in their shelter, preserves the unrelated three-person household,
and begins the guest's physical departure. Immediate snapshots, pre-event replay
and SQLite reopen retain the same route, provisions and single pending event.
Whole-household relocation checks cover fragmented spare beds, actual physical
arrival, support lost during travel and conserved return goods. Local housing
shuffle tests invoke the ordinary family-check body directly: a foreign-only
dwelling blocks a birth and retains four housed residents, while a valid local
dwelling permits the donor shuffle and birth. Both write successive SQLite
checkpoints and compare one-day continuation in one chunk versus 37-minute chunks.
Pure observation, independent household ownership, work-owner retirement and
site-specific food/grain cargo validation also have regression coverage.

Frontend lint, TypeScript checks, the complete 216-case Vitest suite and the
production build pass. After the mobile control layout correction, the affected
29 App/HUD tests, lint, TypeScript checks and build pass again. Final frontend
index SHA256 is
`0D4B0A63AB56B3E8BF946670FC62ABB793DB485BB41F4E59BE51B2287A0302AC`.

## Legacy fingerprints

The non-Long suite includes the literal M1 geography, M2 founder, M3 survival,
M4 settlement, M5 social, M6 history and M8 short-run golden vectors. These cover
seed 42 and, where applicable, seed 0 and `UInt64.MaxValue`; M5/M6 include the
seed-42 360-day vectors. Their explicit older rules remain unchanged.

An isolated archive of the exact baseline commit is built with the same SDK and
compared with the frozen V2 binary. All 33 one-year pairs match for
M9 growth, M10 agriculture, M11 barter, M12 spaced, living1, living2, M13 unified,
M14 migration, M15 roads, M16 planned and M16 festivals, on seeds 7, 17 and 42.
Both seed-42 M16 five-year pairs match too. Comparisons include the entire
persistence snapshot and Living JSON hashes, all subsystem fingerprints,
counters, event queue and population. Four ten-year pairs also match: M16 planned
seed 42, and M16 festivals seeds 7, 17 and 42. These activate daughter founding,
relocation, roads and fifteen completed festivals on each festivals seed. All
39 pairs used frozen version two; independent review confirms that version
three's resident-priority change is fully M17-gated. The final binary also passes
33 focused legacy rule tests. Supplementary exact-baseline comparisons of M16
planned and M16 festivals, seed 42 for one year, both match the final frozen
version-three binary across every compared canonical property.
Four version-four pairs also match every common canonical property: M16 planned
seed 42 for one year and M16 festivals seeds 7, 17 and 42 for ten years. That
earlier harness omits the baseline's supplemental history-count map; complete
snapshot and history fingerprints still match. The raw schema mismatch and
explicit common-field audit are preserved. A freshly compiled final-version-five
harness emits the map directly; all four corresponding comparisons pass. The
three ten-year festivals reports are byte-identical to the baseline reports,
including the history-count map. The one-year planned pair matches all baseline
canonical fields, with the current supplemental diagnostic recorded separately.

## Requested M17 horizons

Each acceptance case compares an uninterrupted reference with a real SQLite
close/reopen and different advancement chunks. Checkpoint year is 8 for the
decades and 80 for the centuries; the first chunk is 518,400 minutes and the
continuation uses the headless harness's different chunk. All nine final reports pass
all 33 invariants, canonical snapshot equivalence and zero mismatches. Housing,
mortality and resources are separate measured outcomes.

| Seed | Years | Residents | Resident deaths | Appeared / joined / departed / guest deaths | Run A / B seconds |
| ---: | ---: | ---: | --- | --- | ---: |
| 7 | 10 | 36 | 0 | 1 / 1 / 0 / 0 | 144.9 / 153.7 |
| 17 | 10 | 37 | 0 | 0 / 0 / 0 / 0 | 174.5 / 183.1 |
| 42 | 10 | 32 | 0 | 0 / 0 / 0 / 0 | 140.9 / 148.4 |
| 99 | 10 | 36 | 0 | 0 / 0 / 0 / 0 | 173.7 / 179.3 |
| 1234 | 10 | 31 | 0 | 2 / 2 / 0 / 0 | 136.9 / 146.8 |
| 2024 | 10 | 31 | 1 natural | 1 / 1 / 0 / 0 | 193.5 / 203.8 |
| 7 | 100 | 101 | 3 exposure, 51 natural | 16 / 12 / 4 / 0 | 10085.3 / 10059.1 |
| 17 | 100 | 109 | 40 natural, 13 starvation | 19 / 13 / 6 / 0 | 7615.7 / 7570.7 |
| 42 | 100 | 95 | 44 natural | 16 / 14 / 2 / 0 | 6766.4 / 6690.9 |

Per-site values below are measured at the final minute. Shelter columns are housed / unhoused / completed capacity; projects are active construction projects. Food, wood, stone and storage are settlement resource quantities, separate from traveler provisions.

| Seed | Years | Site | Residents | Shelter | Guests | Projects | Food | Wood | Stone | Storage |
| ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 7 | 10 | 1 | 30 | 30 / 0 / 36 | 0 | 0 | 2521 | 173 | 126 | 55600 |
| 7 | 10 | 2 | 6 | 6 / 0 / 8 | 0 | 0 | 7307 | 122 | 43 | 49200 |
| 17 | 10 | 1 | 33 | 33 / 0 / 36 | 0 | 0 | 6577 | 122 | 149 | 58000 |
| 17 | 10 | 2 | 4 | 4 / 0 / 4 | 0 | 0 | 9391 | 94 | 58 | 44400 |
| 42 | 10 | 1 | 29 | 29 / 0 / 36 | 0 | 0 | 2578 | 207 | 51 | 50800 |
| 42 | 10 | 2 | 3 | 3 / 0 / 4 | 0 | 0 | 7831 | 106 | 46 | 30000 |
| 99 | 10 | 1 | 32 | 32 / 0 / 40 | 0 | 0 | 2324 | 138 | 142 | 65200 |
| 99 | 10 | 2 | 4 | 4 / 0 / 4 | 0 | 0 | 5929 | 102 | 61 | 27600 |
| 1234 | 10 | 1 | 27 | 27 / 0 / 32 | 0 | 0 | 4159 | 88 | 83 | 50800 |
| 1234 | 10 | 2 | 4 | 4 / 0 / 4 | 0 | 0 | 2850 | 84 | 65 | 20400 |
| 2024 | 10 | 1 | 27 | 27 / 0 / 32 | 0 | 0 | 1915 | 141 | 0 | 58000 |
| 2024 | 10 | 2 | 4 | 4 / 0 / 4 | 0 | 0 | 7262 | 80 | 61 | 46800 |
| 7 | 100 | 1 | 35 | 35 / 0 / 48 | 0 | 0 | 25279 | 85 | 40 | 202400 |
| 7 | 100 | 2 | 66 | 66 / 0 / 72 | 0 | 1 | 975701 | 235 | 0 | 1135200 |
| 17 | 100 | 1 | 44 | 44 / 0 / 56 | 0 | 0 | 1922 | 177 | 97 | 189200 |
| 17 | 100 | 2 | 65 | 65 / 0 / 68 | 0 | 0 | 629877 | 146 | 0 | 1078400 |
| 42 | 100 | 1 | 46 | 46 / 0 / 56 | 0 | 0 | 5540 | 144 | 65 | 244400 |
| 42 | 100 | 2 | 49 | 49 / 0 / 52 | 0 | 0 | 696711 | 215 | 68 | 1062400 |

Traveler provisions balance in every case: initial = remaining + consumed + imported + exported + lost. Imports are transferred once on admission and are distinct from local production.

| Seed | Years | Initial | Remaining | Consumed | Imported | Exported | Lost | Difference |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 7 | 10 | 120 | 0 | 10 | 110 | 0 | 0 | 0 |
| 17 | 10 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42 | 10 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 99 | 10 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 1234 | 10 | 240 | 0 | 40 | 200 | 0 | 0 | 0 |
| 2024 | 10 | 120 | 0 | 10 | 110 | 0 | 0 | 0 |
| 7 | 100 | 1920 | 0 | 380 | 1210 | 330 | 0 | 0 |
| 17 | 100 | 2280 | 0 | 540 | 1340 | 400 | 0 | 0 |
| 42 | 100 | 1920 | 0 | 380 | 1390 | 150 | 0 | 0 |

All six version-five decades have zero unhoused residents and balanced traveler
provisions, exact target minutes and 33 invariant/equivalence passes. The seed-42
century also passes all 33 invariants and full canonical equivalence, with all
95 residents housed and provisions balancing as 1,920 initial = 380 consumed +
1,390 imported + 150 exported. The seed-17 century also passes all 33 invariants,
canonical equivalence and provisions balance, with all 109 residents housed.
The seed-7 century also passes all 33 invariants and canonical equivalence, with
all 101 residents housed and provisions balancing as 1,920 initial =
380 consumed + 1,210 imported + 330 exported. It records three exposure deaths.
A bounded replay from its actual final-build year-80 checkpoint reproduces all
three IDs and death minutes. Ordinary local partnerships form households that
cannot fit the fragmented shelter vacancies; incomplete storehouses and existing
material/rest eligibility prevent timely shelter relief. No active guest or
reservation exists in those windows. This is separate from the superseded V3
relocation/locality deaths; the retained material-access policy and physical
recovery sequences are documented in the century diagnosis. Private stocks are
present but protected by the existing ten-stone reserve, and positive wild stone
is unreachable to local workers. These native policies remain unchanged.
Both seed-17 century arms have thirteen starvation deaths
by their year-32 observations. That is a separate behavioral assessment requiring
actual site food and household access evidence. The bounded actual V5 replay
reproduces all thirteen IDs and exact death minutes: origin food exhausts during
the retained 21-day drought, all forage is empty and no harvest or eligible
cooking batch is available. Victims have correct local homes and no work, cargo,
guest or transit block. Daughter stock is physically separate. Independent review
finds no proven M17 transition defect; the severe inherited weather/resource
policy is retained without rebalance. This measured outcome is separate from
invariants and equivalence, and is not a baseline counterfactual.
See [the century diagnosis](m17-century-diagnosis.md) for superseded V3 exposure
evidence and correction scope. No unfinished century is called passed.
Per-site resources, shelter
capacity, construction and full ledgers
are retained in `artifacts/m17-acceptance/v5/acceptance-summary.json` and `.md`;
all nine definitive final-build reports are complete. The raw report audit is
`v5/final-report-audit.json`. The independent nine-case horizon/freeze audit is
`v5/root-horizon-audit.json`.

The first frozen attempt was superseded when a new regression proved that a
reserved guest ID can precede native birth IDs at admission. The final binary
sorts the Living roster canonically. A later execution-server interruption
terminated unfinished version-two centuries around years 33-38. Their annual
logs contained summary observations, not full state, and the configured
year-80 SQLite checkpoints had not been reached. Only those three unfinished
century prefixes were regenerated using the same immutable binary. Completed
decades and other passing evidence were retained. Replacement runs use hidden
task-local background processes with tracked PIDs and flushed progress logs.

Independent review then proved that a native partnership merging four residents
could lose housing because a visitor retained a reserved bed. Version three gives
native whole-household placement priority and withdraws the conflicting visitor
reservation through an atomic physical departure. All nine version-three reports
completed with invariant/parity passes, but their century exposure outcomes
required further investigation. Version four corrected whole-household native
relocation support, read-dependent ownership and exact harvest validation. Its
six completed decades and unfinished century logs are preserved, superseded
after the local housing shuffle defect was proved. Only its verified task-owned
unfinished processes were stopped, on 2026-10-02 at 07:34:27-28 UTC. Version five
started all nine cases fresh at 07:35:11 UTC. No earlier result is counted as
definitive version-five acceptance.

## Browser observation

The full lifecycle/recovery/fallback Edge/Playwright suite was repeated on frozen
version four. Its observer evidence remains applicable: the version-five local
housing filter changes no frontend or observer source. That suite exercised
physical approach, stay and departure
from an actual local engine fixture, painted-avatar selection, follow, separate
roster counts, factual timeline, unknown outside birth date, and departed
archives. Pause/high speed/reduced motion preserve authoritative position and
the unfollowed camera. Mobile controls are unobscured at 390 pixels. WebSocket
interruption falls back to REST and reconnects. Blocking the scene chunk shows
the working legacy map and records. Earlier unchanged observer evidence also
covers direct painted-avatar selection, reduced motion and WebGL denial.

The final version-five binary was smoke-tested separately, including desktop
painted boot, unobscured 390-pixel mobile controls and a real runtime API fixture
that approaches, visits, leaves and finishes alive at the original map edge.
That repeated API lifecycle is distinct from the full browser interaction suite.
Its canonical facts occur at minutes 86,400, 88,396 and 97,336. Reopening the
archived checkpoint
and advancing a year preserves all factual biography fields, estimated age,
events and last observed living status. The QA service was stopped afterward.
Screenshots and detailed manifests live in the task's `browser-qa` directory.

Limits: this is local Windows validation, not hosted Linux/CI evidence. Browser
admission, death, native relocation, reservation preemption and other browsers were not exercised;
backend scenarios cover those transitions. Denying all Canvas2D support blanks both canvas renderers,
while records and controls remain usable. Natural horizon outcomes do not
replace the forced lifecycle/replay scenarios or guarantee every seed's growth.
