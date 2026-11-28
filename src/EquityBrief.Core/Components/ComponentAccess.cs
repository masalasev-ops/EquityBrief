namespace EquityBrief.Core.Components;

// What a component may do to a store.
//
// Insert, Update and Delete are the three operations SCHEMA.md declares
// ownership for, so a declaration reconciles against that table without a
// translation between two vocabularies. Read is the fourth because the read and
// write matrix asserts reads as well, and a blank cell there is a claim as much
// as a filled one.
[Flags]
public enum Touch
{
    None = 0,
    Read = 1,
    Insert = 2,
    Update = 4,
    Delete = 8,
}

// One member per table SCHEMA.md describes, the member name being the table name
// in Pascal case. component-access asserts the two sets against each other in
// both directions, so a table added to SCHEMA with no member here fails rather
// than going undeclared.
public enum Store
{
    Membership,
    Bar,
    MarketBar,
    LiveQuote,
    Calendar,
    PulledBar,
    PulledEarnings,
    PulledSurprise,
    PulledMarketBar,
    PulledCompany,
    PulledShares,
    PulledSplit,
    TickerJoin,
    ListedName,
    SessionMark,
    MarkRuling,
    DataCheck,
    LongSnapshot,
    LongHolding,
    LongSpan,
    LongBar,
    LongMarketBar,
    LongPrint,
    LongSplit,
    LongDividend,
    LongFiling,
    LongHeldNone,
    // The second sources' fill: each session kept, each hole asked with its outcome, and each request to a source.
    LongFillBar,
    LongFillHole,
    SourceAsk,
    // From 24.47 the night's order changes, each waiting order the 4:30 pm check read, and each rule's fill odds.
    OrderChange,
    FillCheck,
    FillOdds,
    // From the 24.47 correction of 2026-11-28 each open trailing trade's stop as each night raised it.
    TrailStop,
    // The check's settling: each stock-session or stretch marked unreliable or excluded, and each close held to a source.
    UnreliableMark,
    PriceCheck,
    CheckSettled,
    LongFact,
    LongSetup,
    LongReading,
    DepartedSetup,
    LongSearch,
    PlanPoint,
    LongOutlier,
    DividendWait,
    LateBarAsk,
    EntryReading,
    SplitName,
    ChartCheck,
    SplitEvent,
    PulledRevenue,
    PulledMember,
    PulledIncome,
    PulledSnapshot,
    PulledHolding,
    Indicator,
    ChartAverage,
    EarningsMove,
    LongView,
    Swing,
    VolumeProfile,
    Level,
    Ladder,
    Move,
    PeerReading,
    EarningsReaction,
    SwingReading,
    MarketReading,
    GateResult,
    FilterVersion,
    ShapeProposal,
    Listing,
    ListRule,
    FamilyResult,
    FamilyNight,
    FamilyPick,
    FamilyTrade,
    HeavyweightNight,
    HeavyweightHolding,
    HeavyweightRuleNight,
    HeavyweightRuleHolding,
    IndexFamilyNight,
    IndexFamilyResult,
    IndexFamilyPick,
    IndexFamilyTrade,
    IndexHeavyweightHolding,
    IndexRuleTrade,
    IndexHeavyweightRuleNight,
    IndexHeavyweightRuleHolding,
    DipNight,
    DipResult,
    DipPick,
    DipTrade,
    DecisionCard,
    RuleRecord,
    RuleNight,
    RulePick,
    TradeFill,
    FormingRow,
    KeptBar,
    Setup,
    SetupNight,
    FiledFact,
    FiledFactPull,
    // From 24.12 the two fallback lines a filer's operating income falls back to, read by no rule.
    FallbackFact,
    // Every member's readings on every session from 2005, and each index's years of them counted; no night reads either.
    SessionReading,
    SessionReadingYear,

    // Each case standing at a monthly run with its figures, and the local model's words over each case's figures.
    PromotionCase,
    PromotionCaseWords,

