# Auto-Sync de Colaboracao

Este projeto pode ser usado por duas maquinas sem ficarem brigando pela mesma linha de trabalho.

## Modelo recomendado

- Cada desenvolvedor usa uma branch pessoal.
- Existe uma branch de integracao no GitHub.
- O auto-sync local faz `commit` e `push` apenas da branch pessoal da propria maquina.
- A branch de integracao e atualizada quando voces decidirem juntar o trabalho.

Exemplo:

- Sua branch: `thiago-dev`
- Branch do colega: `colega-dev`
- Branch de integracao: `main`

## Regras praticas

- Nao trabalhem os dois na mesma branch pessoal.
- O auto-sync nao deve rodar em branch de integracao.
- Antes de integrar, revisem no GitHub o que entrou na branch pessoal de cada um.

## Como fica o fluxo

1. Voce trabalha na sua branch pessoal.
2. O script local salva no GitHub automaticamente essa branch.
3. Seu colega trabalha na branch pessoal dele.
4. Quando um quiser entregar algo para o outro continuar, integra a branch pessoal na branch de integracao.
5. O outro faz pull da branch de integracao e continua do ponto atualizado.

## O que foi corrigido nesta maquina

- O script Linux agora ignora `.sync`, `.git`, `bin`, `obj`, `TestResults` e `.trx`.
- O log do auto-sync nao entra mais no commit automatico.
- O script tenta configurar `upstream` quando a branch remota ja existe.
- O pull automatico so acontece quando nao ha alteracoes locais relevantes.

## Passos manuais recomendados para esta maquina

1. Escolher uma branch pessoal estavel, por exemplo `thiago-dev`.
2. Deixar o auto-sync rodando apenas nela.
3. Usar a branch de integracao apenas para juntar trabalho.

## Prompt para o Codex do colega

Use este prompt no computador dele:

```text
Verifique e configure este repositorio para auto-sync seguro com GitHub.

Contexto:
- Projeto compartilhado com outro desenvolvedor.
- Cada maquina deve usar branch propria para evitar conflitos.
- Esta maquina deve usar a branch pessoal do colega, por exemplo `colega-dev`.
- A branch de integracao do projeto sera `main` (ou outra que eu confirmar).
- O auto-sync deve fazer commit e push apenas da branch pessoal desta maquina.
- O script deve ignorar `.git`, `.sync`, `bin`, `obj`, `TestResults` e arquivos `.trx`.
- O log do auto-sync nao pode entrar em commit automatico.
- Se a branch remota pessoal nao existir, o script deve criar com `git push -u origin <branch>`.
- Se existir, deve sincronizar com cuidado e nao fazer pull automatico quando houver alteracoes locais relevantes.

Objetivos:
- Confirmar o remoto GitHub correto.
- Criar ou trocar para a branch pessoal da maquina do colega.
- Configurar upstream da branch pessoal.
- Corrigir ou instalar o script de auto-sync local no Linux ou Windows, conforme a maquina.
- Garantir que os artefatos locais do auto-sync estejam no `.gitignore`.
- Testar o fluxo e me dizer exatamente quais comandos foram executados e qual branch ficou configurada.

Se houver um script existente de auto-sync no repositorio, reaproveite-o e ajuste apenas o necessario.
```
