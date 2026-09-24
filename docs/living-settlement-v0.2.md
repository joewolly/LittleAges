# v0.2 — Living Settlement

This is the successor to the preserved [v0.1 design](design-v0.1.md).
One communal settlement sustains daily life, learns techniques, and responds to
environmental pressures. People choose their work. The observer can inspect,
follow, pause, and change speed. The canonical simulation never calls an LLM.

## Selecting rules for a new world

Living Settlement has two versioned rules identifiers. `v02-rng1-living1`
retains its original recipes. `v02-rng1-living2` is an opt-in new-world variant
that changes the Cut fuel output from 10 to 30 fuel for the same 10 wood. This
raises fuel production capacity to address the fuel-blocked work diagnosed in
the living1 seed-42 run; it does not change the living1 rules or convert an
existing save. `m12-rng1-spaced1` remains a historical rules boundary; new
worlds now use `m14-rng1-migration1` by default.

Existing worlds retain their saved rules and histories. Select living2 only
when creating a **disposable new world**:

```powershell
dotnet run --project src/LittleAges.Server --configuration Release -- `
  --DataRoot ./artifacts/living-demo --ActiveWorld living-demo --WorldSeed 42 `
  --NewWorldRules v02-rng1-living2 --ListenUrls http://127.0.0.1:5284
```

Use `--NewWorldRules v02-rng1-living1` to create a world with the original
Living Settlement recipe. Neither selection changes an already saved world.

The existing Windows installation is a separate deployment decision. Do not
point this development command at its data directory. The development frontend
must proxy to the chosen server, or serve its production `dist` directory with
the server's `--webroot` option.

## People coordinate work

Settlement planning creates persistent orders. Each order records its site,
supply location, subject, prerequisite technique, priority, ingredients, claim,
phase, work completed, and cargo. Claimed materials leave communal inventory
and enter that order's escrow. Escrow and cargo continue to occupy storage;
depositing cargo transfers ownership without creating capacity.

Citizens collect supplies, travel, perform bounded work shifts, and return
outputs. Interrupted work keeps its progress and supplies. Another qualified
citizen can claim it. Death releases a claim; obsolete work is canceled and its
reserved inputs returned. Pickup and loaded transport are distinct persisted
states: a replacement worker first reaches abandoned supplies. Essential food
and care orders can still be admitted when optional work fills the board.
Needs, personality, skills, family relationships,
experience, goals, and travel affect selection. Food emergencies raise the
priority of harvesting, fuel preparation, and cooking. Citizens eat available
food when hungry and rest when exhausted. With no ready food, hungry citizens
can finish the production steps that make eating possible. Severe debility
limits work.

Planning runs every six simulation hours. Production and travel use the
existing deterministic event engine between those boundaries. Priorities are
re-evaluated as stocks and conditions change. Building and field sites are
chosen autonomously from reachable, suitable, unoccupied geography.

## Daily economy

Fields are persisted overlays on immutable generated terrain. Cultivation
requires knowledgeable workers. Sowing, tending, and harvesting take work;
growth depends on fertility, moisture, temperature, and crop condition.
Harvests produce grain and fiber. Hearth capacity expands with population.

| Work | Inputs | Outputs |
| --- | --- | --- |
| Cook | 20 grain, 1 fuel | 30 meals into communal food |
| Preserve | 20 grain, 2 fuel | 20 preserved food |
| Cut fuel | 10 wood | 10 fuel (`living1`); 30 fuel (`living2`) |
| Make tools | 4 wood, 2 stone, workshop | 1 tool |
| Weave | 8 fiber, loom | 1 garment |
| Prepare medicine | 5 food, 2 fiber, care house | 5 treatment supplies |
| Build a facility | 12 wood, 6 stone | Hearth, loom, or care house |

The recipes and work requirements are checked against the rules on load.
Production cannot redefine a recipe through saved JSON. Tools wear with work;
clothing wears daily. Citizens collect replacements. Grain, preserved food,
fiber, and hides deteriorate at explicit integer rates. Preserved food is
released when communal food runs low. Fuel supports cold-weather warmth.
There is no private property, money, or market.

Preservation targets 1,000 food units per person: a 90-day winter consumes
about 778 units at the fixed hunger rate, leaving a margin for spoilage and
a poor spring. Harvesting, sowing, tending, and preservation respond to this
reserve deficit throughout the growing season. Meals use the portion needed
to satisfy hunger. These preservation targets apply only to new
living-settlement worlds.

## Personal lives and knowledge

Citizens have persistent goals for family security, comfort, mastery, or
exploration. Daily mood and stress respond to needs, resilience, illness,
injury, and recent experiences. Active experiences are capped at 24 per person
and expire after 30 days. Significant historical facts remain retained.

Care, recreation, teaching, and relationship repair compete with production.
Care can carry food to young children and medicine to patients. Care houses
improve nearby treatment. Successful assistance and teaching build trust;
relationship repair reduces conflict. Children under six depend on adult
production and care; children can learn and recreate; productive settlement
jobs require age thirteen. Injury, illness, and old age reduce labor capacity.

Experimentation combines practice, curiosity, and relevant prior experience or
materials. Initial techniques are cultivation, preservation, toolmaking,
textiles, and care. Knowledge belongs to people. Teachers transmit it to
recipients, including children. Dead holders do not satisfy prerequisites;
surviving teachers or rediscovery can restore unavailable expertise. The
Agrarian label requires cultivation expertise and an actual harvest, with no
calendar unlock.

