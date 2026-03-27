# Protons

**Local-first desktop automation system for accounting offices.**
Built with .NET 8 + Avalonia UI — runs offline, no cloud dependency, no external APIs.

> **PT-BR:** Sistema de automação desktop local para escritórios de contabilidade. Roda 100% offline, sem dependência de nuvem.

---

## What it solves

Accounting offices operate with sensitive fiscal data — NFS-e invoices, tax reports, shared-drive files — spread across multiple desktops and a local server. Manual processes create audit gaps, data loss risk, and no traceability of who did what.

Protons provides:
- **Secure local authentication** with full audit trail
- **Admin approval workflow** for new user access
- **Multi-database support** (SQLite offline or PostgreSQL for multi-PC environments)
- **Cross-platform desktop UI** that runs on both Windows and Linux

---

## Architecture

Protons follows a strict **Clean Architecture** / **MVVM** pattern:

```
UI (Avalonia/MVVM)
   └── Protons.Core      — domain logic, services, validation rules
         └── Protons.Infrastructure  — SQLite / PostgreSQL repositories
```

**Rule:** The UI layer never accesses the database directly.
All data flows through `Protons.Core` services → `Protons.Infrastructure` repositories.

See [`docs/showcase/architecture-overview.md`](docs/showcase/architecture-overview.md) for the full component diagram.

---

## Tech stack

| Layer | Technology |
|---|---|
| UI framework | [Avalonia UI](https://avaloniaui.net/) (MVVM, XAML, cross-platform) |
| Runtime | .NET 8 |
| Language | C# (nullable enabled, implicit usings) |
| Local database | SQLite (via `Microsoft.Data.Sqlite`) |
| Server database | PostgreSQL (optional, multi-PC mode) |
| Password hashing | PBKDF2 + salt (no plaintext storage) |
| Test framework | xUnit + Moq |
| Installer (Windows) | WiX Toolset + Inno Setup |
| Installer (Linux) | AppImage + DEB |

---

## Modules

| Module | Status | Description |
|---|---|---|
| **Login** | Active | Authentication, registration, admin approval, audit log |
| **Painel Principal** | In progress | Main dashboard, notification center, admin controls |
| **INSTALADOR** | Active | Cross-platform installers (Windows MSI/Inno, Linux AppImage/DEB) |

See [`docs/showcase/modules-overview.md`](docs/showcase/modules-overview.md) for details on each module.

---

## Repository structure

```
.
├── Login/                        # Login module (main deliverable)
│   ├── Protons.Core/             # Domain: services, models, validation
│   ├── Protons.Infrastructure/   # Repositories: SQLite + PostgreSQL
│   ├── Protons.UI/               # Avalonia UI: views, viewmodels
│   ├── testes/                   # Test projects (xUnit + Moq)
│   └── documentos/               # Module-level documentation
│       └── doc_login/            # Architecture docs, test results, guides
│
├── painel principal/             # Main dashboard module (in progress)
│   ├── telas/                    # Avalonia views
│   └── modelos_de_visao/         # ViewModels
│
├── INSTALADOR/                   # Installer scripts and assets
│   ├── windows/                  # WiX + Inno Setup
│   ├── linux/                    # AppImage + DEB
│   ├── comum/                    # Shared scripts and versioning
│   ├── ativos/                   # Icon pipeline and visual assets
│   └── documentos/               # Installer documentation
│
├── docs/
│   └── showcase/                 # Public-facing technical documentation
│       ├── architecture-overview.md
│       ├── modules-overview.md
│       ├── roadmap-public.md
│       └── repo-branding-suggestions.md
│
└── INDEX.md                      # Internal project map and migration plan
```

---

## Build & test

**Prerequisites:** .NET 8 SDK (`dotnet --version` must show `8.x`)

```bash
# Build the Login module
cd Login
dotnet build Protons.sln -c Release

# Run tests
cd Login
dotnet test Protons.sln -c Release
```

**Validated environment:** Ubuntu 22.04, .NET 8.0.122
**Build result:** `Compilação com êxito — 0 Aviso(s), 0 Erro(s)`
**Test result:** 192 total — 191 passed, 1 known failure (lockout audit mock, tracked)

> Tests for `Protons.UI` require a display (Avalonia) and are not run in headless CI. Core and Infrastructure tests run fully headless.

---

## Database configuration

Protons supports two modes, configured in `appsettings.json`:

```json
{
  "Database": {
    "Mode": "Local",
    "Sqlite": { "Path": "" },
    "Server": {
      "Provider": "Postgres",
      "ConnectionString": "Host=localhost;Port=5432;Database=protons;Username=protons;Password=..."
    }
  },
  "Audit": { "UseHashChain": false }
}
```

- **Local mode:** SQLite at `%AppData%\Protons\protons.db` (Windows) or `~/.local/share/Protons/protons.db` (Linux)
- **Server mode:** PostgreSQL — tables are auto-created on first run
- Config file location priority: `%AppData%/Protons/appsettings.json` → `appsettings.json` next to the executable

---

## Security model

- Passwords stored with **PBKDF2 + salt** — no plaintext ever persisted
- **Lockout:** 5 consecutive failed attempts → 30-second block (configurable)
- Auth errors return a **generic message** — no e-mail existence leak
- **Audit chain:** optional hash-chained audit log — detects tampering of local records
- Sensitive data (CPF, passwords) never written to log files

---

## Project status

| Area | Status |
|---|---|
| Login module — core & infra | Stable, 192 tests |
| Login module — UI (Avalonia) | Functional, manual-tested on Windows |
| Admin approval workflow | Implemented |
| Audit log with hash chain | Implemented |
| Dual-database (SQLite + Postgres) | Implemented |
| Main dashboard | In progress |
| Windows installer (MSI + Inno) | Active development |
| Linux installer (AppImage + DEB) | Active development |
| CI/CD pipeline | Planned |

---

## Roadmap

See [`docs/showcase/roadmap-public.md`](docs/showcase/roadmap-public.md) for the full roadmap.

**Short version:**
1. Stabilize Login + installer → production-ready release
2. Main dashboard with notification center
3. First automation module (NFS-e import from PDF)
4. PostgreSQL server mode — multi-PC deployment
5. CI/CD pipeline (GitHub Actions)

---

## Development notes

- Internal documentation lives in `Login/documentos/doc_login/` and `INSTALADOR/documentos/`
- Architecture decisions are documented in `INDEX.md` (project map) and individual module docs
- Every code file has a mirrored documentation file — see `INDEX.md` for the mirroring convention
- The `painel principal/` folder is currently compiled by `Login/Protons.UI` — it will be extracted into its own project when the module is ready
- `win-test-results/`, `imagem para referencia/`, `ENTREGA-COMPARTILHADA/` are local working folders, not part of the source tree

---

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for guidelines.

## Security

See [`SECURITY.md`](SECURITY.md) for the vulnerability disclosure policy.

---

*This repository is under active development. APIs and module structure may change.*
