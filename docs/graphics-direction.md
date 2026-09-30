# Graphics Direction — Painted 2D Isometric Village

Status: local implementation, pending visual review. This replaces the earlier
React Three Fiber diorama (the v0.1.0–v0.4.0 historical baseline). It does not
amend the simulation or product contract.

## Intent

The world view is a cozy, painted isometric village with the chunky, readable
hierarchy of a polished mobile builder: bright lit faces, stone plinths, thatch,
timber and tile, soft ground shadows, and teal-and-gold banners as the Little
Ages accent. The reference is presentation clarity, not another game's assets
or visual identity. Little Ages remains an observer, not a second simulation
and not an order-entry surface.

The view presents immutable server observations. Connected clients consume
compact SignalR live frames while REST bootstraps and recovers the observer;
neither transport carries simulation authority. Cosmetic variation (resource
size, decorative forest, villager clothing) is derived from the world seed,
coordinates, and stable decimal entity IDs with the FNV-1a `stableVisualHash`.
Cosmetic values are neither persisted nor fingerprinted.

## Scene contract

- Fixed-angle isometric projection (`src/world/iso/projection.ts`). A world tile
  is a diamond two units wide and one unit tall; world tile coordinates are tile
  centres. There is no camera rotation.
- The ground is flat. Canonical elevation only lightens high ground and darkens
  hollows slightly.
- The 160×160 terrain is drawn in cached 16×16-tile chunks, rasterized once per
  zoom level and season. Sprites (buildings, resources, decorative trees, fields,
  facilities, animals, villagers, settlement-site markers) are bucketed by chunk,
  culled to the screen, and depth-sorted by `x + y`.
- The initial camera frames structures when any exist, keeping distant gatherers
  from pulling the settlement off-centre. Before the first structure it stays on
  the starting site. Home zoom shows about 7 tiles across on a phone and 16 on a
  wide desktop.
- Pointer drag pans, the wheel or a pinch zooms around the pointer, arrow keys
  pan, `+`/`−` zoom, tapping a villager selects them, and following a selected
  villager keeps the camera on them.
- Citizens move along the non-persisted `movementPlan` from the canonical
  pathfinder. At 1, 5 and 10 min/s the visual clock advances over those timed
  segments; faster speeds, pause and reduced motion snap to authority without
  inventing a path. Resting citizens walk to their shelter's door and disappear
  indoors.
- Seasons follow the world calendar (four 90-day seasons in a 360-day year).
  Each season has its own ground palette and sprite set: spring blossom, summer
  gold wheat, autumn leaves and hay bales, winter snow on roofs, fields and pines,
  with an iced river.
- Weather (`Rain`, `ColdSpell`) is a light screen overlay that never hides the map.
- Map overview switches to the top-down records map (`LegacyMap`), which is also
  used when a 2D canvas is unavailable.

## Shelter tiers

Every Shelter shares one look, read from facts the simulation already records.
Each input only grows, so a settlement never visibly slips back a tier
(`src/world/iso/tiers.ts`):

| Tier | Look | Shown when |
|---|---|---|
| 1 | Hide tent | at founding |
| 2 | Round hut | a living citizen knows Cultivation |
| 3 | Wattle cottage | a living citizen knows Toolmaking and a Workshop is complete |
| 4 | Timber longhouse | the citizens, living and dead, have 150,000 lifetime minutes of woodcutting (about year 5 for seed 42) |
| 5 | Stone house | the citizens, living and dead, have 400,000 lifetime minutes of stoneworking (about year 20 for seed 42) |

Worlds without living-settlement rules have no techniques; a completed Workshop
stands in for both techniques there. The tier is presentation only and changes
no rule, save, or history.

## Art and asset contract

All art is original and repository-owned, drawn procedurally as SVG by
[`scripts/sprite_art.py`](../scripts/sprite_art.py) with the small isometric
painter in [`scripts/sprite_iso.py`](../scripts/sprite_iso.py). Regenerate with
Python 3.10+ and no third-party packages:

```powershell
python scripts/build-sprite-assets.py
```

It writes `src/LittleAges.Web/public/assets/sprites/<season|people|common>/*.svg`
and `src/LittleAges.Web/src/world/sprite-manifest.json`, which records each
sprite's size and the image point that sits on its tile centre, in projection
units. Do not hand-edit generated sprites or the manifest.

The kit is 140 sprites and about 1.4 MB of SVG; a session loads one season
(about 360 KB) plus the shared people and markers (about 220 KB). Sprites are
rasterized at the current zoom and cached, so they stay sharp when zoomed.

## Performance and accessibility

- Canvas device-pixel ratio is capped at 1.5.
- The view redraws only while something changes: camera movement, walking
  villagers, following, rain, or new observations.
- Development builds (or `?diagnostics`) display FPS, p95 frame time, sprites
  drawn, ground chunks, season, and Shelter tier.
- The page is a `100dvh` game shell. Observer records remain a tabbed DOM panel.
- All operational controls and record-heavy inspection remain accessible DOM.
  The world canvas never owns the only path to citizen information.

## Explicit boundaries

This work adds no gameplay endpoint, deterministic rule, pathfinding behavior,
database migration, canonical history, or direct-control system. Existing
persistence and fingerprint goldens remain the compatibility gate.

## Art-study workflow

Run the web dev server and open `/?art-slice`. This development-only entry loads
typed observation fixtures through the production viewport without any server
connection, persistence writes, or saved-world mutation. Production builds
exclude the fixture module and its stylesheet.
