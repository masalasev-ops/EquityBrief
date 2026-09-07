#!/usr/bin/env bash
# Every CI step, in order, against a dropped store. Exits non-zero on the first
# failure and names the step that failed.
#
# Not a wrapper around dotnet test, and not a translation of tools/ci.ps1.
# Windows PowerShell cannot parse &&, so the two files differ in syntax by
# necessity. ci-parity asserts they run the same steps in the same order, and
# that a failing step fails the script it runs in.
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

step() {
  name="$1"
  shift
  printf '\n=== %s ===\n' "$name"

  if ! "$@"; then
    printf '\nci: failed at step: %s\n' "$name" >&2
    exit 1
  fi
}

drop_store() {
  rm -rf data
}

step "drop the store"  drop_store
step "restore"         dotnet restore EquityBrief.slnx
step "build"           dotnet build EquityBrief.slnx --no-restore
step "suite"           dotnet test EquityBrief.slnx --no-build
step "migrate"         ./tools/migrate
step "migrate again"   ./tools/migrate

printf '\nci: green\n'
