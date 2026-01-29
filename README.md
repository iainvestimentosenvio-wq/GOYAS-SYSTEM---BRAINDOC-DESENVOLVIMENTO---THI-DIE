# PROJETO PROTONS

WPF Desktop Application for secure user authentication and management.

## Overview

- **Platform**: Windows (offline-only)
- **Framework**: .NET Framework 4.8
- **Architecture**: MVVM with strict layer separation
- **Database**: SQLite (local)
- **UI**: WPF with MaterialDesignInXamlToolkit

## Current Status

**Planning/Documentation Phase** - No code implementation yet.

Comprehensive documentation available in `documentos/doc_login/`:
- Requirements and specifications
- Architecture and design
- Security and audit guidelines
- Testing strategy

## Project Structure

```
PROJETO PROTONS/
├── .codex/                 # Architecture rules and policies
├── .vscode/                # VSCode configuration
├── documentos/             # Documentation (mirrors code structure)
│   ├── doc_login/          # Login module documentation
│   └── TEMPLATES/          # Documentation templates
├── App/                    # Source code (empty - planning phase)
│   └── Login/              # Login module structure
│       ├── UI/             # Views and ViewModels
│       ├── Core/           # Business logic and services
│       └── Infrastructure/ # Data access and repositories
└── scripts/                # Utility scripts
```

## Critical Rules

### Documentation Mirroring (MANDATORY)

Every code file MUST have a corresponding documentation file:
- Code: `App/Login/AuthService.cs`
- Doc: `documentos/doc_login/App/Login/AuthService.md`

**Before ANY commit**:
1. Create/update .md for each .cs file
2. Update `documentos/DOCS_OVERVIEW.md`
3. Update module INDEX (e.g., `documentos/doc_login/INDEX.md`)

### Architecture Rules

- **Layer Flow**: UI → Core → Infrastructure (one-way only)
- **No Direct DB Access**: ViewModels must NOT access SQLite
- **Service Isolation**: Core services must NOT reference WPF

### Security Requirements

- **Password Storage**: PBKDF2 + salt (NEVER plaintext)
- **Lockout**: 5 failed attempts → 30 second lockout
- **Audit Events**: All critical actions logged
- **No Sensitive Logs**: Never log passwords, full CPF, etc.

## Setup

### Prerequisites

- Windows 10+
- Visual Studio 2019/2022 Community Edition (recommended) OR VSCode
- .NET Framework 4.8 SDK
- Git

### Initial Setup

1. Clone repository (or initialize git if starting fresh)
```bash
git init
```

2. Install Git hooks (enforces documentation rules)
```powershell
.\scripts\setup-git-hooks.ps1
```

3. Install VSCode extensions (if using VSCode)
```bash
code --install-extensions
```

4. Copy environment template
```bash
cp .env.example .env
```

### Complete Setup (Automated)

Run the complete setup script:
```powershell
.\scripts\complete-setup.ps1
```

This will:
- Verify Git installation
- Initialize repository if needed
- Configure Git settings
- Install pre-commit hooks
- Create .env from template
- Install VSCode extensions
- Verify setup completeness

### Development with VSCode

**Important**: VSCode support for WPF is limited. For full WPF development (visual designers, XAML intellisense), use Visual Studio.

VSCode setup is suitable for:
- Documentation work
- Light code editing
- Code review
- Scripts and automation

### Development with Visual Studio

Recommended IDE for WPF development:
1. Open `.sln` file when created
2. Install Extensions: MaterialDesignInXamlToolkit
3. Build with F5

## Documentation

All documentation in `documentos/`:
- `DOCS_OVERVIEW.md` - Central index
- `doc_login/` - Login module documentation
- `TEMPLATES/` - Templates for new documentation

