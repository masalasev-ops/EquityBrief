using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Worker.Filter;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Moves;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Research;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Volume;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Quarters;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Rules;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Checks;

// pinned-constants: the versions the specs state against the build files, section
// 17's figures against what holds them, and the listed restatements against the code.
//
// CHANGELOG.md and PROGRESS.md are records and are not read; a decision that stands is.
public class PinnedConstants
{
    const string Framework = @"net[0-9]+\.[0-9]+";
    const string Band = @"[0-9]+\.[0-9]+\.[0-9]xx";

    // The specs, which is where a version may be stated. Records are exempt for
    // the reason given above. Read once so both checks scan the same population
    // and neither can quietly narrow to the file that happens to state it.
    //
    // The rules files are in it, because a constant stated in one is a constant
    // stated in a spec: they carry CLAUDE.md's own text, and a version that moved
    // in the file that loads for a session working in `tools/` is exactly the one
    // that would go unread.
    internal static IReadOnlyDictionary<string, string> Specs() =>
        Corpus.SpecsAndRules.ToDictionary(spec => spec, Corpus.Read, StringComparer.Ordinal);

    [Fact]
    public void EveryFigureSectionSeventeenStatesInDigitsIsHeldByTheCodeOrNamedForWhatItIs()
    {
        var limits = ArchitectureTables.In(File.ReadAllText(Repository.Architecture)).Single(table => table.Heading == Scope.LimitsTable);
        var stated = StatedFigures.InValues(limits);
        var census = SectionSeventeen(limits, Corpus.Read("docs/PROGRESS.md"), stated);

        Assert.Empty(StatedFigures.Unheld(stated, census));
    }

    // The reports a trial or a review runs over, which the shipped settings hold rather than a constant, since the
    // operator sets it.
    static decimal ShippedTrialReports(string field = ResearchLane.TrialField) =>
        decimal.Parse(
            new ConfigurationBuilder()
                .AddJsonFile(Providers.ResearchModelFeedTests.ShippedConfiguration)
                .Build()[ModelProfiles.JobField(ModelProfiles.ResearchJob, field) + ":Reports"]!,
            CultureInfo.InvariantCulture);

