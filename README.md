# Little Ages

Little Ages is a persistent-world simulation project. M0 (Foundations) through M6 (history, biographies, and statistics) remain compatibility contracts, and fresh worlds currently use the deterministic M8 rules boundary `m8-rng1-balance1`. M7 adds persistent-host hardening, coalesced observer invalidation, static publishing, Windows Service guidance, backup/recovery guidance, and sleep/resume verification. M8 adds the headless/MAX acceptance runner and sampled shortage-recovery boundary. The server remains the sole owner of canonical simulation state; the browser is an optional observer.

## Prerequisites

- .NET SDK `10.0.100` is the baseline selected by [`global.json`](./global.json); its compatible .NET 10 feature-band roll-forward is allowed. CI installs `10.0.x`.
- Node.js 22 LTS and npm.

## Backend

Run these commands from the repository root:

```powershell
dotnet restore
dotnet restore --locked-mode
dotnet build -c Release
dotnet test -c Release
```

The second restore is the reproducibility check; CI uses locked mode. NuGet lock files are committed for all .NET projects.

## Headless acceptance

The headless console runs the normal deterministic engine without wall-clock
pacing. It supports `run`, `benchmark`, and `acceptance` with invariant JSON and
Markdown output, seed/rules selection, and public horizons of 1, 10, 100, or
500 years. The acceptance command can checkpoint to SQLite, reopen, and compare
the continued run against an uninterrupted run. Timing is operational metadata;
canonical fingerprints exclude it.

```powershell
dotnet run --project .\src\LittleAges.Headless -- acceptance `
  --seed 42 --years 100 --rules m8-rng1-balance1 `
  --checkpoint-year 37 --database .\artifacts\acceptance.db `
  --output .\artifacts
```

See [`docs/v0.1-acceptance-report.md`](./docs/v0.1-acceptance-report.md) for
the candidate-only 100-year evidence and Section 62 matrix.

## Frontend

Run these commands from `src/LittleAges.Web`:

```powershell
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm run dev
```

The Vite development server proxies relative `/api` requests to `http://127.0.0.1:5274`. Start the backend first, then open the Vite URL shown by Vite (normally `http://localhost:5173`). The browser is optional: the server and simulation host run without a connected client.

## Install the Windows release (recommended)

The v0.1 release artifact is a self-contained directory for 64-bit Windows;
the target host does not need Git, Node.js, the .NET SDK, or a .NET runtime.
Download `LittleAges-v0.1.0-win-x64.zip`, extract it, open PowerShell as
Administrator in the extracted folder, and run:

```powershell
.\install.ps1 -EnableLan
```

Open the local or private-LAN URL printed by the installer. Running
`install.ps1` again upgrades the application without deleting the world. The
default uninstall command is `.\uninstall.ps1`; it removes the service and
application files while preserving the civilization at
`C:\ProgramData\LittleAges\worlds`.

LAN mode is trusted-network-only. Little Ages v0.1 has no built-in
authentication or TLS and must not be exposed to the public Internet.

## Run the server on Windows (advanced/manual)

From the repository root, run the console host in PowerShell:

```powershell
dotnet run --project .\src\LittleAges.Server -- --DataRoot .\data --ListenUrls http://127.0.0.1:5274 --ActiveWorld default-world --WorldSeed 0
```

The default binding is loopback-only at `http://127.0.0.1:5274`. The current endpoints are:

- `GET http://127.0.0.1:5274/api/v1/health`
- `GET http://127.0.0.1:5274/api/v1/status`

The default operational settings are `SimulationMinutesPerSecond=10`, a periodic checkpoint every `360` simulation minutes subject to a minimum `30` real seconds, `CheckpointRetryCount=3` retries after the initial attempt with a `2` second delay, and observer invalidation every `500` milliseconds. Operational controls are bounded host commands: `POST /api/v1/control/pause`, `POST /api/v1/control/resume`, and `POST /api/v1/control/speed` with a finite positive speed up to the configured maximum. A zero startup speed is paused; resume restores the default positive speed. Pause, resume, and speed never enter canonical state or history. Periodic checkpoints are serialized by the host and browser notifications are bounded/coalesced; a failed checkpoint reports degraded persistence, retries within that policy, and becomes faulted when retries are exhausted.

