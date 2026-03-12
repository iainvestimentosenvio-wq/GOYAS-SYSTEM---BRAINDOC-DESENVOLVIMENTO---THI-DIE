# ClienteService.cs

## O que este arquivo faz
Implementa regras de negocio de clientes (cadastro, edicao, grupos e RBAC).

## Atualizacoes relevantes
- Cadastro retorna falhas estruturadas com `CodigoErro`, `CampoErro` e `Severidade`.
- Tipo de documento e validacao CPF/CNPJ continuam inferidos do documento.
- Duplicidade de `codigo_cliente` e `documento` tem resposta distinta.
- Falha tecnica retorna mensagem amigavel com `ReferenciaErro`.
- Falha de auditoria nao invalida cadastro ja persistido.

## Pontos de atencao
- Backend e a fonte de verdade para aceite/rejeicao do cadastro.
- `FalhaTecnica` nao deve vazar detalhes internos.