    static IReadOnlyList<HeldFigure> SectionSeventeen(
        ArchitectureTable limits,
        string progress,
        IReadOnlyList<(string Row, string Stated)> stated)
    {
        decimal Figure(string row, int at) => StatedFigures.ValueOf(stated.Where(figure => figure.Row == row).ElementAt(at).Stated);

        bool Recorded(string figure) => Regex.IsMatch(progress, $@"(?<![\d.,]){Regex.Escape(figure)}(?![\d]|[.,]\d)");

        bool Landed(string checkpoint) => Regex.IsMatch(progress, $@"^### {Regex.Escape(checkpoint)} - ", RegexOptions.Multiline);

        const string Zero = "a limit of zero, which nightly-cost counts over a recorded night";
        const string Measured = "a measurement the record carries";
        const string Checkpoint = "the checkpoint that measured it";

        const string Calls = "Model calls in the nightly run";
        const string Clock = "Nightly wall clock, at index size";
        const string Retry = "Per-request timeout and the night's deadline";
        const string Budget = "Weighted-call budget";
        const string QueueRow = "Overnight queue";
        const string Admissible = "Source admissibility";
        const string Significance = "Significance threshold";
        const string Versions = "Rule versions scored at once";
        const string BlocksRow = "Blocks a record is judged over";
        const string LooksRow = "Looks a candidate's verdict is read at";
        const string CalibrationRow = "The calibrated bar";
        const string PowerRow = "Power stated at a look";
        const string CandidatesRow = "The three candidates' numbers";
        const string SweepGridRow = "Sweep grid";
        const string SweepStart = "Sweep starting point";
        const string SweepVariantsRow = "Sweep variants";
        const string SweepRun = "Sweep run";
        const string SweepEdgeRow = "Sweep edge";
        const string SweepConditionsRow = "Sweep conditions";
        const string SweepPointInTimeRow = "Sweep point in time";
        const string SweepSearchRow = "Sweep search";

        return
        [
            new(Calls, "0", 0, Zero),
            new("Per-name network calls in the nightly run", "0", 0, Zero),
            new(Clock, "40", (decimal)RetryPolicy.WallClock.TotalMinutes, "RetryPolicy.WallClock in minutes"),
            new(Clock, "503", null, Measured, () => Recorded("503")),
            new(Clock, "580", null, Measured, () => Recorded("580")),
            new(Clock, "701", null, Measured, () => Recorded("701")),
            new(Clock, "29", null, Measured, () => Recorded("29")),
            new(Clock, "35", null, Measured, () => Recorded("35")),
            new(Retry, "3", RetryPolicy.Standard.Attempts, "RetryPolicy.Standard.Attempts"),
            new(Retry, "2", (decimal)RetryPolicy.Standard.WaitBefore(2).TotalSeconds, "the wait before the second attempt"),
            new(Retry, "4", (decimal)RetryPolicy.Standard.WaitBefore(3).TotalSeconds, "the wait before the third attempt"),
            new(Retry, "30", (decimal)RetryPolicy.Standard.Timeout.TotalSeconds, "RetryPolicy.Standard.Timeout in seconds"),
            new(Retry, "120", (decimal)RetryPolicy.Standard.Deadline.TotalMinutes, "RetryPolicy.Standard.Deadline in minutes"),
            new("Waiting on another writer", "600", StoreConnection.WaitSeconds, "StoreConnection.WaitSeconds"),
            new(Budget, "100,000", ProviderWeights.DailyAllowance, "ProviderWeights.DailyAllowance"),
            new(Budget, "100", ProviderWeights.BulkEndOfDay, "ProviderWeights.BulkEndOfDay"),
            new(Budget, "1", ProviderWeights.HistoricalPerTicker, "ProviderWeights.HistoricalPerTicker"),
            new(Budget, "10", ProviderWeights.Fundamentals, "ProviderWeights.Fundamentals"),
            new(Budget, "5", ProviderWeights.News, "ProviderWeights.News"),
            new(Budget, "1", ProviderWeights.EarningsCalendar, "ProviderWeights.EarningsCalendar"),
            new("Bar history kept", "1", BarFetcher.RetentionYears, "BarFetcher.RetentionYears"),
            new("Backfill", "5", Backfill.RetryNights, "Backfill.RetryNights"),
            new("Backfill", "7", Backfill.WeeklyRetryDays, "Backfill.WeeklyRetryDays"),
            new("Level window", "60", VolumeProfileSeries.Window, "VolumeProfileSeries.Window"),
            new("Swing lookback", "3", SwingSeries.Lookback, "SwingSeries.Lookback"),
            new("Tranches, exits", "3", LadderSeries.MostTranches, "LadderSeries.MostTranches"),
            new("Tranches, exits", "5", LadderSeries.MostExits, "LadderSeries.MostExits"),
            new("Earnings horizon", "20", ShortlistSeries.EarningsHorizonSessions, "ShortlistSeries.EarningsHorizonSessions"),
            new("List display", "20", SinglePageApp.TonightDrawn, "SinglePageApp.TonightDrawn"),
            new(QueueRow, "1", OvernightQueue.DefaultHours, "OvernightQueue.DefaultHours"),
            new(QueueRow, "6.10", null, Checkpoint, () => Landed("6.10")),
            new(QueueRow, "503", null, Measured, () => Recorded("503")),
            new(QueueRow, "43", Math.Round(Figure(QueueRow, 2) * Figure(QueueRow, 4) / 60m), "the names at the slowest pass, in minutes"),
            new(QueueRow, "5.13", null, Measured, () => Recorded("5.13")),
            new("Research passes per name per open", "1", null, "a count the runner keeps by refusing a second pass on the same day, which no constant holds", () => FixtureExpectations.Reach.Covers(Scope.LimitsTable, "Research passes per name per open")),
            new("Research staleness triggers", "90", Staleness.BaselineDays, "Staleness.BaselineDays"),
            new("Spend cap", "10", SpendCaps.DefaultDay, "SpendCaps.DefaultDay"),
            new("Spend cap", "50", SpendCaps.DefaultMonth, "SpendCaps.DefaultMonth"),
            new("Section trial", "3", ShippedTrialReports(), "the shipped EquityBrief:Models:Research:Trial:Reports"),
            new("Section review", "3", ShippedTrialReports(ResearchLane.ReviewField), "the shipped EquityBrief:Models:Research:Review:Reports"),
            new("Report rates", "20", EquityBrief.Web.Marks.ReportsView.RateWindow, "ReportsView.RateWindow"),
            new("Report rates", "20", EquityBrief.Web.Marks.ReportsView.RateWindow, "ReportsView.RateWindow, for the two cases' count"),
            new("Paid model retirement warning", "30", ModelProfiles.RetirementWarningDays, "ModelProfiles.RetirementWarningDays"),
            new("News labelling window", "30", Worker.News.NewsLabeller.WindowDays, "NewsLabeller.WindowDays"),
            new("News labelling window", "20", Worker.News.NewsLabeller.ArticlesAName, "NewsLabeller.ArticlesAName"),
            new("News labelling window", "8,000", Core.News.NewsInstruction.TextCharacters, "NewsInstruction.TextCharacters"),
            new("News labeller time limit", "20", Worker.News.NewsLimits.DefaultTimeLimitMinutes, "NewsLimits.DefaultTimeLimitMinutes"),
            new("News labeller month limit", "5", Worker.News.NewsLimits.DefaultMonthLimit, "NewsLimits.DefaultMonthLimit"),
            new("Store copies", "3", Core.Configuration.StoreCopies.Kept, "StoreCopies.Kept"),
            new("Store copies", "20", (decimal)Core.Configuration.StoreCopies.WaitsAtMost.TotalHours, "StoreCopies.WaitsAtMost in hours"),
            new("Store copies", "30", (decimal)Worker.Backup.StoreBackup.Between.TotalSeconds, "StoreBackup.Between in seconds"),
            new("Store copies", "10", (decimal)Worker.Backup.StoreBackup.PastTheLabellersLimit.TotalMinutes, "StoreBackup.PastTheLabellersLimit in minutes"),
            new("News article retention", "31", Worker.News.NewsPulseCounter.ArticleRetentionDays, "NewsPulseCounter.ArticleRetentionDays"),
            new("A report named for its cost", "2", SpendCaps.ReportNamedAbove, "SpendCaps.ReportNamedAbove"),
            new("Risk kinds", "7", RiskFields.Kinds.Length, "RiskFields.Kinds.Length"),
            new("Theme search parameters", "3", ThemeSearch.ResultsASite, "ThemeSearch.ResultsASite"),
            new("Theme search parameters", "10", ThemeSearch.MostPages, "ThemeSearch.MostPages"),
            new("Theme search parameters", "5", ThemeSearch.MentionsPerTenThousand, "ThemeSearch.MentionsPerTenThousand"),
            new("Theme search parameters", "10,000", ThemeSearch.CountedOver, "ThemeSearch.CountedOver"),
            new("Theme search parameters", "30,000", ThemeSearch.CharactersAPage, "ThemeSearch.CharactersAPage"),
            new("Sector sites", "1", SourceMeasurement.PagesToJoin, "SourceMeasurement.PagesToJoin"),
            new(Admissible, "6.3", null, Checkpoint, () => Landed("6.3")),
            new(Admissible, "3", Admissibility.SentencesInAParagraph, "Admissibility.SentencesInAParagraph"),
            new(Admissible, "40", Admissibility.WordsInAParagraph, "Admissibility.WordsInAParagraph"),
            new(Admissible, "1", Admissibility.RiskWarningsThatRefuse, "Admissibility.RiskWarningsThatRefuse"),
            new(Admissible, "1", Admissibility.InvitationsBesideAProduct, "Admissibility.InvitationsBesideAProduct"),
            new(Admissible, "411", null, Measured, () => Recorded("411")),
            new("Setup resolution", "63", ForwardReturnSeries.SetupSessionCap, "ForwardReturnSeries.SetupSessionCap"),
            new("Minimum resolved setups", "250", ReasonVerdict.MinimumResolved, "ReasonVerdict.MinimumResolved"),
            new("Minimum resolved setups", "60", ReasonVerdict.MinimumSessions, "ReasonVerdict.MinimumSessions"),
            new("Minimum resolved setups", "400", ReasonVerdict.MinimumBeforeALiveReasonIsRetired, "ReasonVerdict.MinimumBeforeALiveReasonIsRetired"),
            new("Family size and correction", "9", CandidateFamily.Maximum, "CandidateFamily.Maximum"),
            new("Distinct trials", "0.05", (decimal)ReasonVerdict.Significance, "ReasonVerdict.Significance"),
            new(Significance, "0.05", (decimal)ReasonVerdict.Significance, "ReasonVerdict.Significance"),
            new(Significance, "6", ReasonVerdict.LiveFamily, "ReasonVerdict.LiveFamily"),
            new(Significance, "9", CandidateFamily.Maximum, "CandidateFamily.Maximum"),
            new(BlocksRow, "63", Blocks.Sessions, "Blocks.Sessions"),
            new(BlocksRow, "8", Blocks.Floor, "Blocks.Floor"),
            new(LooksRow, "8", Looks.At[0], "Looks.At[0]"),
            new(LooksRow, "12", Looks.At[1], "Looks.At[1]"),
            new(LooksRow, "16", Looks.At[2], "Looks.At[2]"),
            new(LooksRow, "8", Blocks.Floor, "Blocks.Floor, which the first look is read at"),
            new(CalibrationRow, "4000", NullWin.Paths, "NullWin.Paths"),
            new(CalibrationRow, "63", NullWin.VolatilityWindow, "NullWin.VolatilityWindow"),
            new(CalibrationRow, "20260922", NullWin.Seed, "NullWin.Seed"),
            new(CalibrationRow, "10", (decimal)NullWin.CostBasisPoints, "NullWin.CostBasisPoints"),
            new(CalibrationRow, "30", (decimal)NullWin.SensitivityBasisPoints, "NullWin.SensitivityBasisPoints"),
            new(CandidatesRow, "1", (decimal)ArrivedAndNarrow.ProposedWidth, "ArrivedAndNarrow.ProposedWidth"),
            new(CandidatesRow, "2", (decimal)VolumeAgainstTheNight.ProposedMultiple, "VolumeAgainstTheNight.ProposedMultiple"),
            new(CandidatesRow, "0.5", (decimal)CrossedByAMargin.SettledMargin, "CrossedByAMargin.SettledMargin"),
            new(PowerRow, "80", (decimal)Looks.PowerStatedAt * 100, "Looks.PowerStatedAt as a percentage"),
            new(PowerRow, "0.43", null, Measured, () => Recorded("0.43")),
            new(PowerRow, "3.26", null, Measured, () => Recorded("3.26")),
            new(PowerRow, "2.61", null, Measured, () => Recorded("2.61")),
            new(PowerRow, "1.43", null, Measured, () => Recorded("1.43")),
            new(Versions, "2", RuleVersions.MostOfTheMergeDistance, "RuleVersions.MostOfTheMergeDistance"),
            new(Versions, "4", RuleVersions.MostPerRule, "RuleVersions.MostPerRule"),
            new(Versions, "4", LadderRules.All.Count - 1, "the ladder rules other than the merge distance"),
            new(Versions, "18", RuleVersions.MostAtOnce, "RuleVersions.MostAtOnce"),
            new("Reports the night asks for", "6", RequestDrain.NightAsksFor, "RequestDrain.NightAsksFor"),
            new("Names a family lists", "5", Core.Families.SetupFamilies.ListedANight, "SetupFamilies.ListedANight"),
            new("Breakout high window", "126", Core.Families.BreakoutRule.HighSessions, "BreakoutRule.HighSessions"),
            new("Breakout high window", "251", Core.Families.BreakoutRule.ProvisionalHighSessions, "BreakoutRule.ProvisionalHighSessions"),
            new("Breakout volume multiple", "1.5", (decimal)Core.Families.BreakoutRule.VolumeMultiple, "BreakoutRule.VolumeMultiple"),
            new("Breakout range window", "20", Core.Families.BreakoutRule.RangeSessions, "BreakoutRule.RangeSessions"),
            new("Breakout range window", "20", Core.Families.BreakoutRule.RangeSessions, "BreakoutRule.RangeSessions"),
            new("Breakout range ceiling", "0.85", (decimal)Core.Families.BreakoutRule.RangeCeiling, "BreakoutRule.RangeCeiling"),
            new("Breakout range ceiling", "1.0", (decimal)Core.Families.BreakoutRule.ProvisionalRangeCeiling, "BreakoutRule.ProvisionalRangeCeiling"),
            new("Breakout stop", "1.5", (decimal)Core.Families.BreakoutRule.StopMoves, "BreakoutRule.StopMoves"),
            new("Breakout stop", "2", (decimal)Core.Families.BreakoutRule.ProvisionalStopMoves, "BreakoutRule.ProvisionalStopMoves"),
            new("Breakout session cap", "63", Core.Families.BreakoutRule.CapSessions, "BreakoutRule.CapSessions"),
            new("Drift window", "3", Core.Families.DriftRule.WindowSessions, "DriftRule.WindowSessions"),
            new("Drift window", "5", Core.Families.DriftRule.ProvisionalWindowSessions, "DriftRule.ProvisionalWindowSessions"),
            new("Drift reaction", "0.5", (decimal)Core.Families.DriftRule.ReactionMoves, "DriftRule.ReactionMoves"),
            new("Drift reaction", "1.0", (decimal)Core.Families.DriftRule.ProvisionalReactionMoves, "DriftRule.ProvisionalReactionMoves"),
            new("Drift volume multiple", "2.0", (decimal)Core.Families.DriftRule.VolumeMultiple, "DriftRule.VolumeMultiple"),
            new("Drift volume multiple", "1.5", (decimal)Core.Families.DriftRule.ProvisionalVolumeMultiple, "DriftRule.ProvisionalVolumeMultiple"),
            new("Drift target", "2", (decimal)Core.Families.DriftRule.TargetBandMoves, "DriftRule.TargetBandMoves"),
            new("Drift target", "2.5", (decimal)Core.Families.DriftRule.TargetRiskMultiple, "DriftRule.TargetRiskMultiple"),
            new("Drift target", "1", (decimal)Core.Families.DriftRule.VariantStopFloorMoves, "DriftRule.VariantStopFloorMoves"),
            new("Drift session cap", "60", Core.Families.DriftRule.CapSessions, "DriftRule.CapSessions"),
            new("Leader sectors", "3", Core.Families.LeaderRule.TopSectors, "LeaderRule.TopSectors"),
            new("Leader sectors", "126", SwingReadings.ReturnLongSessions, "SwingReadings.ReturnLongSessions"),
            new("Leader share", "4", Core.Families.LeaderRule.QuarterOf, "LeaderRule.QuarterOf"),
            new("Sector ranking floor", "5", Core.Families.LeaderRule.SectorFloor, "LeaderRule.SectorFloor"),
            new("Pullback base", "2", (decimal)Worker.Filter.ShapeCommand.FreezeRewardToRiskFloor, "ShapeCommand.FreezeRewardToRiskFloor"),
            // The analysts' revisions, 14.6.
            new("Estimates raised", "30", Core.Quarters.EstimateReading.Days, "EstimateReading.Days"),
            new("A family rule's list", "5", Core.Families.SetupFamilies.ListedANight, "SetupFamilies.ListedANight"),
            new("Market switches", "200", Worker.Candidates.TheSetupFamilies.BreakoutSwitch.IndexAverageSessions, "TheSetupFamilies.BreakoutSwitch.IndexAverageSessions"),
            new("Market switches", "10", Worker.Candidates.TheSetupFamilies.DriftSwitch.VixLookbackSessions, "TheSetupFamilies.DriftSwitch.VixLookbackSessions"),
            new("Market switches", "400", Worker.Bars.MarketSeriesFetcher.WindowDays, "MarketSeriesFetcher.WindowDays"),
            new("Group floor", "5", Groups.Floor, "Groups.Floor"),
            new("Peer return window", "60", PeerReadings.ReturnWindow, "PeerReadings.ReturnWindow"),
            new("Peers drawn", "10", PeerPicks.Shown, "PeerPicks.Shown"),
            new("Peer likeness floor", "60", PeerPicks.FewestSessions, "PeerPicks.FewestSessions"),
            new("Relative strength windows", "63", SwingReadings.ReturnShortSessions, "SwingReadings.ReturnShortSessions"),
            new("Relative strength windows", "126", SwingReadings.ReturnLongSessions, "SwingReadings.ReturnLongSessions"),
            new("Recent high window", "20", SwingReadings.HighWindow, "SwingReadings.HighWindow"),
            new("Range tightness windows", "10", SwingReadings.TightShortSessions, "SwingReadings.TightShortSessions"),
            new("Range tightness windows", "50", SwingReadings.TightLongSessions, "SwingReadings.TightLongSessions"),
            new("Breadth", "200", SwingReadings.BreadthAverageSessions, "SwingReadings.BreadthAverageSessions"),
            new("Breadth", "50", SwingReadings.ContextAverageSessions, "SwingReadings.ContextAverageSessions"),
            new("Market gate", "50", (decimal)FilterSettings.ProposedBreadthFloor * 100, "FilterSettings.ProposedBreadthFloor as a percentage"),
            new("Market gate", "45", (decimal)FilterCounts.LowerBreadthFloor * 100, "FilterCounts.LowerBreadthFloor as a percentage"),
            new("Market gate", "50", (decimal)FilterSettings.ProposedBreadthFloor * 100, "FilterSettings.ProposedBreadthFloor as a percentage, the second floor counted"),
            new("Market label", "60", (decimal)MarketLabel.HealthyFrom * 100, "MarketLabel.HealthyFrom as a percentage"),
            new("Market label", "200", SwingReadings.BreadthAverageSessions, "SwingReadings.BreadthAverageSessions"),
            new("Pullback depth", "2", (decimal)FilterSettings.ProposedDepthLow, "FilterSettings.ProposedDepthLow"),
            new("Pullback depth", "5", (decimal)FilterSettings.ProposedDepthHigh, "FilterSettings.ProposedDepthHigh"),
            new("Volume dry-up", "1.0", (decimal)FilterSettings.ProposedDryUpCeiling, "FilterSettings.ProposedDryUpCeiling"),
            new("Trade reward to risk", "2", (decimal)FilterSettings.ProposedRewardToRiskFloor, "FilterSettings.ProposedRewardToRiskFloor"),
            new("Trade stop distance", "1.0", (decimal)FilterSettings.ProposedStopLow, "FilterSettings.ProposedStopLow"),
            new("Trade stop distance", "2.5", (decimal)FilterSettings.ProposedStopHigh, "FilterSettings.ProposedStopHigh"),
            new("Earnings exclusion", "15", FilterSettings.ProposedEarningsWindowSessions, "FilterSettings.ProposedEarningsWindowSessions"),
            new("Trigger arrival window", "3", FilterSettings.ProposedArrivalSessions, "FilterSettings.ProposedArrivalSessions"),
            new("Event session share", "25", (decimal)ShapeClock.EventShare * 100, "ShapeClock.EventShare as a percentage"),
            new("Event volume ratio", "1.8", (decimal)ShapeClock.EventVolumeRatio, "ShapeClock.EventVolumeRatio"),
            new("Shape calibration nights", "60", ShapeClock.CalibrationNights, "ShapeClock.CalibrationNights"),
            new("Gate bands", "38", ShapeClock.GateBands[0].Low, "ShapeClock.GateBands[0].Low"),
            new("Gate bands", "347", ShapeClock.GateBands[0].High, "ShapeClock.GateBands[0].High"),
            new("Gate bands", "14", ShapeClock.GateBands[1].Low, "ShapeClock.GateBands[1].Low"),
            new("Gate bands", "126", ShapeClock.GateBands[1].High, "ShapeClock.GateBands[1].High"),
            new("Gate bands", "6", ShapeClock.GateBands[2].Low, "ShapeClock.GateBands[2].Low"),
            new("Gate bands", "56", ShapeClock.GateBands[2].High, "ShapeClock.GateBands[2].High"),
            new("Gate bands", "1", ShapeClock.GateBands[3].Low, "ShapeClock.GateBands[3].Low"),
            new("Gate bands", "12", ShapeClock.GateBands[3].High, "ShapeClock.GateBands[3].High"),
            new("List band", "1", ShapeClock.ListLow, "ShapeClock.ListLow"),
            new("List band", "9", ShapeClock.ListHigh, "ShapeClock.ListHigh"),
            new("Shape lever ranges", "0.40", (decimal)ShapeProposals.LeverRanges[0].From / 100, "ShapeProposals.LeverRanges[0].From in hundredths"),
            new("Shape lever ranges", "0.95", (decimal)ShapeProposals.LeverRanges[0].To / 100, "ShapeProposals.LeverRanges[0].To in hundredths"),
            new("Shape lever ranges", "0.01", (decimal)ShapeProposals.LeverRanges[0].Hundredths / 100, "ShapeProposals.LeverRanges[0].Hundredths in hundredths"),
            new("Shape lever ranges", "0.50", (decimal)ShapeProposals.LeverRanges[1].From / 100, "ShapeProposals.LeverRanges[1].From in hundredths"),
            new("Shape lever ranges", "2.00", (decimal)ShapeProposals.LeverRanges[1].To / 100, "ShapeProposals.LeverRanges[1].To in hundredths"),
            new("Shape lever ranges", "0.05", (decimal)ShapeProposals.LeverRanges[1].Hundredths / 100, "ShapeProposals.LeverRanges[1].Hundredths in hundredths"),
            new("Shape lever ranges", "1.00", (decimal)ShapeProposals.LeverRanges[3].From / 100, "ShapeProposals.LeverRanges[3].From in hundredths"),
            new("Shape lever ranges", "4.00", (decimal)ShapeProposals.LeverRanges[3].To / 100, "ShapeProposals.LeverRanges[3].To in hundredths"),
            new("Shape lever ranges", "0.10", (decimal)ShapeProposals.LeverRanges[3].Hundredths / 100, "ShapeProposals.LeverRanges[3].Hundredths in hundredths"),
            new("Swing trade plan", "1", (decimal)SwingGates.ClearStopMoves, "SwingGates.ClearStopMoves"),
            new("Swing trade plan", "2", (decimal)SwingGates.ClearTargetMoves, "SwingGates.ClearTargetMoves"),
            new("The swing family", "1", (decimal)TheSwingFamily.VariantDepthLow, "TheSwingFamily.VariantDepthLow"),
            new("The swing family", "3", (decimal)TheSwingFamily.VariantDepthHigh, "TheSwingFamily.VariantDepthHigh"),
            new("The swing family", "3", TheSwingFamily.VariantBestOf, "TheSwingFamily.VariantBestOf"),
            new("The swing family", "9", TheSwingFamily.For("1", FilterSettings.Proposed).Count, "the registrations TheSwingFamily writes"),
            new("The swing family", "0.05", (decimal)ReasonVerdict.Significance, "ReasonVerdict.Significance"),
            new("Swing plan outcome", "63", ForwardReturnSeries.CapOf(ForwardReturnSeries.Swing), "ForwardReturnSeries.CapOf(ForwardReturnSeries.Swing)"),
            new("Twenty-session outcome", "20", ForwardReturnSeries.CapOf(ForwardReturnSeries.SwingTwenty), "ForwardReturnSeries.CapOf(ForwardReturnSeries.SwingTwenty)"),
            new("Quarters step", "15", (decimal)QuarterFetcher.Limit.TotalMinutes, "QuarterFetcher.Limit in minutes"),
            new("Quarters step", "5", QuarterFetcher.RetryNights, "QuarterFetcher.RetryNights"),
            new("Quarters step", "7", QuarterFetcher.WeeklyRetryDays, "QuarterFetcher.WeeklyRetryDays"),
            new("Quarters fill", "260", QuarterFetcher.FillPerNight, "QuarterFetcher.FillPerNight"),
            new("Quarter prices", "3", QuarterFetch.PriceYears, "QuarterFetch.PriceYears"),
            new("Quarter prices", "1", ProviderWeights.HistoricalPerTicker, "ProviderWeights.HistoricalPerTicker"),
            new("Trajectory quarters", "2", QuarterReadings.TrajectoryQuarters, "QuarterReadings.TrajectoryQuarters"),
            new("Estimate record", "8", QuarterReadings.RecordQuarters, "QuarterReadings.RecordQuarters"),
            new("Estimate record", "4", QuarterReadings.RecordMinimum, "QuarterReadings.RecordMinimum"),
            new("Met tolerance", "0.01", QuarterReadings.MetCents, "QuarterReadings.MetCents"),
            new("Met tolerance", "1", QuarterReadings.MetShare * 100, "QuarterReadings.MetShare as a percentage"),
            new("Earnings quality", "4", QuarterReadings.QualityQuarters, "QuarterReadings.QualityQuarters"),
            new("Earnings quality", "1.0", QuarterReadings.QualityLow, "QuarterReadings.QualityLow"),
            new("Earnings quality", "1.0", QuarterReadings.QualityLow, "QuarterReadings.QualityLow, where the middle band starts"),
            new("Earnings quality", "2.0", QuarterReadings.QualityHigh, "QuarterReadings.QualityHigh, where the middle band ends"),
            new("Earnings quality", "2.0", QuarterReadings.QualityHigh, "QuarterReadings.QualityHigh"),
            new("Valuation position", "12", QuarterFetch.Kept, "QuarterFetch.Kept"),
            new("Valuation position", "8", QuarterReadings.ValuationMinimum, "QuarterReadings.ValuationMinimum"),
            new("Weighted calls a quarters ask", "11", QuarterFetcher.WeightOfAnAsk, "QuarterFetcher.WeightOfAnAsk"),
            new("Weighted calls a quarters ask", "10", ProviderWeights.Fundamentals, "ProviderWeights.Fundamentals"),
            new("Weighted calls a quarters ask", "1", ProviderWeights.HistoricalPerTicker, "ProviderWeights.HistoricalPerTicker"),
            new("Weighted calls a quarters ask", "10", ProviderWeights.Fundamentals, "ProviderWeights.Fundamentals, what an ask storing nothing costs"),
            // The sweep's rows, each figure in the order its row states it, the rows in the table's order.
            new(SweepGridRow,"46,656", Core.Sweep.SweepAxes.Designs(selectionOnly: false).Count, "the designs SweepAxes holds"),
            new(SweepGridRow,"54,432", Core.Sweep.SweepAxes.AllCombinations, "SweepAxes.AllCombinations"),
            new(SweepGridRow,"7,776", Core.Sweep.SweepAxes.AllCombinations - Core.Sweep.SweepAxes.Designs(selectionOnly: false).Count, "the combinations SweepAxes leaves out"),
            new(SweepGridRow,"19,683", Core.Sweep.SweepGrid.Coarse.Variations, "SweepGrid.Coarse.Variations"),
            new(SweepGridRow,"300", Core.Sweep.SweepMeasures.TradeFloor, "SweepMeasures.TradeFloor"),
            new(SweepGridRow,"6", Core.Sweep.SweepMeasures.YearsBeating, "SweepMeasures.YearsBeating"),
            new(SweepGridRow,"8", Core.Sweep.SweepFigures.Years, "SweepFigures.Years"),
            new(SweepGridRow,"100", Worker.Sweep.SweepStages.ViableForARank, "SweepStages.ViableForARank"),
            new(SweepGridRow,"5", Worker.Sweep.SweepSearch.Carried, "SweepSearch.Carried"),
            new(SweepGridRow,"2,016,000", Core.Sweep.SweepGrid.Fine.Variations, "SweepGrid.Fine.Variations"),
            new(SweepStart, "0.05", (decimal)Worker.Sweep.SweepSearch.PlateauMargin, "SweepSearch.PlateauMargin"),
            new(SweepStart, "0.05", (decimal)Worker.Sweep.SweepSearch.PlateauMargin, "SweepSearch.PlateauMargin, across the designs"),
            new(SweepStart, "6", Core.Sweep.SweepMeasures.YearsBeating, "SweepMeasures.YearsBeating"),
            new(SweepStart, "8", Core.Sweep.SweepFigures.Years, "SweepFigures.Years"),
            new(SweepStart, "300", Core.Sweep.SweepMeasures.TradeFloor, "SweepMeasures.TradeFloor"),
            new(SweepStart, "22", Core.Sweep.SweepMeasures.BlockFloor, "SweepMeasures.BlockFloor"),
            new(SweepStart, "30", Core.Sweep.SweepFigures.WholeBlocks, "SweepFigures.WholeBlocks"),
            new(SweepStart, "60", (decimal)Core.Sweep.SweepMeasures.ListingShare * 100, "SweepMeasures.ListingShare as a percentage"),
            new(SweepStart, "3", Core.Sweep.SweepMeasures.RecentYears, "SweepMeasures.RecentYears"),
            new(SweepStart, "0.03", (decimal)Worker.Sweep.SweepSearch.OtherMargins[0], "SweepSearch.OtherMargins, the first"),
            new(SweepStart, "0.08", (decimal)Worker.Sweep.SweepSearch.OtherMargins[1], "SweepSearch.OtherMargins, the second"),
            new(SweepStart, "3", (decimal)Worker.Sweep.SweepReport.MissingThreshold * 100, "SweepReport.MissingThreshold as a percentage"),
            new(SweepVariantsRow, "23", Worker.Sweep.SweepPlateau.StructuralNeighbours(Core.Sweep.SweepDesign.Live).Count, "SweepPlateau.StructuralNeighbours of the live design"),
            new(SweepVariantsRow, "5", Worker.Sweep.SweepPlateau.YearsEitherMayWin, "SweepPlateau.YearsEitherMayWin"),
            new(SweepVariantsRow, "8", Core.Sweep.SweepFigures.Years, "SweepFigures.Years"),
            new(SweepVariantsRow, "25", (decimal)Worker.Sweep.SweepPlateau.OutsideFloor * 100, "SweepPlateau.OutsideFloor as a percentage"),
            new(SweepVariantsRow, "30", Worker.Sweep.SweepPlateau.TradesAYear, "SweepPlateau.TradesAYear"),
            new(SweepVariantsRow, "6", Worker.Sweep.SweepPlateau.MostVariants, "SweepPlateau.MostVariants"),
            new(SweepVariantsRow, "3", Worker.Sweep.SweepPlateau.StrongestReported, "SweepPlateau.StrongestReported"),
            new(SweepVariantsRow, "3", Worker.Sweep.SweepPlateau.StrongestReported, "SweepPlateau.StrongestReported, the ones that follow"),
            new(SweepRun, "20", Worker.Sweep.SweepRunner.FirstSessions, "SweepRunner.FirstSessions"),
            new(SweepRun, "100", Worker.Sweep.SweepRunner.SessionsPerChunk, "SweepRunner.SessionsPerChunk"),
            new(SweepRun, "50", Worker.Sweep.SweepRunner.DesignsPerChunk, "SweepRunner.DesignsPerChunk"),
            new(SweepRun, "50", Worker.Sweep.SweepRunner.CrossingsPerChunk, "SweepRunner.CrossingsPerChunk"),
            new(SweepRun, "23", Worker.Sweep.SweepRunner.PauseFrom.Hours, "SweepRunner.PauseFrom's hour"),
            new(SweepRun, "00", Worker.Sweep.SweepRunner.PauseFrom.Minutes, "SweepRunner.PauseFrom's minute"),
            new(SweepRun, "2", Worker.Sweep.SweepRunner.ClearPolls, "SweepRunner.ClearPolls"),
            new(SweepRun, "60", (decimal)Worker.Sweep.SweepRunner.Poll.TotalSeconds, "SweepRunner.Poll in seconds"),
            new(SweepRun, "7", (decimal)(Worker.Sweep.SweepRunner.GiveUpWaitingAt + (TimeSpan.FromHours(24) - Worker.Sweep.SweepRunner.PauseFrom)).TotalHours, "SweepRunner's wait for a night that never came, in hours from the window's opening"),
            new(SweepRun, "5", (decimal)Worker.Sweep.SweepRunner.Longest.TotalDays, "SweepRunner.Longest in days"),
            new(SweepRun, "5", (decimal)Worker.Sweep.SweepRunner.Longest.TotalDays, "SweepRunner.Longest in days, the sample's bound"),
            new(SweepEdgeRow, "8", Core.Sweep.SweepAxes.Exits, "SweepAxes.Exits"),
            new(SweepEdgeRow, "63", Core.Returns.ForwardReturnSeries.SetupSessionCap, "ForwardReturnSeries.SetupSessionCap"),
            new(SweepEdgeRow, "5", Worker.Sweep.SweepStages.LargestLeftOut, "SweepStages.LargestLeftOut"),
            new(SweepEdgeRow, "20", (decimal)Worker.Sweep.SweepReport.ResultBound, "SweepReport.ResultBound"),
            new(SweepConditionsRow, "7", Core.Sweep.SweepConditions.Count, "SweepConditions.Count"),
            new(SweepConditionsRow, "31", Core.Sweep.SweepConditions.Settings.Count, "SweepConditions.Settings"),
            new(SweepConditionsRow, "252", Core.Sweep.SweepConditions.HighSessions, "SweepConditions.HighSessions"),
            new(SweepConditionsRow, "126", SwingReadings.ReturnLongSessions, "SwingReadings.ReturnLongSessions"),
            new(SweepConditionsRow, "50", Core.Sweep.SweepConditions.VolumeSessions, "SweepConditions.VolumeSessions"),
            new(SweepConditionsRow, "10", Worker.Sweep.SweepSearch.DesignsTried, "SweepSearch.DesignsTried"),
            new(SweepConditionsRow, "6", Worker.Sweep.SweepSearch.YearsUpToKeep, "SweepSearch.YearsUpToKeep"),
            new(SweepConditionsRow, "8", Core.Sweep.SweepFigures.Years, "SweepFigures.Years"),
            new(SweepConditionsRow, "2", Worker.Sweep.SweepSearch.RecentYearsUpToKeep, "SweepSearch.RecentYearsUpToKeep"),
            new(SweepConditionsRow, "3", Core.Sweep.SweepMeasures.RecentYears, "SweepMeasures.RecentYears"),
            new(SweepConditionsRow, "300", Core.Sweep.SweepMeasures.TradeFloor, "SweepMeasures.TradeFloor"),
            new(SweepConditionsRow, "6", Worker.Sweep.SweepSearch.DesignsKeeping, "SweepSearch.DesignsKeeping"),
            new(SweepConditionsRow, "10", Worker.Sweep.SweepSearch.DesignsTried, "SweepSearch.DesignsTried, the ones a setting is kept on"),
            new(SweepConditionsRow, "10", Worker.Sweep.SweepSearch.DesignsTried, "SweepSearch.DesignsTried, crossed in step (c)"),
            new(SweepConditionsRow, "19", Worker.Sweep.SweepSearch.SelectionNeighbours(Core.Sweep.SweepDesign.Live.Selection).Count, "SweepSearch.SelectionNeighbours of the live selection"),
            new(SweepConditionsRow, "5", Worker.Sweep.SweepSearch.Carried, "SweepSearch.Carried"),
            new(SweepPointInTimeRow, "25", Worker.Sweep.SweepPointInTime.SamplesAYear, "SweepPointInTime.SamplesAYear"),
            new(SweepPointInTimeRow, "13", Worker.Sweep.SweepPointInTime.CandidateSamplesAYear, "SweepPointInTime.CandidateSamplesAYear"),
            new(SweepPointInTimeRow, "12", Worker.Sweep.SweepPointInTime.SamplesAYear - Worker.Sweep.SweepPointInTime.CandidateSamplesAYear, "SweepPointInTime's member-sessions a year"),
            new(SweepPointInTimeRow, "200", Worker.Sweep.SweepPointInTime.YearOfBars, "SweepPointInTime.YearOfBars"),
            new(SweepPointInTimeRow, "200", Worker.Sweep.SweepPointInTime.SamplesAYear * Core.Sweep.SweepFigures.Years, "SweepPointInTime's samples over the eight years"),
            new(SweepPointInTimeRow, "8", Core.Sweep.SweepFigures.Years, "SweepFigures.Years"),
            new(SweepSearchRow, "5", Worker.Sweep.SweepSearch.Carried, "SweepSearch.Carried"),
            new(SweepSearchRow, "10,000", Worker.Sweep.SweepSearch.TimedPoints, "SweepSearch.TimedPoints"),
            new(SweepSearchRow, "4", (decimal)Worker.Sweep.SweepSearch.SampleBudget.TotalHours, "SweepSearch.SampleBudget in hours"),
            new(SweepSearchRow, "10,000,000", Worker.Sweep.SweepSearch.MostSampled, "SweepSearch.MostSampled"),
            new(SweepSearchRow, "0.05", (decimal)Worker.Sweep.SweepSearch.PlateauMargin, "SweepSearch.PlateauMargin"),
            new(SweepSearchRow, "100", Worker.Sweep.SweepSearch.Leaders, "SweepSearch.Leaders"),
            new(SweepSearchRow, "6", Worker.Sweep.SweepSearch.MostDepth, "SweepSearch.MostDepth"),
            new(SweepSearchRow, "2", Core.Sweep.SweepSpace.Beyond, "SweepSpace.Beyond"),
            new(SweepSearchRow, "14", Core.Sweep.SweepGrid.Extended.Freshness[^1], "SweepGrid.Extended's longest freshness"),
            new(SweepSearchRow, "19", Core.Sweep.SweepSpace.For([1, 2, 3, 4, 5, 6, 7]).Count, "the dials of a space holding every condition"),
            new(SweepSearchRow, "3", Worker.Sweep.SweepSearch.SliceReach, "SweepSearch.SliceReach"),
            new(SweepSearchRow, "5", (decimal)Worker.Sweep.SweepRunner.Longest.TotalDays, "SweepRunner.Longest in days"),
            new("Ideas on the base", "2", (decimal)Worker.Sweep.SweepIdeas.BaseRewardToRisk, "SweepIdeas.BaseRewardToRisk"),
            new("Ideas on the base", "200", Worker.Sweep.SweepIdeas.SlowAverage, "SweepIdeas.SlowAverage, a member's own"),
            new("Ideas on the base", "10", Worker.Sweep.SweepIdeas.Lookback, "SweepIdeas.Lookback, breadth's"),
            new("Ideas on the base", "252", Worker.Sweep.SweepIdeas.HighLowWindow, "SweepIdeas.HighLowWindow"),
            new("Ideas on the base", "50", Worker.Sweep.SweepIdeas.FastAverage, "SweepIdeas.FastAverage"),
            new("Ideas on the base", "200", Worker.Sweep.SweepIdeas.SlowAverage, "SweepIdeas.SlowAverage, the index's"),
            new("Ideas on the base", "20", (decimal)Worker.Sweep.SweepIdeas.VixLevel, "SweepIdeas.VixLevel"),
            new("Ideas on the base", "10", Worker.Sweep.SweepIdeas.Lookback, "SweepIdeas.Lookback, the VIX's"),
            new("Ideas on the base", "3", Worker.Sweep.SweepIdeas.BestOf, "SweepIdeas.BestOf"),
            new("Ideas on the base", "2", (decimal)Worker.Sweep.SweepIdeas.TrailTwoMoves, "SweepIdeas.TrailTwoMoves"),
            new("Ideas on the base", "3", (decimal)Worker.Sweep.SweepIdeas.TrailThreeMoves, "SweepIdeas.TrailThreeMoves"),
            new("Ideas on the base", "63", Worker.Sweep.SweepIdeas.Cap, "SweepIdeas.Cap"),
            new("Ideas on the base", "1", (decimal)Core.Sweep.SweepGrid.Extended.StopBounds[Worker.Sweep.SweepIdeas.AtLeastAMove].Low, "SweepGrid.Extended's stop setting at least a move"),
            new("Ideas on the base", "10", Worker.Sweep.SweepIdeas.ShortHold, "SweepIdeas.ShortHold"),
            new("Ideas on the base", "20", Worker.Sweep.SweepIdeas.MiddleHold, "SweepIdeas.MiddleHold"),
            new("Ideas test", "6", Worker.Sweep.SweepIdeas.YearsBetter, "SweepIdeas.YearsBetter"),
            new("Ideas test", "8", Core.Sweep.SweepFigures.Years, "SweepFigures.Years"),
            new("Ideas test", "2", Worker.Sweep.SweepIdeas.RecentYearsBetter, "SweepIdeas.RecentYearsBetter"),
            new("Ideas test", "3", Worker.Sweep.SweepIdeas.RecentYears, "SweepIdeas.RecentYears, the years"),
            new("Ideas test", "3", Worker.Sweep.SweepIdeas.RecentYears, "SweepIdeas.RecentYears, together"),
            new("Ideas test", "5", Worker.Sweep.SweepIdeas.LargestLeftOut, "SweepIdeas.LargestLeftOut"),
            new("Ideas test", "1,000", Worker.Sweep.SweepIdeas.TradeFloor, "SweepIdeas.TradeFloor"),
            new("Ideas test", "40", (decimal)Worker.Sweep.SweepIdeas.NightFloor * 100, "SweepIdeas.NightFloor as a percentage"),
            new("Breakout sweep grid", "126", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[0].Levels[0], "BreakoutSweep.Grid, the shorter high window"),
            new("Breakout sweep grid", "251", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[0].Levels[1], "BreakoutSweep.Grid, the year's high window"),
            new("Breakout sweep grid", "1.25", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[1].Levels[0], "BreakoutSweep.Grid, the lowest volume multiple"),
            new("Breakout sweep grid", "1.5", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[1].Levels[1], "BreakoutSweep.Grid, the middle volume multiple"),
            new("Breakout sweep grid", "2.0", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[1].Levels[2], "BreakoutSweep.Grid, the highest volume multiple"),
            new("Breakout sweep grid", "0.85", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[2].Levels[0], "BreakoutSweep.Grid, the tighter range ceiling"),
            new("Breakout sweep grid", "1.0", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[2].Levels[1], "BreakoutSweep.Grid, the provisional range ceiling"),
            new("Breakout sweep grid", "1.5", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[3].Levels[0], "BreakoutSweep.Grid, the nearest stop"),
            new("Breakout sweep grid", "2", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[3].Levels[1], "BreakoutSweep.Grid, the provisional stop"),
            new("Breakout sweep grid", "3", (decimal)Worker.Sweep.BreakoutSweep.Grid.Dials[3].Levels[2], "BreakoutSweep.Grid, the widest stop"),
            new("Drift sweep grid", "3", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[0].Levels[0], "DriftSweep.Grid, the shortest window"),
            new("Drift sweep grid", "5", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[0].Levels[1], "DriftSweep.Grid, the provisional window"),
            new("Drift sweep grid", "10", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[0].Levels[2], "DriftSweep.Grid, the longest window"),
            new("Drift sweep grid", "0.5", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[1].Levels[0], "DriftSweep.Grid, the smallest rise"),
            new("Drift sweep grid", "1.0", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[1].Levels[1], "DriftSweep.Grid, the provisional rise"),
            new("Drift sweep grid", "1.5", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[1].Levels[2], "DriftSweep.Grid, the largest rise"),
            new("Drift sweep grid", "1.25", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[2].Levels[0], "DriftSweep.Grid, the lowest volume multiple"),
            new("Drift sweep grid", "1.5", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[2].Levels[1], "DriftSweep.Grid, the provisional volume multiple"),
            new("Drift sweep grid", "2.0", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[2].Levels[2], "DriftSweep.Grid, the highest volume multiple"),
            new("Drift sweep grid", "2.0", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[3].Levels[0], "DriftSweep.Grid, the nearest target"),
            new("Drift sweep grid", "2.5", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[3].Levels[1], "DriftSweep.Grid, the rule's target"),
            new("Drift sweep grid", "3.0", (decimal)Worker.Sweep.DriftSweep.Grid.Dials[3].Levels[2], "DriftSweep.Grid, the farthest target"),
            new("Leader sweep grid", "2", (decimal)Worker.Sweep.LeaderSweep.Grid.Dials[0].Levels[0], "LeaderSweep.Grid, the fewest sectors"),
            new("Leader sweep grid", "3", (decimal)Worker.Sweep.LeaderSweep.Grid.Dials[0].Levels[1], "LeaderSweep.Grid, the rule's sectors"),
            new("Leader sweep grid", "4", (decimal)Worker.Sweep.LeaderSweep.Grid.Dials[0].Levels[2], "LeaderSweep.Grid, the most sectors"),
            // The heavyweights' sweep, 14.5.
            new("Heavyweights' sweep grid", "5", Worker.Sweep.HeavyweightSweep.Sizes[0], "HeavyweightSweep.Sizes[0]"),
            new("Heavyweights' sweep grid", "10", Worker.Sweep.HeavyweightSweep.Sizes[1], "HeavyweightSweep.Sizes[1]"),
            new("Heavyweights' sweep grid", "63", Worker.Sweep.HeavyweightSweep.LookBacks[0], "HeavyweightSweep.LookBacks[0]"),
            new("Heavyweights' sweep grid", "126", Worker.Sweep.HeavyweightSweep.LookBacks[1], "HeavyweightSweep.LookBacks[1]"),
            new("Heavyweights' sweep grid", "251", Worker.Sweep.HeavyweightSweep.LookBacks[2], "HeavyweightSweep.LookBacks[2]"),
            new("Heavyweights' sweep grid", "1", Worker.Sweep.HeavyweightSweep.LeaderCounts[0], "HeavyweightSweep.LeaderCounts[0]"),
            new("Heavyweights' sweep grid", "2", Worker.Sweep.HeavyweightSweep.LeaderCounts[1], "HeavyweightSweep.LeaderCounts[1]"),
            new("Family sweep floors", "300", Worker.Sweep.FamilySweep.TradeFloor, "FamilySweep.TradeFloor"),
            new("Family sweep floors", "6", Worker.Sweep.FamilySweep.YearsBeating, "FamilySweep.YearsBeating"),
            new("Family sweep floors", "8", Core.Sweep.SweepFigures.Years, "SweepFigures.Years"),
            new("Family sweep floors", "8", Worker.Sweep.FamilySweep.Variants, "FamilySweep.Variants"),
            new("Family sweep test", "63", Core.Returns.Blocks.Sessions, "Blocks.Sessions"),
            new("Family sweep test", "0.05", (decimal)Core.Returns.ReasonVerdict.Significance, "ReasonVerdict.Significance"),
            // The readings the S&P 400's and 600's rules read, 15.2, the figures in the row's order.
            new("Liquidity floors of the 400's and 600's rules", "5", Core.Readings.MemberReadings.LowestPrice, "MemberReadings.LowestPrice"),
            new("Liquidity floors of the 400's and 600's rules", "50", Core.Readings.MemberReadings.DollarVolumeSessions, "MemberReadings.DollarVolumeSessions"),
            new("Liquidity floors of the 400's and 600's rules", "10,000,000", Core.Readings.MemberReadings.MidCapDollarVolume, "MemberReadings.MidCapDollarVolume"),
            new("Liquidity floors of the 400's and 600's rules", "5,000,000", Core.Readings.MemberReadings.SmallCapDollarVolume, "MemberReadings.SmallCapDollarVolume"),
            // The pulls' readings, 14.2, the figures in each row's order.
            new("Company value", "10", Core.Families.FiledSplit.PlainMost, "FiledSplit.PlainMost"),
            new("Sector on a session", "2018", Core.Families.GicsSectors.First.Year, "GicsSectors.First's year"),
            new("Sector on a session", "14", Core.Families.GicsSectors.Moves.Count, "GicsSectors.Moves"),
            new("Sector on a session", "2023", Core.Families.GicsSectors.MovedAfter.Year, "GicsSectors.MovedAfter's year"),
            new("Sector on a session", "8", Core.Families.GicsSectors.Moves.Count(move => move.To == Core.Families.GicsSectors.Financials), "the moves GicsSectors.Moves makes to Financials"),
            new("Sector on a session", "3", Core.Families.GicsSectors.Moves.Count(move => move.To == Core.Families.GicsSectors.Industrials), "the moves GicsSectors.Moves makes to Industrials"),
            new("Sector on a session", "3", Core.Families.GicsSectors.Moves.Count(move => move.To == Core.Families.GicsSectors.ConsumerStaples), "the moves GicsSectors.Moves makes to Consumer Staples"),
            new("Rank by company", "50", Core.Families.CompanyRank.DollarVolumeSessions, "CompanyRank.DollarVolumeSessions"),
            new("Revenue as first filed", "80", Core.Families.FirstFiledRevenue.QuarterFewest, "FirstFiledRevenue.QuarterFewest"),
            new("Revenue as first filed", "100", Core.Families.FirstFiledRevenue.QuarterMost, "FirstFiledRevenue.QuarterMost"),
            new("Revenue as first filed", "350", Core.Families.FirstFiledRevenue.YearFewest, "FirstFiledRevenue.YearFewest"),
            new("Revenue as first filed", "380", Core.Families.FirstFiledRevenue.YearMost, "FirstFiledRevenue.YearMost"),
            new("Revenue as first filed", "260", Core.Families.FirstFiledRevenue.NineMonthsFewest, "FirstFiledRevenue.NineMonthsFewest"),
            new("Revenue as first filed", "285", Core.Families.FirstFiledRevenue.NineMonthsMost, "FirstFiledRevenue.NineMonthsMost"),
            new("Revenue as first filed", "6", Core.Families.FirstFiledRevenue.Concepts.Count, "FirstFiledRevenue.Concepts"),
            new("Archive requests", "10", HistoryPull.ArchiveRequestsASecond, "HistoryPull.ArchiveRequestsASecond"),
            // The sector heavyweights' settings, 14.3, at the freeze of 14.6, and the registrations it wrote.
            new("Heavyweights' size cut", "10", Core.Families.HeavyweightRule.Largest, "HeavyweightRule.Largest"),
            new("Heavyweights' look-back", "251", Core.Families.HeavyweightRule.LookBack, "HeavyweightRule.LookBack"),
            new("Heavyweights' leaders", "2", Core.Families.HeavyweightRule.Leaders, "HeavyweightRule.Leaders"),
            new("Heavyweights' leaders", "1", (decimal)Core.Families.HeavyweightRule.BetaFloor, "HeavyweightRule.BetaFloor"),
            new("Heavyweights' leaders", "251", Core.Families.HeavyweightRule.BetaReturns, "HeavyweightRule.BetaReturns"),
            new("Heavyweights' registrations", "3", Worker.Candidates.TheSetupFamilies.Heavyweights.Count - 1, "TheSetupFamilies.Heavyweights' variants"),
            new("Heavyweights' registrations", "63", Core.Returns.Blocks.Sessions, "Blocks.Sessions"),
            // The context checks, 14.4.
            new("A print's revenue quarter", "100", Core.Families.RevenueGrowth.ReportedWithin, "RevenueGrowth.ReportedWithin"),
            new("A print's revenue quarter", "7", Core.Families.RevenueGrowth.YearBeforeWithin, "RevenueGrowth.YearBeforeWithin"),
            new("RSI fall order", "14", Core.Indicators.IndicatorSeries.Wilder, "IndicatorSeries.Wilder"),
            new("RSI fall order", "3", Worker.Sweep.SweepIdeas.BestOf, "SweepIdeas.BestOf"),
        ];
    }

