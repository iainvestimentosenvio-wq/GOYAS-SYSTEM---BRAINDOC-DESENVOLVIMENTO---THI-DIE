# Fluxo Futuro com API Oficial Serpro

## Arquitetura-alvo
- Provedor oficial para consulta cadastral por CNPJ.
- Estrategia BYOC: credenciais da empresa contratante.
- Pipeline com validacao, auditoria e fallback seguro.

## Etapas de consulta cadastral
1. Receber CNPJ informado no cadastro.
2. Chamar provedor oficial com timeout e correlation id.
3. Mapear resposta oficial para modelo interno.
4. Salvar resultado e status da validacao.
5. Liberar automacao somente apos criterios de confianca.

## Politica de resiliencia
- Retry com backoff para falhas transitorias.
- Timeout curto para nao travar UX.
- Fallback para revisao humana quando API indisponivel ou divergente.

## Criterio de "dado confiavel"
- Fonte oficial validada.
- Retorno consistente com documento informado.
- Registro auditavel de quando e por qual fonte o dado foi confirmado.
