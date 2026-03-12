# Pre-commit Hook Setup

## Purpose
Enforce documentation mirroring rules BEFORE allowing commits. The hook verifies that:
1. Every modified .cs file in Login and Painel Principal has a corresponding .md file
2. No empty/incomplete documentation files exist

## Installation

### Automatic Installation (Recommended)

Run the setup script:

```powershell
# PowerShell
.\scripts\setup-git-hooks.ps1
```

```bash
# Bash (Git Bash on Windows)
bash scripts/setup-git-hooks.sh
```

Or use the complete setup script:
```powershell
.\scripts\complete-setup.ps1
```

### Manual Installation

1. Navigate to `.git/hooks/` directory
2. Create file named `pre-commit` (no extension)
3. Make it executable (Linux/Mac): `chmod +x .git/hooks/pre-commit`
4. Copy content from `scripts/pre-commit-hook-template.sh`

## Hook Behavior

### Check 1: Documentation Mirror Verification
- Get list of staged .cs files under `Login/Protons.*`
- For each .cs file, verify corresponding .md exists:
  - Login: `Login/documentos/doc_login/`
  - Painel Principal: `painel principal/documentos/doc_painel_principal/`
- Exemplo Login: `Login/Protons.Core/Login/servicos/AuthService.cs` → `Login/documentos/doc_login/Login/Protons.Core/Login/servicos/AuthService.cs.md`
- Exemplo Painel: `painel principal/codigos/painel_principal/interface/PainelView.axaml.cs` → `painel principal/documentos/doc_painel_principal/codigos/painel_principal/interface/PainelView.axaml.cs.md`

### Check 2: Empty Documentation
- Verify all staged .md files have minimum content
- Minimum: Must include "## Objetivo" and "## Responsabilidades" sections

### On Failure
- Commit is BLOCKED
- Error message shows which files are missing documentation
- Developer must create/update docs before committing

### Bypass (Emergency Only)
```bash
git commit --no-verify -m "Emergency commit"
```
**WARNING**: Only use `--no-verify` in emergencies. Creates technical debt and bypasses important validations.

## Troubleshooting

### Hook not running

**Problem**: Pre-commit hook is not being executed

**Solutions**:
1. Check file permissions (must be executable)
   ```bash
   ls -l .git/hooks/pre-commit
   chmod +x .git/hooks/pre-commit
   ```

2. Verify file has no extension (should be `pre-commit`, not `pre-commit.sh`)

3. Check shebang line: `#!/bin/bash` or `#!/usr/bin/env bash`

4. On Windows, ensure Git Bash is installed (hook script is Bash, not PowerShell)

5. Test hook execution:
   ```bash
   .git/hooks/pre-commit
   echo $?  # Should output 0 (success)
   ```

### False positives

**Problem**: Hook reports errors for valid documentation

**Solutions**:
1. Ensure file paths are correct (case-sensitive on Linux/Mac)
   - `Login/Protons.UI/` not `login/protons.ui/`
   - `Login/documentos/doc_login/` not `Login/Documentos/Doc_Login/`

2. Check that .md files are staged with `git add`
   - Hook only checks **staged** files
   - Unstaged changes are ignored

3. Verify minimum content requirements
   - Must have `## Objetivo` section
   - Must have `## Responsabilidades` section

### Hook script location

- Primary location: `.git/hooks/pre-commit`
- Template location: `scripts/pre-commit-hook-template.sh` (in repository)
- Install script: `scripts/setup-git-hooks.ps1`

## Verification

### Test 1: Hook blocks commit without docs

```bash
# Create C# file without documentation
echo "public class Test {}" > Login/Protons.Core/Test.cs
git add Login/Protons.Core/Test.cs

# Try to commit (should FAIL)
git commit -m "test"

# Expected output:
# ❌ COMMIT BLOCKED: Documentation requirements not met
# Missing or unstaged documentation:
# Login/Protons.Core/Test.cs → Login/documentos/doc_login/Login/Protons.Core/Test.cs.md (MISSING)
```

### Test 2: Hook passes with proper docs

```bash
# Create both .cs and .md
echo "public class Test {}" > Login/Protons.Core/Test.cs
echo "# Test\n## Objetivo\nTest class\n\n## Responsabilidades\nSimple test class" > Login/documentos/doc_login/Login/Protons.Core/Test.cs.md

# Stage both files
git add Login/Protons.Core/Test.cs
git add Login/documentos/doc_login/Login/Protons.Core/Test.cs.md

# Commit (should PASS)
git commit -m "feat: Add Test class"

# Expected output:
# ✅ All documentation requirements met
```

## Documentation Requirements

### Minimum .md Content

Every .md file must include:
- `## Objetivo`
- `## Responsabilidades`
