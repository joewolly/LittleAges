# M15 - Roads and intersite trade

**Status:** implemented on `codex/m15-roads-trade`; `m15-rng1-roads1` was the
new-world default until [M16 planned layout](m16-planned-layout.md) and then
[M16 festivals](m16-festivals.md) succeeded it. Phase 6 calibrated the values once marked *initial*; the
measurements are under "Phase 6 calibration" and "Acceptance evidence".

## Intent

M14 gave a world two settlements joined only by family visits and occasional
relocation. M15 lets that contact leave a mark on the land and give both sites
a reason to keep it up:

- repeated foot traffic wears **tracks** and then **trails** into the terrain;
- settlements can deliberately improve heavily used trails into built
  **roads**;
- **traders** carry one settlement's surplus to the other and bring back what
  home is short of.

Roads make trips cheaper and faster, and trade creates more trips. That loop
should produce a visible story over generations: a faint path, then a worn
trail, then a stone road, busier or quieter as the two sites prosper or
decline.

## Compatibility and scope

After the phase 6 acceptance runs, `m15-rng1-roads1` became the new-world
default; M16 later succeeded it. M14 and M15 can still be selected with `--rules` in the
headless runner or with the server's `NewWorldRules` setting. Existing saves
keep their recorded rules and behavior, and M14 worlds stay M14. Migrating an existing
world to M15 is out of scope. M15 includes every M14 system:
`MigrationSystemsEnabled` and `UnifiedSimulationRulesEnabled` return true for
both identifiers and M16. `RoadSystemsEnabled` returns true for M15 and M16.

Worlds on M14 and earlier rules must use the unchanged pathfinder, travel-cost,
and step-cost code paths. Their fingerprints and acceptance evidence must not
change.

Roads form in every M15 world, including worlds that never found a daughter.
In those worlds, streets form around the original site. Trade requires a
founded daughter settlement.

## Road network

### Canonical state

The terrain map stays immutable. Roads are a canonical overlay stored with the
world state and checkpointed. Each tile that has any wear or grade has one
entry: its coordinate, integer `Wear`, and `Grade`. Tiles with no wear and no
grade have no entry. The entries are ordered by coordinate and validated like
other canonical state.

Grades are `None`, `Track`, `Trail`, and `Road`. Only walkable tiles can carry a
grade. Travel costs, path caches, and the observer's view of the network are
all derived from this overlay.

### Movement cost

A step's base cost stays `10` (orthogonal) or `14` (diagonal) multiplied by the
destination tile's terrain `MovementCost`. Under M15 that base is scaled by the
destination tile's grade, rounding up:

| Grade | Cost multiplier (*initial*) |
| ----- | --------------------------- |
| None  | 100%                        |
| Track | 90%                         |
| Trail | 75%                         |
| Road  | 50%                         |

Movement minutes, `LifetimeMovementCost`, path search, `LivingTravelCosts`, and
remaining-path cost all use the single `TravelCost` rule. Without a road
overlay it produces the unchanged terrain cost, so M14 and earlier worlds keep
their exact behavior. `LifetimeMovementCost` records the reduced cost, because
that is the cost the citizen actually paid.

The M15 A* heuristic must stay admissible for the cheapest possible step (a
50% grassland road: 5 orthogonal, 7 diagonal). Tie ordering stays the
existing explicit `NodePriority` ordering.

### Wear and grading

Every completed movement step by a living citizen adds 1 wear to the tile
stepped onto. Wear changes only the overlay; it never changes routes
immediately.

Grades change only at a **season boundary**, in one deterministic pass over
the overlay in coordinate order:

1. Recompute each tile's grade from its wear, using hysteresis so tiles do not
   flicker between grades (*initial* values):
   - `None` to `Track` at wear 24 or more, and `Track` back to `None` below 12.
   - `Track` to `Trail` at wear 72 or more, and `Trail` back to `Track` below
     36.
   - `Road` never changes because of wear.
