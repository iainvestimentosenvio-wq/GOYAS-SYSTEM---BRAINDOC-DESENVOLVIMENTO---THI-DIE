# Update do Instalador

Escopo
- Definir contrato unico de metadata de update com rastreabilidade.
- Manter verificacao de integridade e assinatura no cliente de update.
- Separar fluxo ativo local (rodada atual) de fluxo externo (pos-validacao Windows).

## Estado do fluxo nesta rodada (2026-02-10)

- EstadoAtivo: `LOCAL_INTERNO`
- EstadoFuturoPlanejado: `GITHUB_VERCEL_POS_WINDOWS`
- Regra de gate: sem validacao final Windows, nao ativar publicacao externa.
- AssinaturaLocalEstrita: `ATIVA` (manifesto sem `UNSIGNED` e validado em modo estrito).

## Regra obrigatoria de execucao em 2 momentos

- `AGORA (execucao continua)`: hardening de scripts, validacoes estritas de assinatura/hash, cobertura anti-falso-positivo e definicao de papeis de aprovacao.
- `FINAL (fechamento da release)`: assinatura final com certificado corporativo e timestamp, publicacao dos hashes finais, SBOM final da versao e decisao formal de `GO/NO-GO`.
- Regra de interpretacao: este fluxo nao pode ser tratado como "apenas no fechamento"; parte obrigatoria deve ser executada continuamente durante o projeto.

## Fluxo ativo nesta rodada (local)

Endpoint local do manifesto
- Canonico (URI): `file:///srv/DocumentosCompartilhados/PROJETO%20PROTONS/INSTALADOR/saida/update/update-manifest.json`
- Caminho local equivalente: `/srv/DocumentosCompartilhados/PROJETO PROTONS/INSTALADOR/saida/update/update-manifest.json`

Regra de uso local
- Manifesto e artefatos permanecem no workspace local.
- O campo `artifacts[].url` continua exigindo URL absoluta (`https://` ou `file://`).
- Nao ha dependencia operacional de GitHub/Vercel nesta rodada.

## Fluxo futuro (pos-validacao Windows)

Endpoint externo planejado (ainda nao ativo)
- `stable`: `https://<dominio-vercel>/updates/stable/update-manifest.json`
- `beta`: `https://<dominio-vercel>/updates/beta/update-manifest.json`

Regra planejada de hospedagem
- Manifesto no endpoint HTTP (Vercel).
- Artefatos (`msi`, `exe`, `deb`, `appimage`) em GitHub Releases com URL absoluta.
- Ativacao somente apos fechamento do gate Windows e novo ciclo de validacao.

## Politica de update (sem mudar schema)

Politica padrao
- Update opcional por padrao.
- Update obrigatorio somente quando `current_version < min_supported_version`.
- Sem adicionar campo novo no `update-manifest.json` nesta rodada.

Interpretacao operacional
- `current_version >= min_supported_version`: cliente pode adiar update.
- `current_version < min_supported_version`: cliente deve bloquear continuidade sem atualizar.

## Contrato do manifesto

Arquivo e schema
- Arquivo: `INSTALADOR/saida/update/update-manifest.json`
- Schema: `INSTALADOR/comum/update/update-manifest.schema.json`

Campos obrigatorios
- `version`
- `channel` (`stable|beta`)
- `published_at_utc` (`YYYY-MM-DDTHH:MM:SSZ`)
- `min_supported_version`
- `artifacts`
- `release_notes_url` (URL absoluta `https://` ou `file://`)
- `rollout_percent` (inteiro `0..100`)

Regras por artefato
- `platform` em `windows|linux`
- `type` em `msi|exe|deb|appimage`
- `url` absoluta (`https://` ou `file://`)
- `sha256` obrigatorio (64 hex)
- `size_bytes` obrigatorio (`>0`)
- `signature` obrigatoria:
  - URL absoluta do arquivo de assinatura, ou
  - `UNSIGNED` (permitido apenas fora do modo estrito)

Formato de log (JSONL)
- `timestamp_utc`
- `event`
- `result`
- `artifact`
- `details`

## Politica de assinatura

- CI: modo estrito por padrao (`publish-update-manifest.sh` usa validacao estrita quando `CI=true|1`).
- Local: modo nao estrito permitido para desenvolvimento.
- Rodada atual: modo estrito validado localmente com artefatos assinados em `saida/signatures/`.
- Em modo estrito:
  - `validate-update-manifest.sh --strict-signature` rejeita `UNSIGNED`.
  - `check-update.sh --require-signature --public-key <pem>` exige assinatura valida.

## Scripts principais

- `INSTALADOR/comum/scripts/generate-update-manifest.sh`
- `INSTALADOR/comum/scripts/sign-update-artifacts.sh`
- `INSTALADOR/comum/scripts/validate-update-manifest.sh`
- `INSTALADOR/comum/scripts/publish-update-manifest.sh`
- `INSTALADOR/comum/scripts/archive-update-manifest.sh`
- `INSTALADOR/comum/scripts/rollback-update-manifest.sh`
- `INSTALADOR/comum/scripts/check-update.sh`
- `INSTALADOR/comum/scripts/test-update-manifest.sh`

## Fluxo operacional ativo (local)

1. Gerar manifesto com URLs absolutas locais (`file://`).
2. Validar manifesto (modo estrito no CI e modo local no desenvolvimento).
3. Executar verificacao de update com `check-update.sh` apontando para manifesto local.
4. Registrar log JSONL de verificacao para rastreabilidade.

Exemplo de geracao local
```bash
bash INSTALADOR/comum/scripts/generate-update-manifest.sh \
  --base-url file:///srv/DocumentosCompartilhados/PROJETO%20PROTONS/INSTALADOR/saida \
  --channel stable \
  --rollout-percent 100 \
  --signature-base-url file:///srv/DocumentosCompartilhados/PROJETO%20PROTONS/INSTALADOR/saida/signatures
```

Exemplo de validacao estrita
```bash
bash INSTALADOR/comum/scripts/validate-update-manifest.sh \
  --manifest INSTALADOR/saida/update/update-manifest.json \
  --strict-signature
```

Exemplo de verificacao de update
```bash
bash INSTALADOR/comum/scripts/check-update.sh \
  --manifest INSTALADOR/saida/update/update-manifest.json \
  --platform windows \
  --type msi \
  --require-signature \
  --public-key /caminho/chave-publica.pem
```

## Fluxo operacional futuro (externo)

1. Publicar artefatos no GitHub Releases.
2. Gerar manifesto com URLs absolutas HTTPS dos artefatos.
3. Publicar manifesto no endpoint da Vercel (`stable` e `beta`).
4. Validar fluxo site -> manifesto -> download.

## Limites desta etapa

- `check-update.sh` valida download, hash e assinatura; nao aplica instalacao automaticamente.
- Aplicacao do update segue via comando nativo da plataforma (`msiexec`, `unins000.exe`, `dpkg`).
- Integracao externa GitHub/Vercel e distribuicao WinGet ficam para etapa pos-validacao Windows.
