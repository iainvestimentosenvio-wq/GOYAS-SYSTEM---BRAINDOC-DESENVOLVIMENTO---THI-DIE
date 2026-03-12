# Desabilitar Diagnósticos de Markdown no VSCode

Se os problemas nos arquivos .md ainda aparecerem, faça isto:

## Opção 1: Desinstalar Extensões (RECOMENDADO)

1. **Abra Extensions (Ctrl+Shift+X)**
2. **Procure e desinstale:**
   - `Code Spell Checker` (streetsidesoftware.code-spell-checker)
   - `Markdown Linter` (davidanson.vscode-markdownlint)
   - `Markdown All in One` (yzhang.markdown-all-in-one)

3. **Recarregue VSCode:**
   - Ctrl+Shift+P → "Developer: Reload Window"

## Opção 2: Desabilitar no Workspace

Se não quer desinstalar, desabilite para este projeto:

1. **Ctrl+Shift+X** (Extensions)
2. **Procure a extensão** (ex: Code Spell Checker)
3. **Clique na extensão → "Disable (Workspace)"**
4. **Recarregue VSCode**

## Opção 3: Editar settings.json manualmente

Abra `.vscode/settings.json` e adicione:

```json
{
  "cSpell.enabled": false,
  "markdownlint.enable": false,
  "cSpell.ignorePaths": ["**/*.md", "**/documentos/**"],
  "[markdown]": {
    "editor.diagnostics.enabled": false
  }
}
```

## Opção 4: Limpar Cache do VSCode

```bash
# Linux
rm -rf ~/.config/Code/Cache
rm -rf ~/.config/Code/CachedData

# macOS
rm -rf ~/Library/Application\ Support/Code/Cache
rm -rf ~/Library/Application\ Support/Code/CachedData

# Windows
rmdir %APPDATA%\Code\Cache /s /q
rmdir %APPDATA%\Code\CachedData /s /q
```

Depois reabra VSCode.

## Opção 5: Usar Global settings.json

Se o workspace settings não funcionar, edite as configurações globais:

**Ctrl+Shift+P → "Preferences: Open Settings (JSON)"**

Adicione:

```json
{
  "cSpell.enabled": false,
  "[markdown]": {
    "editor.diagnostics.enabled": false
  }
}
```

---

**Recomendação:** Use **Opção 1** (desinstalar) para remover completamente o problema.
