# Auditoria Tecnica Enterprise - Ferramenta Ancorar PDF

Data da auditoria: **2026-03-01**  
Remedição de pilares: **2026-03-02** (440 testes, evidências confirmadas)  
Escopo: **analise de codigo + comportamento atual + benchmark externo**



**rodar o painel principal : **  
  
cd "../Login" && bash scripts/hot_reload_painel_[direto.sh](http://direto.sh) no-hot-reload

## 1) Contexto e objetivo

Esta auditoria avalia a funcionalidade **Ancorar PDF** de ponta a ponta para responder:

1. Como o sistema funciona hoje (fluxo real E2E).
2. O que ja esta robusto.
3. Onde estao bugs, fragilidades, gargalos e inconsistencias.
4. Quais melhorias priorizar para atingir maturidade enterprise alta (confiabilidade, performance, seguranca, UX e operacao).

## 2) Como o sistema funciona hoje (E2E)

Fluxo observado no codigo:

1. **Drop na regua de tempo** chama handler por ferramenta e, para `ancorar_pdf` (canônico), abre wizard basico.

Evidencia: `PainelViewModel.ReguaTempo.cs:447-521`.
2. **Wizard basico** (`AncorarPdfAgendamentoBasicoViewModel`) coleta dados de agendamento, pasta, PDF modelo e envia para etapa de ancoras.  
Evidencia: `AncorarPdfAgendamentoBasicoViewModel.cs:130-355`.
3. **Editor de ancoras** (`AncorarPdfConfiguracaoViewModel`) monta template e salva configuracao.  
Evidencia: `AncorarPdfConfiguracaoViewModel.cs:905-979`.
4. **Servico de configuracao** valida regra de negocio, timezone/DST, path policy e persiste.  
Evidencia: `AncorarPdfConfiguracaoService.cs:69-210`, `AncorarPdfPathPolicy.cs:28-50`.
5. **Scheduler runtime** faz polling, ordena por prioridade/criacao e despacha para fila.  
Evidencia: `AncorarPdfSchedulerRuntime.cs:205-253`.
6. **Fila + workers** processam itens com retry/timeout (Polly), lease e recovery.  
Evidencia: `AncorarPdfFilaExecucaoService.cs:75-130`, `AncorarPdfExecutionWorker.cs:64-87`.
7. **Motor real** executa descoberta de arquivo, extracao, validacao cliente, ancoragem espacial e persistencia de saida.  
Evidencia: `AncorarPdfMotorExecucao.cs:70-299`.

## 3) Baseline tecnico validado (testes e gates)

### 3.1 Resultado de testes executados

1. `dotnet test testes/Protons.Core.Tests --filter FullyQualifiedName~AncorarPdf`

Resultado: **205 pass / 0 fail** (medição 2026-03-02).
2. `dotnet test testes/Protons.Infrastructure.Tests --filter FullyQualifiedName~AncorarPdf`
Resultado: **235 pass / 0 fail** (medição 2026-03-02; total **440 testes** AncorarPdf).

Falhas historicas (ja resolvidas nesta rodada):

1. `PainelAncorarPdfChecklist01ViewModelTests.F1_DropCanonico...` (resolvido com `TimeProvider` deterministico).
2. `AncorarPdfChecklist02ExecutionFlowTests.C2_F10_DeveSalvarMetadadosEditadosPorAncora` (resolvido com persistencia da regra manual).

### 3.2 Resultado dos scripts de checklist

1. Checklist 01 (`checklist01_ancorar_pdf_validate.sh`): **PASS**.
2. Checklist 11 (`checklist11_fluxo_agendamento_validate.sh`): **PASS**.

Evidencias:

1. `Login/testes/TestResults/checklist_ancora_pdf/summary.md`
2. `Login/testes/TestResults/checklist_ancora_pdf/G4_Integracao_Painel_Base.log`
3. `Login/testes/TestResults/checklist11_fluxo_agendamento/summary.md`

### 3.3 Atualizacao pos-implementacao (2026-03-02)

Status consolidado apos execucao tecnica:

1. **P0-1 concluido**: modal avancado padronizado para `DateTimeOffset?` fim-a-fim, sem wrapper ambiguo (`DataSelecionada` date-only com offset=0).

Evidencia: `AncorarPdfConfiguracaoViewModel.cs:58-75`, binding em `AncorarPdfConfiguracaoView.axaml:75`.
2. **P0-2 concluido**: persistencia de `RegraNormalizacao` editada pelo usuario priorizada no save.  
Evidencia: `AncorarPdfConfiguracaoViewModel.cs:1593-1599`, `:1623`.
3. **P1 concluido (determinismo temporal de teste)**: `TimeProvider` adicionado no `PainelViewModel`/harness para remover flakiness de horario.  
Evidencia: `PainelViewModel.cs:173-186`, `:243-255`, `PainelAncorarPdfChecklist01TestHarness.cs:30-49`, `PainelAncorarPdfChecklist01ViewModelTests.cs:34-37`.
4. **P1 concluido (timezone no resumo)**: `ProgramadoPorResumo` no salvar usa timezone da configuracao da tarefa, nao timezone da maquina.  
Evidencia: `AncorarPdfConfiguracaoViewModel.cs:983`.
5. **P1 concluido (regressao DatePicker)**: teste dedicado para evitar retorno do erro `DateTime` x `DateTimeOffset?`.  
Evidencia: `PainelAncorarPdfChecklist02ViewModelTests.cs:194-222`.
6. **Tecnologia de fila confirmada**: `.NET Channels` ja esta em producao interna da fila e workers.  
Evidencia: `AncorarPdfFilaExecucaoService.cs:2`, `:43`, `:75`, `:191`, `AncorarPdfExecutionWorker.cs:43`, `:117`.
7. **P0 concluido (confiabilidade de enqueue)**: scheduler/job handler migrados para fluxo aguardado (`await`), removendo fire-and-forget no despacho.
Evidencia: `AncorarPdfJobHandler.cs:47`, `:87`, `AncorarPdfSchedulerRuntime.cs:203`, `:230`.
8. **P1 concluido (padrao temporal em repositorio)**: repositórios `AncorarPdf*` padronizados com injecao de `TimeProvider`.
Evidencia: `SqliteAncorarPdfExecucaoRepository.cs:20`, `PostgresAncorarPdfExecucaoRepository.cs:20`, `SqliteAncorarPdfConfiguracaoRepository.cs:20`, `PostgresAncorarPdfConfiguracaoRepository.cs:20`.
9. **P0 concluido (deduplicacao semantica por hash)**: `PayloadHashV2` removeu campos volateis (`ExecucaoId` e timestamp de execucao) e manteve `schemaVersion`.
Evidencia: `AncorarPdfExecucaoModels.cs:147-218`, `AncorarPdfMotorExecucao.cs:261-280`.
10. **P0 concluido (indice de deduplicacao por hash + migracao)**: schema atualizado para versao 16 (v13 indice dedup + v14 alias + v15 OCR + v16 unicidade condicional V1); indice **unico parcial** `UX_AncorarPdfSaida_Tarefa_PayloadHash_V1` em `(TarefaId, PayloadHashSha256) WHERE SchemaVersion = 1`.
Evidencia: `SqliteDb.cs` (`SchemaVersionAtual = 16`, `MigrarParaVersao16`, `UX_AncorarPdfSaida_Tarefa_PayloadHash_V1`), `PostgresDb.cs` (mesmos pontos), `AncorarPdfChecklist03PersistenceTests.cs` (`C3_G8_SalvarDuplicadoMesmoHashV1_DeveSerIdempotente`).
11. **P0 concluido (fail-fast temporal auditavel)**: repositorios `AncorarPdf*` deixam de mascarar datas invalidas com "agora" e passam a lançar `FormatException` com contexto da coluna.
Evidencia: `SqliteAncorarPdfExecucaoRepository.cs:398`, `SqliteAncorarPdfSaidaRepository.cs:182`, `PostgresAncorarPdfExecucaoRepository.cs:400`, `PostgresAncorarPdfSaidaRepository.cs:183`; teste `AncorarPdfChecklist03PersistenceTests.cs:608`.
12. **P1 concluido (UX picker PDF modal x wizard)**: modal avancado passou a selecionar apenas arquivo `.pdf` com filtro nativo; wizard manteve selecao de pasta com mensagem explicita.
Evidencia: `IAncorarPdfFilePicker.cs:5-10`, `AncorarPdfStorageProviderFilePicker.cs:33-62`, `AncorarPdfConfiguracaoViewModel.cs:750-753`, `:1081-1091`, `AncorarPdfAgendamentoBasicoViewModel.cs:379-387`, `AncorarPdfAgendamentoBasicoView.axaml:44`, `:103-110`, testes `PainelAncorarPdfChecklist02ViewModelTests.cs:251-289` e `AncorarPdfChecklist02ExecutionFlowTests.cs:54-77`.

1. **P1 concluido (matriz timezone/DST)**: suite C13 com 24 testes cobrindo UTC, America/Sao_Paulo e Europe/Berlin (spring forward + fall back 2026), binding DatePicker, cruzamento de meia-noite e normalizacao de offset.

Evidencia: `AncorarPdfChecklist13TimezoneMatrixCoreTests.cs` (TZ01–TZ12); `AncorarPdfChecklist13ViewModelTimezoneMatrixTests.cs` (VT01–VT12); `checklist13_timezone_matrix_validate.{sh,ps1}`; gates C13_G1–G3 PASS (2026-03-02).
14. **P1 concluido (erro tipado no modal de save)**: contrato estruturado com `code/category/details/correlationId/retryable` no fluxo de salvar configuração.
Evidencia: `AncorarPdfErrorCode.cs`, `AncorarPdfError.cs`, `AncorarPdfErrorCatalog.cs`, `AncorarPdfSaveOrchestrator.cs`, `AncorarPdfConfiguracaoViewModel.cs`, `PainelAncorarPdfChecklist02ViewModelTests.cs`.
15. **P2 concluido (deprecação de alias legado C14)**: alias `extrator_pdf` removido da identificação canônica da ferramenta; migração de dados aplicada no banco.
Evidencia: `AncorarPdfConfiguracaoModels.cs` (`FerramentaTarefaIds.EhAncorarPdf`), `SqliteDb.cs`/`PostgresDb.cs` (`MigrarParaVersao14`), `AncorarPdfChecklist01ContractTests.cs`.
16. **P1 concluido (OCR opcional robusto C15)**: OCR fallback (Tesseract + Docnet) integrado fim-a-fim (UI modal avancado -> save -> servico -> motor), com perfil configuravel (`OcrFallbackAtivo`, `OcrDpi`, `OcrLang`), hardening de runtime e erro tipado `ANCORA-NEG-OCR_INDISPONIVEL`.
Evidencia: `AncorarPdfConfiguracaoViewModel.cs`, `AncorarPdfConfiguracaoView.axaml`, `AncorarPdfSaveOrchestrator.cs`, `AncorarPdfConfiguracaoService.cs`, `AncorarPdfErroCodigos.cs`, `AncorarPdfErrorCatalog.cs`, `AncorarPdfExtratorTextoComOcrFallback.cs`, `AncorarPdfExtratorOcrTesseract.cs`, `AncorarPdfMotorExecucao.cs`, `PainelAncorarPdfChecklist02ViewModelTests.cs`, `AncorarPdfChecklist06MotorIntegrationTests.cs`, `AncorarPdfChecklist07EventoEmissaoTests.cs`.
17. **P2 concluido (renderer dedicado de preview C14-Renderer)**: camada `IPdfPreviewRenderer` criada no Core com modelos imutaveis (`PdfRenderRequest`, `PdfRenderResult` discriminated union `Sucedido`/`Falhou`, `PdfRenderMetrics` struct, `PdfRendererCapabilities`), `AncorarPdfNullPreviewRenderer` seguro; infra implementa `AncorarPdfDocnetRenderer` (PDFium via Docnet.Core, sem instalacao externa), `AncorarPdfGhostscriptRenderer` (extrai logica existente do `PreviewState`) e `AncorarPdfFallbackPreviewRenderer` (cadeia de fallback com `ViouFallback` e log); `AncorarPdfPreviewState` refatorado para injetar `IPdfPreviewRenderer`; `PainelViewModel` monta a cadeia `[DocnetRenderer, GhostscriptRenderer]`.
Evidencia: `IPdfPreviewRenderer.cs` (Core), `AncorarPdfDocnetRenderer.cs`, `AncorarPdfGhostscriptRenderer.cs`, `AncorarPdfFallbackPreviewRenderer.cs` (Infra), `AncorarPdfPreviewState.cs`, `AncorarPdfConfiguracaoViewModel.cs`, `PainelViewModel.cs`; `AncorarPdfChecklist14PreviewRendererContractTests.cs` (19 testes Core, C14_G2); `AncorarPdfChecklist14PreviewRendererInfraTests.cs` (13 testes Infra, C14_G3); `checklist14_preview_renderer_validate.{sh,ps1}`; gates C14_G1–G3 PASS (2026-03-02).

Validacao de testes apos implementacao:

1. Regressao critica (`F1`, `C2_F2`, `C2_F10`, `C2_DatePicker`): **4/4 passando**.
2. `PainelAncorarPdfChecklist02ViewModelTests`: **17/17 passando**.
3. `AncorarPdfChecklist02ExecutionFlowTests`: **10/10 passando**.
4. `AncorarPdfChecklist11WizardVmTests`: **7/7 passando**.
5. `AncorarPdfChecklist06MotorIntegrationTests`: **9/9 passando**.
6. `AncorarPdfChecklist07EventoEmissaoTests`: **9/9 passando**.
7. `AncorarPdfChecklist02ValidationTests`: **18/18 passando**.
8. `PainelAncorarPdfChecklist02ReadonlyAndReopenTests`: **2/2 passando**.
9. `AncorarPdfChecklist13TimezoneMatrixCoreTests`: **12/12 passando** (C13_G2).
10. `AncorarPdfChecklist13ViewModelTimezoneMatrixTests`: **12/12 passando** (C13_G3).
11. `AncorarPdfChecklist14PreviewRendererContractTests`: **19/19 passando** (C14_G2_RendererCore — CT01–CT12 + inline data CT02).
12. `AncorarPdfChecklist14PreviewRendererInfraTests`: **13/13 passando** (C14_G3_RendererInfra — IT01–IT13 incluindo render real PDFium).
13. Observacao operacional: suites completas podem oscilar por testes de performance de validadores globais (fora do escopo ancorar_pdf), sem regressao funcional no fluxo auditado.
14. Hardening 2026-03-02 (Core): `AuthServiceTests` + `TarefaServiceTests` + `ClienteServiceTests` = **63/63 passando**.
15. Hardening 2026-03-02 (Infra): `AncorarPdfChecklist03PersistenceTests` + `AncorarPdfSmartDetectorEstabilidadeTests` + `AncorarPdfSmartDetectorFRETests` + `AncorarPdfChecklist14PreviewRendererInfraTests` = **38/38 passando**.
16. **P1 concluido (tracing OTel fim-a-fim 2026-03-02)**: spans de persistência em `SqliteAncorarPdfSaidaRepository`, `PostgresAncorarPdfSaidaRepository` (`ancorar_pdf.persistence.saida.save`, `exists_hash`); spans em `SalvarExecucaoCompleta` (`ancorar_pdf.persistence.execucao.save_completa`); export OTLP configurável via `OTEL_EXPORTER_OTLP_ENDPOINT`; pacote `OpenTelemetry.Exporter.OpenTelemetryProtocol`.
17. **P2 concluido (preview Docnet-only configurável 2026-03-02)**: `appsettings.json` AncorarPdf.Preview.UseGhostscriptFallback ou env `ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT`; ambos funcionam; env tem precedência.
18. **P2 concluido (resiliência avançada 2026-03-02)**: `UseJitter = true`; circuit breaker (FailureRatio=0.5, MinimumThroughput=5, BreakDuration=30s); métrica `circuit_breaker_opened_total`; tratamento de `BrokenCircuitException`.
19. **P1 concluido (hardening de suites sem skip silencioso 2026-03-02)**: testes de Smart Detector/Preview trocaram early-return silencioso por precondições explícitas; CI pode forçar falha quando artefatos/dependências faltam.

Evidencia: `InfraTestPreconditions.cs`, `AncorarPdfSmartDetectorEstabilidadeTests.cs`, `AncorarPdfSmartDetectorFRETests.cs`, `AncorarPdfChecklist14PreviewRendererInfraTests.cs`, scripts `checklist12_smart_detector_validate.sh` e `checklist14_preview_renderer_validate.sh` (flags `PROTONS_CI_REQUIRE_*`).
22. **P1 concluido (Roadmap 60d#1 - unicidade condicional de dedup 2026-03-02)**: índice legado não-único substituído por índice único parcial V1 com deduplicação de legado na migração.
Evidencia: `SqliteDb.cs`/`PostgresDb.cs` (`MigrarParaVersao16`, `UX_AncorarPdfSaida_Tarefa_PayloadHash_V1`), `SqliteAncorarPdfSaidaRepository.cs`, `PostgresAncorarPdfSaidaRepository.cs`, `AncorarPdfChecklist03PersistenceTests.cs`.
23. **P1 concluido (Roadmap 60d#2 - TimeProvider fora de AncorarPdf 2026-03-02)**: `AuthService`, `TarefaService` e `ClienteService` migrados para injeção de `TimeProvider` com cobertura de testes determinísticos.
Evidencia: `AuthService.cs`, `TarefaService.cs`, `ClienteService.cs`; testes `AuthServiceTests.cs`, `TarefaServiceTests.cs`, `ClienteServiceTests.cs`.
24. **P1 concluido (métricas OTel no runtime 2026-03-02)**: `MeterProvider` OTel em Motor/Fila/Scheduler/Worker; métrica `ancorar_pdf.fila.process_file_latency_ms`; export OTLP configurável.
Evidencia: `App.axaml.cs`, `AncorarPdfExecutionWorker.cs`, `AncorarPdfFilaExecucaoService.cs`, `AncorarPdfChecklist08PerformanceGuardsTests.cs`.

## 4) Diagnostico tecnico (achados com evidencia)

## 4.1 Achados criticos (P0) - status atual

1. **[RESOLVIDO em 2026-03-01] Bug de data/hora no modal avancado (binding)**

Antes: wrapper ambiguo gerava risco de excecao em timezone nao-UTC.  
Implementado: `DataSelecionada` em `DateTimeOffset?` fim-a-fim + contrato date-only.  
Evidencia: `AncorarPdfConfiguracaoViewModel.cs:58-75`, `AncorarPdfConfiguracaoView.axaml:75`, teste `PainelAncorarPdfChecklist02ViewModelTests.cs:194-222`.  
Impacto: abertura/edicao estabilizadas; regressao protegida por teste.

1. **[RESOLVIDO em 2026-03-01] Perda de regra de normalizacao editada pelo usuario**

Antes: `ToModel` descartava valor editado e sobrescrevia com inferencia.  
Implementado: `ObterRegraNormalizacaoParaSalvar()` prioriza valor explicito do usuario.  
Evidencia: `AncorarPdfConfiguracaoViewModel.cs:1593-1599`, `:1623`; teste `AncorarPdfChecklist02ExecutionFlowTests.cs:99-130`.  
Impacto: metadado salvo fiel ao configurado; fechamento da falha C2_F10.

1. **[RESOLVIDO em 2026-03-01] Despacho de fila sem fire-and-forget**

Antes: enqueue era disparado sem `await`, com risco de perda silenciosa de falha assinc.  
Implementado: `ProcessarAsync` no handler e processamento assinc aguardado no scheduler.  
Evidencia: `AncorarPdfJobHandler.cs:47`, `:87`; `AncorarPdfSchedulerRuntime.cs:203`, `:230`.  
Impacto: falhas de enqueue passam a participar do fluxo de erro/observabilidade.

1. **[RESOLVIDO em 2026-03-01] Deduplicacao semantica com `PayloadHashV2`**

Antes: hash incluia `ExecucaoId` e `DataExecucaoUtc` (campos volateis).  
Implementado: hash V2 por conteudo real, incluindo `schemaVersion`; compatibilidade com legado V1 no motor para dedup historica.  
Evidencia: `AncorarPdfExecucaoModels.cs:147-218`, `AncorarPdfMotorExecucao.cs:261-280`, testes `AncorarPdfOutputContractTests.cs:130-160`.  
Impacto: dedup por conteudo real sem quebrar base antiga.

1. **[RESOLVIDO em 2026-03-01] Leitura de datas agora em fail-fast auditavel**

Antes: parse invalido era mascarado com `now`.  
Implementado: `FormatException` com contexto de repositorio/coluna em todos repositórios `AncorarPdf*`.  
Evidencia: `SqliteAncorarPdfExecucaoRepository.cs:398`, `SqliteAncorarPdfExecucaoFilaRepository.cs:296`, `SqliteAncorarPdfSaidaRepository.cs:182`, `PostgresAncorarPdfExecucaoRepository.cs:400`, `PostgresAncorarPdfExecucaoFilaRepository.cs:280`, `PostgresAncorarPdfSaidaRepository.cs:183`, teste `AncorarPdfChecklist03PersistenceTests.cs:608`.  
Impacto: integridade temporal preservada e diagnostico imediato de corrupcao.

1. **[RESOLVIDO em 2026-03-01] Validacao de cliente por documento exato (sem substring)**

Antes: validacao usava substring de `clienteId` em CPF/CNPJ extraido.  
Implementado: validador busca cliente por `Id`, valida documento oficial com digito verificador e exige match exato com documento valido extraido do PDF.  
Evidencia: `AncorarPdfValidadorClienteRegex.cs:15`, `:28`, `:36`, `:64`; wiring em `App.axaml.cs:407`; testes `AncorarPdfChecklist06PipelineTests.cs`, `AncorarPdfChecklist09PipelineAdversarialTests.cs:264`.  
Impacto: elimina falso positivo por substring em ambiente real.

## 4.2 Achados altos (P1)

1. **Teste de integracao temporalmente fragil**

**[RESOLVIDO em 2026-03-01]** com `TimeProvider` fixo no teste/harness.  
Evidencia: `PainelAncorarPdfChecklist01ViewModelTests.cs:34-37`, `PainelAncorarPdfChecklist01TestHarness.cs:30-49`.

1. **[RESOLVIDO em 2026-03-01] Inconsistencia de UX no seletor de PDF modelo**

Antes: picker de PDF abria pasta (`OpenFolderPickerAsync`) no modal avancado, divergindo da path policy.  
Implementado: contrato de picker separado por contexto (`SelecionarPdfModeloArquivoAsync` no modal e `SelecionarPdfModeloPastaWizardAsync` no wizard), filtro nativo `.pdf` no modal, validação explicita para pasta/extensao no modal e mensagem orientativa no wizard.  
Evidencia: `IAncorarPdfFilePicker.cs:5-10`, `AncorarPdfStorageProviderFilePicker.cs:33-62`, `AncorarPdfConfiguracaoViewModel.cs:750-753`, `:1081-1091`, `AncorarPdfAgendamentoBasicoViewModel.cs:379-387`, `AncorarPdfAgendamentoBasicoView.axaml:44`, `:103-110`, testes `PainelAncorarPdfChecklist02ViewModelTests.cs:251-289` e `AncorarPdfChecklist02ExecutionFlowTests.cs:54-77`.  
Impacto: reduz friccao operacional e suporte por selecao indevida de pasta no modal.

1. **[RESOLVIDO PARCIAL em 2026-03-01] ViewModel de configuracao quebrada em modulos menores**

Antes: responsabilidades de agendamento, preview, ancoras e save em um bloco único de alto acoplamento.  
Implementado: extração para `AncorarPdfAgendamentoState`, `AncorarPdfAncorasEditorState`, `AncorarPdfPreviewState`, `AncorarPdfSaveOrchestrator`; handlers de interação `AncorarPdfPreviewInteractionHandler`, `AncorarPdfAncorasInteractionHandler`, `AncorarPdfSmartClickHandler`; VM principal atua como orquestradora. **Dívida residual:** VM ~~1046 linhas (meta enterprise < 500); acoplamento reduzido, mas simplificação adicional pendente.~~  
~~Evidencia: `AncorarPdfConfiguracaoViewModel.cs` (**~~1046 linhas**), `AncorarPdfAgendamentoState.cs`, `AncorarPdfAncorasEditorState.cs`, `AncorarPdfPreviewState.cs`, `AncorarPdfSaveOrchestrator.cs`, `AncorarPdfPreviewInteractionHandler.cs`, `AncorarPdfAncorasInteractionHandler.cs`, `AncorarPdfSmartClickHandler.cs`.  
Impacto: manutenção/testabilidade melhores; regressão mais controlável; dívida residual documentada.

1. **[RESOLVIDO em 2026-03-02] Indice de dedup por hash endurecido com unicidade condicional**

Evidencia: `UX_AncorarPdfSaida_Tarefa_PayloadHash_V1` (índice único parcial `WHERE SchemaVersion = 1`) em `SqliteDb.cs` e `PostgresDb.cs`; migração `SchemaVersionAtual = 16` com `MigrarParaVersao16` e deduplicação de legado.  
Impacto: `ExisteSaidaComHash` escala melhor em alto volume e gravações duplicadas V1 passam a ser idempotentes no banco.

1. **Bypass direto do painel exige guardas multiplas (operacionalmente correto, pouco discoverable)**

Evidencia: `MainWindowViewModel.cs:56-64` e guardas `:163-197`; default de politica em `appsettings.json:18` = `false`.  
Impacto: equipe pode interpretar login como bug quando variaveis de bypass nao estao ativas.

## 4.3 Achados moderados (P2)

1. **[RESOLVIDO em 2026-03-02] Suites com skip silencioso endurecidas**: precondições explícitas via `Requires*Fact`, sem early-return silencioso; CI pode forçar falha com `PROTONS_CI_REQUIRE_FRE_PDF=1` e `PROTONS_CI_REQUIRE_PREVIEW_RENDERERS=1`.
2. **Dois hosts visuais para mesma view de configuracao** no `PainelView.axaml` (modal + wizard fullscreen), aumentando complexidade de estado visual.
3. **[RESOLVIDO em 2026-03-02] Métricas OTel**: export OTLP configurável; métricas de latência no worker.

## 5) Nota de maturidade (0 a 10)

### 5.1 Pesos adotados (enterprise)


| Pilar                      | Peso | Rationale                                        |
| -------------------------- | ---- | ------------------------------------------------ |
| Arquitetura                | 20%  | Base para evolução, manutenção e escalabilidade. |
| Confiabilidade/Resiliencia | 20%  | Crítico para sistemas de produção.               |
| Performance                | 20%  | SLO e throughput definem capacidade operacional. |
| Seguranca/Compliance       | 15%  | Exigência regulatória e governança.              |
| Testabilidade/Qualidade    | 15%  | Garante evolução segura.                         |
| UX Operacional             | 10%  | Adoção e produtividade do usuário.               |


**Fórmula:** `Nota = Σ (Nota_pilar × Peso_pilar)` com `Σ Pesos = 100%`.

### 5.2 Metodologia de medição (critérios objetivos)

Cada pilar é avaliado de 0 a 10 conforme rubric abaixo. A nota deve ser justificada por evidências verificáveis no código ou em execução de testes.


| Pilar              | 0–2                              | 3–4                                | 5–6                              | 7–8                                                   | 9–10                                              |
| ------------------ | -------------------------------- | ---------------------------------- | -------------------------------- | ----------------------------------------------------- | ------------------------------------------------- |
| **Arquitetura**    | Caótico, sem camadas             | Camadas confusas, acoplamento alto | E2E identificável, mas monolitos | E2E claro, separação Core/Infra/UI, schema controlado | Modular, baixo acoplamento, extensível            |
| **Confiabilidade** | Falhas silenciosas, sem retry    | Retry básico, fallbacks perigosos  | Retry + timeout, dedup parcial   | Fail-fast, dedup semântica, enqueue aguardado         | Circuit breaker, jitter, observabilidade completa |
| **Performance**    | Bloqueios, sem métricas          | Métricas parciais                  | Channels, índices críticos       | SLOs definidos e atendidos em CI                      | Métricas OTel, backpressure                        |
| **Seguranca**      | Path traversal, sem validação    | Path policy básica                 | Path policy + allowlist rede     | Validação cliente robusta                             | Audit trail, compliance documentado               |
| **Testabilidade**  | Sem testes automatizados         | Poucos testes, flaky               | Suíte estável, TimeProvider      | Regressão protegida, gates CI                         | 100% pass, matriz timezone/DST                    |
| **UX**             | Erros frequentes, fluxo quebrado | Fluxo básico, erros de binding     | Binding estável, feedback claro  | Picker consistente, mensagens úteis                   | Zero fricção, acessibilidade                      |


### 5.3 Medição atual (evidências verificáveis)


| Pilar              | Evidência objetiva                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Conclusão                                                                                                           |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| **Arquitetura**    | VM `AncorarPdfConfiguracaoViewModel.cs` = **~1046 linhas** (dívida residual) + módulos dedicados (`AgendamentoState`, `AncorasEditorState`, `PreviewState`, `SaveOrchestrator`) + handlers (`PreviewInteractionHandler`, `AncorasInteractionHandler`, `SmartClickHandler`); fluxo E2E em 7 etapas; schema **v16** (C14 alias + C15 OCR + C16 dedup condicional); Channels em `AncorarPdfFilaExecucaoService`; `TimeProvider` padronizado também fora do escopo AncorarPdf (`AuthService`, `TarefaService`, `ClienteService`) | E2E claro, schema evolutivo, handlers extraídos e determinismo temporal mais consistente entre módulos              |
| **Confiabilidade** | Fail-fast; indice dedup **único parcial V1**; enqueue await; OCR fallback; **UseJitter=true**; **circuit breaker** (FailureRatio=0.5, BreakDuration=30s); métrica `circuit_breaker_opened_total`; tratamento `BrokenCircuitException`; inserts com `ON CONFLICT DO NOTHING` idempotentes                                                                                                                                                                                                                                     | Observabilidade completa; dedup persistente mais forte e menor risco de duplicatas operacionais                     |
| **Performance**    | Índice único parcial em hash V1; SLO_P02 passou; métricas OTel em Motor/Fila/Scheduler/Worker; tracing fim-a-fim; export OTLP configurável; `PdfRenderMetrics` por render; PDFium sem processo externo                                                                                                                                                                        | SLO atendido; métricas e tracing disponíveis                                                                        |
| **Seguranca**      | Path policy em `AncorarPdfPathPolicy.cs`; allowlist rede; validação cliente por documento exato em `AncorarPdfValidadorClienteRegex.cs`                                                                                                                                                                                                                                                                                                                                                                                      | Path policy + validação forte de cliente                                                                            |
| **Testabilidade**  | **440 testes** AncorarPdf passam (205 Core + 235 Infra, 0 falhas); hardening sem skip silencioso (`InfraTestPreconditions`, `Requires*Fact`, flags `PROTONS_CI_REQUIRE_*`); TimeProvider em repos + serviços core fora de AncorarPdf; regressão de substring + matriz timezone (UTC, BRT, CET/CEST) em `AncorarPdfChecklist13*`; validação focada 2026-03-02: **63/63 Core + 38/38 Infra**                                                                                                                                   | Suíte mais determinística e transparente em CI, com menor risco de falso verde por ausência de artefato/dependência |
| **UX**             | `DateTimeOffset` fim-a-fim; picker segregado por contexto (`SelecionarPdfModeloArquivoAsync` no modal e `SelecionarPdfModeloPastaWizardAsync` no wizard); erro tipado `AncorarPdfErrorCode` no save; validação explícita no modal e mensagem orientativa no wizard                                                                                                                                                                                                                                                           | Fluxo consistente, feedback estruturado e previsível                                                                |


### 5.4 Pontuação (medição realista)


| Pilar                      | Nota | Justificativa resumida                                                                                                                                                                                                                                                      |
| -------------------------- | ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Arquitetura                | 8.1  | E2E claro, schema v16 com migração controlada; modularização concluída; handlers extraídos (Lifecycle, Save, PdfRender, PreviewInteraction, AncorasInteraction, SmartClick) reduziram VM de ~1247 para ~745 linhas; `TimeProvider` expandido; VM ainda acima da meta < 500. |
| Confiabilidade/Resiliencia | 9.2  | Fail-fast, dedup semântico + unicidade condicional V1 no banco, enqueue await, validação cliente, OCR fallback, jitter e circuit breaker com métrica dedicada.                                                                                                              |
| Performance                | 9.0  | SLO_P02 passou; métricas e tracing OTel ativos; export OTLP configurável.                                                                                                                                   |
| Seguranca/Compliance       | 7.1  | Path policy presente; validação cliente endurecida por documento oficial (CPF/CNPJ válido + match exato).                                                                                                                                                                   |
| Testabilidade/Qualidade    | 9.4  | **440 pass** (205 Core + 235 Infra), hardening de precondições sem skip silencioso (flags CI estritas), TimeProvider em repositórios e serviços core, validação focada recente 101/101 (63 Core + 38 Infra).                                                                |
| UX Operacional             | 8.0  | Binding data corrigido; picker de PDF consistente por contexto; erro tipado no save; validação e mensagem explícitas.                                                                                                                                                       |


**Cálculo (atualizado 2026-03-02):**
`8.1×0.20 + 9.2×0.20 + 9.0×0.20 + 7.1×0.15 + 9.4×0.15 + 8.0×0.10 = 8.54`

**Nota final ponderada: 8.54 / 10** *(remedição 2026-03-02: 440 testes, evidências confirmadas, handlers VM extraídos)*  
Baseline inicial desta auditoria: **6.53 / 10**.  
**Ganho pós-implementação: +2.01 pontos** (6.53 → 8.54).  
Meta enterprise (90 dias): **≥ 8.5 / 10** (**atingida nesta etapa**).

## 6) Melhorias numeradas (priorizadas)

Formato: `Problema -> Evidencia -> Proposta -> Impacto -> Esforco -> Risco -> Prioridade`

### 6.1 Status de execucao (ate 2026-03-02)

1. Item 1 (`DateTimeOffset` fim-a-fim): **Concluido**.
2. Item 2 (`RegraNormalizacao` manual): **Concluido**.
3. Item 3 (fire-and-forget no enqueue): **Concluido**.
4. Item 4 (`PayloadHashV2` sem campos volateis): **Concluido**.
5. Item 5 (indice de dedup por hash): **Concluido** com schema v16 (v13 indice dedup + v14 alias + v15 OCR + v16 unicidade condicional V1).
6. Item 6 (parse temporal fail-fast): **Concluido**.
7. Item 7 (validação de cliente por documento exato): **Concluido**.
8. Item 8 (picker PDF modal x wizard): **Concluido**.
9. Item 10 (erro tipado `AncorarPdfErrorCode`): **Concluido**.
10. Item 11 (`TimeProvider`): **Concluido** com escopo ampliado (repositorios/servicos `AncorarPdf` + `AuthService`, `TarefaService`, `ClienteService`).
11. Item 12 (matriz timezone/DST): **Concluido** com C13_TimezoneMatrix (24 testes, UTC+BRT+CET/CEST, gates G1–G3 PASS).
12. Item 17 (deprecação de legado C14 - alias/stub): **Concluido**.
13. Item 14 (OCR fallback C15): **Concluido** (UI modal + save + servico + runtime + erro tipado + observabilidade OCR).
14. Item 15 (renderer dedicado C14-Preview): **Concluido** (IPdfPreviewRenderer, DocnetRenderer PDFium, GhostscriptRenderer, FallbackPreviewRenderer, PdfRenderMetrics, 32 testes, gates G1–G3 PASS).
15. Item 13 (tracing OTel fim-a-fim): **Concluido** (spans persistência, export OTLP, OpenTelemetry.Exporter.OpenTelemetryProtocol).
16. Item 15 (preview Docnet-only configurável): **Concluido** (appsettings AncorarPdf.Preview.UseGhostscriptFallback + env ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT; AppSettings.cs tem Preview; loader aplica override).
17. Item 16 (jitter + circuit breaker): **Concluido** (UseJitter=true, AddCircuitBreaker, BrokenCircuitException, métrica circuit_breaker_opened_total).
18. Item 18 (suites com skip silencioso): **Concluido** com precondições explícitas e gate CI estrito por ambiente.
19. **[CONCLUIDO 2026-03-01] Padronizar data/hora no modal avancado para `DateTimeOffset` fim-a-fim**

Problema: binding quebravel em timezone nao UTC.  
Evidencia: `AncorarPdfConfiguracaoViewModel.cs:58-75`, `AncorarPdfConfiguracaoView.axaml:75`, `PainelAncorarPdfChecklist02ViewModelTests.cs:194-222`.  
Proposta: mover estado para `DateTimeOffset?` real (sem wrapper ambiguo) e normalizar `Kind=Unspecified` nas fronteiras UI.  
Impacto: corrige erro de abertura/edicao e elimina excecoes de cast.  
Esforco: medio.  
Risco: baixo.  
Prioridade: **P0 (concluido)**.

1. **[CONCLUIDO 2026-03-01] Preservar regra de normalizacao manual da ancora**

Problema: valor editado descartado no save.  
Evidencia: `AncorarPdfConfiguracaoViewModel.cs:1593-1599`, `:1623`, `AncorarPdfChecklist02ExecutionFlowTests.cs:99-130`.  
Proposta: usar `RegraNormalizacao` editada; inferencia apenas como default inicial.  
Impacto: corrige bug funcional e fecha falha C2_F10.  
Esforco: baixo.  
Risco: baixo.  
Prioridade: **P0 (concluido)**.

1. **[CONCLUIDO 2026-03-01] Eliminar fire-and-forget de enqueue no scheduler**

Problema: perda silenciosa de erro.  
Evidencia: `AncorarPdfJobHandler.cs:47`, `:87`, `AncorarPdfSchedulerRuntime.cs:203`, `:230`.  
Proposta: tornar fluxo assinc aguardado (`ProcessarAsync`) com tratamento de erro + evento operacional.  
Impacto: melhora confiabilidade e observabilidade de despacho.  
Esforco: medio.  
Risco: baixo.  
Prioridade: **P0 (concluido)**.

1. **[CONCLUIDO 2026-03-01] Revisar algoritmo de hash para `PayloadHashV2`**

Problema: dedup semantica comprometida por campos volateis.  
Evidencia: `AncorarPdfExecucaoModels.cs:147-218`, `AncorarPdfMotorExecucao.cs:261-280`, `AncorarPdfOutputContractTests.cs:130-160`.  
Proposta: excluir `ExecucaoId` e timestamp de execucao do hash e incluir `schemaVersion`.  
Impacto: dedup por conteudo real.  
Esforco: medio.  
Risco: baixo/medio (mitigado com fallback V1 legado no motor).  
Prioridade: **P0 (concluido)**.

1. **[CONCLUIDO 2026-03-01] Garantia de deduplicacao no banco por indice logico**

Problema: consulta de hash sem indice especializado.  
Evidencia: `SqliteDb.cs:727`, `PostgresDb.cs:712`, migracao `SchemaVersionAtual=15` em `SqliteDb.cs:13` e `PostgresDb.cs:13`; teste `AncorarPdfChecklist03PersistenceTests.cs:79-89`.  
Proposta: indice em `(TarefaId, PayloadHashSha256)` com migracao explicita.  
Impacto: melhora de escala na deduplicacao por hash em alto volume.  
Esforco: medio.  
Risco: baixo/medio (mitigado por migracao versionada).  
Prioridade: **P0 (concluido)**.

1. **[CONCLUIDO 2026-03-01] Trocar fallback silencioso de parse de data por fail-fast auditavel**

Problema: substituia dado invalido por now.  
Evidencia: `SqliteAncorarPdfExecucaoRepository.cs:398`, `SqliteAncorarPdfExecucaoFilaRepository.cs:296`, `SqliteAncorarPdfSaidaRepository.cs:182`, `PostgresAncorarPdfExecucaoRepository.cs:400`, `PostgresAncorarPdfExecucaoFilaRepository.cs:280`, `PostgresAncorarPdfSaidaRepository.cs:183`; teste `AncorarPdfChecklist03PersistenceTests.cs:608`.  
Proposta: erro explicito (`FormatException`) com contexto de coluna/repositorio.  
Impacto: integridade temporal e rastreabilidade de incidente.  
Esforco: medio.  
Risco: baixo.  
Prioridade: **P0 (concluido)**.

1. **[CONCLUIDO 2026-03-01] Fortalecer validacao cliente para match exato e regras de documento**

Problema: substring do `clienteId` era permissiva demais.  
Evidencia: implementação em `AncorarPdfValidadorClienteRegex.cs:15`, `:28`, `:36`, `:64`; integração em `App.axaml.cs:407`; regressão adversarial em `AncorarPdfChecklist09PipelineAdversarialTests.cs:264`.  
Proposta: validação por documento oficial do cliente (CPF/CNPJ válido) e match exato com documento extraído do PDF.  
Impacto: reduz falso positivo e endurece segurança semântica da execução.  
Esforco: medio.  
Risco: baixo/medio (ajuste de cenários de teste legados).  
Prioridade: **P0 (concluido)**.

1. **[CONCLUIDO 2026-03-01] Corrigir UX de selecao de PDF modelo para arquivo (nao pasta) no modal avancado**

Problema: picker e contrato de configuracao divergiam (modal aceitava fluxo de pasta, mas policy/salvamento exigiam arquivo).  
Evidencia: `IAncorarPdfFilePicker.cs:5-10`, `AncorarPdfStorageProviderFilePicker.cs:33-62`, `AncorarPdfConfiguracaoViewModel.cs:750-753`, `:1081-1091`, `AncorarPdfAgendamentoBasicoViewModel.cs:379-387`, `AncorarPdfAgendamentoBasicoView.axaml:44`, `:103-110`, testes `PainelAncorarPdfChecklist02ViewModelTests.cs:251-289` e `AncorarPdfChecklist02ExecutionFlowTests.cs:54-77`.  
Proposta: (implementada) `OpenFilePickerAsync` com filtro `*.pdf` no modal; pasta mantida apenas no wizard com mensagem explicita.  
Impacto: reduz erro operacional e suporte; melhora consistencia de UX com path policy.  
Esforco: baixo.  
Risco: baixo.  
Prioridade: **P1 (concluido)**.

1. **[CONCLUIDO PARCIAL 2026-03-01] Quebrar `AncorarPdfConfiguracaoViewModel` em modulos menores**

Problema: ViewModel monolitica com responsabilidades mistas de agendamento, preview, editor de ancoras e save.  
Evidencia (antes): `AncorarPdfConfiguracaoViewModel.cs` concentrava essas responsabilidades em um unico arquivo.  
Proposta: separar `AgendamentoState`, `AncorasEditorState`, `PreviewState`, `SaveOrchestrator` mantendo contrato publico da VM.  
Implementacao (parcial):  

- `AncorarPdfAgendamentoState.cs` (timezone/date-only/mapeamentos de recorrencia/policies/modo).  
- `AncorarPdfAncorasEditorState.cs` (estado de undo/redo + fabrica/conversao de ancoras).  
- `AncorarPdfPreviewState.cs` (orquestracao de preview adapter + render/upgrade DPI).  
- `AncorarPdfSaveOrchestrator.cs` (gate de concorrencia, validacao de save e mapeamento de erro).  
- `AncorarPdfAncoraItemViewModel` movida para arquivo dedicado (`AncorarPdfAncoraItemViewModel.cs`).  
**Dívida residual:** VM principal ~1046 linhas (handlers extraídos em 2026-03-02); meta de simplificação adicional para < 500 linhas pendente.  
Impacto: manutencao e testabilidade melhores, com menor acoplamento e mesma UX/contrato externo.  
Esforco: alto (executado).  
Risco: medio (mitigado com regressao automatizada).  
Validacao executada:  
- `dotnet build Protons.sln -v minimal` (ok).  
- `dotnet test Protons.Infrastructure.Tests.csproj --filter FullyQualifiedName~PainelAncorarPdfChecklist02ViewModelTests` (11/11).  
- `dotnet test Protons.Core.Tests.csproj --filter FullyQualifiedName~AncorarPdfChecklist11WizardVmTests` (7/7).  
- `dotnet test Protons.Infrastructure.Tests.csproj --filter FullyQualifiedName~AncorarPdfChecklist02ExecutionFlowTests` (10/10).  
- `dotnet test Protons.Infrastructure.Tests.csproj --filter FullyQualifiedName~PainelAncorarPdfChecklist02ReadonlyAndReopenTests` (2/2).  
Prioridade: **P1 (concluido)**.

1. **[CONCLUIDO 2026-03-02] Adotar contrato de erro tipado (`AncorarPdfErrorCode`)**

Problema: mensagens textuais heterogeneas no fluxo de salvar configuração do modal avançado.  
Implementação (concluida):  

- Novo contrato canônico de códigos em `AncorarPdfErrorCode.cs` (`Validation`, `DuplicateTaskName`, `RequesterInvalid`, `ConcurrencyConflict`, `TaskNotFound`, `InvalidPathPolicy`, `SaveTimeout`, `SaveUnexpected`).  
- `AncorarPdfError` ganhou alias explícito `Details` para padronizar leitura operacional (`code/category/details/correlationId/retryable`).  
- `AncorarPdfErrorCatalog` atualizado com mapeamento de categoria/retry/user-message para os novos códigos de configuração/salvamento.  
- `AncorarPdfSaveOrchestrator` passou a gerar `AncorarPdfError` tipado com `correlationId` e metadata, incluindo mapeamento determinístico de exceções textuais legadas para `AncorarPdfErrorCode`.  
- `AncorarPdfConfiguracaoViewModel` passou a expor `UltimoErroTipado`, preencher contrato tipado em validação local, timeout e falha de save, e registrar observabilidade estruturada (`corr`, `code`, `category`, `retryable`).  
Evidencia: `AncorarPdfErrorCode.cs`, `AncorarPdfError.cs`, `AncorarPdfErrorCatalog.cs`, `AncorarPdfSaveOrchestrator.cs`, `AncorarPdfConfiguracaoViewModel.cs`.  
Testes adicionados: `PainelAncorarPdfChecklist02ViewModelTests.cs` (erro tipado para validação local e nome duplicado).  
Impacto: observabilidade consistente, suporte mais rápido, base pronta para automação de resposta por código estável.  
Esforco: medio.  
Risco: baixo.  
Prioridade: **P1 (concluido)**.

1. **[CONCLUIDO 2026-03-02] Padronizar `TimeProvider` em repositorios, serviços e testes de tempo**

Problema: mistura de `TimeProvider` com `DateTime.UtcNow`.  
Evidencia: `SqliteAncorarPdfExecucaoRepository.cs`, `PostgresAncorarPdfExecucaoRepository.cs`, `SqliteAncorarPdfExecucaoFilaRepository.cs`, `PostgresAncorarPdfExecucaoFilaRepository.cs`, `SqliteAncorarPdfSaidaRepository.cs`, `PostgresAncorarPdfSaidaRepository.cs`, `SqliteAncorarPdfExecucaoLeaseRepository.cs`, `PostgresAncorarPdfExecucaoLeaseRepository.cs`, `SqliteAncorarPdfConfiguracaoRepository.cs`, `PostgresAncorarPdfConfiguracaoRepository.cs`, `AuthService.cs`, `TarefaService.cs`, `ClienteService.cs`.  
Proposta: concluída com injeção opcional de `TimeProvider` e cobertura determinística nos testes `AuthServiceTests`, `TarefaServiceTests` e `ClienteServiceTests`.  
Impacto: testes deterministicos, menos flakiness.  
Esforco: medio.  
Risco: baixo.  
Prioridade: **P1 (concluido)**.

1. **[CONCLUIDO 2026-03-02] Fortalecer testes de UI/data binding e timezone matrix**

Problema: casos de binding e DST pouco cobertos.
Implementado: suite de regressao `C13_TimezoneMatrix` com 24 testes (12 Core + 12 Infra), cobrindo UTC, America/Sao_Paulo (BRT fixo, sem DST desde 2019) e Europe/Berlin (CET/CEST — spring forward 29 mar 2026 e fall back 25 out 2026), datas invalidas/ambiguas com todas as politicas DST, cruzamento de meia-noite (local date ≠ UTC date), setter/getter do DatePicker com offsets positivos e negativos, binding bidirecional e null.
Evidencia: `AncorarPdfChecklist13TimezoneMatrixCoreTests.cs` (12 testes, TZ01–TZ12); `AncorarPdfChecklist13ViewModelTimezoneMatrixTests.cs` (12 testes, VT01–VT12); scripts `checklist13_timezone_matrix_validate.{sh,ps1}` + `checklist13_timezone_matrix_sync_md.sh`; gates C13_G1–G3 todos PASS (24/24 pass / 0 fail).
Impacto: prevencao de regressao de DST e binding de data em producao para os fusos mais usados no Brasil e Europa.
Esforco: medio.
Risco: baixo.
Prioridade: **P1 (concluido)**.

1. **[CONCLUIDO 2026-03-02] Adicionar tracing distribuido com `ActivitySource` + OTel**

Problema: ha metricas, mas rastreio transacional ainda parcial.  
Implementado: spans completos fim-a-fim: `scheduler.dispatch`, `fila.enqueue`, `worker.process`, `motor.execute`, `persistence.saida.save`, `persistence.saida.exists_hash`, `persistence.execucao.save_completa`; export OTLP configurável via `OTEL_EXPORTER_OTLP_ENDPOINT`; Console em Debug.  
Evidencia: `AncorarPdfActivitySource.cs`, `SqliteAncorarPdfSaidaRepository.cs`, `PostgresAncorarPdfSaidaRepository.cs`, `SqliteAncorarPdfExecucaoRepository.cs`, `PostgresAncorarPdfExecucaoRepository.cs`, `App.axaml.cs` (AddOtlpExporter condicional); pacote `OpenTelemetry.Exporter.OpenTelemetryProtocol`.  
Impacto: RCA rápido e SLO real; export OTLP configurável.  
Proposta: spans por etapa (`scheduler`, `enqueue`, `worker`, `motor`, `persist`) com `correlationId` e export OTLP.  
Prioridade: **P1 (concluido)**.

1. **[CONCLUIDO 2026-03-02] Estrategia OCR opcional para PDF escaneado (hardening Tesseract + UI->motor)**

Implementado: OCR configuravel no modal avancado (`OcrFallbackAtivo`, `OcrDpi`, `OcrLang`), validacao no `SaveOrchestrator` e no servico de configuracao (DPI `150..600` e formato Tesseract em `lang`), normalizacao de idioma no save, extrator Tesseract endurecido (pre-validacao de tessdata/idiomas, `TesseractEngine` por documento, renderizacao em memoria, escala por DPI) e erro tipado `ANCORA-NEG-OCR_INDISPONIVEL` emitido em `ExecucaoFalhou` sem retry.
Evidencia: `AncorarPdfConfiguracaoView.axaml`, `AncorarPdfConfiguracaoViewModel.cs`, `AncorarPdfSaveOrchestrator.cs`, `AncorarPdfConfiguracaoService.cs`, `AncorarPdfErroCodigos.cs`, `AncorarPdfErrorCatalog.cs`, `AncorarPdfExtratorOcrTesseract.cs`, `AncorarPdfExtratorTextoComOcrFallback.cs`, `AncorarPdfMotorExecucao.cs`, `AncorarPdfChecklist06MotorIntegrationTests.cs`, `AncorarPdfChecklist07EventoEmissaoTests.cs`.
Compatibilidade: OCR desligado continua em `NegPdfSemTexto`; OCR ligado sem palavras apos tentativa tambem retorna `NegPdfSemTexto`, com detalhe de que OCR foi tentado.
Prioridade: **P1 (concluido)**.

14.1 **Operacao OCR (pre-requisitos e troubleshooting rapido)**

1. Pre-requisitos: tesseract instalado no host e tessdata disponivel em `ANCORA_TESSDATA_PATH`, `./tessdata` ou `/usr/share/tesseract-ocr/5/tessdata`.
2. Idiomas: garantir arquivos `.traineddata` para todos os tokens de `OcrLang` (ex.: `por.traineddata`, `eng.traineddata` para `por+eng`).
3. Quando ocorrer `ANCORA-NEG-OCR_INDISPONIVEL`: validar caminho de tessdata, validar idiomas configurados e conferir logs `ocr_fallback_attempt`/`ocr_fallback_unavailable` com `correlationId`.
4. **[CONCLUIDO 2026-03-02] Substituir/encapsular Ghostscript de preview por renderer dedicado**

Problema resolvido: dependencia externa de `gs` removida do caminho primario de preview; variacao de ambiente mitigada por fallback chain.
Implementado: camada `IPdfPreviewRenderer` (Core) com modelos imutaveis; `AncorarPdfDocnetRenderer`, `AncorarPdfGhostscriptRenderer`, `AncorarPdfFallbackPreviewRenderer`; **configurável via appsettings ou env**: `appsettings.json` AncorarPdf.Preview.UseGhostscriptFallback=false ou `ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT=false` para cadeia Docnet-only. AppSettings.cs tem `AncorarPdfPreviewSettings`; loader aplica override; PainelViewModel lê env (definida pelo loader). Default: `[DocnetRenderer, GhostscriptRenderer]`.
Evidencia: `AppSettings.cs` (Preview), `AppSettingsLoader` (override), `appsettings.json`, `PainelViewModel.cs` (LerUseGhostscriptFallback); testes C14_G2–G3 PASS.
Impacto: preview sem dependencia de `gs` no caminho primario; fallback transparente; Docnet-only opcional para ambientes controlados.
Prioridade: **P2 (concluido)**.

1. **[CONCLUIDO 2026-03-02] Aprimorar resiliencia de retries com jitter + circuit breaker**

Problema: retry sem jitter pode sincronizar picos sob falha massiva.  
Implementado: `UseJitter = true`; `AddCircuitBreaker` na pipeline (FailureRatio=0.5, MinimumThroughput=5, BreakDuration=30s); `BrokenCircuitException` tratada com erro `circuit_breaker_open`; métrica `ancorar_pdf.fila.circuit_breaker_opened_total`.  
Evidencia: `AncorarPdfExecutionWorker.cs` (linhas 74–100, 207–211).  
Impacto: estabilidade sob incidente; evita thundering herd em retries.  
Prioridade: **P2 (concluido)**.

1. **[CONCLUIDO 2026-03-02] Limpar legado desconectado e formalizar deprecacao de alias**

Problema: artefatos e caminhos legados aumentam ruido.  
Implementado: alias `extrator_pdf` removido da canonicalização da ferramenta; migração de banco (v14) converte legado para `ancorar_pdf`; stub removido do fluxo de código.  
Evidencia: `AncorarPdfConfiguracaoModels.cs` (`FerramentaTarefaIds.EhAncorarPdf`), `SqliteDb.cs`/`PostgresDb.cs` (`MigrarParaVersao14`), `AncorarPdfChecklist01ContractTests.cs` (assert de alias removido).  
Proposta: manter somente monitoramento de regressão via testes de contrato/migração já existentes.  
Impacto: menor ambiguidade de manutencao.  
Esforco: baixo/medio.  
Risco: baixo.  
Prioridade: **P2 (concluido)**.

1. **[CONCLUIDO 2026-03-02] Endurecer suites contra skip silencioso**

Problema resolvido: testes que dependem de artefatos/dependências nativas deixaram de passar silenciosamente sem validar critério.  
Implementado: `InfraTestPreconditions` + atributos `RequiresFrePdfFact`, `RequiresDocnetRendererFact`, `RequiresGhostscriptRendererFact`; remoção de `return` silencioso em `AncorarPdfSmartDetectorEstabilidadeTests`, `AncorarPdfSmartDetectorFRETests` e `AncorarPdfChecklist14PreviewRendererInfraTests`; scripts de checklist em CI agora podem forçar falha explícita por ambiente (`PROTONS_CI_REQUIRE_FRE_PDF=1`, `PROTONS_CI_REQUIRE_PREVIEW_RENDERERS=1`).  
Evidencia: `InfraTestPreconditions.cs`, `AncorarPdfSmartDetectorEstabilidadeTests.cs`, `AncorarPdfSmartDetectorFRETests.cs`, `AncorarPdfChecklist14PreviewRendererInfraTests.cs`, `checklist12_smart_detector_validate.sh`, `checklist14_preview_renderer_validate.sh`.  
Impacto: reduz falso verde em pipeline e acelera diagnóstico operacional.  
Esforco: baixo.  
Risco: baixo.  
Prioridade: **P1 (concluido)**.

## 6.1 Evolução futura — Quartz.NET

O scheduler atual usa polling (AncorarPdfSchedulerRuntime). A interface `IAncorarPdfScheduler` foi criada para permitir troca futura.

**Quando considerar Quartz.NET:**
- Cron complexo (ex.: "toda 2ª feira às 9h")
- Calendários (excluir feriados)
- Persistência de jobs entre restarts

**Como migrar:**
1. Implementar `AncorarPdfQuartzScheduler : IAncorarPdfScheduler`
2. Cada tarefa vira um `IJob` que, ao disparar, enfileira na fila existente (Channels)
3. Configurar via appsettings qual scheduler usar

**Fluxo:** Quartz dispara → enfileira em Channels → workers processam (fila e workers permanecem iguais).

## 7) Tecnologias de ponta recomendadas (benchmark externo)

Cada item: **o que resolve / como aplicar / complexidade / risco / inspiracao**

1. **.NET Channels (manter e evoluir fila interna)**
2. O que resolve: backpressure e throughput com baixo overhead.
3. Aplicacao: **ja implementado** com canal bounded e enqueue aguardado no scheduler; evoluir para confirmacao transacional/outbox.
4. Complexidade: baixa.
5. Risco: baixo.
6. Inspiracao: [https://learn.microsoft.com/en-us/dotnet/core/extensions/channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
7. **Polly v8 Resilience Pipelines**
8. O que resolve: retry/timeout/circuit breaker padronizados.
9. Aplicacao: estender pipeline do worker com jitter e breaker por falha tecnica.
10. Complexidade: baixa/media.
11. Risco: baixo.
12. Inspiracao: [https://www.pollydocs.org/](https://www.pollydocs.org/)
13. **OpenTelemetry + metricas .NET**
14. O que resolve: observabilidade enterprise (SLO/SLI/RCA).
15. Aplicacao: spans fim-a-fim + export OTLP configurável.
16. Complexidade: media.
17. Risco: baixo.
18. Inspiracao:

[https://opentelemetry.io/docs/languages/dotnet/](https://opentelemetry.io/docs/languages/dotnet/)  
[https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)

1. **Quartz.NET (misfire e calendarios)**
2. O que resolve: politicas de misfire e scheduling rico.
3. Aplicacao: opcao para cenarios de agenda complexa mantendo contrato atual.
4. Complexidade: media.
5. Risco: medio (migracao comportamental).
6. Inspiracao: [https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/more-about-triggers.html](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/more-about-triggers.html)
7. **Hangfire (jobs persistentes com dashboard)**
8. O que resolve: persistencia de jobs e operacao com dashboard.
9. Aplicacao: alternativa para fila/scheduler em cenarios com operacao manual forte.
10. Complexidade: media.
11. Risco: medio.
12. Inspiracao: [https://docs.hangfire.io/en/latest/background-processing/index.html](https://docs.hangfire.io/en/latest/background-processing/index.html)
13. **Temporal .NET (durable workflows)**
14. O que resolve: orquestracao duravel, retries e historico de workflow robusto.
15. Aplicacao: opcao para etapa futura multi-servico/multi-datacenter.
16. Complexidade: alta.
17. Risco: medio/alto (adocao de plataforma).
18. Inspiracao:

[https://dotnet.temporal.io/](https://dotnet.temporal.io/)  
[https://github.com/temporalio/sdk-dotnet/blob/main/README.md](https://github.com/temporalio/sdk-dotnet/blob/main/README.md)

1. **PdfPig (base atual), PDFium e OCR hibrido**
2. O que resolve: extracao nativa + renderizacao robusta + OCR para escaneados.
3. Aplicacao: manter PdfPig para texto nativo; avaliar PDFium para preview/render; Tesseract/PaddleOCR como fallback.
4. Complexidade: media/alta.
5. Risco: medio (infra nativa e tuning).
6. Inspiracao:

[https://github.com/UglyToad/PdfPig](https://github.com/UglyToad/PdfPig)  
[https://pdfium.googlesource.com/pdfium/](https://pdfium.googlesource.com/pdfium/)  
[https://github.com/tesseract-ocr/tesseract](https://github.com/tesseract-ocr/tesseract)  
[https://github.com/charlesw/tesseract](https://github.com/charlesw/tesseract)  
[https://github.com/PaddlePaddle/PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)

1. **Avalonia performance guidance**
2. O que resolve: UI mais leve e responsiva.
3. Aplicacao: virtualizacao, reducao de bindings pesados, assets/lifecycles otimizados.
4. Complexidade: media.
5. Risco: baixo.
6. Inspiracao: [https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance](https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance)
7. **Date/Time guidance oficial .NET**
8. O que resolve: erros de timezone/DST/casting.
9. Aplicacao: `DateTimeOffset` nas fronteiras e `TimeProvider` para determinismo.
10. Complexidade: media.
11. Risco: baixo.
12. Inspiracao: [https://learn.microsoft.com/en-us/dotnet/standard/datetime/choosing-between-datetime](https://learn.microsoft.com/en-us/dotnet/standard/datetime/choosing-between-datetime)
13. **TimeProvider oficial .NET (determinismo cross-módulo)**
14. O que resolve: testes estáveis e relógio injetável para domínios de negócio.
15. Aplicacao: injeção opcional de `TimeProvider` em serviços de autenticação, tarefas e clientes.
16. Complexidade: baixa.
17. Risco: baixo.
18. Inspiracao: [https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)
19. **PostgreSQL partial unique index + upsert idempotente**
20. O que resolve: deduplicação forte sem bloquear evolução de schema de saída.
21. Aplicacao: índice único parcial por `SchemaVersion = 1` + `ON CONFLICT DO NOTHING` para idempotência.
22. Complexidade: media.
23. Risco: baixo/medio (exige migração com saneamento de legado).
24. Inspiracao: [https://www.postgresql.org/docs/current/indexes-partial.html](https://www.postgresql.org/docs/current/indexes-partial.html) e [https://www.postgresql.org/docs/current/sql-insert.html](https://www.postgresql.org/docs/current/sql-insert.html)
25. **SQLite partial indexes (paridade com Postgres)**
26. O que resolve: manter comportamento consistente entre ambientes locais e produção.
27. Aplicacao: índice único parcial equivalente no SQLite para o mesmo contrato V1.
28. Complexidade: baixa/media.
29. Risco: baixo.
30. Inspiracao: [https://www.sqlite.org/partialindex.html](https://www.sqlite.org/partialindex.html)
31. **xUnit v3 skip explícito por precondição**
32. O que resolve: elimina skip silencioso e melhora diagnóstico quando artefato/dependência não existe.
33. Aplicacao: atributos de precondição com `Skip` explicativo local e gate estrito em CI.
34. Complexidade: baixa.
35. Risco: baixo.
36. Inspiracao: [https://xunit.net/docs/getting-started/v3/whats-new](https://xunit.net/docs/getting-started/v3/whats-new)

## 8) Roadmap 30-60-90 dias

### 30 dias (estabilizacao imediata P0/P1)

1. **[Concluido]** Corrigir bug `DateTimeOffset` do modal.
2. **[Concluido]** Corrigir persistencia de `RegraNormalizacao` manual.
3. **[Concluido]** Corrigir testes quebrados (`F1` e `C2_F10`) e gates relacionados.
4. **[Concluido]** Remover fire-and-forget do enqueue.
5. **[Concluido]** Ajustar hash V2 com compatibilidade legado V1 no motor.
6. **[Concluido]** Trocar fallback silencioso de parse por fail-fast auditavel.
7. **[Concluido]** Fortalecer validacao de cliente (match exato).
8. **[Concluido]** Ajustar picker de PDF do modal para arquivo (com pasta mantida no wizard).

Metas:

1. `Infrastructure AncorarPdf`: manter 0 falhas funcionais (atual: 235/235).
2. `Core AncorarPdf`: manter 0 falhas (atual: 205/205).
3. `Checklist01` e `Checklist11`: PASS continuo.
4. Zero erro de binding de data reportado em homologacao.

### 60 dias (endurecimento P1)

1. ~~Evoluir indice de dedup para estrategia de **unicidade condicional~~** — **[Concluido 2026-03-02]** schema v16 com `UX_AncorarPdfSaida_Tarefa_PayloadHash_V1` (unique partial index `WHERE SchemaVersion = 1`) + migração de deduplicação de legado.
2. ~~Expandir padronizacao `TimeProvider` para repositórios fora do escopo AncorarPdf~~ — **[Concluido 2026-03-02]** `AuthService`, `TarefaService` e `ClienteService` migrados para injeção de `TimeProvider`, com testes determinísticos.
3. ~~Erro tipado + telemetria estruturada~~ — **[Concluido 2026-03-02]** contrato `AncorarPdfErrorCode` + `UltimoErroTipado` + eventos estruturados no save do modal.
4. ~~Tracing OTel fim-a-fim~~ — **[Concluido 2026-03-02]** spans persistência + export OTLP.
5. ~~Refatoracao inicial da VM (split em modulos)~~ — **[Concluido parcial 2026-03-02]** (`AgendamentoState`, `AncorasEditorState`, `PreviewState`, `SaveOrchestrator` + handlers `PreviewInteractionHandler`, `AncorasInteractionHandler`, `SmartClickHandler`); dívida residual: VM ~1046 linhas.
6. ~~Matriz de testes de timezone/DST~~ — **[Concluido 2026-03-02]** C13_TimezoneMatrix: 12 Core + 12 Infra, UTC/BRT/CET/CEST, G1–G3 PASS.

Metas:

1. Processamento dentro do SLO definido em CI.
2. RCA em < 15 min com traces e logs.
3. Regressao temporal < 1% por release.

### 90 dias (evolucao enterprise)

1. ~~Fallback OCR para documentos escaneados~~ — **[Concluido 2026-03-02]** UI modal + save + runtime + erro tipado `ANCORA-NEG-OCR_INDISPONIVEL` + testes C02/C06/C07.
2. ~~Avaliacao comparativa renderer~~ — **[Concluido 2026-03-02]** `IPdfPreviewRenderer` com DocnetRenderer (PDFium) + GhostscriptRenderer + FallbackPreviewRenderer; **Docnet-only configurável** (ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT).
3. ~~Politicas avancadas de resiliencia (jitter + breaker + limite de carga)~~ — **[Concluido 2026-03-02]** UseJitter=true, circuit breaker na pipeline.
4. ~~Plano de deprecacao de legado~~ — **[Concluido 2026-03-02]** stub removido e `extrator_pdf` migrado para `ancorar_pdf` (C14).

Metas:

1. Cobertura de documentos escaneados com qualidade definida.
2. Reducao de incidentes operacionais repetitivos.
3. Auditoria externa com score >= 8.5/10.

## 9) Pecas soltas / legado para remover ou reconectar

1. ~~Stub~~ — removido.
2. ~~Alias extrator_pdf~~ — migração C14 (FerramentaId → ancorar_pdf); alias removido.
3. Duplicidade de host visual para `AncorarPdfConfiguracaoView` no painel (`PainelView.axaml:491` e `:507`) — mantido por design (modal vs wizard fullscreen).
4. **Simplificação adicional da VM `AncorarPdfConfiguracaoViewModel`**: meta < 500 linhas; extrair mais comandos ou sub-states; atual: ~1046 linhas (handlers extraídos em 2026-03-02).

## 10) Riscos de nao executar o plano

1. Regressao de UX sem manutencao dos testes de fluxo e mensagens operacionais.
2. Incidentes intermitentes de agendamento/tempo com dificil reproducao.
3. Deduplicacao inefetiva e crescimento de dados sem ganho.
4. Falsos positivos de validacao cliente.
5. Custo crescente de manutencao pelas pendencias remanescentes: duplo host visual da configuracao ainda nao simplificado e VM principal ainda extensa.

## 11) Criterios de aceite para declarar nivel enterprise

1. **Confiabilidade**: 0 falhas em suites AncorarPdf por 3 ciclos de CI consecutivos.
2. **Operacao**: Checklist01 + Checklist11 em PASS continuo.
3. **Observabilidade**: metricas e traces completos por ciclo com `correlationId`.
4. **Performance**: metas p50/p95 definidas em CI para 10/50/200 paginas.
5. **Seguranca**: validacao cliente robusta, path policy endurecida e logs sem vazamento sensivel.
6. **UX**: fluxo basico sem erro de data/binding; seletor de PDF intuitivo e consistente.
7. **Governanca tecnica**: legado mapeado com plano de deprecacao e ownership claro.

## 12) Resumo executivo

Estado atual: **funcional, robusto e com base arquitetural enterprise** (tempo/binding corrigido, metadado manual corrigido, enqueue confiável, hash semântico, schema v16 com unicidade condicional de dedup, OCR fallback, parse temporal fail-fast, validação cliente por documento exato, renderer dedicado com Docnet-only configurável, **tracing OTel fim-a-fim** com spans de persistência e export OTLP, **jitter e circuit breaker** na pipeline do worker, hardening de suites sem skip silencioso, expansão de `TimeProvider` fora de AncorarPdf, **440 testes** AncorarPdf em PASS + validação focada recente 101/101).
Nota atual: **8.54/10**. Meta enterprise (≥ 8.5) **atingida**; pendências remanescentes: simplificação do duplo host visual da configuração (PainelView.axaml: modal + wizard fullscreen, por design) e redução adicional da VM principal (~745 linhas, meta < 500).

### 12.1 Evolução pós-implementação (medição 2026-03-02)


| Aspecto               | Baseline (início) | Atual (pós-auditoria)      | Melhoria                                                                                           |
| --------------------- | ----------------- | -------------------------- | -------------------------------------------------------------------------------------------------- |
| **Nota ponderada**    | 6.53/10           | 8.54/10                    | **+2.01**                                                                                          |
| **Testes AncorarPdf** | ~380 (estimado)   | 440 (205 Core + 235 Infra) | +60                                                                                                |
| **Arquitetura**       | 6.5               | 8.1                        | E2E claro, schema v16, TimeProvider expandido, LifecycleHandler extraído                           |
| **Confiabilidade**    | 6.0               | 9.2                        | Fail-fast, dedup, jitter, circuit breaker, métrica                                                 |
| **Performance**       | 6.0               | 9.0                        | Índice único parcial, SLO_P02, tracing + métricas OTel disponíveis                                 |
| **Segurança**         | 6.0               | 7.1                        | Path policy, validação cliente por documento exato                                                 |
| **Testabilidade**     | 7.0               | 9.4                        | 440 pass, hardening sem skip, matriz timezone                                                      |
| **UX**                | 6.5               | 8.0                        | Binding corrigido, picker consistente, erro tipado                                                 |


**Implementações que mais impactaram:** tracing OTel fim-a-fim, jitter + circuit breaker, preview Docnet-only configurável, dedup condicional v16, hardening de suites sem skip silencioso, expansão de TimeProvider fora de AncorarPdf.