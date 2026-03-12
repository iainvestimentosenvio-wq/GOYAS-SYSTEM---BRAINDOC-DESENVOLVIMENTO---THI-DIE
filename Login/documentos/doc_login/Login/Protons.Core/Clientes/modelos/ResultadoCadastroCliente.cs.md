# ResultadoCadastroCliente.cs

## O que este arquivo faz
Define o contrato de retorno do cadastro de cliente com suporte a sucesso/falha, mensagem e metadados estruturados para a UI.

## Campos principais
- `Sucesso`: status final da operacao.
- `Mensagem`: texto exibivel ao usuario.
- `Cliente`: entidade persistida quando houver sucesso.
- `CodigoErro`: codigo estavel para tratamento de erro na UI.
- `CampoErro`: campo relacionado ao erro para destaque visual.
- `Severidade`: `Info`, `Warning` ou `Error`.
- `ReferenciaErro`: correlacao curta para falhas tecnicas.

## Pontos de atencao
- `Mensagem` e `Sucesso` permanecem para compatibilidade.
- Evitar usar texto livre como regra; priorizar `CodigoErro` e `CampoErro`.
