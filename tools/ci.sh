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

# The store this drops is its own and never the operator's.
#
# Until 5.7 both resolved to `data`, so verifying a checkpoint deleted the
# store the nightly job had been filling, and the next night silently ran a
# first-run backfill of the whole index. A tool that verifies the build must
# not be able to reach the store the build produced, and the cheapest way to
# make that true is for it never to know the path.
export EquityBrief__DataRoot="$root/data-ci"

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
  rm -rf data-ci
}

step "drop the store"  drop_store
step "restore"         dotnet restore EquityBrief.slnx
step "build"           dotnet build EquityBrief.slnx --no-restore
step "suite"           dotnet test EquityBrief.slnx --no-build
step "migrate"         ./tools/migrate
step "migrate again"   ./tools/migrate

printf '\nci: green\n'
