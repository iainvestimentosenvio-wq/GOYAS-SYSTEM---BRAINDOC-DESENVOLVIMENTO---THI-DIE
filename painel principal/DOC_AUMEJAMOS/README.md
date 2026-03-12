# DOC_AUMEJAMOS

## Objetivo da pasta
Esta pasta registra, de forma objetiva, o estado atual e o alvo futuro de evolucoes funcionais do sistema.

## Estado atual
- Ainda nao existe integracao automatica oficial com API da Serpro/Receita.
- O cadastro atual permite CPF/CNPJ, com validacao de formato e persistencia local/servidor.
- Conferencia humana continua obrigatoria para evitar erro de vinculacao em automacoes criticas.
- A ferramenta `ancorar_pdf` esta em fase de planejamento tecnico com checklist dedicado.

## Estrategia em fases
1. Fase atual: cadastro manual com supervisao humana.
2. Fase intermediaria: fluxo gratuito assistido (pre-preenchimento quando houver fonte valida) + aprovacao humana.
3. Fase futura: integracao oficial por API paga (Serpro), com fallback seguro.

## Mapa de documentos
- `cadastro_clientes/01_objetivo_escopo.md`: problema, objetivo e limites da fase atual.
- `cadastro_clientes/02_fluxo_atual_sem_api_oficial.md`: operacao atual do cadastro sem API oficial.
- `cadastro_clientes/03_fluxo_gratuito_assistido.md`: fluxo recomendado de curto prazo com supervisao humana.
- `cadastro_clientes/04_fluxo_futuro_api_serpro.md`: arquitetura de referencia para integracao oficial.
- `cadastro_clientes/05_modelo_dados_alvo_cliente_grupo.md`: modelo de dados alvo para cliente, grupo e codigos.
- `cadastro_clientes/06_validacao_tarefa_por_cliente.md`: algoritmo de vinculacao de tarefa ao cliente correto.
- `../codigos/painel_principal/funcionalidades/ferramentas/ancorar_pdf/documentacao/CHECKLIST_ANCORA_PDF.md`: checklist mestre da ferramenta ancorar_pdf (documentacao centralizada na propria pasta da ferramenta).
- `seguranca/01_lgpd_minimizacao_dados.md`: diretrizes de minimizacao e governanca de dados pessoais.
- `seguranca/02_controles_tecnicos.md`: controles tecnicos minimos para operacao segura.
- `seguranca/03_auditoria_rastreabilidade.md`: eventos auditaveis e trilha de decisao.
- `referencias/01_fontes_oficiais.md`: fontes oficiais e observacoes de custo/contratacao.