    [Fact]
    public void EveryRestatementOfAFigureTheCodeHoldsAgreesWithItInEveryDocumentThatStatesIt()
    {
        var documents = StatedFigures.Documents.ToDictionary(
            path => path,
            path => StatedFigures.Prose(path, Corpus.Read(path)),
            StringComparer.Ordinal);

        var restatements = Restatements();
        var counted = restatements.Sum(restatement => restatement.Statements.Values.Sum());

        Assert.True(counted >= 70, $"Counted {counted} restatements in advance, expected at least 70.");
        Assert.Empty(StatedFigures.Disagreeing(documents, restatements));
    }

    static IReadOnlyList<Restatement> Restatements()
    {
        // The bound's arithmetic, from the figures section 17's row states.
        var bound = string.Join(" ", ArchitectureTables.In(File.ReadAllText(Repository.Architecture))
            .Single(table => table.Heading == Scope.LimitsTable)
            .Body.Single(cells => cells.Count > 2 && cells[0] == "Rule versions scored at once"));

        decimal Read(string pattern) =>
            decimal.Parse(Regex.Match(bound, pattern).Groups[1].Value, CultureInfo.InvariantCulture);

        var night = Read(@"(\d+) seconds over the steps before the close");
        var levels = Read(@"a level stage of (\d+) seconds");
        var ladders = Read(@"a ladder stage of (\d+) at");
        var names = Read(@"a ladder stage of \d+ at (\d+) names");
        var worst = (decimal)RuleVersions.WorstCaseSeconds((double)levels, (double)ladders);

        const string Architecture = "docs/ARCHITECTURE.html";
        const string Schema = "docs/SCHEMA.md";
        const string Plan = "docs/BUILD_PLAN.md";
        const string Runbook = "docs/RUNBOOK.md";
        const string Decisions = "docs/DECISIONS.md";

        static Dictionary<string, int> In(params (string Path, int Count)[] counts) =>
            counts.ToDictionary(count => count.Path, count => count.Count, StringComparer.Ordinal);

        return
        [
            new("the merge distance's cap", RuleVersions.MostOfTheMergeDistance,
                [@"{N}\s+(?:windows|rows)\s+of\s+the\s+merge\s+distance(?!\s+would\b)"],
                In((Architecture, 1), (Schema, 1), (Plan, 2), (Runbook, 1), (Decisions, 2))),
            new("each other rule's cap", RuleVersions.MostPerRule,
                [@"{N}\s+of\s+each\s+(?:of\s+the\s+)?other\b"],
                In((Architecture, 1), (Schema, 1), (Plan, 2), (Runbook, 1), (Decisions, 1))),
            new("the windows open at once", RuleVersions.MostAtOnce,
                [@"among\s+them,\s+(?:which\s+is\s+)?{N}\s+at\s+once", @"{N}\s+is\s+the\s+sum\s+of\s+the\s+caps"],
                In((Architecture, 2), (Plan, 2))),
            new("the ladder rules", LadderRules.All.Count,
                [@"{N}\s+ladder\s+rules\s+the\s+build\s+carries", @"\bthe\s+{N}\s+rules\s+and\s+the\s+names"],
                In((Schema, 1), (Runbook, 1))),
            new("the ladder rules other than the merge distance", LadderRules.All.Count - 1,
                [@"the\s+other\s+{N}\s+ladder\s+rules"],
                In((Architecture, 1))),
            new("the night the bound is measured on, in seconds", night,
                [@"{N}\s+seconds\s+over\s+(?:the\s+steps\s+before\s+the\s+close|steps\s+\d+\s+to\s+\d+)", @"on\s+a\s+{N}\s+second\s+night"],
                In((Architecture, 1), (Plan, 2), (Decisions, 1))),
            new("that night's level stage, in seconds", levels,
                [@"level\s+stage\s+(?:of\s+)?{N}\s+seconds"],
                In((Architecture, 1), (Plan, 2))),
            new("that night's ladder stage, in seconds", ladders,
                [@"ladder\s+stage\s+(?:of\s+)?{N}\s+at\b", @"every\s+other\s+version\s+{N}\b", @"replay\s+the\s+ladder\s+stage\s+at\s+{N}\b"],
                In((Architecture, 2), (Plan, 2), (Decisions, 1))),
            new("the names that night ran over", names,
                [@"ladder\s+stage\s+(?:of\s+)?\d+\s+at\s+{N}\s+names"],
                In((Architecture, 1), (Plan, 2))),
            new("a merge distance version's replay, in seconds", levels + ladders,
                [@"merge\s+distance\s+version\s+costs\s+{N}\s+seconds", @"replay\s+the\s+level\s+stage\s+at\s+{N}\s+seconds"],
                In((Architecture, 1), (Decisions, 1))),
            new("the fullest register's replays, in seconds", worst,
                [@"(?:others|ladder\s+replays),\s+{N}\s+seconds", @"adds\s+{N}\s+seconds"],
                In((Architecture, 1), (Plan, 1), (Decisions, 1))),
            new("the night at the fullest register, in seconds", night + worst,
                [@"(?:puts\s+the\s+night\s+at|sits\s+at)\s+{N}\b"],
                In((Architecture, 1), (Plan, 2))),
            new("the night's deadline, in seconds", (decimal)RetryPolicy.Standard.Deadline.TotalSeconds,
                [@"(?:against|inside)\s+(?:a|the)\s+deadline\s+of\s+{N}\b"],
                In((Architecture, 1), (Plan, 2), (Decisions, 1))),
            new("the merge distance versions the fullest register replays", RuleVersions.MostOfTheMergeDistance - 1,
                [@"replays\s+{N}\s+merge\s+distance\s+versions?\b", @"replays\s+{N}\s+of\s+the\s+first\b", @"\bat\s+{N}\s+level\s+replays?\s+and\b"],
                In((Architecture, 1), (Plan, 1), (Decisions, 1))),
            new("the other versions the fullest register replays", (LadderRules.All.Count - 1) * (RuleVersions.MostPerRule - 1),
                [@"and\s+{N}\s+others,", @"and\s+{N}\s+of\s+the\s+second\b", @"and\s+{N}\s+ladder\s+replays,\s+\d+\s+seconds"],
                In((Architecture, 1), (Plan, 1), (Decisions, 1))),
            new("the candidate family's maximum", CandidateFamily.Maximum,
                [@"maximum\s+family\s+size\s+of\s+{N}\b", @"the\s+other,\s+at\s+most\s+{N}\b", @"family\s+of\s+{N},\s+needs", @"family\s+is\s+at\s+most\s+{N}\b", @"{N}\s+is\s+the\s+size\s+at\s+which"],
                In((Architecture, 3), (Plan, 2))),
            new("the registration past the family's maximum", CandidateFamily.Maximum + 1,
                [@"\ba\s+{O}\s+candidate\b"],
                In((Runbook, 1))),
            new("a setup's session cap", ForwardReturnSeries.SetupSessionCap,
                [@"(?:\bor|cap\s+of|Neither,)\s+{N}\s+sessions\b"],
                In((Architecture, 2), (Plan, 1), (Decisions, 1))),
            new("the resolved setups a verdict waits on", ReasonVerdict.MinimumResolved,
                [@"minimum\s+(?:is|of)\s+(?<n>\d+)\b", @"(?<n>\d+)\s+because\s+it\s+is\s+the\s+minimum", @"(?<n>\d+)\s+resolved\s+setups,\s+which\s+is\s+the\s+minimum", @"rather\s+than\s+(?<n>\d+)\s+rows\s+however"],
                In((Plan, 4), (Decisions, 2))),
            new("the listing sessions a verdict waits on", ReasonVerdict.MinimumSessions,
                [@"at\s+least\s+{N}\s+distinct\s+listing\s+sessions"],
                In((Architecture, 1), (Plan, 1), (Decisions, 1))),
            new("the live reasons' family", ReasonVerdict.LiveFamily,
                [@"live\s+reasons\s+are\s+(?:one\s+|a\s+)?family\s+of\s+{N}\b"],
                In((Architecture, 1), (Plan, 1))),
            new("the merge distance, in typical moves", (decimal)RuleVersionScorer.LiveParameters(LadderRules.MergeDistance)["typicalMoveMultiple"],
                [@"closer\s+than\s+{N}\s+a\s+typical\s+day's\s+move", @"{N}\s+a\s+typical\s+day's\s+move\s+is\s+the\s+merge\s+distance", @"merge\s+distance\s+{N}\s+a\s+typical\s+day's\s+move", @"merged\s+into\s+bands\s+within\s+{N}\s+a\s+typical", @"anchors\s+sit\s+more\s+than\s+{N}\s+a\s+typical", @"narrower\s+than\s+{N}\s+a\s+typical"],
                In((Architecture, 4), (Plan, 2), (Decisions, 1))),
        ];
    }

