# The Windows entry point for tools/ledger-build. The work is done by the one script;
# run-bash.ps1 is the only place that knows how to reach it.
& (Join-Path $PSScriptRoot 'run-bash.ps1') 'ledger-build' @args
exit $LASTEXITCODE