2. Reduce the wear on every non-`Road` tile by one eighth, rounding the
   reduction up (`wear -= (wear + 7) / 8`), so unused trails fade to zero.
3. Remove entries left with zero wear and grade `None`.

These values are sized for the traffic between the two sites, not for the
streets. Monthly traders in both directions, plus visits, put about 12 steps a
season on each tile of the route. With a one-eighth decay, that settles at
about 84 wear, which is enough for a `Trail`. Streets inside a settlement carry
far more traffic, so they will reach `Trail` too; road-building planning,
below, makes sure the route between the sites still gets built first.

Any grade change clears the path cache and the travel-cost cache, so new
plans see the new grades. Travelers already on the move keep their planned
route: each living traveler's route is part of the canonical road state and
is checkpointed, so a reopened world continues along exactly the route an
uninterrupted run follows. Each step's timing uses the grade of the tile at
the moment it is scheduled.

Under M15, snapshot validation does not re-derive a moving citizen's step
timing from terrain. It checks instead that the checkpointed route contains
the citizen's tile and ends at their target.

### Building roads

A settlement can improve a `Trail` tile it owns into a `Road`:

- **Ownership.** A tile belongs to whichever site has the lower travel cost to
  it over base terrain, ignoring roads so ownership never shifts. Ties go to
  settlement 1. Ownership is derived, not stored.
- **Requirement.** The settlement knows `Toolmaking`.
- **Work order.** The new work kind `BuildRoad` uses the existing Living work
  pipeline: collect, travel, work, complete. It takes 2 Stone and 120 work
  minutes, at priority 1800 (*initial*), so food, fuel, and shelter work come
  first.
- **Planning.** Each season, right after the grading pass, each settlement
  tops its pending road orders up to 4. It plans nothing when its communal
  Stone is below `10 + 2 × population` and no household can sell it Stone
  (the same test construction uses). A worker who claims a road order while
  the commons hold less than its 2 Stone buys the difference from a household
  with communal Food, as construction does. It chooses among
  its own `Trail` tiles in this order:
  1. tiles on the current route between the two sites;
  2. then any other tile, highest wear first;
  3. ties broken by coordinate.

  The route between the sites is the road-aware path at planning time.
- **Completion.** When the order completes, the tile becomes `Road`
  immediately, and the derived caches are cleared as above. An unclaimed order
  whose tile is no longer a `Trail` is cancelled. If a claimed order's tile
  fades below `Trail` at a season boundary before the work finishes, the work
  completes without paving and its Stone is spent. This keeps the invariant
  that roads are only built on `Trail` tiles. In M15, built
  roads never decay. Disrepair and ruins are deferred.

The observer needs no new handling for this work kind. Work kinds reach the
web client as enum names, so `BuildRoad` is shown as "Build Road" with the
construction animation, like other building work.

Roads don't block building or field placement in M15.

## Intersite trade

### Trade journeys

Trade reuses the M14 transit-party model through a new
`MigrationJourneyKind.Trade`. The trader's cargo lives in the party's cargo
stacks, and ordinary needs, mortality, meal and rest pauses, party loss, and
cargo recovery all work the same way as for M14 journeys. The trade phases are
`Outbound`, `Exchange`, and `Returning`.

On the first day of each month, once the daughter settlement exists, each
settlement may send out a trader. It sends none if it already has a trade
party in transit, or if it has no qualifying exchange.

### Choosing what to trade

For each good, a settlement has a local target based on population. These are
the same targets the Living economy already plans against: grain, fuel, tools,
clothing, medicine, and preserved food, plus Food, Wood, and Stone commons.

- A site is **short** of a good when its stock is below the target.
- A site has a **surplus** of a good when its stock exceeds one and a half
  times the target (rounded down). The first draft used twice the target; see
  "Phase 6 calibration".

A trade is possible when the origin has a surplus of a good the destination is
short of, and the destination has a surplus of a good the origin is short of.
Among the possible pairs, the origin picks the one with the largest shortfall
first, then ties are broken by good ID.

