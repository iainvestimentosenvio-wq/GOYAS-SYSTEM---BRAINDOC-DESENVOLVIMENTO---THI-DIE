# Controles Tecnicos Minimos

## Controles obrigatorios
- Criptografia em transito entre cliente e servicos.
- Controle de acesso por perfil e principio do menor privilegio.
- Rate limit para consultas cadastrais.
- Validacao estrita de entrada (documento, email, tamanho de campos).
- Protecao contra enumeracao de documentos.
- Backup e plano de recuperacao.

## Boas praticas complementares
- Segregar ambientes (dev/homolog/producao).
- Alertas para falhas repetidas de validacao.
- Politica de secrets para credenciais de APIs externas.
