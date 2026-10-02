# M17 - Visitors and newcomers

`m17-rng1-newcomers1` builds on M16 festivals. It is an explicit opt-in ruleset
for new worlds. Saved worlds retain their recorded rules;
loading M16 does not add visitors or apply the M17 housing corrections. The
released M16 rules remain available.

Local household housing changes stay within their settlement. Moving between
settlements uses the existing physical migration path and rechecks that a whole
household can occupy the destination's completed housing.

## The housing prerequisite

Before adding external people, seed 7 was reproduced for ten years with the
released household-sharing rule and with an isolated diagnostic copy without
sharing. Both exposed the same defect: aggregate spare beds did not necessarily
fit a whole household, and demand could stop ordering shelters while families
had no home. The M17 correction orders a shelter when a resident is unhoused,
while preserving the current construction project. It changes neither exposure
damage nor work priorities. M16 keeps its existing behavior and fingerprints.

| Seed 7, ten years | Living residents | Exposure deaths |
| --- | ---: | ---: |
| M16 planned with sharing | 25 | 8 |
| M16 planned diagnostic without sharing | 34 | 2 |
| Same M16 planned rules with only the housing correction | 36 | 0 |
| Unchanged default M16 festivals, exact released baseline | 35 | 0 |
| Pre-newcomer M17 housing and festivals | 35 | 0 |

[The diagnosis](m17-exposure-diagnosis.md) records the affected households,
construction state, regression checks and frozen evidence. The 35/0 result
includes festivals and is not a causal isolation of the housing correction.
The eight-death control uses retained M16 planned rules; the default M16 festivals
control is distinct and has no deaths in this seed's measured ten-year run.
The matched M16-planned diagnostic changes only the housing-demand predicate;
its source, binary and trace hashes are retained with the diagnosis. All these
observations are separate from complete M17 acceptance.

Native relocation also uses the same whole-household placement as housing
reconciliation. M17 checks feasibility both before departure and on arrival;
four spare beds distributed two per shelter cannot support a three-person
family. Existing homes and local unhoused families participate in that check.
If support disappears during travel, the family physically returns with its
goods and retains its original residence. Food reserves, construction policy,
exposure damage and work priorities keep their existing values.

## A bounded external visitor

There is at most one active outsider worldwide. Once per summer, after year 5,
the engine makes a keyed one-in-four opportunity draw. It skips extinct worlds,
occupied visitor slots and worlds without a reachable inhabited host and a
spare bed in a completed shelter. The host may be either existing settlement.
There is no third settlement, off-map economy, caravan or return visit.

An adult appears on a walkable map edge and physically follows a canonical
route to the selected shelter. Route choice, host and shelter ties use stable
identities and independent newcomer randomness. Candidates use at most the
first spare shelter at each inhabited site. The actual approach is bounded to
two days of movement. Identity is allocated only after a valid route is found.

The initial adult age is 18–40; six existing skill values are each 500–6,000.
Personality values use the existing 0–10,000 range. These are deterministic,
bounded initial values, not assertions about a simulated external settlement.
Only existing techniques may become available through the admitted resident.

The traveler carries 120 food units in an independent provisions account.
This covers the bounded two-day approach, seven-day visit and two-day return
at the existing winter hunger rate, with a buffer. Guests eat their own food.
They reserve one spare shelter bed without replacing a resident. Family capacity
checks count that reservation. Native household placement keeps priority: if a
partnership or another native housing change needs the bed, the guest releases
it immediately and begins a physical departure from their current location.

The visit clock starts on physical arrival. A guest has at most seven days
before physical departure; the engine starts the return early enough to leave
movement and needs time. Loss of host support initiates a visible exit. The
traveler remains subject to ordinary hunger, exhaustion and exposure while
traveling. Arrival does not count as a birth, and departure does not count as a
death.

## Contact and admission

Guests can eat, rest and make actual social contact. A contact requires a
living resident at the same host to be nearby when it starts and completes.
The completed interaction records familiarity, affinity, trust and conflict.
It does not create a partnership. Guests cannot work, teach, trade as a
household, partner or reproduce.

Admission is autonomous, after at least a day of visiting. It requires an
actual relationship with a living local resident: familiarity at least 2,000,
affinity at least 1,500, trust at least 1,000 and conflict below 2,000.
Cooperativeness, sociability and the relationship must together satisfy the
fixed willingness threshold of 11,000. The host is checked again at admission:
it must be inhabited, retain a spare completed shelter bed, have communal food
of at least 20 times its projected resident population, and have room for the
remaining provisions. Admission also respects the existing population limit.

