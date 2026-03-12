# Diagrama do Instalador

Como ver o diagrama
- Abra `diagrama_instalador.html` no navegador.
- Ou abra `diagrama_instalador.svg` direto no navegador.

Resumo do fluxo

1. **Leitura de versao**
   - `comum/version.env` fornece `PROTONS_VERSION` para todos os builds.

2. **Publish do .NET**
   - `dotnet publish` gera binarios em `Login/publish/<RID>/`.

3. **Build especifico por plataforma**
   - **AppImage**: `build-appimage.sh` monta AppDir e executa `appimagetool`.
   - **DEB**: `build-deb.sh` preenche template e executa `dpkg-deb`.
   - **MSI**: `build-msi.ps1` gera `Variables.wxi` e compila via WiX.
   - **Inno**: `build-inno.ps1` compila `.iss` via ISCC.

4. **Artefatos finais**
   - Todos vao para `INSTALADOR/saida/<plataforma>/`.

5. **Testes e evidencias**
   - Scripts em `testes/` validam pacotes.
   - `collect-test-evidence.sh` coleta logs e metadados.

Fluxo visual (resumido)
```
version.env --> dotnet publish --> build script --> INSTALADOR/saida/
                                        |
                                        v
                                    testes/
```

Nota
- O preview nativo do editor pode abrir SVG como texto.
- Use o HTML para visualizar o desenho.
