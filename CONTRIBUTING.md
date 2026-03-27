# Contributing to Protons

Thank you for your interest in contributing.

## Before you start

- Read [`INDEX.md`](INDEX.md) to understand the project structure and architectural decisions.
- Read the module-specific guide: [`Login/documentos/doc_login/GUIA_DE_TRABALHO.md`](Login/documentos/doc_login/GUIA_DE_TRABALHO.md)
- Make sure you understand the **mirroring rule**: every code file must have a corresponding documentation file in the same tree structure.

## Development environment

**Required:**
- .NET 8 SDK
- Git

**Recommended IDE:** Visual Studio 2022, Rider, or VS Code with the C# Dev Kit extension.

**Verify your setup:**
```bash
dotnet --version   # must show 8.x
dotnet build Login/Protons.sln -c Release
dotnet test Login/Protons.sln -c Release
```

## Branching and commits

- Work on a feature branch: `feat/<module>/<short-description>`
- Bug fixes: `fix/<module>/<short-description>`
- Documentation only: `docs/<short-description>`
- Infrastructure/tooling: `chore/<short-description>`

Commit messages follow the format: `<type>: <imperative description>`

## Code rules

1. **UI never accesses the database directly.** Data flows: `UI → Core → Infrastructure`.
2. Every new code file requires a mirrored documentation file in the corresponding `documentos/` tree.
3. Namespaces must match folder structure.
4. No sensitive data (passwords, CPF, connection strings) in logs or committed files.
5. Run `dotnet build` and `dotnet test` before opening a PR — zero build errors expected.

## Pull request checklist

- [ ] Branch is up to date with `master`
- [ ] `dotnet build Protons.sln -c Release` passes with 0 errors
- [ ] `dotnet test Protons.sln -c Release` — no new failures introduced
- [ ] Mirrored documentation updated for every changed code file
- [ ] No sensitive data committed (check `.env.example` for reference)
- [ ] PR description explains *what* changed and *why*

## Documentation standard

Each mirrored doc must include at minimum:
- File objective
- Responsibilities
- Dependencies (services/repos used)
- Main flow (summary)
- Edge cases / points of attention
- How to test (manual or automated)

## Questions

Open an issue or refer to the internal documentation in `Login/documentos/doc_login/`.