## Environment and consequences

Seed-derived weekly weather and seasonal temperature produce rain, drought,
and cold spells. Daily weather changes crop growth, wild food availability,
exposure, illness, fuel demand, and work priorities. Wildlife moves, feeds,
reproduces, consumes crops, and can be hunted. Predators and dangerous work
can injure people. Rest, warmth, food, medicine, and assistance support
recovery. Local wildlife and settlements can become extinct.

The Living records tab exposes supplies, pending and claimed work, input
requirements, blocked reasons, field condition, knowledge holders, and recent
consequences. Citizen records expose goals, mood, stress, health conditions,
equipment, knowledge, and experiences. Fields, facilities, supplies, weather,
animals, and active work sites appear in the diorama; fields, facilities, and
animals also appear in its 2D fallback, which can switch between settlement and
whole-world views. New-world buildings fit their occupied tiles so adjacent
fields remain visible. Transported goods appear with their carrier. These
views do not simulate anything.

## Ownership, ordering, and persistence

- `LivingSimulation`: scheduling, claims, reservations, transport, and work.
- `LivingEconomy` / `LivingWorkDefinitions`: planning, production, prerequisites,
  facility expansion, and canonical recipes.
- `LivingPeople`: personal experience, labor, care, and relationships.
- `LivingKnowledge`: discovery prerequisites, learning, and teaching opportunities.
- `LivingEnvironment`: weather, crops, spoilage, warmth, and wildlife.
- `LivingValidation`: persisted identity, references, progress, capacity,
  ownership, recipes, and historical-prefix checks.
- `LivingTravelCosts` / `LivingNavigation`: derived distance and resource lookup
  caches. Exact costs and resource tie ordering match the compatibility solver.
  Distance arrays use an LRU cache with a 256 MiB budget and at most 512 origins.

The pulse has priority 5 and its own reserved name `living.pulse.v1`. At a
daily boundary it advances environment before personal conditions, then
reconciles work and plans new opportunities. Stable identity ordering and
integer arithmetic govern all canonical decisions. Random purpose keys are
separate from legacy calls. Living IDs and factual-history IDs have independent
persisted counters.

Canonical state is stored in `world_meta.living_state_json`, inside the same
SQLite checkpoint transaction as citizens, scheduled events, and legacy
history. It includes every future-affecting goal, claim, reservation, condition,
crop, animal, weather value, knowledge item, and progress value. Generated
geography is unchanged. Only derived route/read caches are rebuilt. The
migration also extends the citizen-action constraint for `LivingWork = 13`.
It changes storage schema without switching old simulation rules.

`GET /api/v1/living` and observer scene frames expose an immutable snapshot with
an explicit rules identifier, capability list, and contract version. Legacy
worlds return no living component. Initial connections and reconnects fetch
authoritative REST state. The read payload contains the most recent 100 living
facts; the checkpoint retains their complete append-only sequence.

## Validation

Fast tests cover autonomous discovery and food production, chunking and reload,
actual SQLite transactions and rollback, mortality cleanup, replacement claims,
care, knowledge transfer and loss, personal preferences, cold exposure, invalid
recipes/cargo/capacity/claims, observer immutability, and saved-rules precedence.
Legacy compatibility fixtures and fingerprint goldens remain unchanged.

The fixed long-run suite is seeds **42, 7, and 12345** for each selected
Living Settlement rules version. Seed 42 runs through
100 years with an independent replay and a real SQLite checkpoint/reload at
year 50; seeds 7 and 12345 run ten years. The two century worlds run independently
in parallel, with different advance chunk sizes and one writer per world.
Annual JSON lines identify each run and report
population, production, supplies, deaths by cause, outstanding work age,
canonical living-state size, managed memory, and process working set.
Performance/memory measurements are operational data, excluded from canonical
fingerprints. The century process's memory measurements include both worlds;
canonical living-state bytes are measured separately for each world. Extinction
is reported rather than silently replacing a seed.

```powershell
dotnet run --project src/LittleAges.Headless -c Release -- acceptance `
  --seed 42 --years 100 --checkpoint-year 50 --rules v02-rng1-living2 `
  --output artifacts/living-settlement/living2-acceptance
```

`scripts/test-living-acceptance.ps1` runs the complete fixed suite, preserves
reports and annual progress, rejects failed invariants, and requires both
SQLite continuation equality and a second descendant generation for seed 42.
Pass `-DotNetPath` when the pinned SDK is not the default `dotnet`.

The script accepts either `v02-rng1-living1` or `v02-rng1-living2` through its
`-Rules` parameter. See the [living1 results](living-settlement-acceptance.md)
and the separate [living2 local acceptance report](living-settlement-living2-acceptance.md)
for rule-specific measurements and limitations. These reports do not establish
hosted CI, deployment, or a guarantee for every seed.

## Later ages

Property and inheritance, migration, multiple settlements, trade, institutions,
organized conflict, diplomacy, energy, industry, and later technological ages
extend these work and knowledge systems in subsequent releases. The proposed
AI historian follows this simulation expansion and can interpret factual
history without changing canonical facts.
