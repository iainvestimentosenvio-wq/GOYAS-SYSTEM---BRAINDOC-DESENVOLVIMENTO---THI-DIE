# Linux AppImage

Arquivos principais
- `INSTALADOR/linux/appimage/build-appimage.sh`
- `INSTALADOR/linux/appimage/AppRun`

AppRun (comportamento)
- Ajusta `PATH` e `LD_LIBRARY_PATH` para usar `AppDir/usr`.
- Define `XDG_DATA_HOME` (padrao `~/.local/share`) e `PROTONS_DATA_DIR`.
- Cria `PROTONS_DATA_DIR` e executa `Protons.UI`.

Build
```bash
bash INSTALADOR/linux/appimage/build-appimage.sh
```

Artefato
- `INSTALADOR/saida/appimage/Protons-<VERSION>-x86_64.AppImage`

Desktop entry
- Arquivo gerado: `protons-login.desktop` (raiz do AppDir e copiado para `usr/share/applications`)
- Exec esperado: `Protons.UI`
- Icon esperado: `protons`
- StartupWMClass esperado: `Protons.UI`

Icones
- Fonte: `INSTALADOR/ativos/icons/png/256x256.png`
- Destino no AppImage: `usr/share/icons/hicolor/256x256/apps/protons.png`

Validacao rapida
- Conferir `AppRun` executavel no AppImage.
- Conferir `protons-login.desktop` com `Exec=Protons.UI` e `Icon=protons`.

Zonas criticas
- `AppRun`: se quebrar, AppImage nao inicia pelo clique.
- `protons-login.desktop`: Exec/Icon incorretos quebram atalhos e menu.
- `appimagetool`: versao/flags podem impactar reproducibilidade.

Requisitos de runtime
- FUSE: `libfuse2`
- glibc 2.31+
- fontconfig e freetype

Instalar deps (Ubuntu 22.04+)
```bash
sudo apt install libfuse2 libfontconfig1 libfreetype6
```

Testes
```bash
bash INSTALADOR/testes/linux/test-appimage.sh
```

Evidencias
```bash
bash INSTALADOR/comum/scripts/collect-test-evidence.sh
```
