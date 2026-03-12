# Linux DEB

Arquivos principais
- `INSTALADOR/linux/deb/build-deb.sh`
- `INSTALADOR/linux/deb/protons-template/DEBIAN/control`
- `INSTALADOR/linux/deb/protons-template/DEBIAN/postinst`
- `INSTALADOR/linux/deb/protons-template/DEBIAN/postrm`

DEBIAN scripts (papel)
- `control`: metadados do pacote (nome, versao, deps, descricao).
- `postinst`: atualiza desktop database e icon cache, remove atalho duplicado do usuario.
- `postrm`: atualiza desktop database e icon cache; em `purge` avisa sobre dados do usuario.
- Observacao: `postinst` usa `getent` para localizar HOME do `SUDO_USER`.

Build
```bash
bash INSTALADOR/linux/deb/build-deb.sh
```

Artefato
- `INSTALADOR/saida/deb/protons_<VERSION>_amd64.deb`

Arquivos criticos instalados
- Exec principal: `/opt/protons/Protons.UI`
- Symlink: `/usr/bin/protons` aponta para `/opt/protons/Protons.UI`
- Desktop entry: `/usr/share/applications/protons-login.desktop`

Instalacao
```bash
sudo dpkg -i INSTALADOR/saida/deb/protons_<VERSION>_amd64.deb
```

Desinstalacao
```bash
sudo dpkg -r protons
```

Testes
```bash
bash INSTALADOR/testes/linux/test-deb.sh
```

Evidencias
```bash
bash INSTALADOR/comum/scripts/collect-test-evidence.sh
```

Zonas criticas
- `DEBIAN/control`: metadados errados quebram instalacao.
- `postinst/postrm`: falhas impactam atalhos e cache de icones.
- Symlink `/usr/bin/protons`: se errado, atalhos nao funcionam.
