# M15 - Roads and intersite trade

**Status:** in progress on `codex/m15-roads-trade`; phase 1 is implemented.
The rules identifier is `m15-rng1-roads1`. Tuning values marked *initial* are
starting points to be calibrated by the phase measurements below, not settled
rules.

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

Until the phase 6 acceptance runs pass, M15 is opt-in: select it with
`--rules m15-rng1-roads1` in the headless runner or with the server's
`NewWorldRules` setting. `m14-rng1-migration1` stays the new-world default until
then, following the precedent set by `living2`. Existing saves keep their
recorded rules and behavior, and M14 worlds stay M14. Migrating an existing
world to M15 is out of scope. M15 includes every M14 system:
`MigrationSystemsEnabled` and `UnifiedSimulationRulesEnabled` return true for
both identifiers, and a new `RoadSystemsEnabled` returns true only for M15.

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

Any grade change bumps a network revision. At that moment the engine must
clear the path cache, the travel-cost cache, and every citizen's active path.
Each traveler then re-plans from their current tile at their next step. This
rule is required for determinism: after reopening a save, active paths are
already re-planned from the current tile (`MoveStep` falls back to
`FindPathCached` when `_activePaths` is empty). Re-planning at the same
moment in an uninterrupted run keeps the two in step. Any derived completion
estimate, such as `ActionCompletesMinute`, must be recomputed the same way in
both cases.

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
- **Planning.** Each season, each settlement requests at most 4 (*initial*)
  road orders, and plans nothing when Stone is below a per-population reserve.
  It chooses among its own `Trail` tiles in this order:
  1. tiles on the current route between the two sites;
  2. then any other tile, highest wear first;
  3. ties broken by coordinate.

  The route between the sites is the road-aware path at planning time.
- **Completion.** When the order completes, the tile becomes `Road`
  immediately and the network revision bumps, with the same cache and
  active-path reset as above. In M15, built roads never decay. Disrepair and
  ruins are deferred.

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
- A site has a **surplus** of a good when its stock exceeds twice the target.

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
2. **Wear, grading, and persistence.** Add the canonical overlay, wear
   accrual in `MoveStep`, the seasonal grading pass, and revision-driven cache
   and active-path resets. Add the persistence migration and the reopen-parity
   tests across a grade change.
3. **Road building.** Add the `BuildRoad` work kind, derived ownership,
   seasonal planning, and completion-driven resets.
4. **Trade journeys.** Add the trade journey kind and phases, monthly
   evaluation, lot exchange, cargo accounting, the history events, and the
   persistence migration for the new event types.
5. **Observer.** Add the road overlay route, the settlement trade fields, 2D
   and 3D road rendering, and history rendering.
6. **Tuning and acceptance.** Calibrate the *initial* values from multiseed
   runs, run the 10-year and 100-year acceptance, and update the README,
   `simulation-model.md`, `feature-ideas.md`, and the release notes.

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

## Remaining risk

When a save is reopened, travelers re-plan from their current tile. Parity with
an uninterrupted run depends on A* from partway along a route giving the rest
of the original route. Road costs create more equal-cost ties. Phase 2 must
test reopening mid-route on graded tiles specifically. If ties diverge, the fix
is to checkpoint each active route rather than re-derive it.
