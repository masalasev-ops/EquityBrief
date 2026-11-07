# The operator's order of 2026-11-07: paper trading and Home's account, all three indexes

Issued in conversation on 2026-11-07 at about 05:05 UTC, word for word.

Paper trading and Home's account, all three indexes, one PR in a commit per item. Read every change back on 5152 at 1440 and a true 390 in both palettes, list what changed, and add a test per item that fails the page as it read on 2026-11-06. Merge only with tools/ci.ps1 and tools/verify-phase.ps1 green on my machine and no other writer running. Stop with one report.

Start dates
1. Every account counts from the day its rule's current version was registered, never earlier. A rule whose earlier trades replay the same under this version lists them apart, under the account, as "The same trades before this version (since <date>), kept in the rule's record and not in this account". One date per account, the same on its tile, the chart, the scorecard's "counting from" and every sentence; remove "The accounts above count only the rules as registered now, since 3 Nov" and any line contradicting a tile.
2. Home's account starts on the day every live rule on the index had its current version, the latest of their start dates (3 Nov on the S&P 500), worded "Since today's rules all took effect on <d Mon>", with the index's fund over the same days. The system's total stands apart: "All versions since <date>: <n> ideas bought and sold on paper, <n> made money." Home no longer shows 2 Oct.
3. Each tile draws the index's fund over that account's own days; no tile draws the fund since 2 Oct beside a rule that started later.
4. "What the versions did differently" gives each row its start: the live setup "record since <d Mon>", each version "trading since <d Mon>".

Wrong on the page
5. XYZ's company draws "Run of 63 trading days, Inc" in "What the system bought": a plain-words swap is replacing "Block" inside a company name. Company names, tickers and anything a filer or provider wrote are never passed through the swap. Sweep every page for any other company name or filer's text the swap changed and list it.
6. Each table and list names the account it walks. The "Not bought" line under "What the system bought" lists GWW, DD, IDXX and VLTO as not bought for cash while the combined account holds them open: say which account each refers to, and draw no "not bought" for a stock that account holds.
7. The monthly books' sizing in the combined account: state the actual rule from the code. Its words say "each set of holdings a fifth of the account", yet FTNT holds $897, LYB $863 and the heavyweights about $800 each. Make the sizing match the stated rule or the words match the sizing, and show each holding's dollars.
8. Holdings with no price stop (Fundamentals, Quality at a fair price, Sector heavyweights): the Stop and Target columns draw how each sells ("Sells when a filing drops it below 7 in every 9 of the checks that apply, or after 126 trading days"; "Sold at the reshuffle if it no longer qualifies, next on <date>"). "Where it stands" draws whether it still qualifies tonight, never "Not read tonight".
9. ABNB draws as held and as "Waits on the next trading day's open". Find why and fix it; a held stock never draws as waiting.

The accounts
10. "All setups together" becomes the equal average of each live rule's own account, named "The live setups' accounts, averaged", so no pick is skipped twice and it reads the same as following every rule. Home's account reads it, from the date in item 2.
11. Each rule's own account caps a position at a tenth of the account instead of a fifth, sizing by risk as the cards do, so it holds ten positions before it skips. The header says so: "Each position risks 1% and is capped at a tenth of the account."
12. A skipped pick never sits in a trade table. Each account draws its trades, then one line beneath: "Not bought: n picks, <reason>: tickers", named by reason.

The versions
13. Where a rule and its versions have no trade since the rule's record began, say why in one line instead of a table of zeros, for example "Pullback has made no trade since its record began on 3 Nov, and each of its versions narrows it, so none could trade either." Mark each version "narrows the live rule" where it can only take a subset of its trades, and "widens it" where it can take trades the live rule cannot.
14. List, for each family on each index, how many versions narrow the live rule and how many widen it. For any family whose versions all narrow it, propose one widening version per its most refused step (for pullback: the uptrend strength, and the days it may take to turn up), with the step's refusal count from the ledger, as proposals for phase 23's restart. Register nothing without my word.

The chart
15. Each line starts on its account's own start day from item 1, never as a flat 0% before; the span starts at the earliest of those days, with a choice of "Since each began" and "Last 30 days".
16. Every line in its family's own colour, matching its legend chip (Quality at a fair price and Fundamentals draw white today); the averaged account thicker, in the text colour, the only line in it.
17. The index's fund as a dashed grey line over the same days, in the legend.
18. A labelled zero line, gridlines at round steps, dates at least weekly, and each line ending in a label with its name and latest value, moved apart where two would overlap.
19. A hover box with the day and each line's return on it.
20. An account with no trade draws no line; its legend chip says "no trades yet".

Last
21. Read the whole page and Home's account on the S&P 400 and 600 for the same faults and list anything else found, fixed in this PR where it is the same kind.

And beneath it, the operator's instruction on filing:

File each fix as a correction under the checkpoint that first built the code it corrects (for example "Phase 21 / 21.11 - correction: ..." for the combined account, "Phase 22 / 22.16 - correction: ..." for Home's account), reading which from the record. Nothing is filed under phase 23. These land before the phase 21 and 22 sign-offs, which check them.