Goods are exchanged at a fixed integer value table (*initial*): Food, Grain,
Wood, and Fiber 1; Stone and Hide 2; Meal and Fuel 2; PreservedFood 3;
Medicine 8; Clothing 12; Tool 15. Trades are made in whole **lots**, where
`a` of the outbound good and `b` of the return good have exactly equal value,
using the smallest such `a` and `b`. No value is ever created or lost by
rounding. Prices do not move with supply in M15.

### Choosing the trader

The trader is the capable adult resident, not already traveling, with the
highest Hauling skill, then the lowest citizen ID. The trader travels alone
with 14 days of provisions, as for family visits.

The load limit depends on the road quality along the route. It is fixed at
departure from the planned path:

`load = 30 + 30 × trailOrBetterSteps / steps + 30 × roadSteps / steps`

Both divisions round down, and every value is *initial*. The load ranges from
30 units over open ground to 90 on a route that is all road, which reads as
handcarts on good roads. Trips are shorter than a month, so without this rule
roads would never change how much gets traded.

### Departure, exchange, and return

1. **Departure.** Outbound goods and provisions are withdrawn from the origin's
   stocks into party stacks. The departure is recorded.
2. **Arrival.** The destination's shortage and surplus are recalculated at
   arrival time, not reused from departure. The trader exchanges as many whole
   lots as all of these allow:
   - the outbound cargo carried;
   - the destination's current surplus of the return good;
   - the destination's shortfall;
   - the destination's free storage.

   If nothing can be exchanged, the trader returns with the original cargo.
   The exchange is instantaneous after a dwell of one hour, matching visits.
3. **Return.** On arriving home, the trader deposits the returned goods and any
   unsold outbound goods. Anything beyond the origin's free storage follows the
   existing M14 cargo recovery rules.
4. **Loss.** If the trader dies, cargo is recovered where the party is, using
   the M14 rules.

Every unit of cargo is accounted for exactly once from departure to deposit,
exchange, or recovery. The only goods consumed on the way are provisions, and
that consumption is explained by the trader's ordinary needs.

## History and observer contract

History records only what happened, using world-wide IDs:

- **Trade:** `TradeDeparted`, `TradeCompleted` (goods and quantities exchanged
  in each direction), `TradeReturned` (whether the trader exchanged or came
  back unsold), and `TradeLost`.
- **Roads:** `RoadWorkSeason` records, once per settlement per season in which
  road work completed, the number of tiles built (Minor importance). There are
  no per-tile events.
- **Routes:** `RouteConnected` (Notable) is recorded the first time a
  continuous route of `Trail` or better, and separately the first time a
  continuous route of `Road`, links the two sites. It is checked after each
  seasonal grading pass and after each road completion.

The observer adds a read-only road overlay: graded tiles only, without raw
wear. It is exposed through a new `GET /api/v1/roads` route or on the existing
world payload, whichever fits the delivery pattern in
[architecture.md](architecture.md). Both the 2D map and the 3D viewport render
tracks, trails, and roads so they are clearly different. The settlement detail
routes add trade parties in transit and recent completed trades. Existing
routes keep their shapes.

## Explicitly deferred

- Road decay without maintenance, abandoned roads, and ruins.
- Roads that affect building or field placement.
- Dynamic prices.
- Credit, or trade between households across sites.
- Multi-trader caravans.
- Traders spreading techniques between sites.
- Bridges or crossings over non-walkable terrain.
- More than two settlements.
- Outsiders and newcomers.

## Acceptance criteria

M15 is acceptable when evidence shows:

- **M14 is unchanged.** Worlds on M14 and earlier rules produce identical
  fingerprints, and the existing M14 acceptance tests pass without their
  expected values changing.
- **Chunk-size determinism.** Results are identical regardless of the chunk
  size a run is advanced in. This covers seasonal grading passes, road
  completions, and every trade phase.
- **Reopen parity.** SQLite close and reopen matches uninterrupted execution
  in each of these states:
  - trade outbound;
  - trade dwell and exchange;
  - trade returning;
  - trader lost;
  - a seasonal grade change while citizens are mid-route;
  - a road completion while citizens are mid-route.