    // The operator's presses on the case for promotion, and the night's answer to each, the swap it wrote and the records it
    // carried.
    PromotionPress,
    PromotionSwap,

    // Every idea ever tested, from any source, taking inserts alone.
    Notebook,

    // The ideas that passed, each state they reached and the picks a queued one was walked to.
    Survivor,
    SurvivorState,
    SurvivorPick,

    // The background variants a full family has no room for, and each live order walked again under a waiting one's floor.
    WaitingVariant,
    WaitingVariantTrade,

    // The reading recipes: each recipe entered, each run of one on an index, each member's figure of it on each session, and
    // each piece of data a recipe asked for that the store lacks.
    Recipe,
    RecipeRun,
    RecipeReading,
    DataRequest,

    // The trained partner: each run of it on an index and each cell of the tables read off it.
    PartnerRun,
    PartnerCell,

    // The reviewer: what history expected of each pick of a night and of each member its random draw took.
    Review,

    // The researcher's asks of the local model, each setup's failure lists, and the ideas it answered with what became of each.
    ResearchAsk,
    ResearchFailure,
    ResearchIdea,

    // The buy range's widths test, each index's figures for each width and the width its early years chose.
    BuyRangeWidth,

    // The idea box: each idea typed, each answer the local model gave it, and each line the operator confirmed.
    IdeaBox,
    IdeaBoxAnswer,
    IdeaBoxConfirm,
    FilingDay,
    InsiderTrade,
    InsiderRead,
    SecTicker,
    ReadingPin,
    LabelPin,
    ReleaseDocument,
    ReleaseCheck,
    ReleaseDay,
    ReleaseFiler,
    ReleaseReading,
    ReleaseProbe,
    LoopIdea,
    ReleaseAnswerEdge,
    LedgerSummary,
    LedgerReading,
    LoopRun,
    LoopProposal,
    LoopTest,
    LoopFinding,
    LoopReading,
    LoopModel,
    LoopReference,
    LoopRule,
    LoopRuleYear,
    LoopRuleUnit,
    BookRecord,
    LoopPooled,
    LoopLink,
    LoopDecision,
    LoopApplied,
    ProvisionalSetting,
    LoopAlarm,
    TakenTrade,
    TakenRecord,
    DividendReading,
    DividendEvent,
    SweepAnswer,
    EstimateReading,
    EstimateTrend,
    EstimateTrendNight,
    TreasuryYield,
    AnalystView,
    AnalystRating,
    ShortInterest,
    ForwardReturn,
    Facts,
    Fundamentals,
    FundamentalsSnapshot,
    ReportedQuarter,
    Company,
    QuarterAsk,
    FundamentalReading,
    MemberReading,
    FundamentalScore,
    QualityNight,
    QualityResult,
    QualityHolding,
    ImprovingNight,
    ImprovingResult,
    ImprovingTrade,
    SwitchReading,
    NewsPulse,
    NewsArticle,
    NewsLabel,
    ResearchSection,
    ThemeSection,
    SourceDocument,
    CandidateRegister,
    RuleVersion,
    VersionScore,
    VersionBlock,
    SeriesState,
    ResearchRequest,
    QuoteRequest,
    AnalystRequest,
    WatchList,
    // From 24.3 the AI stock picker's facts of each night and its naming test's answers, which no rule reads.
    JudgeFact,
    JudgeNaming,
    // From 24.4 the judge's pin, its runs, every question it asked and its picks, which no rule reads.
    JudgePin,
    JudgeRun,
    JudgeAsk,
    JudgePick,
    RunLog,
}

