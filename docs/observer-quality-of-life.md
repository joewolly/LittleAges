# Observer quality of life

Implemented on `codex/observer-qol`, following the v0.7.0 observer. This update
adds people search, browser favorites, record navigation and phone layouts.

## People and records

Citizens opens a people directory before household details. Search matches
names and decimal citizen IDs without converting IDs to JavaScript numbers.
Residents, visitors, archives, occupation, life stage and favorites filters
combine. Results sort by name, then numeric ID; pages contain up to 50 people.
Counts use the entire roster. Household records have their own subview;
Growth and Economy live in Overview.

Select a person to read their needs, factual family links, biography or
relationships. Person references in household and historical records open
the same inspector. Missing roster records remain explicit. No ancestry or
events are inferred.

Back retains the last 50 navigation steps, including the directory query and
page, selected record, Family root/direction/depth/search/relation pages,
History filters and loaded pages, and record/list/graph scroll positions.
Close/reopen preserves the current records view. Selection and reading never
start Follow automatically.

On screens below 700 pixels, and phone landscape below 951 by 501 pixels,
records fill the viewport. A section selector replaces the tab bar, and a
person's detail replaces the directory. Back and Close remain available in
the header. Controls have 44-pixel minimum heights; phone inputs use at least
16-pixel text. The dialog traps keyboard focus and Escape restores its opener.
Dynamic viewport sizing and safe-area padding support browser chrome and
phone keyboards. Short portrait screens let the main page scroll to keep
camera controls reachable.

## Favorites and world identity

Only people are favorited. Directory, person record, Family root and map card
share the same preferences. Favorites survive death or departure, remain in
archive search, and synchronize across tabs of the same browser origin.

Storage uses `little-ages:observer-favorites:v1:<worldInstanceId>` with a
versioned list of decimal string IDs. Preferences belong to this browser
origin; they are not part of a world save and do not synchronize across
devices. Clearing browser storage removes them.

The optional `worldInstanceId` status field is the lowercase SHA-256 of the
existing persisted map fingerprint, a separator, and the persisted creation
timestamp normalized to UTC round-trip format. It is also included in streamed
status. The host reads these metadata through a read-only SQLite connection.
Checkpoints, restarts and copied databases retain identity; independently
created worlds using the same seed have distinct creation metadata.

Older servers without identity, malformed preferences, unavailable storage or
quota errors fall back to session favorites with a visible notice. A different
identified world clears record navigation and camera requests, loads that
world's preference key and refetches the map and secondary records. Request
revisions fence responses from the previous world.

## Camera and phone map

Locate moves once to a living, present person and leaves Follow off. Follow
tracks explicitly until cancelled. Manual camera input, Locate, Reset and
settlement selection cancel Follow. Selecting a settlement immediately focuses
it. Locate works in the village renderer and the map overview. Dead or departed
people retain records/favorites and have unavailable map actions.

The selected-person phone card starts compact with Locate, Favorite, Follow
and Open record. Details expands the portrait, needs and family action; Less
collapses it. Close clears selection. Header controls and activity labels flow
with their content instead of using fixed vertical offsets.

## Validation on 2026-10-07

| Check | Result |
| --- | --- |
| Frontend suite | 271 passed, 0 failed |
| TypeScript, ESLint, production build | Passed |
| .NET Release build | Passed, 0 warnings/errors |
| Persistent host and server delivery integration tests | 32 passed, 0 failed |
| World identity checkpoint/restart/copy/fresh-world and stream checks | Passed |
| Read-only observation/checkpoint canonical snapshot comparison | Passed |
| Browser interaction checks | Passed in headless Microsoft Edge through Playwright |
| Desktop / phone / small phone / landscape | 1440x1000 / 390x844 / 320x568 / 844x390 |
| 390x844 clear map space with compact card | 237 pixels; 217 with two settlement controls |
| Application exceptions, console errors, API mutation requests during browser checks | None |

Browser interactions covered name/ID search, favorite synchronization between
surfaces and persistence after reload, directory Back, biography Back,
Family/history navigation and retained depth, Locate in both renderers,
Follow cancellation by manual zoom, expanded/compact cards, dialog focus and
Escape return. Unit tests also cover 121-person pagination, archive filters,
world replacement and late responses, history filter retention, Family search,
relation pages and graph scroll, corrupt/quota storage and cross-tab events.
The browser run used an isolated paused seed-42 world at port 5275, with a
read-only two-settlement response fixture for the extra layout check.

Native iPhone Safari, the actual software keyboard, hardware safe areas and
screen-reader behavior remain manual checks. Century simulation acceptance
was not rerun: this update changes no simulation rules, schedules, paths,
canonical state, schema or migrations. It has not been released or installed.
