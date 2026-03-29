# Repository Branding Suggestions — Protons

## Context

The current repository name (`teste-windows-instalador`) reflects a testing/working phase, not the product identity. This document provides ranked suggestions for a permanent, public-facing repository name and GitHub "About" description.

---

## Repository name suggestions

### 1. `protons` *(recommended)*

**Why:** Clean, direct, already the product name. One word, no hyphens, easy to type, easy to remember.
Works in all contexts: `github.com/org/protons`, import paths, CI references.

**Risk:** May conflict with existing `protons` repos on GitHub — check availability before adopting.

---

### 2. `protons-desktop`

**Why:** Adds the `desktop` qualifier, which immediately signals the application type (local desktop app, not a web service or library). Clear to recruiters and open-source viewers.

**When to prefer this:** if `protons` alone is taken or if the org plans to release multiple Protons products (e.g., a future `protons-server`).

---

### 3. `protons-core` *(least preferred)*

**Why:** Signals it's the foundational layer. Technically accurate — the `Protons.Core` project is the domain heart of the system.

**Problem:** `core` implies a library, not a deployable application. Recruiters and users may not understand this is a full desktop app with installer and UI. Avoid unless this repo will be split into lib + app.

---

## GitHub "About" description suggestions

*The About field on GitHub is limited to 350 characters. Aim for a single sentence that is technical, specific, and non-hypey.*

### 1 *(recommended)*
> Local-first desktop automation for accounting offices. .NET 8 + Avalonia UI, Clean Architecture, SQLite/PostgreSQL, offline-capable, cross-platform (Windows + Linux).

**Why:** hits the key technical signals in one line — framework, architecture pattern, database, offline-first, cross-platform. A developer reading this immediately understands the tech decisions.

---

### 2
> Secure local authentication and desktop automation for accounting workflows. C# / .NET 8 / Avalonia, PBKDF2, audit log, dual-database (SQLite + PostgreSQL).

**Why:** leads with the security angle (relevant for the target domain — sensitive fiscal data) and then lists the stack. Good for recruiters looking at security-conscious engineering.

---

### 3
> .NET 8 + Avalonia desktop app with Clean Architecture, MVVM, audit-trail login, and cross-platform installers (MSI, AppImage, DEB).

**Why:** leads with the stack, good for developers evaluating the architecture pattern. Less context about the business domain.

---

## Naming recommendation

**Use `protons-desktop` as the repository name** and description option **#1** as the About field.

**Rationale:**
- `protons` alone is ideal but risks name collision. `protons-desktop` is still short, maps directly to the product, and signals "application" rather than "library."
- Description #1 is the most information-dense option without being verbose — it answers the three questions a technical reader asks in the first 3 seconds: *what does it do, what is it built with, does it run on my platform.*

---

## Phase 2 — root cleanup (for future consideration)

The following files and folders at the root are internal/operational and reduce the professional appearance of the public root. They should not be deleted now, but should be moved or gitignored in a future cleanup phase:

| Item | Type | Suggested action |
|---|---|---|
| `INSTALADOR_DOSSIER.txt` (226 MB) | Large binary | Ensure it is gitignored; move to external storage |
| `Login.zip` (226 MB) | Large binary | Ensure it is gitignored; remove from tracked files |
| `win-test-results/` | Test artifacts | Add to `.gitignore` |
| `snapshot_estrutura_2026-02-02.txt` | Internal snapshot | Move to `docs/internal/` or gitignore |
| `LEIA-ME-TESTE-WINDOWS.txt` | Internal note | Move to `docs/internal/` |
| `RELATORIO_CORRECAO_PISCADAS_2026-02-04.md` | Internal report | Move to `docs/internal/` |
| `CHECKLIST_ANALISE_JANELA.md` | Internal checklist | Move to `docs/internal/` |
| `CHECKLIST_TESTE_WINDOWS.md` | Internal checklist | Move to `docs/internal/` |
| `imagem para referencia/` | Working folder | Rename (no spaces in folder names) + gitignore |
| `ENTREGA-COMPARTILHADA/` | Delivery folder | Gitignore or move to external storage |
| `RUN-WINDOWS-TESTS.cmd` / `.ps1` | Internal scripts | Move to `scripts/` or `INSTALADOR/testes/` |
| `TESTAR.cmd` | Internal shortcut | Move to `scripts/` |

**Priority order for cleanup:** large binaries first (size + PR noise), then internal reports, then scripts.
