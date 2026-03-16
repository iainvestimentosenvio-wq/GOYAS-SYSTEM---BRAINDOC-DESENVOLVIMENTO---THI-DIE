#!/usr/bin/env bash

set -euo pipefail

extract_status_path() {
    local line="$1"
    local path="${line:3}"
    if [[ "$path" == *" -> "* ]]; then
        path="${path##* -> }"
    fi
    printf '%s\n' "$path"
}

should_ignore_path() {
    local path="$1"
    path="${path//\\//}"

    case "$path" in
        */.git/*|.git/*)
            return 0
            ;;
        */.sync/sync.log|.sync/sync.log)
            return 0
            ;;
        */.sync/*.pid|.sync/*.pid)
            return 0
            ;;
        */.sync/runtime/*|.sync/runtime/*)
            return 0
            ;;
        */bin/*|bin/*)
            return 0
            ;;
        */obj/*|obj/*)
            return 0
            ;;
        */TestResults/*|TestResults/*)
            return 0
            ;;
        *.trx)
            return 0
            ;;
    esac

    return 1
}

list_relevant_changes() {
    git status --porcelain=v1 --untracked-files=all | while IFS= read -r line; do
        local path
        path="$(extract_status_path "$line")"
        if should_ignore_path "$path"; then
            continue
        fi
        printf '%s\n' "$path"
    done
}
