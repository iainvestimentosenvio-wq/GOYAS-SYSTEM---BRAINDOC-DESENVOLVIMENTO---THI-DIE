# Persistencia e Cadastro de Clientes

## Objetivo
Evitar perda de dados no cadastro de clientes e padronizar a operacao para testes confiaveis no painel.

## Fluxo de cadastro com teclado (Enter)
1. Ordem de foco por Enter: `Codigo -> Nome -> Fantasia -> Documento -> Grupo -> Email -> Telefone`.
2. Cada Enter valida o campo atual antes de avancar.
3. Se houver erro, o foco nao avanca e o campo recebe destaque.
4. No ultimo campo (`Telefone`), Enter abre confirmacao de salvar.
5. O cadastro so persiste apos confirmar explicitamente.

## Lista de clientes no modal
- A lista permite buscar por `codigo`, `nome`, `fantasia`, `grupo` e `documento`.
- Acao `Abrir` carrega o cliente no formulario em modo edicao.
- Acao `Inativar` executa inativacao logica com confirmacao.

## Persistencia local (ambiente de desenvolvimento)
- Banco local padrao do bypass: `~/.local/share/protons-dev/Protons/protons.db` (Linux).
- O script `Login/scripts/hot_reload_painel_direto.sh` evita usar `/tmp` como perfil padrao.
- Em startup, o app registra no log o `base_dir` e o caminho real do `sqlite_db` em uso.
- Se existir base alternativa conhecida em outro caminho, o app registra aviso operacional.

## Bypass do painel direto
- Comando unico: `cd "../Login" && bash scripts/hot_reload_painel_direto.sh`
- Bypass permitido apenas em `DEBUG + Development` com `PROTONS_PAINEL_DIRETO=1` e `PROTONS_PAINEL_DIRETO_HABILITADO=1`.
- Se `PROTONS_PAINEL_USER_ID` for invalido/inexistente, o app resolve um usuario ativo automaticamente.
- Se nao existir usuario ativo, o app cria um usuario dev inicial (ativo/admin) para a sessao de desenvolvimento.

## Chave de protecao de dados (BYOK)
- Em ambiente protegido, a leitura de dados depende de chave compativel.
- Se a chave divergir da base, o app bloqueia o modulo sensivel (fail-closed).
- Para recuperar acesso na mesma base, configure `PROTONS_DATA_KEY_BASE64` com a chave original.

## Checklist rapido
1. Execute o painel direto: `cd "../Login" && bash scripts/hot_reload_painel_direto.sh`
2. Confirme no log o caminho da base em uso.
3. Cadastre cliente, feche e reabra no mesmo perfil.
4. Valide que o cliente permanece listado.
5. Em erro de chave, alinhe `PROTONS_DATA_KEY_BASE64` antes de novo teste.
