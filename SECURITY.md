# Security Policy

## Supported versions

This project is in active development. Security fixes are applied to the current `master` branch only.

| Version | Supported |
|---|---|
| Latest (`master`) | Yes |
| Older snapshots | No |

## Scope

This policy covers the Protons application code, including:
- Authentication and credential handling (`Protons.Core`, `Protons.Infrastructure`)
- Password hashing (PBKDF2 + salt)
- Audit log integrity (hash chain)
- SQLite and PostgreSQL data access
- Installer scripts (Windows and Linux)

## Known design decisions

The following are intentional design choices, not vulnerabilities:

- **Local-only mode uses SQLite without network exposure.** The database file lives at `%AppData%\Protons\` (Windows) or `~/.local/share/Protons/` (Linux) and is not network-accessible by default.
- **Hash-chain audit log is tamper-detectable but not tamper-proof.** It detects record modification but does not prevent deletion. This is a documented limitation for the local/offline threat model.
- **No external authentication provider.** Protons manages credentials locally by design — it is not integrated with Active Directory, OAuth, or any cloud identity provider.

## Reporting a vulnerability

If you discover a security vulnerability in this project, **please do not open a public GitHub issue.**

Report it privately by contacting the maintainer directly. Include:
1. A clear description of the vulnerability
2. Steps to reproduce
3. Potential impact assessment
4. Any suggested fix (optional but appreciated)

You will receive an acknowledgement within 5 business days and a resolution timeline as soon as the severity is assessed.

## Security practices in the codebase

- Passwords are hashed with **PBKDF2 + random salt** — no plaintext storage at any layer
- Authentication errors return a **generic message** — no information about whether an email exists
- **Lockout policy:** 5 consecutive failures trigger a 30-second block (configurable)
- Sensitive fields (CPF, passwords, connection strings) are never written to log files
- `appsettings.json` with connection strings must not be committed — see `.gitignore` and `.env.example`
