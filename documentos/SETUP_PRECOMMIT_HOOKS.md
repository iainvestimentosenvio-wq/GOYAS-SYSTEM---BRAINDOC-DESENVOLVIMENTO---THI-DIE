# Pre-commit Hook Setup

## Purpose

Enforce documentation mirroring rules BEFORE allowing commits. The hook verifies that:
1. Every modified .cs file has a corresponding .md file
2. Indices are updated when files are added/changed
3. No empty documentation files exist

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

The pre-commit hook will:

### Check 1: Documentation Mirror Verification
- Get list of staged .cs files: `git diff --cached --name-only --diff-filter=ACMR | grep '.cs$'`
- For each .cs file, verify corresponding .md exists in `documentos/doc_login/`
- Example: `App/Login/AuthService.cs` → `documentos/doc_login/App/Login/AuthService.md`

### Check 2: Index Updates
- If any files in `App/` are staged, verify that indices are also staged:
  - `documentos/DOCS_OVERVIEW.md`
  - Module-specific INDEX.md (e.g., `documentos/doc_login/INDEX.md`)

### Check 3: Empty Documentation
- Verify all staged .md files have content beyond templates
- Minimum: Must include "## Objetivo" and "## Como funciona" sections

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
   - `App/Login/` not `app/login/`
   - `documentos/doc_login/` not `Documentos/Doc_Login/`

2. Check that .md files are staged with `git add`
   - Hook only checks **staged** files
   - Unstaged changes are ignored

3. Verify minimum content requirements
   - Must have `## Objetivo` section
   - Must have `## Como funciona` section

### Hook script location

- Primary location: `.git/hooks/pre-commit`
- Template location: `scripts/pre-commit-hook-template.sh` (in repository)
- Install script: `scripts/setup-git-hooks.ps1`

### On Linux/Mac: Hook not executable

```bash
# Make hook executable
chmod +x .git/hooks/pre-commit

# Verify
ls -l .git/hooks/pre-commit
# Should show: -rwxr-xr-x
```

## Verification

### Test 1: Hook blocks commit without docs

```bash
# Create C# file without documentation
echo "public class Test {}" > App/Login/Test.cs
git add App/Login/Test.cs

# Try to commit (should FAIL)
git commit -m "test"

# Expected output:
# ❌ COMMIT BLOCKED: Documentation requirements not met
# Missing or unstaged documentation:
# App/Login/Test.cs → documentos/doc_login/App/Login/Test.md (MISSING)
```

### Test 2: Hook passes with proper docs

```bash
# Create both .cs and .md
echo "public class Test {}" > App/Login/Test.cs
echo "# Test
## Objetivo
Test class

## Como funciona
Simple test class" > documentos/doc_login/App/Login/Test.md

# Stage both files
git add App/Login/Test.cs
git add documentos/doc_login/App/Login/Test.md

# Commit (should PASS)
git commit -m "feat: Add Test class"

# Expected output:
# ✅ All documentation requirements met
```

## Alternative: Git Commit Template

If hooks are problematic on your system, enable commit message template as fallback:

```bash
git config commit.template .gitmessage
```

This displays a template with documentation checklists on every commit, but doesn't block commits (advisory only).

## Git Config for Hooks

View current hook settings:
```bash
git config --list | grep -i hook
```

Disable hooks temporarily (not recommended):
```bash
git config core.hooksPath /dev/null  # Linux/Mac
git config core.hooksPath NUL        # Windows
```

Re-enable hooks:
```bash
git config core.hooksPath .git/hooks
```

## Documentation Requirements

### Minimum .md Content

Every .md file must include:

```markdown
## Objetivo
[One sentence describing the class/method purpose]

## Como funciona
[Detailed explanation of how it works]

## Inputs e Outputs
[Parameters and return values]

## Dependências
[What this depends on]

## Como testar
[Testing instructions]
```

### Complete Example

See `documentos/TEMPLATES/TEMPLATE_DOC_ARQUIVO.md` for full template.

## Maintenance

### When to Update Hooks

- When documentation requirements change: Update `scripts/pre-commit-hook-template.sh`
- When project structure changes: Update `.gitignore` and hook path patterns
- When new modules added: Update hook to check module-specific indices

### How to Update Hooks

1. Edit `scripts/pre-commit-hook-template.sh`
2. Run `.\scripts\setup-git-hooks.ps1` to reinstall
3. Test with new documentation requirements

## Performance Notes

- Hook runs for every commit with C# files
- Typically executes in <1 second
- If slow: Check for network drives or antivirus software blocking I/O

## See Also

- [README.md](README.md) - Project overview
- [.cursorrules](.cursorrules) - AI assistant rules (includes doc mirroring)
- [CLAUDE.md](CLAUDE.md) - Original operational guidelines
- [.codex/PROJECT_RULES.md](.codex/PROJECT_RULES.md) - Mandatory project rules
