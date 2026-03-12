# Guia de Instalação Multiplataforma (Windows e Linux)

## Objetivo
Padronizar a entrega do aplicativo com **atalho automático**, **pastas corretas** e **dependências resolvidas** para Windows e Linux.

## Princípio
- **Mesmo código** (Avalonia + .NET 8)
- **Dois pacotes**: um para Windows, outro para Linux

## Entregáveis esperados
- Windows: instalador (EXE/MSI) + atalho na área de trabalho
- Linux: pacote (DEB/AppImage) + atalho no menu
- App com dados locais em pasta correta de cada sistema

## Pastas padrão (dados locais) — decisão definitiva
- **Windows**: `%AppData%\Protons` (preferencial).  
- **Linux**: `XDG_DATA_HOME/Protons` (padrão), com fallback em `~/.local/share/Protons`.

## Prompt para a equipe (Windows)
Copie e envie para a equipe responsável:

```
Objetivo: gerar instalador Windows do app Protons (Avalonia + .NET 8) com atalho automático e pasta correta.

Requisitos:
1) Publicar para Windows (win-x64) em modo self-contained.
2) Criar instalador (MSI ou EXE) que:
   - Instale em C:\Program Files\Protons (ou equivalente)
   - Crie atalho no Desktop e Menu Iniciar
   - Inclua ícone do aplicativo (logo_protons.png convertido para .ico)
3) Garantir que o app use `%AppData%\Protons` para dados locais.

Comandos base (exemplo):
- dotnet publish Protons.UI/Protons.UI.csproj -c Release -r win-x64 --self-contained true

Entrega:
- Instalador pronto + instruções de instalação
- Teste: instalar em Windows limpo e abrir pelo atalho
```

## Prompt para a equipe (Linux)
Copie e envie para a equipe responsável:

```
Objetivo: gerar pacote Linux do app Protons (Avalonia + .NET 8) com atalho no menu.

Requisitos:
1) Publicar para Linux (linux-x64) em modo self-contained.
2) Gerar pacote .deb ou AppImage.
3) Criar atalho (.desktop) com:
   - Nome: Protons - Login
   - Ícone: logo_protons.png
   - Exec: caminho do executável
4) Garantir dados locais em `XDG_DATA_HOME/Protons` (fallback `~/.local/share/Protons`).

Comandos base (exemplo):
- dotnet publish Protons.UI/Protons.UI.csproj -c Release -r linux-x64 --self-contained true

Entrega:
- Pacote pronto + instruções de instalação
- Teste: instalar em Linux limpo e abrir pelo menu
```

## Checklist de validação (pós-entrega)
- [ ] Atalho criado automaticamente (Desktop/Menu)
- [ ] App abre sem terminal
- [ ] Pasta de dados criada no local correto
- [ ] Logo exibida no login
- [ ] Login funciona e gera logs locais
