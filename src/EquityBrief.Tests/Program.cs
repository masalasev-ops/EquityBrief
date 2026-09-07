// The harness entry point. tools/verify-phase runs this; dotnet test ignores it
// and runs the suite as before.
return EquityBrief.Tests.Harness.PhaseReportCommand.Run(args);
