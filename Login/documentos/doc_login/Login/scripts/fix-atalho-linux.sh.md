# fix-atalho-linux.sh

## Objetivo
Criar/atualizar o atalho do Protons no Linux **apenas para desenvolvimento**, evitando duplicidade quando o atalho do sistema ja existe.

## Comportamento atual
- **Padrao**: se existir `/usr/share/applications/protons-login.desktop`, o script **nao cria** atalho do usuario.
- **`--system`**: instala icones e grava atalho no sistema (requer root).
- **`--user`**: força criar atalho do usuario em `~/.local/share/applications`.
- Sempre grava `Exec=/usr/bin/protons`, `StartupNotify=true` e `StartupWMClass=Protons.UI`.

## Impacto
- Evita dois atalhos iguais no menu (sistema + usuario).
- Garante WM class correta para regras de janela.

## Como usar
```bash
# Sistema (requer sudo/root)
./scripts/fix-atalho-linux.sh --system

# Usuario (forcado, se necessario)
./scripts/fix-atalho-linux.sh --user
```

## Como validar
- Verificar atalho do sistema:
  `cat /usr/share/applications/protons-login.desktop`
- Testar launcher:
  `gtk-launch protons-login.desktop`
