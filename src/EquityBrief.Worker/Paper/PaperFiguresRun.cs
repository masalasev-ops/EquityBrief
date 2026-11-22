using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Providers;
using EquityBrief.Data;

namespace EquityBrief.Worker.Paper;

// Paper trading's accounts on an index as of a stored night, worked by the core's one function over the one reader the
// page reads through, so the worker states the figures the page draws. It writes nothing.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public static class PaperFiguresRun
{
    public const string Verb = "paper-figures";

    // The three indices' codes the accounts are kept under.
    public static IReadOnlyList<string> Indices { get; } = ["GSPC", FundHoldings.MidCapIndex, FundHoldings.SmallCapIndex];

    // The account settings the page walks the accounts under: the operator's saved account, or the example one.
    public static AccountSettings SettingsOf(string dataRoot) => AccountFile.Read(dataRoot) ?? AccountSettings.Example;

    // Every account's figures on an index as of a night, walked under the account settings given.
    public static async Task<IReadOnlyList<PaperFigures>> ReadAsync(string databaseFile, string index, DateOnly night, AccountSettings? settings, bool yours = true) =>
        PaperWork.Figures(await PaperWork.WorkAsync(
            new PaperReads(databaseFile),
            index,
            OwnStepCandidate.NameOf(index),
            index is not (FundHoldings.MidCapIndex or FundHoldings.SmallCapIndex),
            night,
            settings,
            yours));

    // One account's figures as a line: its key and standing, its trades and those ended, its walk's return, its open and
    // closed trades, the day it counts from, its verdict, its record's unit and its count toward a verdict.
    public static string Line(string index, PaperFigures one) =>
        string.Join(
            " | ",
            index,
            one.Family,
            one.Key,
            one.Standing,
            FormattableString.Invariant($"{one.Trades} trade(s), {one.Ended} ended"),
            one.Return is { } made ? made.ToString("R", CultureInfo.InvariantCulture) : "no return",
            FormattableString.Invariant($"{one.Open} open, {one.Closed} closed"),
            one.Since is { } since ? since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "no start",
            one.Verdict,
            one.Unit,
            one.TooFew ?? "counted");
}