    [Fact]
    public void ACensusOrARestatementThatNoLongerMatchesTheDocumentIsReported()
    {
        var limits = new ArchitectureTable(
            Scope.LimitsTable,
            [
                ["Limit", "Value", "Reason", "Asserted by"],
                ["A limit", $"at most 3 things and 40 others ({Corpus.Decision}: A decision 9)", "because 7", "a check"],
            ]);

        var stated = StatedFigures.InValues(limits);

        Assert.Equal([("A limit", "3"), ("A limit", "40")], stated);

        HeldFigure three = new("A limit", "3", 3, "a constant");
        HeldFigure forty = new("A limit", "40", 40, "another");

        Assert.Empty(StatedFigures.Unheld(stated, [three, forty]));
        Assert.Contains("holds 4", Assert.Single(StatedFigures.Unheld(stated, [three with { Holds = 4 }, forty])), StringComparison.Ordinal);
        Assert.Contains("which no census entry holds", Assert.Single(StatedFigures.Unheld(stated, [three])), StringComparison.Ordinal);
        Assert.Contains("does not state", Assert.Single(StatedFigures.Unheld(stated, [three, forty, new("A limit", "7", 7, "a third")])), StringComparison.Ordinal);
        Assert.Contains("does not stand", Assert.Single(StatedFigures.Unheld(stated, [three, forty with { Holds = null, Stands = () => false }])), StringComparison.Ordinal);
        Assert.Contains("nothing checked", Assert.Single(StatedFigures.Unheld(stated, [three, forty with { Holds = null }])), StringComparison.Ordinal);

        var cap = new Restatement(
            "a cap",
            2,
            [@"{N}\s+windows\s+of\s+the\s+merge\s+distance"],
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a.md"] = 1 });

        Dictionary<string, string> Saying(string text) => new(StringComparer.Ordinal) { ["a.md"] = text };

        Assert.Empty(StatedFigures.Disagreeing(Saying("at most two windows of the merge distance"), [cap]));
        Assert.Contains("and the code holds 2", Assert.Single(StatedFigures.Disagreeing(Saying("at most three windows of the merge distance"), [cap])), StringComparison.Ordinal);
        Assert.Contains("0 time(s), and 1 are counted", Assert.Single(StatedFigures.Disagreeing(Saying("at most a pair of windows of the merge distance"), [cap])), StringComparison.Ordinal);
        Assert.Contains("not a document this reads", Assert.Single(StatedFigures.Disagreeing(new Dictionary<string, string>(StringComparer.Ordinal), [cap])), StringComparison.Ordinal);

        // A superseded decision is what was held, so its figure is not read.
        var decisions = StatedFigures.Prose(
            "docs/DECISIONS.md",
            "**A decision** at most two windows of the merge distance.\n\n## Previously decided\n\n**An old one** at most three windows of the merge distance.");

        Assert.Empty(StatedFigures.Disagreeing(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["docs/DECISIONS.md"] = decisions },
            [cap with { Statements = new Dictionary<string, int>(StringComparer.Ordinal) { ["docs/DECISIONS.md"] = 1 } }]));

        Assert.Equal(9m, StatedFigures.ValueOf("ninth"));
        Assert.Equal(1137m, StatedFigures.ValueOf("1,137"));
        Assert.Equal(0.5m, StatedFigures.ValueOf("half"));
        Assert.Throws<FormatException>(() => StatedFigures.ValueOf("a pair"));
    }

