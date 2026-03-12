#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

WIX_COMPONENTS="$PROJECT_ROOT/INSTALADOR/windows/wix/Components.wxs"
INNO_ISS="$PROJECT_ROOT/INSTALADOR/windows/innosetup/protons-setup.iss"
MSI_TEST="$PROJECT_ROOT/INSTALADOR/testes/windows/test-msi.ps1"
INNO_TEST="$PROJECT_ROOT/INSTALADOR/testes/windows/test-inno.ps1"
MSI_ROLLBACK_TEST="$PROJECT_ROOT/INSTALADOR/testes/windows/test-msi-rollback.ps1"
INNO_ROLLBACK_TEST="$PROJECT_ROOT/INSTALADOR/testes/windows/test-inno-rollback.ps1"
CANCEL_TEST="$PROJECT_ROOT/INSTALADOR/testes/windows/test-cancel-install.ps1"
TX_TEST="$PROJECT_ROOT/INSTALADOR/testes/windows/test-transactional-install.ps1"
PREPARE_UX_SCRIPT="$PROJECT_ROOT/INSTALADOR/windows/scripts/prepare-inno-ux-assets.ps1"
INSTALLER_CONTRACT="$PROJECT_ROOT/INSTALADOR/testes/windows/installer-contract.json"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

pass() {
  echo "PASS: $*"
}

for file in \
  "$WIX_COMPONENTS" \
  "$INNO_ISS" \
  "$MSI_TEST" \
  "$INNO_TEST" \
  "$MSI_ROLLBACK_TEST" \
  "$INNO_ROLLBACK_TEST" \
  "$CANCEL_TEST" \
  "$TX_TEST" \
  "$PREPARE_UX_SCRIPT" \
  "$INSTALLER_CONTRACT"; do
  [ -f "$file" ] || fail "required file missing: $file"
done

# 0) Canonical contract json must exist and contain required keys.
python3 - "$INSTALLER_CONTRACT" <<'PY' || fail "installer-contract.json invalid"
import json
import sys
from pathlib import Path

contract = Path(sys.argv[1])
data = json.loads(contract.read_text(encoding="utf-8"))
required_paths = [
    "install_dir",
    "main_executable",
    "desktop_shortcut",
    "start_menu_shortcut",
    "start_menu_dir",
    "uninstaller_exe",
    "registry_hklm",
    "registry_hkcu_legacy",
    "appdata_dir",
    "localappdata_dir",
]
paths = data.get("paths", {})
missing = [k for k in required_paths if k not in paths]
if missing:
    raise SystemExit("missing contract path keys: " + ",".join(missing))
print("contract-ok")
PY
pass "installer-contract.json schema keys present"

# 1) Per-machine metadata must be in HKLM.
# Shortcuts/DataPathPolicy may be intentionally per-user for ICE compliance.
if rg -n 'Root="HKCU"[^\n]*Name="(InstallPath|Version|InstallScope|Cleanup)"' "$WIX_COMPONENTS" >/dev/null; then
  fail "WiX still defines per-machine metadata in HKCU"
fi

if rg -n 'Root:\s*HKCU;[^\n]*ValueName:\s*"(InstallPath|Version|InstallScope)"' "$INNO_ISS" >/dev/null; then
  fail "Inno Setup still defines per-machine metadata in HKCU"
fi

pass "per-machine metadata mapped to HKLM"

# 2) Legacy HKCU cleanup must remain for compatibility.
rg -n 'RemoveRegistryKey Root="HKCU" Key="Software\\Protons" Action="removeOnUninstall"' "$WIX_COMPONENTS" >/dev/null \
  || fail "WiX legacy HKCU cleanup entry not found"

rg -n "RegDeleteKeyIncludingSubkeys\\(HKCU, 'Software\\\\Protons'\\);" "$INNO_ISS" >/dev/null \
  || fail "Inno legacy HKCU cleanup hook not found"

pass "legacy HKCU cleanup preserved"

