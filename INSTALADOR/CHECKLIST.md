# Instalador — Guia Operacional

Fonte única de operação do instalador. Status técnico em `saida/status/latest-status.json`.

---

## Comandos principais

### Validação Windows (Win10 + Win11)
```bash
bash run-windows-base-gate.sh
```
- Testa MSI e Inno em ambas as VMs
- Duração: 40–90 min
- Pré-requisito: artefatos atualizados (MSI/EXE)

### Build automático Linux → VM
```bash
bash build-and-run-windows-gate.sh
```
- Rebuild de EXE e MSI na VM
- Use quando preflight falhar

### Linux
```bash
# Build completo (ícones + DEB + AppImage)
bash build-linux-completo.sh

# Ou individual:
bash linux/appimage/build-appimage.sh
bash linux/deb/build-deb.sh
```

### Windows (na VM)
```powershell
# MSI
.\windows\scripts\build-msi.ps1

# Inno
.\windows\scripts\build-inno.ps1
```

---

## Documentação canônica

| Doc | Conteúdo |
|-----|----------|
| `LEIA-ME-PRIMEIRO.txt` | Comandos e variáveis |
| `documentos/README.md` | Índice do instalador |
| `documentos/comum.md` | Versão, SBOM, diretórios |
| `documentos/testes.md` | Estratégia de testes |
| `documentos/linux-*.md` | AppImage, DEB |
| `documentos/windows-*.md` | WiX, Inno |

---

## Status de release (referência)

- **Técnico:** GO (rodada FINAL2 validada)
- **Comercial:** NO-GO (certificado corporativo pendente)
- **Evidências:** `saida/validacao-windows-FINAL2-*/`
