#!/bin/bash
# sign-with-cert.sh - Assina artefatos com certificado corporativo (esqueleto)
# Parte de: FASE 6 - Preparação para Assinatura de Código (trabalho paralelo 2026-02-15)
# STATUS: ESQUELETO - Aguardando certificado .pfx corporativo

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CERT_FILE="${CODE_SIGNING_CERT_PATH:-}"
CERT_PASSWORD="${CODE_SIGNING_CERT_PASSWORD:-}"
TIMESTAMP_SERVER="${TIMESTAMP_SERVER:-http://timestamp.digicert.com}"
EXIT_CODE=0

echo "=== sign-with-cert.sh ==="
echo "Assinatura de código com certificado corporativo"
echo

# Função de uso
usage() {
    cat << EOF
Uso: $0 [OPTIONS] ARTIFACT1 [ARTIFACT2 ...]

Assina artefatos (MSI, EXE, DLL) com certificado corporativo .pfx

OPTIONS:
  -c, --cert PATH        Caminho para certificado .pfx
  -p, --password PASS    Senha do certificado (ou use \$CODE_SIGNING_CERT_PASSWORD)
  -t, --timestamp URL    Servidor de timestamp RFC3161 (padrão: DigiCert)
  -h, --help             Mostra esta ajuda

EXAMPLES:
  # Assinar MSI
  $0 --cert /path/to/cert.pfx --password SECRET Protons-1.0.0.msi

  # Assinar múltiplos artefatos
  $0 -c cert.pfx -p SECRET *.msi *.exe

  # Usar variáveis de ambiente
  export CODE_SIGNING_CERT_PATH=/path/to/cert.pfx
  export CODE_SIGNING_CERT_PASSWORD=SECRET
  $0 Protons-Setup.exe

ENVIRONMENT VARIABLES:
  CODE_SIGNING_CERT_PATH      Caminho para .pfx
  CODE_SIGNING_CERT_PASSWORD  Senha do certificado
  TIMESTAMP_SERVER            URL do servidor de timestamp

EOF
    exit 0
}

# Parse argumentos
ARTIFACTS=()
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--cert)
            CERT_FILE="$2"
            shift 2
            ;;
        -p|--password)
            CERT_PASSWORD="$2"
            shift 2
            ;;
        -t|--timestamp)
            TIMESTAMP_SERVER="$2"
            shift 2
            ;;
        -h|--help)
            usage
            ;;
        *)
            ARTIFACTS+=("$1")
            shift
            ;;
    esac
done

# Validações
if [[ ${#ARTIFACTS[@]} -eq 0 ]]; then
    echo "❌ ERRO: Nenhum artefato especificado"
    echo "Use --help para ver exemplos"
    exit 1
fi

if [[ -z "${CERT_FILE}" ]]; then
    echo "❌ BLOQUEADO: Certificado .pfx não especificado"
    echo
    echo "Forneça o certificado via:"
    echo "  --cert /path/to/cert.pfx"
    echo "  ou export CODE_SIGNING_CERT_PATH=/path/to/cert.pfx"
    echo
    echo "STATUS: Aguardando certificado corporativo"
    exit 1
fi

if [[ ! -f "${CERT_FILE}" ]]; then
    echo "❌ ERRO: Certificado não encontrado: ${CERT_FILE}"
    exit 1
fi

if [[ -z "${CERT_PASSWORD}" ]]; then
    echo "⚠️ AVISO: Senha do certificado não fornecida"
    echo "Use --password ou export CODE_SIGNING_CERT_PASSWORD=..."
    read -sp "Digite a senha do certificado: " CERT_PASSWORD
    echo
fi

echo "Configuração:"
echo "  Certificado: ${CERT_FILE}"
echo "  Timestamp:   ${TIMESTAMP_SERVER}"
echo "  Artefatos:   ${#ARTIFACTS[@]}"
echo

# Detectar ferramenta de assinatura
SIGN_TOOL=""
if command -v signtool.exe &> /dev/null; then
    SIGN_TOOL="signtool"
    echo "✓ Ferramenta detectada: signtool.exe (Windows)"
elif command -v osslsigncode &> /dev/null; then
    SIGN_TOOL="osslsigncode"
    echo "✓ Ferramenta detectada: osslsigncode (Linux/macOS)"
else
    echo "❌ ERRO: Nenhuma ferramenta de assinatura encontrada"
    echo
    echo "Instale uma das seguintes:"
    echo "  Windows: signtool.exe (Windows SDK)"
    echo "  Linux:   osslsigncode (apt install osslsigncode)"
    exit 1
fi

# Assinar cada artefato
SIGNED_COUNT=0
FAILED_COUNT=0

for ARTIFACT in "${ARTIFACTS[@]}"; do
    if [[ ! -f "${ARTIFACT}" ]]; then
        echo "  ⚠️ SKIP: Arquivo não encontrado: ${ARTIFACT}"
        ((FAILED_COUNT++))
        continue
    fi

    echo "Assinando: $(basename ${ARTIFACT})"

    if [[ "${SIGN_TOOL}" == "signtool" ]]; then
        # Fluxo signtool registrado para execucao em host Windows com SDK instalado
        echo "  ⚠️ Assinatura com signtool.exe ainda nao implementada neste host"
        echo "  Comando: signtool.exe sign /f \"${CERT_FILE}\" /p \"${CERT_PASSWORD}\" /tr \"${TIMESTAMP_SERVER}\" /td SHA256 /fd SHA256 \"${ARTIFACT}\""
        # signtool.exe sign /f "${CERT_FILE}" /p "${CERT_PASSWORD}" /tr "${TIMESTAMP_SERVER}" /td SHA256 /fd SHA256 "${ARTIFACT}"

    elif [[ "${SIGN_TOOL}" == "osslsigncode" ]]; then
        # Linux osslsigncode
        OUTPUT_FILE="${ARTIFACT}.signed"

        if osslsigncode sign \
            -pkcs12 "${CERT_FILE}" \
            -pass "${CERT_PASSWORD}" \
            -t "${TIMESTAMP_SERVER}" \
            -h sha256 \
            -in "${ARTIFACT}" \
            -out "${OUTPUT_FILE}"; then

            # Substituir arquivo original pelo assinado
            mv "${OUTPUT_FILE}" "${ARTIFACT}"
            echo "  ✓ Assinado: $(basename ${ARTIFACT})"
            ((SIGNED_COUNT++))
        else
            echo "  ❌ FAIL: Erro ao assinar $(basename ${ARTIFACT})"
            ((FAILED_COUNT++))
            EXIT_CODE=1
        fi
    fi
done

# Resumo
echo
echo "========================================="
echo "Resumo da Assinatura:"
echo "  - Assinados com sucesso: ${SIGNED_COUNT}"
echo "  - Falhas: ${FAILED_COUNT}"
echo

if [[ ${FAILED_COUNT} -eq 0 ]]; then
    echo "RESULTADO: SUCESSO"
    echo "Todos os artefatos assinados com certificado corporativo"
else
    echo "RESULTADO: PARCIAL"
    echo "${FAILED_COUNT} artefatos não foram assinados"
fi
echo "========================================="

exit ${EXIT_CODE}
