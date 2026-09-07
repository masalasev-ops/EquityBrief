# The one place a PowerShell entry point hands its work to a bash script.
#
# Every bash entry point in this repository ships with a .ps1 beside it, because
# calling an extensionless bash script by name from PowerShell produces no
# output, leaves $LASTEXITCODE unset and leaves $? true, so a gate that never
# executed is indistinguishable from one that passed.
#
# Written as a script rather than a function on purpose. A PowerShell function's
# return value is its output stream, so returning $LASTEXITCODE from one would
# swallow everything the script printed.
param(
    [Parameter(Mandatory)][string] $Script,
    [Parameter(ValueFromRemainingArguments = $true)] $Arguments
)

$bash = Get-Command bash -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $bash) {
    # Written to the error stream directly rather than with Write-Error. A caller
    # that sets $ErrorActionPreference to Stop, which tools/ci.ps1 does, turns
    # Write-Error into a terminating error, so the exit below never runs, the run
    # dies at 1 and the failed-at-step line never prints. The safety property
    # survived that and the documented 127 did not.
    [Console]::Error.WriteLine(
        "No bash found on PATH, so tools/$Script cannot be run. Install Git for Windows, or " +
        'add a bash to PATH, and run this again. Exiting 127 rather than 0, because a gate ' +
        'that never ran must not look like one that passed.')
    exit 127
}

& $bash.Source (Join-Path $PSScriptRoot $Script) @Arguments
exit $LASTEXITCODE
