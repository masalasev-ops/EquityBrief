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

# The commit the tracked tree stands at, and nothing where the tree holds an
# edit or a stray file, or is no repository. Read before the build and again
# after the suite, because the suite runs what the build compiled.
clean_commit() {
  if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
    return 0
  fi

  git rev-parse HEAD 2>/dev/null || true
}

started="$(clean_commit)"

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

# The suite's result is written where tools/verify-phase reads it, with a stamp
# beside it naming the commit it is the result of: the commit the tree stood
# clean at before the build and still stands clean at after the suite. The
# stamp is emptied otherwise, and an empty stamp names no commit, so the report
# runs the suite itself over a tree that was edited or is no commit's. Both are
# written by the suite step whether the suite passed or failed, so the pair is
# always of one run.
# see: The phase report reads the checkpoint script's suite result over the same commit and a clean tree
run_suite() {
  mkdir -p artifacts
  : > artifacts/suite.commit

  dotnet test EquityBrief.slnx --no-build --results-directory artifacts --logger "trx;LogFileName=suite.trx"
  code=$?

  if [ -n "$started" ] && [ "$started" = "$(clean_commit)" ]; then
    printf '%s\n' "$started" > artifacts/suite.commit
  fi

  return $code
}

step "drop the store"  drop_store
step "restore"         dotnet restore EquityBrief.slnx
step "build"           dotnet build EquityBrief.slnx --no-restore
step "suite"           run_suite
step "migrate"         ./tools/migrate
step "migrate again"   ./tools/migrate

printf '\nci: green\n'