**Key Documents**:
- [Vision and Overview](documentos/doc_login/00_visao_geral.md)
- [Requirements](documentos/doc_login/01_requisitos.md)
- [Architecture](documentos/doc_login/05_arquitetura_do_codigo.md)
- [Security](documentos/doc_login/04_seguranca_e_auditoria.md)
- [Pre-commit Hooks](documentos/SETUP_PRECOMMIT_HOOKS.md)

## Scripts

- `scripts/verify-doc-mirror.ps1` - Verify documentation completeness
- `scripts/setup-git-hooks.ps1` - Install pre-commit hooks
- `scripts/complete-setup.ps1` - Automated complete setup
- `scripts/verify-setup.ps1` - Verify setup completeness

## Verification

Check documentation mirror:
```powershell
.\scripts\verify-doc-mirror.ps1
```

Auto-fix missing docs:
```powershell
.\scripts\verify-doc-mirror.ps1 -Fix
```

Verify complete setup:
```powershell
.\scripts\verify-setup.ps1
```

## Build (Future)

When code exists:
```bash
# VSCode
Ctrl+Shift+B

# Visual Studio
F5 (Build and Run)

# Command Line
msbuild Protons.sln /p:Configuration=Debug
```

## Testing (Future)

Unit tests:
```bash
dotnet test
```

## Contributing

1. Read `.codex/PROJECT_RULES.md`
2. Follow MVVM architecture
3. **Document before/with code** (not after)
4. Run `verify-doc-mirror.ps1` before committing
5. Pre-commit hook will enforce documentation rules

## Development Workflow

### Before Starting Work
```bash
# Update from remote
git pull origin main

# Create feature branch
git checkout -b feature/your-feature-name
```

### While Coding
1. Create C# code file in `App/Login/`
2. Immediately create/update corresponding .md in `documentos/doc_login/App/Login/`
3. Document: objective, functionality, inputs/outputs, dependencies, testing

### Before Committing
```powershell
# Verify documentation is complete
.\scripts\verify-doc-mirror.ps1

# Check git status
git status

# Stage files
git add .

# Commit (will be checked by pre-commit hook)
git commit
```

### Commit Message Template
The project includes a commit message template with checklists for:
- Documentation mirroring
- Architecture compliance
- Security requirements

Template is automatically used: `git commit` opens template in editor.

## Configuration Files

- `.cursorrules` - AI assistant behavior rules (strict documentation enforcement)
- `.editorconfig` - Code formatting standards (C#, XAML, XML)
- `.env.example` - Environment variable template
- `.gitignore` - Git ignore patterns (protects sensitive data)
- `.gitmessage` - Commit message template
- `.vscode/settings.json` - VSCode configuration
- `.vscode/extensions.json` - Recommended extensions
- `.vscode/tasks.json` - Build tasks
- `.vscode/launch.json` - Debug configuration

## Pre-commit Hooks

The project enforces documentation rules via Git pre-commit hooks. These hooks:
- Block commits without corresponding .md files
- Verify .md files are not empty
- Ensure indices are updated

**Important**: Hooks run on Windows with Git Bash. To test:
```bash
# Create test file
echo "test" > App/Login/Test.cs
git add App/Login/Test.cs
git commit -m "test"
# Expected: BLOCKED with error message
```

For manual hook installation or troubleshooting, see:
```
documentos/SETUP_PRECOMMIT_HOOKS.md
```

## Troubleshooting

### Pre-commit hook not running
- Verify Git Bash is installed (required on Windows)
- Check `.git/hooks/pre-commit` exists
- Run: `.\scripts\setup-git-hooks.ps1`

### VSCode IntelliSense not working
- Install `ms-dotnettools.csharp` extension
- Wait for OmniSharp to initialize
- If still not working, use Visual Studio instead

### Documentation mirror verification fails
- Run: `.\scripts\verify-doc-mirror.ps1 -Fix`
- This auto-generates missing .md files from template
- Edit generated files with proper content

## License

[To be determined]

## References

- [WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
- [MVVM Pattern](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm)
- [.NET Framework 4.8](https://learn.microsoft.com/en-us/dotnet/framework/)
