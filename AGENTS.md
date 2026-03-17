# AGENTS.md

## Cursor Cloud specific instructions

### Project overview

Protons is a cross-platform desktop application (Avalonia UI / .NET 8 / C#) for business process management. The solution is at `Login/Protons.sln` with 5 projects: `Protons.Core`, `Protons.Infrastructure`, `Protons.UI`, and two test projects under `testes/`.

### Prerequisites (installed by VM snapshot)

- **.NET SDK 8.0** (`global.json` specifies `8.0.100` with `latestMinor` rollforward)
- **Xvfb** — required for headless Avalonia UI testing and running the app
- **private-packages directory** — `Login/private-packages/` must contain `UglyToad.PdfPig` custom NuGet packages (version `1.7.0-custom-5`). The update script downloads them from nuget.org if the directory is missing.

### Key commands

| Action | Command | Working directory |
|---|---|---|
| Restore | `dotnet restore Protons.sln` | `Login/` |
| Build | `dotnet build Protons.sln -c Debug` | `Login/` |
| Test (all) | `dotnet test Protons.sln -c Debug` | `Login/` |
| Test (core only) | `dotnet test testes/Protons.Core.Tests/ -c Debug` | `Login/` |
| Test (infra only) | `dotnet test testes/Protons.Infrastructure.Tests/ -c Debug` | `Login/` |
| Run (dev mode, bypasses login) | `bash Login/scripts/hot_reload_painel_direto.sh no-hot-reload` | repo root |
| Smoke test | `bash Login/scripts/smoke_painel.sh` | repo root |

### Gotchas

1. **appsettings.json** — `Login/Protons.UI/appsettings.json` is gitignored but required for the build. If missing, create a minimal JSON with `{"Database":{"Mode":"Local"}}`. The app uses defaults and env-var overrides for all other settings.

2. **DISPLAY for Avalonia** — The app and UI-dependent tests need a running X server. Start Xvfb before running: `Xvfb :99 -screen 0 1920x1080x24 &` and `export DISPLAY=:99`.

3. **Dev mode environment variables** — The script `Login/scripts/hot_reload_painel_direto.sh` sets all needed env vars (PROTONS_PAINEL_DIRETO, XDG_DATA_HOME, etc.). When running manually via `dotnet run`, set at minimum:
   - `PROTONS_PAINEL_DIRETO=1`
   - `PROTONS_PAINEL_DIRETO_HABILITADO=1`
   - `PROTONS_ENVIRONMENT=Development`
   - `XDG_DATA_HOME=$HOME/.local/share/protons-dev`
   - `PROTONS_DATA_KEY_BASE64=AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=` (dev-only BYOK key)

4. **Startup probe mode** — Set `PROTONS_STARTUP_PROBE=1` to start the app, measure startup time, and exit immediately without full UI. Useful for quick validation.

5. **Timezone-sensitive test** — `F1_DropCanonico_DeveAbrirFluxoAncorarPdfComCamposEsperados` may fail due to timezone differences (expects BRT). This is a known pre-existing issue in non-BRT environments.

6. **Skipped tests** — Tests under `AncorarPdfSmartDetectorFRETests` require external PDF fixtures not in the repo. Ghostscript-dependent tests skip if `gs` is not installed. Both are expected in cloud environments.

7. **No Docker required** — The app uses embedded SQLite for local/dev mode. PostgreSQL is only needed for production server mode.