    [Fact]
    public void TheFrameworkTheSpecsStateIsTheOneTheBuildUses()
    {
        var built = Versions.FrameworkIn(File.ReadAllText(Repository.DirectoryBuildProps));
        var specs = Specs();
        var stated = Versions.MentionsIn(specs, Framework);

        // Two scopes, and only the second carries the property. The documents
        // opened is a fact about the corpus, so it is context and carries no
        // floor: a floor on it is satisfied by opening a document, and a run
        // finding no mention in any of them would pass having compared nothing.
        //
        // The comparisons are what this check is about. That number cannot be
        // moved by adding a document or by adding a sentence, only by a mention
        // that agrees with the build file, which is the thing being pinned.
        Assert.True(
            stated.Count >= 2,
            $"Compared {stated.Count} framework mentions against {Repository.DirectoryBuildProps}, " +
            $"expected at least 2, over {specs.Count} specs read.");

        Assert.Empty(Versions.Disagreeing(stated, built, "framework mention"));
    }

    [Fact]
    public void TheFeatureBandTheSpecsStateIsTheOneGlobalJsonPins()
    {
        var pinned = Versions.FeatureBand(Versions.SdkVersionIn(File.ReadAllText(Repository.SdkPin)));
        var specs = Specs();
        var stated = Versions.MentionsIn(specs, Band);

        Assert.True(
            stated.Count >= 2,
            $"Compared {stated.Count} band mentions against {Repository.SdkPin}, " +
            $"expected at least 2, over {specs.Count} specs read.");

        Assert.Empty(Versions.Disagreeing(stated, pinned, "band mention"));
    }

