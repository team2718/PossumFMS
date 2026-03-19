# PossumFMS

FMS for the 2026 FRC game REBUILT, built by Team 2718.

This repository contains:
- `PossumFMS.Core`: .NET 10 backend that manages match state, Driver Station communication, field hardware, and SignalR updates.
- `PossumFMS.Web`: Svelte web frontend used to operate and monitor the FMS.
- `PossumFMS.Firmware`: Arduino firmware for the ESP32 field hardware

## Architecture Overview

### Backend (`PossumFMS.Core`)
- ASP.NET Core app targeting `net10.0`.
- SignalR hub endpoint: `/fmshub`.
- Core components:
  - `Arena`: authoritative match state machine and timing.
  - `GameLogic`: game-specific scoring/data hooks.
  - `DriverStationManager`: high-frequency FMS <-> DS networking.
  - `AccessPointManager`: VH-113 AP configuration and status polling.
  - `FieldHardwareManager`: TCP manager for ESP32 field devices.

### Frontend (`PossumFMS.Web`)
- SvelteKit + Vite + Tailwind CSS.
- Uses SignalR client to connect to `/fmshub`.
- In dev mode, Vite proxies `/fmshub` to `http://localhost:5000`.
- Uses `@sveltejs/adapter-static` for production static builds.

## Development Environment (Windows + VS Code)

## 1. Install prerequisites

Install these tools on Windows:
- Git
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Node.js LTS with pnpm] (https://nodejs.org/en/download)
- Visual Studio Code

## 2. Recommended VS Code extensions

- `ms-dotnettools.csharp`
- `ms-dotnettools.csdevkit` (optional but useful)
- `svelte.svelte-vscode`
- `bradlc.vscode-tailwindcss`
- `dbaeumer.vscode-eslint`
- `esbenp.prettier-vscode`

## 3. Open and restore dependencies

From repo root:

```powershell
# Backend restore (from PossumFMS.Core)
dotnet restore PossumFMS.sln

# Frontend install (from PossumFMS.Web)
pnpm install
```

## 4. Run in development

### Option A: Full-stack from `PossumFMS.Core` only (recommended for local field testing)

Build the frontend once, then run only the backend:

```powershell
# Terminal 1 (from PossumFMS.Web)
pnpm build

# Terminal 2 (from PossumFMS.Core)
dotnet run
```

Open the UI from the backend URL (typically `http://localhost:5000`).

The backend serves static files from `PossumFMS.Web/build`.

### Option B: Frontend hot-reload development

Use two terminals when actively editing Svelte UI:

```powershell
# Terminal A (from PossumFMS.Core)
dotnet run

# Terminal B (from PossumFMS.Web)
pnpm dev
```

Open the Vite URL shown in Terminal B (typically `http://localhost:5173`).

## 5. Running unit tests

Unit tests live in `PossumFMS.Core.Tests` (xUnit, targeting `net10.0`).

```powershell
# Run all tests (from repo root or PossumFMS.Core.Tests/)
dotnet test PossumFMS.Core.Tests/PossumFMS.Core.Tests.csproj

# Run with detailed output
dotnet test PossumFMS.Core.Tests/PossumFMS.Core.Tests.csproj --logger "console;verbosity=normal"

# Run a specific test class
dotnet test PossumFMS.Core.Tests/PossumFMS.Core.Tests.csproj --filter "FullyQualifiedName~ArenaTests"

# Run with code coverage (requires coverlet — installed automatically via xunit template)
dotnet test PossumFMS.Core.Tests/PossumFMS.Core.Tests.csproj --collect:"XPlat Code Coverage"
```

## 6. Optional validation commands

```powershell
# Backend build
dotnet build .\PossumFMS.Core\PossumFMS.sln -c Release

# Frontend checks/lint/build
pnpm check
pnpm lint
pnpm build
```

## Production Build Artifacts

The backend and frontend produce separate deployable artifacts.

## Backend artifacts (`PossumFMS.Core`)

From repo root:

```powershell
# Windows x64 publish (framework-dependent)
dotnet publish .\PossumFMS.Core.csproj -c Release -r win-x64 --self-contained false -o .\publish\win-x64

# Linux x64 publish (framework-dependent)
dotnet publish .\PossumFMS.Core.csproj -c Release -r linux-x64 --self-contained false -o .\publish\linux-x64
```

Outputs:
- Windows: `PossumFMS.Core\publish\win-x64\`
- Linux: `PossumFMS.Core\publish\linux-x64\`

If you want standalone binaries without requiring preinstalled .NET runtime, set `--self-contained true` (larger output).

## Frontend artifacts (`PossumFMS.Web`)

From repo root:

```powershell
pnpm install
pnpm build
```

Output:
- Static site: `PossumFMS.Web\build\`

Because this frontend is static (`adapter-static`), the same built files can be served on Windows or Linux by any static web server.