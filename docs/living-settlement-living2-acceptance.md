# Living Settlement living2 local acceptance

Local Windows acceptance for the opt-in `v02-rng1-living2` rules, recorded
2026-09-22. This rule version changes Cut fuel output from 10 to 30 fuel for
10 wood when creating a new world. Existing `v02-rng1-living1` saves retain
their saved rules and history. New worlds still default to
`m12-rng1-spaced1`.

## Why living2 exists

The living1 seed-42 world became extinct by year 72, with 48 starvation deaths.
The year-64–72 monthly diagnosis recorded 43 samples with zero fuel despite at
least 84 communal wood, one pending CutFuel order in each such sample, and 6–19
Cook/Preserve jobs blocked waiting for fuel. The recorded living1 CutFuel recipe
converted 10 wood to 10 fuel. Living2 raises that output to 30 fuel for the same
wood input. This is a rules change for new living2 worlds; it does not alter
living1 saves.

## Results

The fixed suite used seed 42 for 100 years and seeds 7 and 12345 for 10 years.
The seed-42 living2 world finished year 100 with 40 people, 55 births, 35
deaths, and maximum ancestry depth 4. All 35 deaths were natural. The history
records one shortage start and one recovery. At year 100, communal food was
1,195 and preserved food was 33,872. In both the reloaded and uninterrupted
branches, preserved food ranged from 32,733 to 38,729 at the year-end samples
for years 91–100.

The run wrote and reopened a real SQLite checkpoint at year 50. The continued
and uninterrupted runs had canonical equivalence, no mismatches, and the same
snapshot fingerprint. The final canonical fingerprints were:

| State | Fingerprint |
| --- | --- |
| Survival | `bad613474b9ad958200da65015433744fb07cf1ef2ef6c4e7af0ae010c621e0f` |
| Settlement | `44400773d8684f2642d900be6fa7ff10743bfcd558cbcfc29c4238144af1b525` |
| Social | `4204181d913ec6326895ed475973d7a3620eaed7c98a00f826f1951a011816e2` |
| History | `afc9e1c2802190aa371559770415ff42f8dbd3c9e94761909fa2b4b623c99b32` |
| Final snapshot after year-50 checkpoint continuation | `118d9e90bab9490db2389e5a17668ddcd69e0e96823ff4dc08deee9a20a66855` |

Both ten-year runs passed mandatory invariants and finished alive: seed 7 had
34 people; seed 12345 had 31.

Local Release validation passed 530 backend tests and 159 frontend tests.
Frontend lint, typecheck, and production build also passed. No hosted CI or
deployment acceptance is claimed here.

## Reproduce

From the repository root, with the .NET SDK `10.0.100` selected by
`global.json`, run the PowerShell acceptance script with a fresh output folder:

```powershell
.\scripts\test-living-acceptance.ps1 `
  -Rules v02-rng1-living2 `
  -OutputDirectory .\artifacts\living-settlement\living2-acceptance-repro-20260922
```

The script builds Release and runs the same fixed seeds, using a year-50
checkpoint for seed 42. It defaults to `dotnet`; pass `-DotNetPath` if the
pinned SDK is not available as `dotnet` in the current shell.

The measured source artifacts are in
`artifacts/living-settlement/living2-acceptance-20260922/`: the seed-42 report
is `headless-acceptance-seed-42-years-100.json`, its annual progress is
`seed-42-progress.jsonl`, and the ten-year reports are
`headless-run-seed-7-years-10.json` and
`headless-run-seed-12345-years-10.json`. The living1 diagnosis is
`artifacts/living-settlement/diagnostics/seed42-year64-72-monthly.json`. The
repeat-suite entry point is `scripts/test-living-acceptance.ps1`.

## Limits

This is local acceptance evidence for the fixed seeds and this working tree. A
100-year seed-42 success does not guarantee survival for every seed or world.
It does not establish hosted CI, Windows Service deployment, packaging, or
release acceptance. The original living1 rules and their measured historical
results remain unchanged.
