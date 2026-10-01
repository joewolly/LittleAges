# Little Ages v0.5.0

**Status:** published on 2026-10-01 as
[v0.5.0](https://github.com/joewolly/LittleAges/releases/tag/v0.5.0) from
`de0fd3c7b440b5a57500f17d923950b274b09e8c`, with `LittleAges-v0.5.0-win-x64.zip` (SHA-256
`3f329783b0893715a62a4e6d932825689370377170500efb49e3bfbc98fb60d3`).

## Highlights

- New worlds default to `m16-rng1-festivals1`. It keeps M15 roads, trade, and
  migration, and adds a planned settlement layout and harvest festivals.
- **Planned layout.** Villages are laid out in districts instead of growing
  outward one nearest free tile at a time. Shared storehouses take the place
  of scattered stockpiles. Households hand their surplus wood and stone to the
  commons instead of hoarding it. Across four measured seeds, storage
  buildings fell from 192 to 91.
- **Harvest festivals.** Each inhabited settlement holds a harvest afternoon
  once a year. Family visits are timed to it. Attendees keep factual memories
  of it, and the observer shows temporary decorations.
- **Painted 2D village.** The observer replaces the 3D diorama with a painted
  isometric 2D village and a game-style HUD.
- Existing saves keep their recorded rules and behavior. There is no automatic
  upgrade to M16. `m16-rng1-planned1` (planned layout without festivals) is
  still available when selected explicitly.

## Acceptance

The seed-17 and seed-42 100-year Release acceptance runs under
`m16-rng1-festivals1` passed all mandatory invariants. Each one also matched a
SQLite save reopened at year 80 and run on to year 100. At year 100:

| Seed | Original site | Daughter site | Festival attendances | Family visits |
| --- | --- | --- | --- | --- |
| 17 | 26 | 38 | 3,691 | 80 |
| 42 | 20 | 7 | 2,331 | 64 |

These results describe the measured seeds, not every possible world. Details
are in [M16 festivals](m16-festivals.md) and
[M16 planned layout](m16-planned-layout.md).

The first seed-17 century run found a snapshot validation bug: an 18-facility
cap was applied to the whole world instead of to each settlement. Validation
now applies it per settlement. This did not change simulation behavior.

## Known limitations

- Seed 7 had eight exposure deaths with household surplus sharing, against two
  without it. No other measured seed shows this, but the cause has not been
  proven.
- Century runs are slow. In the acceptance runs, seed 42 took about 2 hours
  and seed 17 about 3.7 hours.

## Windows installation

Back up existing world data first. Extract `LittleAges-v0.5.0-win-x64.zip`
and run `install.ps1` from Administrator PowerShell. An upgrade keeps existing
configuration and saved worlds. LAN use is still meant for trusted networks
only.