    [Fact]
    public void TheWorkflowInstallsAnSdkThePinWillAccept()
    {
        var sdk = Versions.SdkVersionIn(File.ReadAllText(Repository.SdkPin));
        var framework = Versions.FrameworkIn(File.ReadAllText(Repository.DirectoryBuildProps));
        var installs = Versions.Occurrences(File.ReadAllText(Repository.Workflow), "dotnet-version: '[^']+'");

        Assert.True(installs.Count >= 2, $"Found {installs.Count} install steps, expected at least 2.");
        Assert.All(installs, step => Assert.Equal(Versions.MajorMinor(sdk), Versions.MajorMinor(step)));
        Assert.Equal(Versions.MajorMinor(sdk), Versions.MajorMinor(framework));
    }

    [Fact]
    public void TheCheckReportsVersionsThatDisagree()
    {
        // The permanent proof that the assertions above can fail: every
        // extractor returns a different value for different input.
        Assert.Equal("net9.0", Versions.Occurrences("targeting `net9.0`", @"net[0-9]+\.[0-9]+").Single());
        Assert.Equal("9.0", Versions.MajorMinor("net9.0"));
        Assert.Equal("10.0.4xx", Versions.FeatureBand("10.0.400"));
    }

    [Fact]
    public void AVersionThatCannotBeReadFailsRatherThanDefaulting()
    {
        Assert.Throws<FormatException>(() => Versions.MajorMinor("no version here"));
        Assert.Throws<FormatException>(() => Versions.FeatureBand("10.0"));
    }

