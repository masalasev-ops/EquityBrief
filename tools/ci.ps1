# Every CI step, in order, against a dropped store. Exits non-zero on the first
# failure and names the step that failed.
#
# Not a wrapper around tools/ci.sh. Windows PowerShell cannot parse &&, so the
# two files differ in syntax by necessity. ci-parity asserts they run the same
# steps in the same order, and that a failing step fails the script it runs in.
$ErrorActionPreference = 'Stop'

Set-Location (Split-Path -Parent $PSScriptRoot)

# The store this drops is its own and never the operator's, for the reason
# tools/ci.sh states at the same point.
$env:EquityBrief__DataRoot = Join-Path (Get-Location) 'data-ci'

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
    & $Command

    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host "ci: failed at step: $Name"
        exit $LASTEXITCODE
    }
}

Step "drop the store"  { if (Test-Path data-ci) { Remove-Item -Recurse -Force data-ci } }
Step "restore"         { dotnet restore EquityBrief.slnx }
Step "build"           { dotnet build EquityBrief.slnx --no-restore }
Step "suite"           { dotnet test EquityBrief.slnx --no-build }
Step "migrate"         { & (Join-Path $PSScriptRoot 'migrate.ps1') }
Step "migrate again"   { & (Join-Path $PSScriptRoot 'migrate.ps1') }

Write-Host ''
Write-Host 'ci: green'
