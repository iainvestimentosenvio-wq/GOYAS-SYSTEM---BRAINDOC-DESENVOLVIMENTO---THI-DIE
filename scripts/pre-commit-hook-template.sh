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

# Get staged C# files
STAGED_CS_FILES=$(git diff --cached --name-only --diff-filter=ACMR | grep '\.cs$' || true)

if [ -z "$STAGED_CS_FILES" ]; then
    echo "✅ No C# files staged, skipping documentation check"
    exit 0
fi

echo "📝 Found staged C# files, verifying documentation..."

MISSING_DOCS=()
EMPTY_DOCS=()

# Check each C# file for corresponding .md
while IFS= read -r cs_file; do
    # Convert App/Login/AuthService.cs → documentos/doc_login/App/Login/AuthService.md
    md_file=$(echo "$cs_file" | sed 's|^App/|documentos/doc_login/App/|' | sed 's|\.cs$|.md|')

    if [ ! -f "$md_file" ]; then
        MISSING_DOCS+=("$cs_file → $md_file (MISSING)")
    else
        # Check if doc file is not empty and has minimum content
        if ! grep -q "## Objetivo" "$md_file" || ! grep -q "## Como funciona" "$md_file"; then
            EMPTY_DOCS+=("$md_file (EMPTY or INCOMPLETE)")
        fi

        # Verify doc file is staged
        if ! git diff --cached --name-only | grep -q "^$md_file$"; then
            MISSING_DOCS+=("$cs_file → $md_file (EXISTS but NOT STAGED)")
        fi
    fi
done <<< "$STAGED_CS_FILES"

# Check if indices are staged when App/ files change
STAGED_APP_FILES=$(git diff --cached --name-only | grep '^App/' || true)
if [ -n "$STAGED_APP_FILES" ]; then
    DOCS_OVERVIEW_STAGED=$(git diff --cached --name-only | grep '^documentos/DOCS_OVERVIEW.md$' || true)
    MODULE_INDEX_STAGED=$(git diff --cached --name-only | grep '^documentos/doc_login/INDEX.md$' || true)

    if [ -z "$DOCS_OVERVIEW_STAGED" ]; then
        MISSING_DOCS+=("documentos/DOCS_OVERVIEW.md (INDEX NOT UPDATED)")
    fi

    if [ -z "$MODULE_INDEX_STAGED" ]; then
        MISSING_DOCS+=("documentos/doc_login/INDEX.md (INDEX NOT UPDATED)")
    fi
fi

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
    echo "  1. Create/update missing .md files using templates in documentos/TEMPLATES/"
    echo "  2. Ensure each .md has '## Objetivo' and '## Como funciona' sections"
    echo "  3. Update documentos/DOCS_OVERVIEW.md"
    echo "  4. Update documentos/doc_login/INDEX.md"
    echo "  5. Stage documentation files: git add <file.md>"
    echo ""
    echo "⚠️  To bypass this check (EMERGENCY ONLY): git commit --no-verify"
    exit 1
fi

echo -e "${GREEN}✅ All documentation requirements met${NC}"
exit 0
