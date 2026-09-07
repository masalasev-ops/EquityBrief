# The Windows entry point for tools/migrate.
#
# It hands the work to the one script rather than reimplementing it, so the two
# cannot drift. Written as a script and not a function on purpose: a PowerShell
# function's return value is its output stream, so returning $LASTEXITCODE from
# one would swallow everything the script printed.

$bash = Get-Command bash -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $bash) {
    Write-Error (
        'No bash found on PATH. tools/migrate.ps1 hands its work to tools/migrate, which is a ' +
        'bash script. Install Git for Windows, or add a bash to PATH, and run this again. ' +
        'Exiting 127 rather than 0, because a gate that never ran must not look like one that passed.')
    exit 127
}

& $bash.Source (Join-Path $PSScriptRoot 'migrate') @args
exit $LASTEXITCODE
