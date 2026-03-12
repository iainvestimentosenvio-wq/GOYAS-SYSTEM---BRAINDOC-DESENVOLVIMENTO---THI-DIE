# Modelo de Dados Alvo - Cliente e Grupo

## Objetivo do modelo
Permitir cadastro robusto para busca por documento, codigo interno e grupo empresarial.

## Campos alvo principais
- `codigo_cliente`: identificador interno unico e estavel.
- `grupo_empresarial_id`: chave de agrupamento empresarial.
- `tipo_vinculo_grupo`: matriz, filial ou independente.
- `cliente_origem_validacao`: manual, gratuito_assistido, oficial_api.
- `cliente_status_validacao`: pendente, validado, divergente, rejeitado.
- `cliente_validado_em_utc`: timestamp da ultima validacao.

## Compatibilidade com CNPJ alfanumerico
- Evitar suposicao fixa de apenas digitos para novos cenarios.
- Planejar validadores e armazenamento compativeis com o padrao futuro.

## Regras de busca alvo
- Busca por grupo empresarial.
- Busca por `codigo_cliente`.
- Busca por documento (CPF/CNPJ).
- Ordenacao por status de validacao quando necessario para operacao.
