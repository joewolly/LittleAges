# Family & lineage observer

Implemented from `main` at `b02fd57c83681202cb13d66af7f24f7aeaa658e0`
(merged PR #24), on `codex/family-lineage-explorer`. Latest remote main was
checked before implementation and matched that SHA. No newer changes needed
reconciliation. The checkout had no additional AGENTS.md or .agents skills;
the supplied CodeGraph guidance and frontend testing/React guidance were used.

Open **Observer records → Family**, or **View family** on a citizen,
biography, or selected map card. Search by name or decimal citizen ID. Duplicate
names show IDs. Start with two ancestor generations; switch to descendants or
choose one through four generations. Clicking a name recenters the family view.
The graph also includes the root's immediate parents, children, known-parent
siblings, and recorded partner. Partners' relatives are never automatically
expanded. Partnership is dashed; parent → child edges are solid. A shared
ancestor has one identity and retains all recorded edges to displayed children.

The desktop view uses DOM cards with SVG edges. Mobile uses the same accessible
cards as a vertical list, with named parent/partner links. The records tabs support
arrow keys and Home/End; recentering focuses the root heading. Closing/reopening
the drawer or visiting another tab retains family controls and graph scrolling.

## Factual boundary and compatibility

This is a pure frontend projection of the existing complete citizen roster,
including dead residents, external guests and archived visitors. No server
endpoint, simulation, persistence, schema, rules, history policy, fingerprint,
release version or CI workflow changes are required. Existing saved worlds keep
their recorded rules and data. PR #24's crowd slots/picker, shared anchors,
factual activity, canonical timing and recovery smoothing are reused unchanged.

Birth minutes are typed and validated as signed safe integers or null. Absent
older fields normalize to unknown; negative founder dates are valid. Birth is
never calculated from age. External birth dates and unrecorded parents remain
unknown. Founder labels require a recorded founder ordinal, and external origin
requires newcomer metadata. A departed archive is **last observed alive**.
Known death dates/causes are displayed, and missing records remain ID-only.
No external genealogy, inferred ancestry, AI prose or historical partners are
created. Current partner IDs come directly from the snapshot.

**Biography** uses the existing citizen API. **Family history** sends
`history?familyCitizenId=<root>` to the existing root-bounded closure: root
ancestors, root siblings, recorded partner and root descendants. Displaying a
larger graph does not broaden FamilyClosureRules or canonical history.

Family navigation leaves map selection and camera unchanged. Only **Follow on
map** selects a living, currently present person and requests follow through
WorldViewport. Dead or departed records cannot be followed. Follow eligibility
continues to update on admission, death and departure. Existing sprite, picker,
hit test, ring, interpolation and pause/reduced-motion authority remain intact.

## Limits and performance

Graphs render at most **100 identities**, including ID-only missing links,
and **four directional generations**, plus root's immediate family. Both limits
are disclosed. Paged immediate-relative links (12 per group) and root search
(first 20 matches, with total count) reach branches hidden by graph limits.
Search can always be narrowed to a citizen's exact decimal ID.

Unique recorded descendant and living resident descendant counts traverse the
complete index independently of graph depth/node limits. Missing records do
not count as observed people. Visitors, departed archives and dead people do
not count as living resident descendants; admitted newcomers do. Duplicate
paths count a person once and never count the root as its own descendant.

Parent/child adjacency uses maps and sets, decimal strings without Number
conversion, and iterative cycle-safe traversals. Cyclic recorded edges remain
visible with a warning; generation placement cannot reconcile contradictions.
Topology keys exclude movement, needs, names and life state. The index, graph
and positions are memoized on relevant topology/root/depth/mode changes; cards
and counts refresh life facts separately. Roster scans are still linear, and
the complete index remains proportional to the recorded archive. Dense graphs
can require horizontal scrolling; cards can scroll for unusually long facts.

## Validation

Focused fixtures reuse the minimal citizen/newcomer observation shapes. They
cover shared ancestry, partial/unknown parents, root-only partner expansion,
large decimal IDs, duplicate names, missing records, cycles, a 10,000-generation
chain, deaths/newcomers, exact counts, truncation/recentering, signed/null/absent
dates, live topology/needs updates, retained controls/scroll, late biography and
history responses, and explicit viewport follow eligibility.

### Final local aggregate

| Gate | Command / evidence | Result |
| --- | --- | --- |
| Locked Web dependencies | `npm ci` | Passed; lockfile unchanged |
| Full Web lint | `npm run lint` | Passed without warnings |
| Web typecheck | `npm run typecheck` | Passed |
| Full Web tests | `npm test` | 255 passed in 21 files |
| Production Web build | `npm run build` | Passed |
| Locked backend restore | `dotnet restore LittleAges.sln --locked-mode` | Passed |
| Backend Release build | `dotnet build LittleAges.sln --configuration Release --no-restore` | Passed; 0 warnings/errors |
| Backend non-Long tests | `dotnet test LittleAges.sln --configuration Release --no-build --no-restore --filter "Category!=Long"` | 781 passed, 0 failed/skipped: Domain 41, Simulation 424, Persistence 200, Integration 80, Headless 36 |
| Windows installer fixture | `scripts/test-install-windows.ps1` under PowerShell 7 and Windows PowerShell 5.1 | Both passed; mocked operations, no installation |
| Browser replay | Installed Chrome via bundled Playwright; `http://127.0.0.1:5187/?diagnostics` | 23 checks passed |
| Hosted Frontend/Ubuntu/Windows CI | Workflow unchanged; no PR/workflow dispatched in this task | Never run for this branch |
| Fresh long simulations / SQLite acceptance matrix | Unchanged canonical layers | Never run; outside this observer change |

The frontend testing/debugging and React performance guidance were applied.
The Browser plugin was unavailable; bundled Playwright used installed Chrome
without installing a browser or adding project dependencies. Disposable REST
and real SignalR-protocol replay responses exercised the actual application
and stream parser. No live world or SQLite database was opened.

Desktop (1440 × 1000) and mobile (390 × 844) checks covered meaningful rendering,
no framework overlay, keyboard roots/tab navigation/focus, camera pixel
stability during family navigation, explicit follow, all seven present crowd
members, biography/history links and query semantics, close/reopen, movement
and topology updates, deaths, guest admission/departure, map fallback and stream
interruption/recovery. There were no relevant console/runtime errors. Expected
console entries were optional older-server endpoints returning 404 and the
deliberately injected stream interruption. Late response races are also covered
by App tests. Screenshots `family-desktop.png` and `family-mobile.png`, replay
script and `browser-evidence.json` are delivered outside tracked source.

A 10,001-record archive produced exactly 10,000 unique descendants, 100 graph
cards and 1,178 family DOM elements. Direct index + descendant traversal + graph
projection took 11.4 ms in the browser smoke measurement. A complete replay
update and navigation took 682 ms, including automation and an intentional
80 ms wait; search rendered in 99 ms. An earlier run measured 410/39 ms. These
are local development measurements under concurrent backend test load, not
cross-device budgets or production guarantees. A nonzero graph scroll position
survived the large live update. The 10,000-generation unit fixture confirms
iterative traversal without stack overflow.

The first focused run exposed an existing test selector made ambiguous by the
new Family tab; its assertion now targets the Family heading. New test typing
issues were corrected before the final passing aggregate. Browser probes also
corrected fixture terrain and locator assumptions; the final rendered checks
passed. None of these early failures remains unresolved.

`git diff` confirms no changes in Domain, Simulation, Persistence, Server,
scripts, rules, migrations, lockfiles or CI. This scope evidence plus old/M17
read fixtures and the non-Long backend gates supports save compatibility;
no fresh decade/century simulation or SQLite matrix was run. The pre-existing
untracked `.codex/` directory is retained and excluded from the feature commit.