The published server serves the Vite build from its `wwwroot` directory. `GET` REST observations are authoritative after startup and reconnect; the observer-only SignalR endpoint `/hubs/world` sends coalesced `worldChanged` invalidations and is safe to reconnect or fall back from to REST polling. It never accepts gameplay mutations.

LAN access is for a trusted local network only; Little Ages is not designed for public Internet exposure. LAN binding must be explicit, for example:

```powershell
dotnet run --project .\src\LittleAges.Server -- --DataRoot .\data --ListenUrls http://0.0.0.0:5274 --ActiveWorld default-world --WorldSeed 0
```

Use an appropriate network boundary before exposing that binding. This is trusted-LAN functionality only: Little Ages has no built-in authentication or TLS and is not designed for public Internet exposure. The advanced console command does not change firewall rules; the release installer creates its scoped `Private`/`LocalSubnet` rule. A graceful stop drains work and attempts a final checkpoint; after a process crash or reboot, startup resumes from the last committed checkpoint and does not catch up suspended wall time. No Docker or cloud service is required.

To produce one clean self-contained `win-x64` deployment directory containing both the server and the Vite site:

```powershell
.\scripts\publish-windows.ps1 -OutputDirectory .\artifacts\windows-publish
```

The script verifies `dotnet`, `node`, and `npm`, runs `npm ci` and `npm run build`, publishes `LittleAges.Server` in Release mode with the .NET runtime included, and copies the Vite `dist` contents into the published `wwwroot`. It does not enable single-file publishing, trimming, or NativeAOT, and does not commit or require a checked-in `dist` directory. To create the release ZIP with its installer and end-user guide, run `.\scripts\package-windows.ps1 -Version 0.1.0`.

## Documentation

- [`docs/architecture.md`](./docs/architecture.md) — actual M0 boundaries and runtime behavior.
- [`docs/simulation-model.md`](./docs/simulation-model.md) — implemented deterministic time, IDs, RNG, and event ordering.
- [`docs/windows-service.md`](./docs/windows-service.md) — Windows Service installation, LAN binding, logs, and removal.
- [`docs/backup-and-recovery.md`](./docs/backup-and-recovery.md) — safe SQLite checkpoint backup and restore.
- [`docs/sleep-resume-checklist.md`](./docs/sleep-resume-checklist.md) — manual target-hardware suspension/resume check.
- [`docs/windows-service.example.json`](./docs/windows-service.example.json) — production configuration example with portable paths and no machine address.
- [`docs/design-v0.1.md`](./docs/design-v0.1.md) — product design baseline.
- [`docs/implementation-plan-v0.1.md`](./docs/implementation-plan-v0.1.md) — implementation plan.
- [`docs/v0.1-acceptance-report.md`](./docs/v0.1-acceptance-report.md) — candidate-only acceptance artifact and Section 62 matrix.

## CI test split

Pull requests run the fast backend suite on Ubuntu and Windows, excluding tests tagged `Category=Long`, plus the unchanged Ubuntu frontend checks. The separate `Long tests` workflow runs only when manually dispatched or on its weekly schedule; it runs the `Category=Long` backend suite on both operating systems with a 180-minute job timeout. The manual [`v0.1 acceptance workflow`](./.github/workflows/v01-acceptance.yml) is `workflow_dispatch`-only on Windows, uses locked restore and a Release build, has a 180-minute timeout, runs seed 42 for 100 years with a year-37 SQLite checkpoint, and uploads JSON/Markdown artifacts. M6 historical contracts and locked goldens remain preserved while fresh acceptance worlds use `m8-rng1-balance1`.
