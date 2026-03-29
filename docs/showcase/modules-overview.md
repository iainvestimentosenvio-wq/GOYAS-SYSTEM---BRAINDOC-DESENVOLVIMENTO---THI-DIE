# Modules Overview — Protons

## Module: Login

**Status:** Stable — 192 automated tests, 0 build errors

The Login module is the first production-grade deliverable of the Protons system. It provides secure local authentication, a user registration and approval workflow, and a tamper-detectable audit log.

### What it does

- User login with email + password (PBKDF2 hashed)
- "Remember email" preference stored locally
- New user registration → status starts as `PENDENTE`
- Admin approves or rejects pending users from the main panel
- Lockout: 5 consecutive failures → 30-second block
- Full audit log of all security events
- Optional hash-chain audit log for tamper detection
- Dual-database mode: SQLite (offline) or PostgreSQL (multi-PC)

### Key services

| Service | Responsibility |
|---|---|
| `AuthService` | Login, register, approve/reject, promote admin |
| `AuditService` | Write audit log entries (with optional hash chain) |
| `AuditLogQueryService` | Paginated read of audit records |
| `PasswordHasher` | PBKDF2 + salt hashing and verification |

### Key validators

| Validator | Rule |
|---|---|
| `EmailValidator` | RFC-compliant email format |
| `CpfValidator` | Brazilian CPF digit validation |
| `PasswordPolicy` | Minimum complexity, max length (DoS prevention) |
| `InputLimits` | Field length caps across the UI |

### Views (Avalonia)

| View | Description |
|---|---|
| `LoginView` | Email + password form, "remember me" |
| `RegisterView` | New account registration |
| `LoadingView` | Startup initialization screen |
| `HomeView` | Authenticated user home |
| `PainelView` | Admin panel (notifications, pending approvals) |
| `AdminApprovalView` | Approve / reject pending users |
| `AuditLogView` | Paginated audit log viewer |
| `ConfirmDialog` | Generic confirmation modal |

### Test coverage

- `Protons.Core.Tests` — 188 tests (xUnit + Moq): AuthService, AuditService, validators, models
- `Protons.Infrastructure.Tests` — 5 tests: repository integration (SQLite)
- 1 known failing test: lockout audit mock mismatch (tracked, non-blocking for production)

### Internal documentation

Full documentation lives in `Login/documentos/doc_login/`:

| Document | Content |
|---|---|
| `00_visao_geral.md` | Module objectives, context, MVP scope |
| `01_requisitos.md` | Functional and non-functional requirements |
| `02_fluxos_e_telas.md` | User flows and screen transitions |
| `03_modelo_de_dados.md` | Data models and database schema |
| `04_seguranca_e_auditoria.md` | Security practices and audit design |
| `05_arquitetura_do_codigo.md` | Component diagram, layer responsibilities |
| `07_plano_de_testes.md` | Test plan |
| `11_documento_testes.md` | Test documentation |
| `13_modo_duplo_db.md` | Dual-database configuration guide |
| `14_roteiro_testes_manuais.md` | Manual test script |
| `PENDENCIAS.md` | Open backlog items |
| `RESULTADOS_EVOLUCAO_TESTES.md` | Historical test run results |

---

## Module: Painel Principal (Main Dashboard)

**Status:** In progress — views and viewmodels scaffolded, not yet a standalone project

The main dashboard is the primary workspace after login. Currently implemented as views compiled into the Login module's `Protons.UI` project.

### What it does (planned)

- Admin notification center for pending user approvals
- Notification button: grey when no pendencies, highlighted with "!" when approvals are waiting
- Approve / reject user from the dashboard without navigating away
- Placeholder area for first automation feature (NFS-e import)

### Current implementation

- `painel principal/telas/PainelView.axaml` — Avalonia view
- `painel principal/modelos_de_visao/PainelViewModel.cs` — ViewModel

Both are compiled via `Login/Protons.UI/Protons.UI.csproj` `<Compile>` and `<AvaloniaXaml>` include directives.

### Extraction plan

When the module is ready to become standalone:
1. Create `painel principal/PainelPrincipal.csproj`
2. Add to `Protons.sln`
3. Remove the `<Compile>` and `<AvaloniaXaml>` includes from `Protons.UI.csproj`
4. Add project reference from `Protons.UI` to the new project

---

## Module: INSTALADOR (Installer)

**Status:** Active development — Windows and Linux pipelines in place

Cross-platform installer pipeline that packages the Protons application for end-user deployment.

### What it produces

| Target | Format | Tool |
|---|---|---|
| Windows | MSI | WiX Toolset |
| Windows | EXE | Inno Setup |
| Linux | AppImage | AppImage tooling |
| Linux | DEB | Debian packaging |

### Structure

```
INSTALADOR/
├── windows/
│   ├── wix/         — WiX source files (.wxs)
│   └── innosetup/   — Inno Setup scripts (.iss)
├── linux/
│   ├── appimage/    — AppImage build config
│   └── deb/         — Debian package config
├── comum/           — Shared: version management, SBOM, common scripts
├── ativos/          — Icon pipeline (ICO, PNG, various sizes)
├── evidencias/      — Test evidence screenshots
├── saida/           — Build output (gitignored)
├── testes/          — Installer test scripts
└── documentos/      — Installer documentation
    └── doc_instalador/
        ├── windows-wix.md
        ├── windows-inno.md
        ├── linux-appimage.md
        ├── linux-deb.md
        ├── comum.md
        ├── ativos.md
        ├── testes.md
        └── arquitetura/README.md
```

### Key scripts

| Script | Platform | Purpose |
|---|---|---|
| `RUN-WINDOWS-TESTS.ps1` | Windows | Run installer integration tests |
| `RUN-WINDOWS-TESTS.cmd` | Windows | Wrapper for the PS1 script |
| `Login/ATALHO_PROFISSIONAL_EXECUTAR.sh` | Linux | Development launch shortcut |
| `Login/FINALIZACAO_INSTALADORES.sh` | Linux | Installer finalization script |

Progress is tracked in `CHECKLIST_INSTALADOR_100.md` at the repository root.
