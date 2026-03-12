#!/bin/bash

# Remove problematic markdown extensions from VSCode
# These extensions cause false positives on .md documentation files

echo "🗑️  Removing problematic markdown extensions..."
echo ""

# Uninstall extensions that cause false positives
code --uninstall-extension streetsidesoftware.code-spell-checker
code --uninstall-extension davidanson.vscode-markdownlint
code --uninstall-extension yzhang.markdown-all-in-one

echo ""
echo "✅ Extensions removed!"
echo ""
echo "🔄 Reloading VSCode..."
echo "   Ctrl+Shift+P → Developer: Reload Window"
echo ""
echo "✨ Markdown files should now have NO false positives!"
