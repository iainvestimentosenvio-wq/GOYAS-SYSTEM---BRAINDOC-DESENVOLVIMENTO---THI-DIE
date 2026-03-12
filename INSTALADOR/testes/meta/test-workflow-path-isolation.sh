#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
CI_WORKFLOW="$PROJECT_ROOT/.github/workflows/instalador-ci.yml"
REL_WORKFLOW="$PROJECT_ROOT/.github/workflows/instalador-release.yml"

[ -f "$CI_WORKFLOW" ] || { echo "FAIL: workflow ausente: $CI_WORKFLOW" >&2; exit 1; }
[ -f "$REL_WORKFLOW" ] || { echo "FAIL: workflow ausente: $REL_WORKFLOW" >&2; exit 1; }

python3 - "$CI_WORKFLOW" "$REL_WORKFLOW" <<'PY'
import sys
from pathlib import Path
import yaml

ci_path = Path(sys.argv[1])
rel_path = Path(sys.argv[2])

def load_yaml(path: Path):
    with path.open("r", encoding="utf-8") as f:
        return yaml.safe_load(f)

def fail(msg: str):
    print(f"FAIL: {msg}", file=sys.stderr)
    raise SystemExit(1)

def require_paths(trigger_obj, expected_values, context):
    paths = trigger_obj.get("paths")
    if not isinstance(paths, list):
        fail(f"{context} nao define lista 'paths'")
    for value in expected_values:
        if value not in paths:
            fail(f"{context} nao contem path esperado: {value}")
    if any(p in ("**", "*", "/**") for p in paths):
        fail(f"{context} contem path amplo demais: {paths}")

ci = load_yaml(ci_path)
rel = load_yaml(rel_path)

ci_on = ci.get("on")
if ci_on is None:
    ci_on = ci.get(True, {})
rel_on = rel.get("on")
if rel_on is None:
    rel_on = rel.get(True, {})

for trigger in ("push", "pull_request"):
    if trigger not in ci_on:
        fail(f"instalador-ci.yml sem trigger {trigger}")
    require_paths(
        ci_on[trigger],
        ["INSTALADOR/**", ".github/workflows/instalador-ci.yml"],
        f"instalador-ci.yml:{trigger}",
    )

if "push" not in rel_on:
    fail("instalador-release.yml sem trigger push")

push = rel_on["push"]
tags = push.get("tags")
if not isinstance(tags, list) or "v*" not in tags:
    fail("instalador-release.yml:push sem tags v*")

require_paths(
    push,
    ["INSTALADOR/**", ".github/workflows/instalador-release.yml"],
    "instalador-release.yml:push",
)

print("PASS: workflows do instalador isolados por paths")
PY
