// The nightly run and the overnight queue. Scheduling lives outside the
// application, so this is one command an external scheduler invokes rather than
// a service that schedules itself.
// see: Nothing is written against one operating system

// Nothing is built yet. Exiting non-zero is the point: a night that ran and did
// nothing must not be indistinguishable from a night that worked.
Console.Error.WriteLine(
    "EquityBrief.Worker: no nightly run exists yet. The membership loader is checkpoint 1.1.");
return 1;
