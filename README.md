# Little Ages

Little Ages is a persistent-world simulation project. M0 (Foundations) through M6 (history, biographies, and statistics) are implemented, with the current deterministic rules boundary `m6-rng1-history1`. M7 adds persistent-host hardening, coalesced observer invalidation, static publishing, Windows Service guidance, backup/recovery guidance, and sleep/resume verification. The server remains the sole owner of canonical simulation state; the browser is an optional observer.

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

## Run the server on Windows

From the repository root, run the console host in PowerShell:

```powershell
dotnet run --project .\src\LittleAges.Server -- --DataRoot .\data --ListenUrls http://127.0.0.1:5274 --ActiveWorld default-world --WorldSeed 0
```

The default binding is loopback-only at `http://127.0.0.1:5274`. The current endpoints are:

- `GET http://127.0.0.1:5274/api/v1/health`
- `GET http://127.0.0.1:5274/api/v1/status`

The default operational settings are `SimulationMinutesPerSecond=10`, a periodic checkpoint every `360` simulation minutes subject to a minimum `30` real seconds, `CheckpointRetryCount=3` retries after the initial attempt with a `2` second delay, and observer invalidation every `500` milliseconds. Set `SimulationMinutesPerSecond=0` to disable operational advancement. Periodic checkpoints are serialized by the host and browser notifications are bounded/coalesced; a failed checkpoint reports degraded persistence, retries within that policy, and becomes faulted when retries are exhausted.

The published server serves the Vite build from its `wwwroot` directory. `GET` REST observations are authoritative after startup and reconnect; the observer-only SignalR endpoint `/hubs/world` sends coalesced `worldChanged` invalidations and is safe to reconnect or fall back from to REST polling. It never accepts gameplay mutations.

LAN access is for a trusted local network only; Little Ages is not designed for public Internet exposure. LAN binding must be explicit, for example:

```powershell
dotnet run --project .\src\LittleAges.Server -- --DataRoot .\data --ListenUrls http://0.0.0.0:5274 --ActiveWorld default-world --WorldSeed 0
```

Use an appropriate network boundary before exposing that binding. This is trusted-LAN functionality only: Little Ages has no built-in authentication or TLS and is not designed for public Internet exposure. No firewall rule is changed automatically. A graceful stop drains work and attempts a final checkpoint; after a process crash or reboot, startup resumes from the last committed checkpoint and does not catch up suspended wall time. No Docker or cloud service is required.

To produce one clean framework-dependent `win-x64` deployment directory containing both the server and the Vite site:

```powershell
.\scripts\publish-windows.ps1 -OutputDirectory .\artifacts\windows-publish
```

The script verifies `dotnet`, `node`, and `npm`, runs `npm ci` and `npm run build`, publishes `LittleAges.Server` in Release mode, and copies the Vite `dist` contents into the published `wwwroot`. It does not commit or require a checked-in `dist` directory.

## Documentation

- [`docs/architecture.md`](./docs/architecture.md) — actual M0 boundaries and runtime behavior.
- [`docs/simulation-model.md`](./docs/simulation-model.md) — implemented deterministic time, IDs, RNG, and event ordering.
- [`docs/windows-service.md`](./docs/windows-service.md) — Windows Service installation, LAN binding, logs, and removal.
- [`docs/backup-and-recovery.md`](./docs/backup-and-recovery.md) — safe SQLite checkpoint backup and restore.
- [`docs/sleep-resume-checklist.md`](./docs/sleep-resume-checklist.md) — manual target-hardware suspension/resume check.
- [`docs/windows-service.example.json`](./docs/windows-service.example.json) — production configuration example with portable paths and no machine address.
- [`docs/design-v0.1.md`](./docs/design-v0.1.md) — product design baseline.
- [`docs/implementation-plan-v0.1.md`](./docs/implementation-plan-v0.1.md) — implementation plan.

## CI test split

Pull requests run the fast backend suite on Ubuntu and Windows, excluding tests tagged `Category=Long`, plus the unchanged Ubuntu frontend checks. The separate `Long tests` workflow runs only when manually dispatched or on its weekly schedule; it runs the `Category=Long` backend suite on both operating systems with a 180-minute job timeout. Neither workflow changes the `m6-rng1-history1` rules or historical M0-M6 contracts.
