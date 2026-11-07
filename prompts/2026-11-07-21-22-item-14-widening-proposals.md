# Item 14 of the order of 2026-11-07: each family's versions by reach, and widening proposals

Proposals for phase 23's restart, written on the order's fourteenth item. Nothing here is registered, and nothing
registers without the operator's word.

## How each count was read

Each standing version's registered settings were read against its family's live rule's, setting by setting, by
`VersionReach.Of` as this pull request leaves it, over the register as it stood on 2026-11-07. A market check turned
on or off moves its floor and the breadth it reads together, and the two are read as one change of the floor.

## Versions by reach, each family on each index

| Index | Family | Narrow | Widen | Same picks, sold differently | Same picks, another order |
|---|---|---|---|---|---|
| S&P 500 | pullback (the swing filter) | 6 | 2 | 0 | 0 |
| S&P 500 | breakout | 3 | 3 | 1 | 1 |
| S&P 500 | drift | 3 | 2 | 3 | 0 |
| S&P 500 | dip | 0 | 2 | 2 | 0 |
| S&P 500 | sector heavyweights | 0 | 2 | 1 | 0 |
| S&P 500 | quality | 3 | 3 | 1 | 0 |
| S&P 500 | Fundamentals | 2 | 3 | 3 | 0 |
| S&P 400 | pullback | 0 | 3 | 1 | 0 |
| S&P 400 | breakout | 0 | 3 | 0 | 0 |
| S&P 400 | drift | 1 | 4 | 1 | 1 |
| S&P 400 | dip | 0 | 3 | 1 | 0 |
| S&P 400 | sector heavyweights | 1 | 0 | 0 | 0 |
| S&P 400 | quality | 3 | 3 | 1 | 0 |
| S&P 400 | Fundamentals | 7 | 1 | 0 | 0 |
| S&P 600 | pullback | 1 | 2 | 1 | 0 |
| S&P 600 | breakout | 1 | 2 | 0 | 2 |
| S&P 600 | drift | 2 | 1 | 1 | 0 |
| S&P 600 | dip | 0 | 3 | 2 | 0 |
| S&P 600 | sector heavyweights | 0 | 3 | 0 | 0 |
| S&P 600 | quality | 3 | 3 | 1 | 0 |
| S&P 600 | Fundamentals | 4 | 4 | 0 | 0 |

No version reads as changing a setting whose reach is not read.

**Only one family's versions all narrow its live rule: the S&P 400 sector heavyweights, whose one version adds the
profit gate.** The S&P 500 pullback, which the order names, has six that narrow it and two that widen it: the market
check off, and the plan at the nearest bands, which can set a target and a stop the live rule's plan does not and so
pass a trade its trade step refuses. Neither of the two touches the steps that refuse most, so its proposals are
given below as the order asks.

## S&P 500 pullback: the steps that refuse

From the swing filter's stored answers for each member each night (`gate_result`, version 5), each member-session
counted once at the first step that refused it:

| Step | 2 Oct to 6 Nov, 26 sessions, 13,082 member-sessions | 3 Nov to 6 Nov, 4 sessions, 2,012 member-sessions |
|---|---|---|
| trend and strength | 10,919 | 1,503 |
| setup | 1,759 | 434 |
| trade | 194 | 43 |
| trigger | 168 | 26 |
| passed every step | 19 | 2 |

Most of the trend and strength step's refusals are of the trend, not the strength: 146 of the 10,919, and 43 of the
1,503, were uptrends refused on strength alone. The rest were ranges or downtrends. A lower strength floor therefore
admits few stocks; widening the trend itself would change what the rule is, so it is not proposed here.

From the setup ledger (`setup`, the S&P 500 pullback, 31,308 history setups from 2019-01-02 to 2026-10-08 and 202
nightly ones from 2026-10-09 to 2026-11-06), the live setting refused 26,702.

### Proposal 1: the uptrend's strength floor at 0.40, from 0.50

- Night answers: 117 of the 146 strength refusals since 2 Oct had strength 0.40 to 0.50, and 7 of them passed every
  other step. Since 3 Nov the counts are 33 and 1.
- Ledger: 4,047 of the 26,702 refused setups had strength 0.40 to 0.50. That is an upper bound on what the version
  admits, since another step may refuse the same setup.

### Proposal 2: the turn up may arrive within 5 sessions, from 3

- Night answers: the trigger step refused 167 member-sessions since 2 Oct with "the trigger did not arrive in the last
  3 sessions". 111 of those had a trigger 3 or 4 sessions back, within a window of 5, and 18 of the 111 then passed
  the trade step. Since 3 Nov the counts are 26 refusals, 17 within 5 sessions and 1 passing the trade step.
- Ledger: 6,900 of the 26,702 refused setups had a trigger 3 or 4 sessions old, and 9,330 an older one. Again an
  upper bound.

## S&P 400 sector heavyweights

The live rule takes each sector's 10 largest members leading the sector's fund over 251 sessions, 2 leaders a sector
with a beta of at least 1, sold under the 200-day average. Its one version adds the profit gate and sells on no
longer leading.

The heavyweights are a book, and the setup ledger holds no setup for a book family on the S&P 400. Its families there
are breakout, dip, drift, fundamentals, improving, pullback and quality. So no step's refusal count can be read from
the ledger for this family. What the store does show: at its two rebalances, 20 Oct and 2 Nov, the live rule's own
book bought 5 and then 4 holdings, each time against 22 places (2 a sector across 11 sectors), 9 stocks in 5 sectors
in all. Every place it left empty was refused by the lead over the sector's fund or by the beta check, and the
stored rows do not say which.

### Proposal 3: the beta check off

Admits a sector's leader whatever its beta. It widens by the table's reading, a floor lowered. Its refusal count
waits on the book's own steps being stored, which no table holds today.
