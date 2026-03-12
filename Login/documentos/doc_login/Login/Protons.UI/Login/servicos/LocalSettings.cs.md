# LocalSettings.cs

## Arquivo
`Login/Protons.UI/Login/servicos/LocalSettings.cs`

## Objetivo
Persistir configuracoes locais simples do login (ex.: ultimo e-mail).

## Melhorias aplicadas
- Substituicao de `Debug.WriteLine` por `OpsLogger.WriteError` com contexto:
  - `local_settings: falha ao ler`
  - `local_settings: falha ao salvar`
- Reuso de opcoes JSON com campo estatico:
  - `s_jsonOptions = new JsonSerializerOptions { WriteIndented = true }`

## Fluxo
- `LoadLastEmail*`: tenta ler e desserializar arquivo de settings.
- `SaveLastEmail*`: serializa e grava no caminho local.

## Pontos de atencao
- Em falha de I/O/permissao, o servico nao derruba o app; retorna fallback seguro.

## Como testar
- Simular falha de permissao e confirmar entrada no `log_ops.jsonl`.
- Salvar e ler ultimo e-mail com sucesso.
