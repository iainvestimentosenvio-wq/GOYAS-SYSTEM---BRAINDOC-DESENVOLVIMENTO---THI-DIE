# UPDATE OFFLINE GUIA - Protons

DataUTC: 2026-02-15T10:54:00Z

## Como Atualizar Sem Internet

1. **Download manual** do instalador:
   - MSI: `Protons-1.1.0-x64.msi`
   - EXE (Inno): `ProtonsSetup-1.1.0.exe`

2. **Verificação de hash** (opcional mas recomendado):
   ```powershell
   Get-FileHash Protons-1.1.0-x64.msi -Algorithm SHA256
   ```
   Comparar com hash publicado em `hashes-sha256.txt`

3. **Instalação silenciosa**:
   - MSI: `msiexec /i Protons-1.1.0-x64.msi /qn`
   - Inno: `ProtonsSetup-1.1.0.exe /SILENT`

4. **Dados preservados automaticamente**
