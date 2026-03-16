#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIB="$ROOT_DIR/.sync/lib-auto-sync-linux.sh"

# shellcheck source=/dev/null
source "$LIB"

assert_ignored() {
    local path="$1"
    if ! should_ignore_path "$path"; then
        echo "expected ignored path: $path" >&2
        exit 1
    fi
}

assert_tracked() {
    local path="$1"
    if should_ignore_path "$path"; then
        echo "expected tracked path: $path" >&2
        exit 1
    fi
}

assert_ignored "$ROOT_DIR/.sync/sync.log"
assert_ignored "$ROOT_DIR/.git/index"
assert_ignored "$ROOT_DIR/Login/bin/Debug/app.dll"
assert_ignored "$ROOT_DIR/Login/obj/Debug/app.o"
assert_ignored "$ROOT_DIR/Login/testes/TestResults/result.trx"
assert_tracked "$ROOT_DIR/README.md"
assert_tracked "$ROOT_DIR/Login/scripts/launch_login.sh"
assert_tracked "$ROOT_DIR/.sync/auto-sync-linux.sh"

echo "auto-sync linux path filter: ok"
