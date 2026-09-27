# Little Ages v0.4.0

**Status:** published on 2026-09-27 as
[v0.4.0](https://github.com/joewolly/LittleAges/releases/tag/v0.4.0) from
`cea2e1d`, with `LittleAges-v0.4.0-win-x64.zip` (SHA-256
`86167e09315875228b9e0a1f6a1232f2feb46fa5cb5b81b145cf116d96243199`).

## Highlights

- New worlds default to `m15-rng1-roads1`, adding roads and trade between the
  two settlements. Foot traffic wears tracks and then trails into the land.
  Settlements pave their busiest trails into stone roads, starting with the
  route between the sites, and buy the stone from households. Roads make
  travel cheaper and let traders carry more. Each month, a settlement may send
  a trader to swap its surplus for what it is short of.
- Roads also form in worlds that never found a daughter settlement. Streets
  wear in and get paved around the original site.
- The observer draws tracks, trails, and roads in both the 2D map and the 3D
  diorama, lists trade parties and recent trades for each settlement, and
  shows trade and road events in the history.
- Existing saves keep their recorded rules and behavior. There is no automatic
  upgrade to M15, and M14 worlds are unchanged.

## Acceptance

The seed-17 and seed-42 100-year Release acceptance runs passed all mandatory
invariants and year-30 SQLite checkpoint equivalence. At year 100, seed 17 had
52 living citizens (25 at the original site and 27 at the daughter site) and
seed 42 had 28 (19 and 9). Both worlds had paved more than 750 road tiles.
Seed 17 made 43 trade trips and seed 42 made 22, and no trader was lost. The
same seeds under M14 end with 12 and 2 living citizens. These results describe
the measured seeds, not every possible world.

M14 worlds produce the same survival, settlement, social, and history
fingerprints as v0.3.1 over 100 years.

## Known limitations

- Worlds that keep more people alive take longer to simulate. On seed 17, a
  10-year run takes about a third longer than under M14, mostly because
  travel costs are recomputed after each road change. Full 100-year runs take
  2 to 5 times as long as M14, because the M15 worlds stay larger and busier.
- Trade is uncommon: some worlds go a decade without a trade, because it needs
  one site to be short of what the other has in surplus.