- **Long runs hold their invariants.** Multiseed 10-year and 100-year runs,
  including no-daughter worlds, preserve every M14 invariant plus:
  - goods conservation that includes trade cargo;
  - grades consistent with wear and hysteresis;
  - `Road` tiles only on tiles that were `Trail` when built;
  - at most one trade party in transit per origin.
- **Observable in the observer.** Roads, trade parties, and trade and road
  history are visible in the observer in 2D and 3D.
- **Performance.** A 100-year headless run stays within an agreed margin of
  the M14 baseline runtime, and the road overlay payload stays small enough
  for the observer's refresh rate.

## Implementation plan

1. **Rules plumbing and shared step cost.** *(Done.)* Add the M15 identifier
   and the `RoadSystemsEnabled` gate. Extract one step-cost function that takes
   an optional road overlay. Prove that M14 fingerprints are unchanged.
2. **Wear, grading, and persistence.** *(Done.)* Add the canonical road
   state (wear, grades, and active routes) inside the M15 migration state,
   wear accrual in `MoveStep`, the seasonal grading pass, and cache resets.
   Road state rides in the existing `migration_state_json` column, so no
   database migration is needed. Add reopen-parity tests across a grade
   change.
3. **Road building.** *(Done.)* Add the `BuildRoad` work kind, derived
   ownership, seasonal planning, and completion-driven cache resets. The tests
   stock a real seed-42 world with household stone just before its second
   season boundary. They then check that orders go only to the most worn
   `Trail` tiles, that finished orders turn exactly those tiles into `Road`
   and use 2 Stone each, and that reopened and chunked runs match
   uninterrupted ones. The `RoadWorkSeason` and `RouteConnected` history
   events move to phase 4 with the other new event types, because they need
   the same persistence migration.
4. **Trade journeys.** *(Done.)* Add the trade journey kind and phases,
   monthly evaluation, lot exchange, cargo accounting, the history events, and
   the persistence migration for the new event types. The tests cover each
   phase, cargo conservation, trader loss, reopen parity (in memory and through
   SQLite) in every phase and after a loss, and chunk size. A 10-year seed-42
   acceptance run passes every invariant and trades on its own. The decisions
   are listed under "Phase 4 decisions" below.
5. **Observer.** *(Done.)* Add the road overlay route, the settlement trade
   fields, 2D and 3D road rendering, and history rendering. The decisions are
   listed under "Phase 5 decisions" below.
6. **Tuning and acceptance.** *(Done.)* Road work buys its stone from
   households, the surplus threshold is calibrated, the 10-year and 100-year
   acceptance runs pass, and M15 became the new-world default. See "Phase 6
   calibration" and "Acceptance evidence" below.

## Phase 4 decisions

- **Trade doesn't block M14 systems.** Trade parties are nearly always on the
  road, but the daily family check, visits, and relocation all wait until no
  party is in transit. These gates ignore trade parties
  (`HasNonTradeMigrationParty`). Founding stays as it is, because trade needs
  a daughter settlement. Visits skip citizens who are already traveling, and
  relocation skips households with any member in a party.
- **Household membership.** Party validation requires members to stay in
  `party.HouseholdId`, but the family check may re-house a trader who is away.
  For trade, `HouseholdId` is only attribution at departure, so
  `MigrationValidation` and the headless acceptance check only that the
  household exists and the trader lives at the origin. Recovered cargo has no
  owner (it is communal).
- **Phases.** Trade reuses the visit phases, `VisitPhase` and
  `VisitDwellEndsMinute`. Dwell is the exchange hour, and the exchange happens
  when the dwell ends. `TradeReturnGood` and `TradeLoad` are fixed at
  departure. Trade cargo in transit never exceeds `TradeLoad`, so the exchange
  also caps lots at the load.