Joining creates exactly one ordinary household and account using the same
citizen identity. The remaining provisions transfer once to that household as
an explicit external import. They are not recorded as local production. The
resident thereafter uses ordinary occupations, work, teaching, partnerships,
parenthood and inheritance. Their actual visitor contacts become ordinary
relationships. Existing expertise is shared through resident systems after
admission; it does not grant global knowledge to either settlement.

## Identity, history and persistence

The lifecycle is explicit: `Approaching`, `Visiting`, `Leaving`, `Resident`,
`Departed` and `Dead`. Original founder ordinals remain unchanged. An outsider
has a new stable citizen ID and an external origin record, with unknown parents
as an ancestry root. Descendants refer to the admitted resident normally.

Guests are persisted outside the resident roster until admission, preventing
implicit household creation, work claims or population changes. A nullable
M17 extension in the existing living JSON stores their biography, host, shelter
reservation, route index and segment timing, action, deadlines, provisions and
actual contacts. Joined records retain origin and admission timing while their
citizen state belongs to the ordinary resident roster. A joined person's
ordinary death updates the episode to `Dead` immediately.

The provisions invariant is:

```text
initial = remaining + consumed + imported + exported at departure + lost at death
```

One dedicated scheduled event advances each active guest. The action sequence
guards against stale work; inactive archives have no scheduled events. Joining,
departure and death are single authoritative transitions. New historical event
values are appended to the persisted vocabulary; the SQLite migration expands
the history constraint while preserving old rows, links and memories.

Departed people remain inspectable as last observed alive at departure. Their
age, needs and location remain observations from that moment; there are no
ongoing events about their life outside the map. Guest death is kept separately
from resident mortality. Historical resident population starts at admission,
never at an external adult's earlier birth minute.

## Observation

The observer combines resident, visitor and archived biographies while keeping
their counts distinct. Approaching, visiting and leaving people are visible in
the painted village and map overview, with a visitor badge and ordinary select
and follow. Records show factual arrival, contact, admission, departure or death
events and external parentage. Departed people remain in archives and cannot be
followed as current map occupants.

Movement and route timing come from the server. Pause, high speed and reduced
motion snap to authoritative positions. Arrival does not force the camera to a
traveler. REST recovery and map fallback retain the same presence distinction.

## Local reproduction

Select M17 only for a new disposable world:

```powershell
dotnet run --project src/LittleAges.Server --configuration Release --no-build -- `
  --DataRoot ./artifacts/m17-demo --ActiveWorld m17-demo --WorldSeed 42 `
  --NewWorldRules m17-rng1-newcomers1 --ListenUrls http://127.0.0.1:5274
```

Headless acceptance uses the normal engine and an SQLite close/reopen:

```powershell
dotnet run --project src/LittleAges.Headless --configuration Release --no-build -- `
  acceptance --seed 7 --years 10 --rules m17-rng1-newcomers1 `
  --checkpoint-year 8 --database ./artifacts/m17-acceptance/seed-7-10/world.db `
  --output ./artifacts/m17-acceptance/seed-7-10
```

The requested horizons are ten years for seeds 7, 17, 42, 99, 1234 and 2024,
and a century for seeds 7, 17 and 42. Headless reports add an M17 newcomer
summary with separate traveler provisions, admission/departure/death outcomes,
resident death causes and local housing/resources. Those observations do not
replace the mandatory identity, ownership, history and replay invariants.
All nine integrated horizons are complete; measured acceptance results and
verification limits are recorded in [M17 validation](m17-validation.md).

M17 observation and checkpoint capture are pure projections of live ownership.
An independent adult's new household receives its residence at the actual
transition, and completed or cancelled work retires its ownership entry at that
transition. Observing more frequently cannot change later household merging,
inheritance or work decisions. M16 retains its existing capture behavior.

Farm checkpoints validate return travel against the owning settlement's actual
stockpile. The existing forty-unit origin load produces at most four grain
units; a daughter settlement's existing 120-unit load produces at most twelve.
M17 validates those bounds, ownership, cargo conservation and the producer's
cumulative tenth-grain split. This repairs checkpoint validation without changing
farm yields or allowing work at an unrelated site.
