# Public Roadmap — Protons

> This roadmap reflects the current development direction. Priorities may shift based on operational needs.
> Internal backlog details are tracked in module-level `PENDENCIAS.md` files.

---

## Phase 1 — Login + Installer (current)

**Goal:** Production-ready authentication and first end-user deployment.

- [x] Login module: authentication, registration, admin approval workflow
- [x] Audit log with optional hash-chain tamper detection
- [x] Dual-database mode: SQLite (offline) + PostgreSQL (multi-PC)
- [x] Automated tests: 192 tests (Core + Infrastructure)
- [x] Windows installer: WiX MSI + Inno Setup EXE
- [x] Linux installer: AppImage + DEB
- [ ] Fix 1 known failing test (lockout audit mock mismatch)
- [ ] CI/CD pipeline for automated builds and tests

---

## Phase 2 — Main Dashboard

**Goal:** Admin can manage users and monitor the system from a single interface.

- [ ] Extract `painel principal` into its own standalone project
- [ ] Admin notification center: pending user approvals with one-click approve/reject
- [ ] Dashboard layout with fixed window size (no layout collapse between screens)
- [ ] User management view: list, filter, promote to admin
- [ ] Audit log viewer in the dashboard

---

## Phase 3 — First Automation Feature (NFS-e Import)

**Goal:** Automate the first real accounting workflow.

- [ ] Parse NFS-e (Nota Fiscal de Serviços Eletrônica) from PDF
- [ ] Organize parsed files into the shared-drive folder structure
- [ ] Compare data between Prefeitura reports and Domínio system
- [ ] Audit trail for every automated action

---

## Phase 4 — Multi-PC Deployment

**Goal:** Support shared environments with multiple workstations.

- [ ] PostgreSQL server mode — stable, documented, tested in production
- [ ] Centralized audit log across all connected machines
- [ ] Admin console for cross-machine user management
- [ ] Deployment guide for the local server setup

---

## Phase 5 — Extended Automation

**Goal:** Cover the core accounting office workflow end-to-end.

- [ ] NFS-e download via browser automation (headless)
- [ ] Domínio system integration (local RDP/server access)
- [ ] Report comparison: Prefeitura data vs Domínio data
- [ ] Task queue and execution panel
- [ ] Scheduled task runner

---

## Not planned (out of scope)

- Cloud storage or cloud authentication
- SaaS or subscription model
- Mobile clients
- Integration with accounting systems other than Domínio

---

## Known open issues

| Issue | Module | Priority |
|---|---|---|
| Lockout audit mock mismatch in test | Login/Core | Medium |
| CI/CD pipeline not configured | Root | Medium |
| `painel principal` not yet a standalone project | Dashboard | Low (next phase) |

Full backlog: see `Login/documentos/doc_login/PENDENCIAS.md` and `INSTALADOR/documentos/doc_instalador/PENDENCIAS.md`.
