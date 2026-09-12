# Little Ages

Little Ages is a persistent-world simulation project. **M0 (Foundations)** is complete: the repository contains deterministic time, identity, counters, RNG, a synthetic scheduled-event shell, SQLite checkpoint persistence, a single-writer ASP.NET Core host, and an observer-only React/Vite status page. M0 does not contain a map, citizens, resources, survival, construction, relationships, families, or gameplay history.

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

LAN access is for a trusted local network only; M0 is not designed for public Internet exposure. LAN binding must be explicit, for example:

```powershell
dotnet run --project .\src\LittleAges.Server -- --DataRoot .\data --ListenUrls http://0.0.0.0:5274 --ActiveWorld default-world --WorldSeed 0
```

Use an appropriate network boundary before exposing that binding. The Windows Service package compatibility is present, but service installation, hardening, firewall setup, and production publishing are deferred to M7. No Docker or cloud service is required.

## Documentation

- [`docs/architecture.md`](./docs/architecture.md) — actual M0 boundaries and runtime behavior.
- [`docs/simulation-model.md`](./docs/simulation-model.md) — implemented deterministic time, IDs, RNG, and event ordering.
- [`docs/design-v0.1.md`](./docs/design-v0.1.md) — product design baseline.
- [`docs/implementation-plan-v0.1.md`](./docs/implementation-plan-v0.1.md) — implementation plan.
