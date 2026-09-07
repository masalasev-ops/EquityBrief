# The Windows entry point for tools/wrapper-probe, built exactly like every
# other wrapper so that testing this one tests all of them.
& (Join-Path $PSScriptRoot 'run-bash.ps1') 'wrapper-probe' @args
exit $LASTEXITCODE
