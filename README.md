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

From the repository root:

```bash
# Backend restore
dotnet restore PossumFMS.Core/PossumFMS.sln

# Frontend install
pnpm --prefix PossumFMS.Web install
```

Or navigate into each directory individually:

```bash
cd PossumFMS.Core && dotnet restore PossumFMS.sln && cd ..
cd PossumFMS.Web && pnpm install && cd ..
```

## 4. Run in development

### Option A: Full-stack from `PossumFMS.Core` only (recommended for local field testing)

Build the frontend once, then run the backend:

```bash
# Build frontend
cd PossumFMS.Web && pnpm build && cd ..

# Run backend
cd PossumFMS.Core && dotnet run
```

Open the UI from the backend URL (typically `http://localhost:5000` or `http://10.0.100.5`).

The backend automatically serves static files from `PossumFMS.Web/build`.

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
# Run all tests (from repo root)
dotnet test PossumFMS.Core.Tests/PossumFMS.Core.Tests.csproj

# Run with detailed output
dotnet test PossumFMS.Core.Tests/PossumFMS.Core.Tests.csproj --logger "console;verbosity=normal"

# Run a specific test class
dotnet test PossumFMS.Core.Tests/PossumFMS.Core.Tests.csproj --filter "FullyQualifiedName~DriverStationManagerTests"

# Run with code coverage
dotnet test PossumFMS.Core.Tests/PossumFMS.Core.Tests.csproj --collect:"XPlat Code Coverage"
```

## 6. Building the Application

### Backend (`PossumFMS.Core`)

Use the `build.sh` script inside `PossumFMS.Core` to build the Release configuration:

```bash
cd PossumFMS.Core
./build.sh
```

On Windows (PowerShell):

```powershell
cd PossumFMS.Core
dotnet build PossumFMS.sln -c Release
```

### Frontend (`PossumFMS.Web`)

```bash
cd PossumFMS.Web
pnpm build
```

The output is generated in `PossumFMS.Web/build/`, which is directly picked up by `PossumFMS.Core`.

## 7. Deployment to Linux FMS Host (`fms`)

On the field server, `PossumFMS.Core` runs as a `systemd` service (`PossumFMS.Core.service`).

### Deploying directly on the server (recommended)

If building directly on the Linux field computer:

```bash
# 1. Pull the latest changes
git pull

# 2. Build the frontend static assets
cd PossumFMS.Web
pnpm install
pnpm build

# 3. Build the backend using build.sh
cd ../PossumFMS.Core
./build.sh

# 4. Restart the systemd service
sudo systemctl restart PossumFMS.Core

# 5. Verify the service is running and inspect logs
sudo systemctl status PossumFMS.Core
journalctl -u PossumFMS.Core -f
```

### Alternative: Publishing from a development machine

To create standalone or framework-dependent published binaries:

```powershell
# Windows x64 publish (from repo root)
dotnet publish PossumFMS.Core/PossumFMS.Core.csproj -c Release -r win-x64 --self-contained false -o ./publish/win-x64

# Linux x64 publish (from repo root)
dotnet publish PossumFMS.Core/PossumFMS.Core.csproj -c Release -r linux-x64 --self-contained false -o ./publish/linux-x64
```

Copy the contents of `publish/linux-x64/` to your target directory on `fms` and restart the `PossumFMS.Core` service.