# 3) Windows uninstall checks must include retry/polling helpers.
rg -n 'Wait-Condition' "$MSI_TEST" >/dev/null || fail "MSI test has no retry/polling"
rg -n 'Wait-Condition' "$INNO_TEST" >/dev/null || fail "Inno test has no retry/polling"

pass "retry/polling present in Windows uninstall checks"

# 3.1) All installer suites must consume canonical contract.
for suite_file in \
  "$MSI_TEST" \
  "$INNO_TEST" \
  "$MSI_ROLLBACK_TEST" \
  "$INNO_ROLLBACK_TEST" \
  "$CANCEL_TEST" \
  "$TX_TEST"; do
  rg -n 'Get-InstallerContract' "$suite_file" >/dev/null || fail "suite does not load installer contract: $suite_file"
done
pass "installer suites consume installer-contract.json"

# 4) UX PT-BR only + visual directives must exist in Inno script.
if rg -n '^\s*Name:\s*"en"\s*;' "$INNO_ISS" >/dev/null; then
  fail "Inno Setup still defines EN language in this PT-BR-only cycle"
fi

if rg -n '^\s*en\.' "$INNO_ISS" >/dev/null; then
  fail "Inno Setup still defines EN custom messages in this PT-BR-only cycle"
fi

rg -n '^\s*ShowLanguageDialog\s*=\s*no\s*$' "$INNO_ISS" >/dev/null \
  || fail "ShowLanguageDialog=no not found in Inno Setup"

rg -n '^\s*WizardStyle\s*=\s*.*\bmodern\b.*\bdynamic\b.*\bhidebevels\b.*\bexcludelightcontrols\b\s*$' "$INNO_ISS" >/dev/null \
  || fail "WizardStyle dynamic profile not found in Inno Setup"

rg -n '^\s*WizardBackImageFile\s*=\s*\.\.\\ativos\\installer\\wizard_back_light\.png\s*$' "$INNO_ISS" >/dev/null \
  || fail "WizardBackImageFile light not found in Inno Setup"

rg -n '^\s*WizardBackImageFileDynamicDark\s*=\s*\.\.\\ativos\\installer\\wizard_back_dark\.png\s*$' "$INNO_ISS" >/dev/null \
  || fail "WizardBackImageFileDynamicDark not found in Inno Setup"

rg -n '^\s*WizardSmallImageFile\s*=\s*\.\.\\ativos\\installer\\wizard_small_logo_light\.png\s*$' "$INNO_ISS" >/dev/null \
  || fail "WizardSmallImageFile light not found in Inno Setup"

rg -n '^\s*WizardSmallImageFileDynamicDark\s*=\s*\.\.\\ativos\\installer\\wizard_small_logo_dark\.png\s*$' "$INNO_ISS" >/dev/null \
  || fail "WizardSmallImageFileDynamicDark not found in Inno Setup"

rg -n '^\s*WizardBackColorDynamicDark\s*=\s*\$[0-9A-Fa-f]{6}\s*$' "$INNO_ISS" >/dev/null \
  || fail "WizardBackColorDynamicDark not found in Inno Setup"

pass "PT-BR only language + visual wizard directives present"

# 5) Asset preparation pipeline must reference expected output file names.
rg -n 'wizard_back_light\.png' "$PREPARE_UX_SCRIPT" >/dev/null \
  || fail "prepare-inno-ux-assets script does not reference wizard_back_light.png"

rg -n 'wizard_back_dark\.png' "$PREPARE_UX_SCRIPT" >/dev/null \
  || fail "prepare-inno-ux-assets script does not reference wizard_back_dark.png"

rg -n 'wizard_small_logo_light\.png' "$PREPARE_UX_SCRIPT" >/dev/null \
  || fail "prepare-inno-ux-assets script does not reference wizard_small_logo_light.png"

rg -n 'wizard_small_logo_dark\.png' "$PREPARE_UX_SCRIPT" >/dev/null \
  || fail "prepare-inno-ux-assets script does not reference wizard_small_logo_dark.png"

pass "prepare-inno-ux-assets pipeline references expected outputs"

echo "All installer static contract checks passed."
