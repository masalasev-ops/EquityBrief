namespace EquityBrief.Core.Filter;

// The version the swing filter's replayed results are stored under.
//
// A session before the filter's first stored night holds no results of its own, and the trigger's
// arrival is read off the results of the sessions before a night. Those sessions' results are replayed
// from the bars, bands and plans the store holds as of each one and stored under this version, which is
// no filter version: the trigger's arrival reads them, and every other reader of the results passes
// them by, so no clock counts them, no setup is scored from them and no page draws them.
// see: The swing filter's results are replayed for the sessions before its first stored night, for the trigger's arrival alone
public static class ReplayedResults
{
    public const string Version = "replayed";
}