- **Targets and values.** Targets use population `p` (*initial*): Food `60p`,
  Wood `20 + 2p`, Stone `10 + 2p`, Grain `200p`, Fuel `3p`, Tool and
  Clothing `max(2, p/4)`, Medicine `p`, and PreservedFood `1000p`. Meal,
  Fiber, and Hide are not traded.
- **Choosing a pair.** Order candidate pairs by the origin's shortfall of the
  return good times its value, then by return good ID, then by outbound good
  ID. Take the first pair that allows at least one lot.
- **Free storage.** Free storage is measured the way the harvest fix does it:
  `AvailableStorage` minus `M12CitizenCargoAt`, using the non-food share for
  Wood and Stone. The exchange limits lots by the destination's net storage
  change. On return, the trader deposits what fits, provisions included, and
  follows the M14 recovery rules for the rest.
- **Departure.** Provisions come from the origin's communal Food, not a
  household, and the outbound good's surplus is measured after provisions are
  set aside. Lots at departure are also limited by the origin's shortfall of the
  return good and by the load. The load is measured on the path between the two
  sites, not on the trader's path from wherever they are standing. A capable
  trader is at least 18, below 7000 injury and illness, not already traveling,
  and free of the same work and barter obligations that visits check.
- **Loss.** Both trader death and an abort caused by an unreachable route record
  `TradeLost`. Under the M14 recovery rules, only Food, Wood, and Stone are
  recovered. Other goods are lost, and lost Grain counts as spoiled, so Living
  validation's communal grain conservation still holds. That conservation now
  also counts Grain in transit.

## Phase 5 decisions

- **Road overlay route.** `GET /api/v1/roads` returns `{ tiles: [{ x, y,
  grade }] }` in row-major order, with graded tiles only and no wear. Road
  grades change every season, so they stay off the immutable `/map` and
  `/world` payloads. Worlds before M15 get `404`, meaning there is no overlay,
  and the observer draws nothing. The web client refreshes the overlay with
  the slower settlement details.
- **Settlement trade fields.** `GET /api/v1/settlements/{id}` adds
  `tradeParties` (trade parties leaving or visiting the site, with phase,
  location, load, return good, and cargo) and `recentTrades` (the 10 newest
  `TradeCompleted` events the site took part in). Both fields are present only
  in M15 worlds. `/settlements` and `/settlement` keep their shapes. A returning
  party reports its trading partner as its destination, not home.
- **Rendering.** Neighboring graded tiles are joined center to center, using
  the lower of the two grades. Tracks are thin, dashed, and tan. Trails are
  wider and dark brown. Roads are the widest, gray, and have a pale edge. In
  3D they are instanced strips lifted above the terrain. The first
  browser pass used a pale Track color and a low lift, and tracks were
  invisible on sand. The colors and lift were then adjusted.
- **History.** Summaries for the six new event types already came from the
  server. The observer marks trade events in teal (and a lost trade in red)
  and road events in gray. The Overview tab adds a "Between the settlements"
  trade panel when the world has roads.

## Phase 6 calibration

Measured with 10-year headless runs of seeds 1 to 14, 17, and 42. The headless
report now has an "M15 roads and trade" section (grade counts, paving by
settlement, connection minutes, and trade totals). It is not part of the
deterministic report fingerprint.

- **Paving.** With stone bought from households, every seed paves at the
  4-orders-a-season cap from the first season with a `Trail`. By year 10 the
  worlds have 90 to 216 `Road` tiles, 13 to 206 `Trail` tiles, and 48 to 370
  `Track` tiles.
- **Route between the sites.** Every measured seed founded its daughter around
  year 5, 2 to 5 tiles from the original site. A `Trail` and then a `Road`
  connect the sites within about a year of founding, well inside the 10 to 20
  year goal. The wear thresholds, decay, and route-first paving stay at their
  initial values.
- **Trade volume.** Trade is limited by one site being short of what the other
  has in surplus, not by the load. Over the 10 years, seeds made 0 to 10
  trips, and seeds 6, 8, 9, and 11 made none. The load is 90 on most routes,
  because the short route is paved quickly, but no trade carried more than 44
  units. Lowering the surplus threshold from 2 to 1.5 times the target made
  lots 1.5 to 2.8 times larger and cut trips that came back unsold (seed 4
  went from 6 of 10 trips trading to 6 of 6) without changing any population.
  The load formula, the value table, and the targets are unchanged.
