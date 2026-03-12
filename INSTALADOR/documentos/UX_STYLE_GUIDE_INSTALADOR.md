# UX STYLE GUIDE INSTALADOR

## Objetivo

Garantir painel de instalacao moderno, profissional e coerente com a identidade visual do login.

## Diretrizes visuais

1. Identidade:
- Icone oficial: `windows/ativos/logo_protons.ico`.
- Fundo oficial do wizard: `Login/Protons.UI/Assets/Brand/login_bg.png`.
- Sem distorcao de imagem: usar recorte central com proporcao preservada.

2. Pipeline de assets:
- Script oficial: `windows/scripts/prepare-inno-ux-assets.ps1`.
- Saidas obrigatorias:
  - `windows/ativos/installer/wizard_back_light.png` (140x459).
  - `windows/ativos/installer/wizard_back_dark.png` (140x459).
  - `windows/ativos/installer/wizard_small_logo_light.png` (55x55).
  - `windows/ativos/installer/wizard_small_logo_dark.png` (55x55).

3. Tom visual:
- Layout limpo, profissional e orientado a confianca.
- Texto curto, direto e em portugues.
- Foco em legibilidade sobre efeitos visuais exagerados.
- Usar estilo dinamico do Inno para acompanhar tema do Windows (light/dark).

4. Feedback de instalacao:
- Exibir etapa atual.
- Exibir tempo estimado restante com fallback `calculando...`.
- Exibir confirmacao clara ao cancelar.

5. Fluxos de falha:
- Erro deve informar o que aconteceu e qual acao o usuario pode tomar.
- Mensagens de teste (failpoint/cancel-test) devem permanecer explicitas e rastreaveis.

## Escopo por instalador

- Inno: UX premium dinamica (light/dark, fundo oficial, copy PT-BR, progresso/ETA, cancelamento gracioso).
- MSI: baseline de branding e consistencia (sem customizacao visual avancada neste ciclo).
