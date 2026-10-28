# The Windows entry point for tools/publish-site. The work is done by the one script;
# run-bash.ps1 is the only place that knows how to reach it.
& (Join-Path $PSScriptRoot 'run-bash.ps1') 'publish-site' @args
exit $LASTEXITCODE
