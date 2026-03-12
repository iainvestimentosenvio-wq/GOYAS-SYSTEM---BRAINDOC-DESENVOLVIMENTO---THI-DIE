# AppSettings.cs

## Objetivo
Configurar o carregamento seguro de `appsettings.json` e aplicar overrides por ambiente.

## Responsabilidades
- Carregar settings de `appsettings.json` da pasta de dados do usuario ou da pasta do executavel.
- Aplicar defaults seguros para painel, auditoria e protecao de dados.
- Aplicar overrides por variaveis de ambiente.

## Dependencias
- Namespace: Protons.UI.Configuration
- Tipo: class AppSettings

## Principais metodos
- LoadAsync()
- AplicarOverridesPorAmbiente()

## Principais propriedades
- Database
- Audit
- UseHashChain
- Security.Painel.PermitirBypassPainelDireto
- Security.DataProtection.RequireByokKey
- Security.DataProtection.KeyEnvVarName
- Security.DataProtection.AllowLegacyPlaintext
- Security.DataProtection.AutoPersistLocalKey

## Fluxo principal
1. Resolve caminho do arquivo de configuracao.
2. Desserializa JSON com `PropertyNameCaseInsensitive = true`.
3. Aplica overrides de ambiente (`PROTONS_*`).
4. Retorna settings final para o bootstrap da aplicacao.

## Pontos de atencao
- `PROTONS_DATA_AUTO_PERSIST_LOCAL_KEY` controla persistencia automatica da chave local no modo SQLite.
- `PROTONS_DATA_REQUIRE_BYOK` e `PROTONS_DATA_ALLOW_LEGACY_PLAINTEXT` continuam influenciando politica de inicializacao.

## Como testar
- Validar load com JSON valido/invalido.
- Validar overrides de `PROTONS_DATA_*`.
- Validar que `AutoPersistLocalKey` segue `appsettings` e variavel de ambiente.
