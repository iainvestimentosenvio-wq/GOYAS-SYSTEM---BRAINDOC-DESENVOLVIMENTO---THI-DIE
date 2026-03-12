# Ativos e Icones

Objetivo
Padronizar a geracao e o uso de icones para Linux e Windows.

Fonte principal
- `INSTALADOR/ativos/icons/source/logo_icone.png`
- Fallback: `Login/Protons.UI/Assets/Brand/logo_protons.png`

Scripts
- `INSTALADOR/ativos/icons/generate-icons-pro.sh`
- `INSTALADOR/ativos/icons/generate-icons.sh`
- `INSTALADOR/ativos/icons/generate-ico.sh`

Saida
- PNGs: `INSTALADOR/ativos/icons/png/`
- ICO: `INSTALADOR/windows/ativos/logo_protons.ico`

Uso recomendado
```bash
cd /home/u/Documentos/PROJETO\ PROTONS/INSTALADOR/ativos/icons
./generate-icons-pro.sh --style simbolo --logo-size 248
./generate-ico.sh
```

Zonas criticas
- Se o `.ico` faltar, MSI/EXE fica sem icone customizado.
- PNG 256x256 precisa existir para AppImage/DEB.
