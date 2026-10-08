# Every CI step, in order, against a dropped store. Exits non-zero on the first
# failure and names the step that failed.
#
# Not a wrapper around tools/ci.sh. Windows PowerShell cannot parse &&, so the
# two files differ in syntax by necessity. ci-parity asserts they run the same
# steps in the same order, that a failing step fails the script it runs in, and
# that both resolve a data root of their own.
$ErrorActionPreference = 'Stop'

# Saved here and restored in the finally below, because a .ps1 invoked by name
# runs inside the caller's own PowerShell process: an $env: assignment and a
# Set-Location both outlive the script and stay in the session that ran it.
# tools/ci.sh cannot do this, since its export dies with its process, so the two
# scripts were not the same in effect on the one platform the operator runs them
# on. RUNBOOK's restore procedure is where it bites: it runs this script and then
# a night by hand, and that night would have gone to data-ci.
#
# finally runs when a script exits, including through the exit in Step below,
# and the exit code survives it.
$previousRoot = $env:EquityBrief__DataRoot
$previousLocation = Get-Location

# The commit the tracked tree stands at, and nothing where the tree holds an
# edit or a stray file, or is no repository. Read before the build and again
# after the suite, because the suite runs what the build compiled.
function CleanCommit {
    # Continue inside this function alone: under a redirected host Windows PowerShell turns a native command's
    # stderr into error records, and a git warning would otherwise end the reading under the Stop preference and
    # leave the stamp empty over a clean tree. The exit code is what decides here.
    $ErrorActionPreference = 'Continue'

    try {
        $changes = @(git status --porcelain)

        if ($LASTEXITCODE -ne 0 -or $changes.Count -gt 0) {
            return $null
        }

        $commit = git rev-parse HEAD

        if ($LASTEXITCODE -ne 0) {
            return $null
        }

        return $commit
    }
    catch {
        return $null
    }
}

# The suite's result is written where tools/verify-phase reads it, with a stamp
# beside it naming the commit it is the result of: the commit the tree stood
# clean at before the build and still stands clean at after the suite. The
# stamp is emptied otherwise, and an empty stamp names no commit, so the report
# runs the suite itself over a tree that was edited or is no commit's. Both are
# written by the suite step whether the suite passed or failed, so the pair is
# always of one run. The stamp ends in one line feed and no carriage return,
# since tools/verify-phase reads it under bash.
# see: The phase report reads the checkpoint script's suite result over the same commit and a clean tree
function StampTheSuite {
    param([string] $Started)

    $stamp = Join-Path 'artifacts' 'suite.commit'

    if ($Started -and $Started -eq (CleanCommit)) {
        Set-Content -Path $stamp -Value "$Started`n" -NoNewline -Encoding Ascii
    }
    else {
        Set-Content -Path $stamp -Value '' -NoNewline -Encoding Ascii
    }
}

# The suite step. The exit code the suite left is the step's, read back after
# the stamp's own commands have moved it.
function RunTheSuite {
    param([string] $Started)

    New-Item -ItemType Directory -Force artifacts | Out-Null
    Set-Content -Path (Join-Path 'artifacts' 'suite.commit') -Value '' -NoNewline -Encoding Ascii

    dotnet test EquityBrief.slnx --no-build --results-directory artifacts --logger "trx;LogFileName=suite.trx"
    $code = $LASTEXITCODE

    StampTheSuite $Started
    $global:LASTEXITCODE = $code
}

function Step {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][scriptblock] $Command
    )

    Write-Host ''
    Write-Host "=== $Name ==="

    # Cleared first, because after a cmdlet this still holds whatever the last
    # native command left behind and a stale zero would pass a failed step.
    $global:LASTEXITCODE = 0

    # A cmdlet-only step reports through a terminating error rather than through
    # an exit code, and under the Stop preference above that would kill the
    # script before the named line below is written. The catch is what makes a
    # step of either kind name itself on the way out, which is the property
    # ci.sh gets from `if ! "$@"` and this script did not have.
    try {
        & $Command
    }
    catch {
        Write-Host ''
        Write-Host "ci: failed at step: $Name"
        Write-Host $_.Exception.Message
        exit 1
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host "ci: failed at step: $Name"
        exit $LASTEXITCODE
    }
}

try {
    Set-Location (Split-Path -Parent $PSScriptRoot)

    # The store this drops is its own and never the operator's, for the reason
    # tools/ci.sh states at the same point.
    $env:EquityBrief__DataRoot = Join-Path (Get-Location) 'data-ci'

    $started = CleanCommit

    Step "drop the store"  { if (Test-Path data-ci) { Remove-Item -Recurse -Force data-ci } }
    Step "restore"         { dotnet restore EquityBrief.slnx }
    Step "build"           { dotnet build EquityBrief.slnx --no-restore }
    Step "suite"           { RunTheSuite $started }
    Step "migrate"         { & (Join-Path $PSScriptRoot 'migrate.ps1') }
    Step "migrate again"   { & (Join-Path $PSScriptRoot 'migrate.ps1') }

    Write-Host ''
    Write-Host 'ci: green'
}
finally {
    # Assigning $null to an $env: entry removes it, which is the right restore
    # for a session that did not carry one.
    $env:EquityBrief__DataRoot = $previousRoot
    Set-Location $previousLocation
}