    [Fact]
    public void ADocumentStatingAVersionTheBuildDoesNotIsReported()
    {
        // The permanent proof over a constructed corpus, naming the document
        // rather than only the value, which is what a person needs to open.
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agrees.md"] = "the projects target `net10.0`",
            ["disagrees.md"] = "the projects target `net9.0`",
        };

        var disagreeing = Versions.Disagreeing(
            Versions.MentionsIn(documents, Framework), "net10.0", "framework mention");

        Assert.Equal("disagrees.md", Assert.Single(disagreeing).Document);
    }

    [Fact]
    public void AScanThatComparedNothingFailsRatherThanPassingOverAnEmptyResult()
    {
        // The other half, and the one a floor on documents opened would miss.
        // Both documents are read and neither states a framework, so there is
        // nothing to disagree and "none of them disagreed" is vacuously true.
        // Assert.Empty over an empty list passes, so the refusal has to happen
        // before the assertion is reached rather than inside it.
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["silent.md"] = "this document states no framework at all",
            ["also-silent.md"] = "and neither does this one",
        };

        var mentions = Versions.MentionsIn(documents, Framework);

        Assert.Empty(mentions);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Versions.Disagreeing(mentions, "net10.0", "framework mention"));

        Assert.Contains("must fail rather than pass over an empty scan", refusal.Message, StringComparison.Ordinal);
    }
}
