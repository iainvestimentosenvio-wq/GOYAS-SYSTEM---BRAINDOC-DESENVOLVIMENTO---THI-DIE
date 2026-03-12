# Documentacao do Projeto

## Objetivo
Manter documentacao curta, confiavel e util para evolucao tecnica do projeto.

## Principios de documentacao honesta
- Escrever somente o que foi confirmado no codigo atual.
- Registrar data da ultima validacao tecnica.
- Informar claramente o que e fato e o que e pendencia.
- Evitar duplicacao do mesmo conteudo em varios arquivos.

## Estrutura
- `documentacao/painel/INDICE.md`: entrada principal da documentacao do painel.
- `documentacao/painel/drag_drop/`: regras e fluxo do drag-and-drop.
- `documentacao/painel/codigo/`: docs por arquivo critico.
- `documentacao/modelos/`: template para criar doc por codigo.
- `../DOC_AUMEJAMOS/README.md`: trilha de evolucao planejada para o futuro.

## Regra para criar doc por codigo
Criar doc por arquivo quando pelo menos um criterio se aplicar:
- fluxo complexo;
- alto risco de regressao;
- regra critica nao obvia;
- arquivo alterado com frequencia por mais de uma pessoa.

## Ultima validacao da base
- Data: 2026-02-18
- Comandos executados:
  - `dotnet build ../Login/Protons.sln`
  - `dotnet test ../Login/Protons.sln`
