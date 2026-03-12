#!/bin/bash
# test-code-signing-cert.sh - Valida certificado corporativo de code signing (esqueleto)
# Parte de: FASE 3 - Testes Meta (trabalho paralelo 2026-02-15)
# STATUS: ESQUELETO - Aguardando certificado .pfx corporativo

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXIT_CODE=0

echo "=== test-code-signing-cert.sh ==="
echo "Validando certificado corporativo de code signing"
echo

# Procurar por certificado .pfx
CERT_FILE=""
if [[ -f "${REPO_ROOT}/comum/certs/code-signing.pfx" ]]; then
    CERT_FILE="${REPO_ROOT}/comum/certs/code-signing.pfx"
elif [[ -f "${REPO_ROOT}/certs/code-signing.pfx" ]]; then
    CERT_FILE="${REPO_ROOT}/certs/code-signing.pfx"
elif [[ -n "${CODE_SIGNING_CERT_PATH:-}" ]]; then
    CERT_FILE="${CODE_SIGNING_CERT_PATH}"
fi

if [[ -z "${CERT_FILE}" || ! -f "${CERT_FILE}" ]]; then
    echo "⏸️ BLOQUEADO: Certificado .pfx não encontrado"
    echo
    echo "Locais verificados:"
    echo "  - ${REPO_ROOT}/comum/certs/code-signing.pfx"
    echo "  - ${REPO_ROOT}/certs/code-signing.pfx"
    echo "  - \$CODE_SIGNING_CERT_PATH (${CODE_SIGNING_CERT_PATH:-não definido})"
    echo
    echo "Este teste será executado quando o certificado corporativo estiver disponível."
    echo
    echo "SKIP_REASON=CERT_NOT_AVAILABLE"
    echo "RESULTADO: SKIP_CERT_NOT_AVAILABLE"
    exit 0
fi

echo "✓ Certificado encontrado: ${CERT_FILE}"
echo

# Validação 1: Verificar que é um arquivo .pfx válido
echo "Validando formato do certificado..."
FILE_TYPE=$(file -b "${CERT_FILE}" 2>/dev/null || echo "")

if echo "${FILE_TYPE}" | grep -qi "PKCS"; then
    echo "  ✓ Arquivo identificado como PKCS (formato válido)"
else
    echo "  WARN: Arquivo não identificado como PKCS, verificação manual necessária"
    echo "        Tipo detectado: ${FILE_TYPE}"
fi

# Validação 2: Verificar data de validade (usando openssl se disponível)
echo
echo "Verificando validade do certificado..."

if command -v openssl &> /dev/null; then
    # Nota: openssl pkcs12 requer senha, aqui é apenas estrutural
    echo "  ⚠️ MANUAL: Validação de validade requer senha do certificado"
    echo "  Execute manualmente:"
    echo "    openssl pkcs12 -in ${CERT_FILE} -nokeys -nodes -passin pass:SUA_SENHA | openssl x509 -noout -dates"
    echo
    echo "  Validade mínima esperada: >= 12 meses a partir de hoje"
else
    echo "  SKIP: openssl não disponível"
fi

# Validação 3: Verificar chain de certificação (quando implementado)
echo
echo "Verificando chain de certificação..."
echo "  ⚠️ TODO: Implementar validação de chain"
echo "  Critérios:"
echo "    - Chain deve ser válida"
echo "    - Raiz deve ser confiável no Windows Certificate Store"
echo "    - Certificado intermediário (se houver) deve estar presente"

# Validação 4: Verificar se é certificado de Code Signing (EKU)
echo
echo "Verificando Extended Key Usage (EKU)..."
echo "  ⚠️ MANUAL: Verificar que certificado tem EKU para Code Signing"
echo "  Execute manualmente:"
echo "    openssl pkcs12 -in ${CERT_FILE} -nokeys -nodes -passin pass:SUA_SENHA | openssl x509 -noout -text | grep -A5 'Extended Key Usage'"
echo "  Esperado: 'Code Signing' presente"

# Validação 5: Verificar titular do certificado
echo
echo "Verificando titular do certificado..."
echo "  ⚠️ MANUAL: Verificar que titular é 'Protons Consultoria' ou nome corporativo correto"
echo "  Execute manualmente:"
echo "    openssl pkcs12 -in ${CERT_FILE} -nokeys -nodes -passin pass:SUA_SENHA | openssl x509 -noout -subject"

# Validação 6: Verificar timestamp server (RFC3161)
echo
echo "Verificando configuração de timestamp..."
if [[ -f "${REPO_ROOT}/comum/scripts/sign-with-cert.sh" ]]; then
    if grep -q "RFC3161" "${REPO_ROOT}/comum/scripts/sign-with-cert.sh"; then
        echo "  ✓ Timestamp RFC3161 configurado em sign-with-cert.sh"
    else
        echo "  WARN: Timestamp RFC3161 não encontrado em sign-with-cert.sh"
    fi
else
    echo "  INFO: sign-with-cert.sh não existe (será criado)"
fi

# Resumo
echo
echo "========================================="
echo "Resumo da Validação do Certificado:"
echo "  - Certificado encontrado: SIM"
echo "  - Formato PKCS detectado: $(if echo "${FILE_TYPE}" | grep -qi "PKCS"; then echo "SIM"; else echo "VERIFICAR"; fi)"
echo "  - Validade verificada: MANUAL PENDENTE"
echo "  - Chain verificada: TODO"
echo "  - EKU Code Signing: MANUAL PENDENTE"
echo "  - Titular correto: MANUAL PENDENTE"
echo "  - Timestamp RFC3161: $(if [[ -f "${REPO_ROOT}/comum/scripts/sign-with-cert.sh" ]] && grep -q "RFC3161" "${REPO_ROOT}/comum/scripts/sign-with-cert.sh"; then echo "CONFIGURADO"; else echo "PENDENTE"; fi)"
echo
echo "RESULTADO: PARTIAL_MANUAL_VALIDATION"
echo "Certificado detectado, mas validações manuais são necessárias."
echo "Para validação completa, execute os comandos openssl manualmente."
echo "========================================="

exit 0
