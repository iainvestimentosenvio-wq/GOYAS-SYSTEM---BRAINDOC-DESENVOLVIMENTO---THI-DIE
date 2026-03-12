#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -P)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd -P)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

VERSION_FILE="$INSTALADOR_ROOT/comum/version.env"
ALLOW_STALE="${PROTONS_ALLOW_STALE_ARTIFACTS:-0}"
TARGET_ARTIFACT="both"

usage() {
  cat <<'EOF'
Uso:
  bash testes/windows/test-artifact-freshness.sh [--artifact msi|exe|both]

Opcoes:
  --artifact   Define alvo da validacao (default: both)
  -h, --help   Exibe ajuda e sai
EOF
}

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

warn() {
  echo "WARN: $*"
}

pass() {
  echo "PASS: $*"
}

resolve_powershell_cmd() {
  if command -v pwsh >/dev/null 2>&1; then
    echo "pwsh"
    return
  fi
  if command -v powershell >/dev/null 2>&1; then
    echo "powershell"
    return
  fi
  echo "pwsh"
}

print_rebuild_hint() {
  local target="$1"
  local ps_cmd
  ps_cmd="$(resolve_powershell_cmd)"
  echo "Rebuild obrigatorio antes da rodada:" >&2
  case "$target" in
    msi)
      echo "  $ps_cmd -ExecutionPolicy Bypass -File windows/scripts/build-msi.ps1" >&2
      ;;
    exe)
      echo "  $ps_cmd -ExecutionPolicy Bypass -File windows/scripts/build-inno.ps1" >&2
      ;;
    both|*)
      echo "  $ps_cmd -ExecutionPolicy Bypass -File windows/scripts/build-msi.ps1" >&2
      echo "  $ps_cmd -ExecutionPolicy Bypass -File windows/scripts/build-inno.ps1" >&2
      ;;
  esac
  echo "Fluxo automatico Linux->VM (recomendado):" >&2
  echo "  bash build-and-run-windows-gate.sh" >&2
}

mtime_epoch() {
  stat -c %Y "$1"
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --artifact)
      if [ "$#" -lt 2 ]; then
        fail "valor ausente para --artifact (use msi|exe|both)"
      fi
      TARGET_ARTIFACT="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "argumento invalido: $1"
      ;;
  esac
done

case "$TARGET_ARTIFACT" in
  msi|exe|both) ;;
  *)
    fail "--artifact invalido: $TARGET_ARTIFACT (use msi|exe|both)"
    ;;
esac

[ -f "$VERSION_FILE" ] || fail "arquivo de versao nao encontrado: $VERSION_FILE"

VERSION="$(awk -F= '/^VERSION=/{gsub(/"/,"",$2); print $2; exit}' "$VERSION_FILE")"
[ -n "$VERSION" ] || fail "VERSION nao definida em $VERSION_FILE"

MSI_ARTIFACT="$INSTALADOR_ROOT/saida/windows/Protons-${VERSION}-x64.msi"
EXE_ARTIFACT="$INSTALADOR_ROOT/saida/windows/ProtonsSetup-${VERSION}.exe"

MSI_CRITICAL_SOURCES=(
  "$INSTALADOR_ROOT/windows/wix/Product.wxs"
  "$INSTALADOR_ROOT/windows/wix/Components.wxs"
  "$INSTALADOR_ROOT/windows/scripts/build-msi.ps1"
  "$INSTALADOR_ROOT/comum/version.env"
)

EXE_CRITICAL_SOURCES=(
  "$INSTALADOR_ROOT/windows/innosetup/protons-setup.iss"
  "$INSTALADOR_ROOT/windows/scripts/build-inno.ps1"
  "$INSTALADOR_ROOT/windows/scripts/prepare-inno-ux-assets.ps1"
  "$INSTALADOR_ROOT/comum/version.env"
)

ACTIVE_SOURCES=()
if [ "$TARGET_ARTIFACT" = "msi" ] || [ "$TARGET_ARTIFACT" = "both" ]; then
  ACTIVE_SOURCES+=("${MSI_CRITICAL_SOURCES[@]}")
fi
if [ "$TARGET_ARTIFACT" = "exe" ] || [ "$TARGET_ARTIFACT" = "both" ]; then
  ACTIVE_SOURCES+=("${EXE_CRITICAL_SOURCES[@]}")
fi

for src in "${ACTIVE_SOURCES[@]}"; do
  [ -f "$src" ] || fail "fonte critica ausente: $src"
done

missing=0
if { [ "$TARGET_ARTIFACT" = "msi" ] || [ "$TARGET_ARTIFACT" = "both" ]; } && [ ! -f "$MSI_ARTIFACT" ]; then
  echo "FAIL: artefato MSI ausente: $MSI_ARTIFACT" >&2
  missing=1
fi
if { [ "$TARGET_ARTIFACT" = "exe" ] || [ "$TARGET_ARTIFACT" = "both" ]; } && [ ! -f "$EXE_ARTIFACT" ]; then
  echo "FAIL: artefato EXE ausente: $EXE_ARTIFACT" >&2
  missing=1
fi

if [ "$missing" -ne 0 ]; then
  print_rebuild_hint "$TARGET_ARTIFACT"
  exit 1
fi

collect_stale_for_artifact() {
  local artifact="$1"
  local -n sources_ref="$2"
  local -n stale_out="$3"
  local artifact_mtime

  stale_out=()
  artifact_mtime="$(mtime_epoch "$artifact")"
  for src in "${sources_ref[@]}"; do
    if [ "$(mtime_epoch "$src")" -gt "$artifact_mtime" ]; then
      stale_out+=("$src")
    fi
  done

  [ "${#stale_out[@]}" -eq 0 ]
}

stale_report=()
msi_stale=()
exe_stale=()

if [ "$TARGET_ARTIFACT" = "msi" ] || [ "$TARGET_ARTIFACT" = "both" ]; then
  if ! collect_stale_for_artifact "$MSI_ARTIFACT" MSI_CRITICAL_SOURCES msi_stale; then
    stale_report+=("MSI")
    stale_report+=("${msi_stale[@]}")
  fi
fi

if [ "$TARGET_ARTIFACT" = "exe" ] || [ "$TARGET_ARTIFACT" = "both" ]; then
  if ! collect_stale_for_artifact "$EXE_ARTIFACT" EXE_CRITICAL_SOURCES exe_stale; then
    stale_report+=("EXE")
    stale_report+=("${exe_stale[@]}")
  fi
fi

if [ "${#stale_report[@]}" -gt 0 ]; then
  if [ "$ALLOW_STALE" = "1" ]; then
    warn "artefatos desatualizados detectados, mas bypass ativo via PROTONS_ALLOW_STALE_ARTIFACTS=1."
    for item in "${stale_report[@]}"; do
      warn "  $item"
    done
    pass "freshness bypass aplicado."
    exit 0
  fi

  echo "FAIL: artefatos desatualizados em relacao a fontes criticas." >&2
  echo "Arquivos mais novos detectados:" >&2
  for item in "${stale_report[@]}"; do
    echo "  - $item" >&2
  done
  print_rebuild_hint "$TARGET_ARTIFACT"
  echo "Bypass (nao recomendado): PROTONS_ALLOW_STALE_ARTIFACTS=1" >&2
  exit 1
fi

case "$TARGET_ARTIFACT" in
  msi)
    pass "artefato MSI esta atualizado para a validacao."
    ;;
  exe)
    pass "artefato EXE esta atualizado para a validacao."
    ;;
  *)
    pass "artefatos MSI/EXE estao atualizados para a validacao."
    ;;
esac
