# 09 - Branding e Assets

## Objetivo
Padronizar a identidade visual do MVP local, mantendo **performance** e **rastreabilidade**. Os assets devem ser **reais e fornecidos pelo cliente**.

## Regras principais
- **Não inventar assets**: usar somente arquivos reais fornecidos.
- **Padronizar nomes e caminhos**:
  - `Protons.UI/Assets/Brand/logo_protons.png`
  - `Protons.UI/Assets/Brand/login_bg.png`

## Processo guiado
Para a coleta e importação, seguir o documento:
- `doc_login/10_coleta_e_importacao_de_assets.md`

## Observações
- Logo em **PNG** preferencialmente transparente.
- Imagem de fundo (background) em **JPG** (preferencial) ou **PNG** (quando necessário).
- O processo de importação é **manual** neste momento; scripts podem ser adicionados depois.
- Ajustar Build Action no Avalonia após importar.

## Atalho do Desktop (Linux)

### Localização
O atalho do sistema é instalado em:
- `/usr/share/applications/protons-login.desktop`

### Configuração
O `.desktop` do sistema usa:
- `Exec=/usr/bin/protons`
- `Icon=protons`
- `StartupNotify=true`
- `StartupWMClass=Protons.UI`

O script `Login/scripts/fix-atalho-linux.sh` e **apenas para desenvolvimento**. Ele:
- Nao cria atalho do usuario se o do sistema ja existir.
- Suporta `--system` (requer root) e `--user` (forca atalho do usuario).
- Instala os icones no tema `hicolor` quando executado.

### Como corrigir atalho quebrado
Se o atalho do sistema nao estiver funcionando:

```bash
cd "/home/u/Documentos/PROJETO PROTONS/Login"
./scripts/fix-atalho-linux.sh --system
```

### Verificar atalho
```bash
# Ver conteúdo do atalho
cat /usr/share/applications/protons-login.desktop

# Testar atalho
gtk-launch protons-login.desktop
```

### Troubleshooting
Problema: Atalho nao abre o aplicativo.  
Causa: Caminho do executavel incorreto ou binario nao foi publicado.  
Solucao: Rode `dotnet publish Protons.UI/Protons.UI.csproj -c Release -r linux-x64 --self-contained` e depois `./scripts/fix-atalho-linux.sh --system`.

Problema: Icone nao aparece.  
Causa: Caminho do logo incorreto.  
Solucao: Verificar se existe `/home/u/Documentos/PROJETO PROTONS/Login/Protons.UI/Assets/Brand/logo_protons.png`.
