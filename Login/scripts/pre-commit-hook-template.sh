#!/bin/bash
# Pre-commit hook for PROJETO PROTONS
# Enforces documentation mirroring rules

set -e

echo "🔍 Checking documentation mirror compliance..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Get staged C# files in Login projects or Painel Principal
STAGED_CS_FILES=$(git diff --cached --name-only --diff-filter=ACMR | grep -E '^(Login/Protons\.(Core|Infrastructure|UI)/.*\.cs|painel principal/.*\.cs)$' || true)

if [ -z "$STAGED_CS_FILES" ]; then
    echo "✅ No Login C# files staged, skipping documentation check"
    exit 0
fi

echo "📝 Found staged C# files, verifying documentation..."

MISSING_DOCS=()
EMPTY_DOCS=()

# Check each C# file for corresponding .md
while IFS= read -r cs_file; do
    if [[ "$cs_file" == painel\ principal/* ]]; then
        # Example: painel principal/codigos/painel_principal/interface/PainelView.axaml.cs
        # Doc: painel principal/documentos/doc_painel_principal/codigos/painel_principal/interface/PainelView.axaml.cs.md
        relative_path="${cs_file#painel principal/}"
        md_file="painel principal/documentos/doc_painel_principal/${relative_path}.md"
    else
        # Example: Login/Protons.Core/Login/servicos/AuthService.cs
        # Doc: Login/documentos/doc_login/Login/Protons.Core/Login/servicos/AuthService.cs.md
        md_file="Login/documentos/doc_login/${cs_file}.md"
    fi

    if [ ! -f "$md_file" ]; then
        MISSING_DOCS+=("$cs_file → $md_file (MISSING)")
    else
        # Check if doc file has minimum content
        if ! grep -q "## Objetivo" "$md_file" || ! grep -q "## Responsabilidades" "$md_file"; then
            EMPTY_DOCS+=("$md_file (EMPTY or INCOMPLETE)")
        fi

        # Verify doc file is staged
        if ! git diff --cached --name-only | grep -q "^$md_file$"; then
            MISSING_DOCS+=("$cs_file → $md_file (EXISTS but NOT STAGED)")
        fi
    fi
done <<< "$STAGED_CS_FILES"

# Report results
if [ ${#MISSING_DOCS[@]} -ne 0 ] || [ ${#EMPTY_DOCS[@]} -ne 0 ]; then
    echo -e "${RED}❌ COMMIT BLOCKED: Documentation requirements not met${NC}"
    echo ""

    if [ ${#MISSING_DOCS[@]} -ne 0 ]; then
        echo -e "${YELLOW}Missing or unstaged documentation:${NC}"
        printf '%s\n' "${MISSING_DOCS[@]}"
        echo ""
    fi

    if [ ${#EMPTY_DOCS[@]} -ne 0 ]; then
        echo -e "${YELLOW}Empty or incomplete documentation:${NC}"
        printf '%s\n' "${EMPTY_DOCS[@]}"
        echo ""
    fi

    echo "📚 Required actions:"
    echo "  1. Create/update missing .md files using templates in Login/documentos/TEMPLATES/"
    echo "  2. Ensure each .md has '## Objetivo' and '## Responsabilidades' sections"
    echo "  3. Stage documentation files: git add <file.md>"
    echo ""
    echo "⚠️  To bypass this check (EMERGENCY ONLY): git commit --no-verify"
    exit 1
fi

echo -e "${GREEN}✅ All documentation requirements met${NC}"
exit 0
