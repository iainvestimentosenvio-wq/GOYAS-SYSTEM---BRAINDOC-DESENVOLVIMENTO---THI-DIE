#!/usr/bin/env bash
set -euo pipefail

resolve_powershell_cmd() {
  if command -v pwsh >/dev/null 2>&1; then
    printf '%s' "pwsh"
    return 0
  fi

  if command -v powershell >/dev/null 2>&1; then
    printf '%s' "powershell"
    return 0
  fi

  # Fallback textual for instructions on hosts where PowerShell is not installed.
  printf '%s' "pwsh"
}
