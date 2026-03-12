# Aprovacao de Usuarios no Painel

## Objetivo
Definir o fluxo operacional de aprovacao de novos usuarios cadastrados no sistema.

## Fluxo
1. Usuario novo realiza cadastro.
2. Conta entra como pendente.
3. Admin abre o sininho de notificacoes e decide:
   - Liberar acesso (usuario comum)
   - Liberar como administrador
   - Rejeitar usuario

## Regra obrigatoria de seguranca
- Senha nunca e exibida nesse fluxo.
- A decisao e tomada com base em dados cadastrais (nome, e-mail, empresa, cargo).

## Observacao
- Primeiro usuario cadastrado continua com comportamento de admin inicial no backend.
