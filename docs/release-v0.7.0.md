# Little Ages v0.7.0 - Family and lineage observer

The observer now includes **Family & lineage** records. Open the Family tab or
use **View family** from a citizen, biography or selected map card. Search by
name or decimal citizen ID, explore one to four ancestor or descendant
generations, and recenter through named relatives. Shared ancestors retain one
identity and every recorded parentage edge; partnership is shown separately.

The graph renders at most 100 identities, with paged immediate-relative links
and search for hidden branches. Unique recorded and living resident descendant
counts use the complete roster. Birth dates remain recorded facts, including
signed founder dates and unknown external dates. Dead relatives, admitted
newcomers and departed visitor archives retain their identities.

Family navigation preserves the camera. **Follow on map** explicitly selects
and follows a living, present person. Keyboard navigation, mobile lists and
retained root, direction, depth and scroll state support ongoing observation.
Biography and Family history use the existing APIs and history closure.

This release also includes PR #24's clearer crowd counts and picker, shared
citizen anchors and factual activity cues. Selection, follow, pause, reduced
motion and stream recovery continue to use authoritative observations.

## Existing worlds and upgrades

This is an observer-only release. Simulation, persistence, schema, rules,
fingerprints and saved data contracts are unchanged. M17 remains the default
for new worlds; existing worlds retain their saved rules and history.

Download and extract `LittleAges-v0.7.0-win-x64.zip`, then run the included
`install.ps1` as described in [Windows operations](windows-service.md).
The self-contained package includes the observer and installer. Publication
does not install or update a local Windows service.

## Validation and limits

PR #25 was reviewed at `844dbc6b8f5693584da71a0a8fb0b24f1aaf4908`, based on
merged PR #24 at `b02fd57c83681202cb13d66af7f24f7aeaa658e0`. Exact-head
[CI](https://github.com/joewolly/LittleAges/actions/runs/37092687046) passed
frontend lint, typecheck, 255 tests and production build, plus 781 non-Long
backend tests on each of Windows and Ubuntu and both Windows installer fixtures.
Independent local frontend lint, typecheck, all 255 tests and build also passed.

Twenty-eight independent browser replay checks passed at 1440 x 1000 and
390 x 844, covering shared ancestry, large IDs, signed dates, missing records,
keyboard and camera behavior, retained controls and scroll, explicit follow,
crowd selection, death/admission/departure, fallback and stream recovery. A
10,001-record archive retained exact 10,000-descendant counts with 100 graph
cards. No relevant console or runtime errors were observed. Replay data was
disposable and no live worlds were opened.

[Family observer details](family-lineage.md) describe graph limits and factual
boundaries. These local measurements are not cross-device performance
guarantees. No fresh decade/century simulations or long SQLite acceptance
matrix were rerun for this observer-only change. CodeRabbit skipped its hosted
review; its CLI is unavailable on this Windows environment, so its status was
not treated as substantive review evidence.
