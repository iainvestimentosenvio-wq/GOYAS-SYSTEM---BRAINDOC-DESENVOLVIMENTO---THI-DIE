# Ferramenta: Ancorar PDF

Editor de âncoras para extração de dados de PDFs. Para abrir a tela: painel principal → tarefa Ancorar PDF ou wizard "+ Nova tarefa" → Ancorar PDF.

## Estrutura

- `interface/`: Views/controles e integração visual no painel.
- `modelos_de_visao/`: ViewModels e estado da ferramenta.
- `dominio/`: Regras de negócio da ancoragem de PDF.
- `infraestrutura/`: Acesso a arquivos/serviços externos.
- `documentacao/`: Checklist e documentação técnica da ferramenta.

## Contratos ja definidos
- `dominio/AncorarPdfContratos.cs`:
  - `AncorarPdfConfig`,
  - `AncorarPdfExecucaoCiclo`,
  - `ArquivoProcessadoCiclo`,
  - `PoliticaAcessoTarefa`,
  - `FerramentaAncorarPdfIds`,
  - `SelecaoNomeAproximadoPorCiclo`.

## Defaults fechados para MVP
- Tool id canonico: `ancorar_pdf`.
- Retry tecnico: `3` tentativas com backoff `5s, 20s, 60s`.
- Subpastas: desabilitado por padrao (toggle por tarefa).
- Idempotencia por ciclo: `1` execucao por nome esperado.

## Checklist 02 (UX) - automacao
- Script baseline/full:
  - `Login/scripts/checklist02_ancorar_pdf_validate.sh`
- Sync de evidencia no markdown:
  - `Login/scripts/checklist02_ancorar_pdf_sync_md.sh`
- Wrapper Windows:
  - `Login/scripts/checklist02_ancorar_pdf_validate.ps1`
