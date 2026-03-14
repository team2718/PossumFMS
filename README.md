# PossumFMS

FMS for the 2026 FRC game REBUILT, built by Team 2718.

This repository contains:
- `PossumFMS.Core`: .NET 10 backend that manages match state, Driver Station communication, field hardware, and SignalR updates.
- `PossumFMS.Web`: Svelte web frontend used to operate and monitor the FMS.

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

Use two terminals.

Terminal A (from PossumFMS.Core):

```powershell
dotnet run
```

Terminal B (from PossumFMS.Web):

```powershell
pnpm dev
```

Open the web UI at the Vite URL shown in Terminal B (typically `http://localhost:5173`).

## 5. Optional validation commands

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