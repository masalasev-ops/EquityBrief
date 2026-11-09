# The Windows entry point for tools/merge. The work is done by the one script;
# run-bash.ps1 is the only place that knows how to reach it.
& (Join-Path $PSScriptRoot 'run-bash.ps1') 'merge' @args
exit $LASTEXITCODE
