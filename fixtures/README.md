# fixtures

One folder per fixture name and date. Committed, never regenerated, and the expectations are derived once from the rules in `docs/ARCHITECTURE.html` and then frozen.

```
<name>-<YYYY-MM-DD>/
  manifest.json        every captured input: the endpoint, the query and the UTC instant of the fetch
  bars.csv             one year of daily bars ending on the fixture date
  fundamentals.json    the provider payload and filing extracts as they stood on the fixture date
  news-<YYYY-MM-DD>.json   the articles a research pass may read, with their publish dates
  inadmissible-documents.json   one document per kind the admissibility test refuses, constructed
                       rather than captured, each stating the marker it carries and where that
                       marker was read off a real page
  expectations/        one file per stage, flat, each naming the tables it covers and how it
                       was derived
    indicators.json    swings.json    volume-profile.json
    levels.json        ladder.json    listings.json
    facts.json         the artefact a run is diffed against
    admissibility.json which document each rule refuses, and the row a refusal leaves
```

**A fixture counts two different things and they are not the same number.** Constituents are the rows the membership payload carries, current and departed. Names are the tickers with a captured price series, which is a subset: a departed name is not owed history, so it has a membership row and no bars. `membership-2026-09-05` holds 6 constituents and 4 names. Where the corpus says a fixture widens to four names, as phase 3 does, it means the second figure, and adding a constituent does not discharge it.

**Every expectation records how it was produced.** An expectation derived independently from the rules verifies something. One frozen from a run detects regression and verifies nothing, and a checkpoint whose expectations are all frozen has added regression detection and called it verification.

**No credential appears in a captured response.** The manifest asserts it and so does the check. `manifest.schema.json` beside this file is where the manifest's shape is declared, and the suite reads its required fields from there rather than restating them, so a field added to the schema is one the check refuses a manifest for leaving out.
