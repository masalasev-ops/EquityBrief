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
# on. RUNBOOK's restore procedure is where it bites: step 7 runs this script and
# step 9 runs a night by hand, and that night would have gone to data-ci.
#
# finally runs when a script exits, including through the exit in Step below,
# and the exit code survives it.
$previousRoot = $env:EquityBrief__DataRoot
$previousLocation = Get-Location

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

    Step "drop the store"  { if (Test-Path data-ci) { Remove-Item -Recurse -Force data-ci } }
    Step "restore"         { dotnet restore EquityBrief.slnx }
    Step "build"           { dotnet build EquityBrief.slnx --no-restore }
    Step "suite"           { dotnet test EquityBrief.slnx --no-build }
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
