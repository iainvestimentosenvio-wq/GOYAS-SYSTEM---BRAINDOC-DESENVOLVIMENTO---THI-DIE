# ASSINATURA DE CODIGO

DataUTC: 2026-02-15T09:06:51Z
VersaoReferencia: 1.0.0

## Objetivo
- Garantir cadeia de confianca para artefatos finais de release.

## Requisitos
- Certificado corporativo de code signing (`.pfx`) com chave privada.
- Segredo de senha do certificado em canal seguro.
- Timestamp RFC3161 durante assinatura.

## Processo alvo (quando certificado estiver disponivel)
1. Validar presenca e validade do certificado.
2. Assinar MSI e EXE.
3. Revalidar assinatura com ferramenta de verificacao.
4. Publicar hashes e evidencias de assinatura na rodada final.

## Bloqueio factual atual
- Certificado corporativo final nao esta disponivel nesta baseline.
- Gate de assinatura final permanece pendente ate entrega do certificado.

---

## Atualizacao 2026-02-15T12:00:00Z - Trabalho Paralelo (FASE 6)

### Esqueletos criados (preparacao para quando certificado estiver disponivel)

**1. Script de assinatura: comum/scripts/sign-with-cert.sh**
- Status: Esqueleto funcional criado
- Funcionalidades implementadas:
  - Parse de argumentos (--cert, --password, --timestamp)
  - Deteccao automatica de ferramenta (signtool.exe ou osslsigncode)
  - Suporte a multiplos artefatos
  - Validacao de certificado .pfx
  - Timestamp RFC3161 configuravel
- Funcionalidades pendentes:
  - Implementacao real do comando signtool.exe (comentado, aguardando teste)
  - Validacao de assinatura pos-sign
  - Log detalhado de erros

**2. Teste de certificado: testes/meta/test-code-signing-cert.sh**
- Status: Esqueleto criado
- Validacoes implementadas:
  - Busca de certificado em locais padrao
  - Validacao de formato PKCS
  - Instrucoes para validacao manual (openssl)
  - Checagem de timestamp RFC3161 configurado
- Validacoes pendentes (manuais):
  - Validade >= 12 meses
  - Chain de certificacao valida
  - EKU (Extended Key Usage) para Code Signing
  - Titular correto (Protons Consultoria)

**3. Documentacao: Este arquivo (ASSINATURA_CODIGO.md)**
- Atualizado com progresso da FASE 6

### Como usar quando certificado estiver disponivel

**Passo 1: Configurar certificado**
```bash
export CODE_SIGNING_CERT_PATH=/path/to/protons-code-signing.pfx
export CODE_SIGNING_CERT_PASSWORD=SUA_SENHA_SECRETA
```

**Passo 2: Validar certificado**
```bash
bash testes/meta/test-code-signing-cert.sh
```

**Passo 3: Assinar artefatos**
```bash
# Assinar MSI
bash comum/scripts/sign-with-cert.sh windows/msi/Protons-1.0.0.msi

# Assinar Inno EXE
bash comum/scripts/sign-with-cert.sh windows/inno/Protons-Setup-1.0.0.exe

# Assinar multiplos
bash comum/scripts/sign-with-cert.sh windows/**/*.{msi,exe}
```

**Passo 4: Verificar assinatura**
```bash
# Windows
signtool.exe verify /pa /v Protons-1.0.0.msi

# Linux (osslsigncode)
osslsigncode verify -in Protons-1.0.0.msi
```

### Integracao com CI/CD

O workflow `.github/workflows/instalador-release.yml` ja contempla etapa de release para o instalador (ainda sem assinatura real):
- Job atual: `release-prepare` (validacoes e staging de artefatos)
- Requer secret: `CODE_SIGNING_CERT` (base64 do .pfx)
- Requer secret: `CODE_SIGNING_CERT_PASSWORD`

Quando certificado estiver disponivel:
1. Adicionar secrets ao GitHub
2. Adicionar etapa dedicada de assinatura para chamar `sign-with-cert.sh`
3. Remover mock e implementar assinatura real

### Proximos passos obrigatorios

1. **Adquirir certificado corporativo .pfx:**
   - Validade minima: 12 meses
   - Chain valida em raiz confiavel Windows
   - EKU: Code Signing
   - Titular: Protons Consultoria (ou nome corporativo correto)

2. **Testar assinatura localmente:**
   - Executar `sign-with-cert.sh` em ambiente de teste
   - Validar assinatura com `signtool verify`
   - Testar instalacao de artefato assinado (SmartScreen nao deve bloquear)

3. **Integrar com CI/CD:**
   - Adicionar certificado aos GitHub Secrets
   - Executar workflow de release em tag de teste
   - Validar assinatura dos artefatos publicados

4. **Atualizar RELEASE_APPROVAL.md:**
   - Marcar [FINAL-11] como CONCLUIDO
   - Marcar [FINAL-12] como CONCLUIDO
   - Atualizar score global (Pilar 4 Seguranca: 70 → 85+)

### Impacto esperado no score

Quando certificado for implementado:
- Pilar 4 (Seguranca): 70 → 85 (+15 pontos)
- Pilar 9 (Compatibilidade): 44 → 55 (+11 pontos, SmartScreen nao bloqueia)
- Nota global: 51.2 → 56.4 (+5.2 pontos conservador)
