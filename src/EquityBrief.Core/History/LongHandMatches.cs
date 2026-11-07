namespace EquityBrief.Core.History;

// One holding a person matched from the SEC's filings: the fund, the quarter end and the name as the schedule filed it, the
// provider's code, and what was read, the filer, the report and its sentence naming the code or the filer's own record where
// the report names none, with the code's close at the quarter end beside the fund's value a share.
public sealed record LongHandMatch(string Index, DateOnly Period, string Name, string Code, string Read);

// The holdings matched by hand, each naming what was read. A hand match stands only where no key matched the holding and its
// code's close at the quarter end equals the fund's value a share to the cent.
// see: A holding is matched or refused by hand from the SEC's filings and names what was read
public static class LongHandMatches
{
    public static IReadOnlyList<LongHandMatch> All { get; } =
    [
        new("MID", new DateOnly(2004, 12, 31), "Scotts Co. (The) Class A", "SMG",
            "The Scotts Company's annual report for its year to 2004-09-30, accession 0000950152-04-008595 of filer 825542, filed as Scotts Company and now Scotts Miracle-Gro Co.: \"The common shares of The Scotts Company trade on the New York Stock Exchange under the symbol SMG\". SMG's close in the provider's day file of 2004-12-31 is 73.52, the fund's value a share."),
        new("MID", new DateOnly(2004, 12, 31), "National-Oilwell Inc.", "NOV",
            "National-Oilwell's annual report for 2004, accession 0000950129-05-002101 of filer 1021860, filed as National Oilwell Inc., then National Oilwell Varco Inc. and now NOV Inc., whose ticker the SEC lists as NOV: \"National Oilwell common stock is listed on the New York Stock Exchange (ticker symbol: NOI)\". The provider files the filer's prices under NOV, whose close in the day file of 2004-12-31 is 35.2899, the fund's value a share of 35.29."),
        new("MID", new DateOnly(2004, 12, 31), "Washington Federal Inc.", "WAFD",
            "Washington Federal's annual report for its year to 2004-09-30, accession 0000891020-04-000618 of filer 936528, which reads its stock's market from its exhibit 13 and names no code in the report itself; the SEC's record of filer 936528 names it Washington Federal Inc. from 1996-09-23 to 2023-09-28 and WaFd Inc. since, its ticker WAFD on Nasdaq. WAFD's close in the provider's day file of 2004-12-31 is 26.54, the fund's value a share."),
        new("MID", new DateOnly(2004, 12, 31), "Brink's Co. (The)", "BCO",
            "The Brink's Company's annual report for 2004, accession 0000078890-05-000014 of filer 78890, filed as Pittston Co. and now Brinks Co.: \"under the symbol BCO\". BCO's close in the provider's day file of 2004-12-31 is 39.52, the fund's value a share."),
        new("GSPC", new DateOnly(2008, 12, 31), "Equitable Resources Inc.", "EQT",
            "The SEC's record of filer 33213 names it Equitable Resources Inc. from 1994-05-18 to 2009-02-04 and EQT Corp since, its ticker EQT on the New York Stock Exchange. EQT's close in the provider's day file of 2008-12-31 is 33.55, the fund's value a share, the one quarter end the fund held the name before it changed."),
    ];

    // The hand matches of one fund, keyed by quarter end and name.
    public static IReadOnlyDictionary<(DateOnly Period, string Name), LongHandMatch> Of(string index) =>
        All.Where(match => match.Index == index).ToDictionary(match => (match.Period, match.Name));

    // The matches a person refused on reading the SEC's filings, each with what was read: the name is matched to that code at
    // no quarter end, by any key.
    public static IReadOnlyList<LongRefusal> Refused { get; } =
    [
        new("GSPC", "Lucent Technologies Inc.", "ALU", "Lucent's annual report, accession 0000950123-05-014751: \"under the symbol LU\"; ALU is Alcatel-Lucent's ADR, whose close of 11.706 in the provider's day file of 2006-09-29 is the price of the shares Lucent's holders received rather than Lucent's own."),
        new("GSPC", "Pepsi Bottling Group Inc.", "OCCH", "the provider's symbol list names OCCH Coca-Cola Hellenic Bottling Co, another company."),
        new("GSPC", "Sara Lee Corp.", "AIMN", "the provider's symbol list names AIMN Lee Pharmaceuticals, another company."),
        new("GSPC", "Stanley Works (The)", "SXE1", "the provider's symbol list names SXE1 Stanley Inc, another company; Stanley Works traded as SWK."),
        new("MID", "Cabot Microelectronics Corp.", "CBT", "Cabot Microelectronics' annual report, accession 0000950137-04-010865: \"under the symbol CCMP since our initial public offering in April 2000\"; CBT is Cabot Corporation."),
        new("MID", "Forest Oil Corp.", "FRX_old", "Forest Oil's annual report, accession 0001104659-07-015038: \"under the symbol FST\"; FRX_old is Forest Laboratories."),
        new("SML", "Southwest Bancorp of Texas Inc.", "OKSB", "Southwest Bancorp of Texas' annual report, accession 0000950129-05-002380: \"under the symbol SWBT\"; OKSB is Southwest Bancorp Inc. of Oklahoma."),
        new("SML", "Tesco Corp.", "TSCDD", "Tesco Corporation's annual report, accession 0001022705-14-000018: \"under the symbol TESO\"; TSCDD is Tesco PLC's receipt."),
        new("SML", "Thomas Industries Inc.", "TPGI", "the provider's symbol list names TPGI Thomas Properties Group, another company."),
        new("SML", "Arbitron Inc.", "KEYUF", "Arbitron's annual report, accession 0000950123-10-019282: \"under the symbol ARB\"; KEYUF is Keyera Corp, its closes at the cent at two quarter ends by chance."),
    ];

    // Whether a name's match to a code was refused by hand.
    public static bool IsRefused(string index, string name, string code) =>
        Refused.Any(refusal => refusal.Index == index && refusal.Name == name && refusal.Code == code);
}

// A match a person refused: the fund, the name as filed, the code refused and what was read.
public sealed record LongRefusal(string Index, string Name, string Code, string Read);