- **No-daughter worlds.** None of the 16 seeds failed to found a daughter, so
  the constrained 100-year no-site test (`M14NoSiteLongRunTests`) now also
  runs under M15. Its synthetic map does not sustain the founders past the
  first year under either M14 or M15, so it checks only that the world stays
  single-site and that no trade or route history appears. Paving without a
  daughter is covered by `M15RoadBuildingTests`, which pave seed 42's streets
  before it founds its daughter.

## Acceptance evidence

- **M14 is unchanged.** 100-year M14 runs of seeds 17 and 42 on this branch and
  on `v0.3.1` give identical survival, settlement, social, and history
  fingerprints and the same event counts. The headless deterministic report
  fingerprint differs only because the per-type history table now lists the
  six M15 event types with a count of 0.
- **100-year M15 acceptance.** Seeds 17 and 42, with a year-30 SQLite
  checkpoint, pass every mandatory invariant and canonical equivalence.

  | Seed | Living at year 100 (site 1 / 2) | Peak | Road tiles | Trades departed / completed / lost |
  | ---: | ---: | ---: | ---: | ---: |
  | 17 | 52 (25 / 27) | 58 | 754 | 43 / 42 / 0 |
  | 42 | 28 (19 / 9) | 35 | 773 | 22 / 16 / 0 |

  The same seeds under M14 end with 12 and 2 living citizens. By year 100,
  almost every `Trail` has been paved. On seed 17 the average trade carries 50
  units and the largest reaches the 90-unit road load.
- **Performance.** On an idle machine, 10-year runs process events as fast as
  M14 on seed 42 (57,700 vs 57,500 per second). On seed 17 the run takes about
  a third longer (148 s vs 111 s) for nearly the same number of events. Phase 6 removed one hot spot: work that produces nothing
  no longer sums the whole site's storage to check for output space. That
  change leaves every fingerprint unchanged. Most of the remaining cost comes
  from recomputing travel costs, which every grade change and road completion
  clears. Over 100 years, M15 worlds stay larger and busier, so full runs take
  longer: 5,705 s vs 2,639 s for seed 42, and 11,588 s vs 2,390 s for seed 17,
  with 2 to 2.6 times as many events. Cheaper travel-cost updates are a known
  follow-up.
- **Observer.** In a live seed-42 world at year 9, the server returned 146
  `Road`, 52 `Trail`, and 89 `Track` tiles, and gray roads showed in both the
  3D view and the 2D map.
- **Tests.** The non-long .NET suite and the web checks (lint, typecheck, 188
  tests, and build) pass.

## Resolved design questions

- **Streets vs. the route between the sites.** The first draft halved wear
  every season with thresholds of 60 and 240. At that rate the route between
  the sites would have settled near 12 wear and never shown up, while the
  streets took all the grades. The slower decay, the lower thresholds, and
  route-first road building above fix this. Phase 6 measurements on seeds 17
  and 42 check that a `Trail` connects the sites within about 10 to 20 years.
- **Trade volume.** Travel time alone can't change monthly trade. The trader's
  load now depends on route grade, as described above.
- **Movement statistics.** `LifetimeMovementCost` records the reduced cost,
  and occupation statistics are unchanged.

- **Stone for paving (resolved in phase 6).** Before phase 6, roads were never
  planned. Communal Stone stayed between 0 and 12 against a reserve of 50,
  because shared storage was full of household-owned goods, while households
  held hundreds of Stone. Road work now buys Stone from households the way
  construction does, as described under "Building roads".

- **Reopening mid-route.** Re-planning after a reopen relied on A* from
  partway along a route reproducing the rest of it, which road ties make
  fragile. M15 checkpoints each active route instead, and the reopen tests
  across a grading pass match uninterrupted runs.