// The outside sources of section 5. A feed is not a store and has no column in
// the read and write matrix, so it is declared apart and reconciled against the
// catalogue's Reads cell alone. Saying that here rather than leaving it as an
// absence is the point: a reader looking for feeds in the matrix should find the
// reason they are not there.
public enum Feed
{
    IndexMembership,
    BulkPrice,
    HistoricalPrice,
    SplitsAndDividends,
    EarningsCalendar,
    DividendCalendar,
    News,
    CompanyFinancials,
    FilingsArchive,
    ResearchModel,
    LocalModel,
    SearchTool,
    // OpenFIGI's mapping of a security's identifier to the tickers it traded under, keyless, asked by the history
    // pull alone for the holdings the provider's symbol lists carry under no code.
    IdentifierMapping,
    // The provider's delayed quote of one listing, asked by the quote job alone for a name page open in the session.
    Quote,
    // The Treasury's daily par yield curve for a year, keyless and free, asked once a night after the close.
    TreasuryYield,
    // Yahoo's daily chart and Nasdaq's quote, keyless and free, asked by the bar fetcher alone for a member whose row the
    // provider's day file refused.
    SubstituteBar,
    // Yahoo's quote summary of one stock's analysts, keyless and free, asked by the analyst job alone, for a chart page
    // opened and from 21.6 for each member the ratings refresh asks it for.
    Analyst,
    // TipRanks' recent ratings of one stock and its count of the month's calls, on the operator's key, asked by the
    // ratings job alone on a chart page's press.
    AnalystRatings,
    // From 21.6 FINRA's consolidated short interest, keyless and free, a settlement's members in one request, asked by the
    // short interest reader alone outside the night.
    ShortInterest,
    // From 21.6 the provider's earnings trends of many symbols in one request at one weighted call a symbol, asked by the
    // night's trends fetcher alone in the swing filter's step.
    EstimateTrends,
    // From 21.13 Yahoo's daily chart of WTI crude, keyless and free, one request a night, asked by the market series
    // fetcher alone in the fetch step, since the provider files no price of crude.
    OilChart,
    // From 21.13 Yahoo's spark of the market strip's symbols, keyless and free, every tile in one request, asked by the
    // read API alone for a page opened in the regular session and kept five minutes.
    StripQuotes,
    // On the operator's answer of 2026-11-17 the provider's list of the US exchanges' symbols with each company's name, one
    // request at a weight of one, asked by the names refresh alone in the off-peak run once a week.
    SymbolList,
    // From the 24.19 correction Yahoo's daily chart, Stooq's daily file and Tiingo's daily prices on the operator's key, asked
    // by the second source fill alone, by hand, for the holes inside a code's own prices.
    SecondSourcePrices,
    // From 24.47 the provider's live quote of many listings in one request, a page of symbols, asked by the fill check alone
    // after the close on each trading day, outside the night.
    LiveQuotes,
    // From 24.47 ntfy, one request a message to the operator's phone, on the topic the secrets file holds, sent by the order
    // changes and the fill check alone.
    PhoneMessages,
}

public readonly record struct StoreTouch(Store Store, Touch Touch);

public sealed record ComponentAccess(
    IReadOnlyList<StoreTouch> Stores,
    IReadOnlyList<Feed> Feeds)
{
    // The empty declaration. A component that touches no store says so, which is
    // a claim and not an omission: the mark renderer's blank matrix cells
    // are asserted as much as any filled one.
    public static ComponentAccess Nothing { get; } = new([], []);

    public Touch On(Store store) =>
        Stores
            .Where(entry => entry.Store == store)
            .Aggregate(Touch.None, (all, entry) => all | entry.Touch);
}

// A component of the catalogue in section 7.
//
// The declaration lives in the type because a declaration written beside the
// check that reads it is a second statement of one fact and nothing keeps the
// two together. It is an interface with a static abstract member rather than a
// plain static property, which is what CheckReach uses inside the suite: that
// one is discovered by the literal string "Reach", so a rename silently empties
// the population. A component declaration crosses an assembly boundary and is
// read reflectively from outside, so discovery is keyed on a type instead, and a
// type that opts in cannot then be missing the member.
public interface IComponent
{
    static abstract ComponentAccess Access { get; }
}
