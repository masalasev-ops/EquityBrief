# The operator's answers on the chart audit, 2026-11-04, word for word

The audit's ask, item 1f as changed while it ran:

Change to item 1f of the chart audit, while it runs: stop the Yahoo comparison and drop it from the nightly check. Replace it with:
 f. A check against EODHD's own records, no outside source: every price jump flagged in b is compared with EODHD's splits and dividends for that stock and date; a jump matching a split EODHD records but not applied in the stored bars is flagged as unadjusted, and a jump with no split, dividend or reported event is flagged for a fresh fetch of that stock's history from EODHD. Every fix uses EODHD's data. Only for a stock still unexplained after a fresh EODHD fetch, list it for me with the dates; I decide whether to check it elsewhere.
Keep any Yahoo figures already fetched out of the report and out of the store.

The fixes the audit's ask names, 3a to 3c and 4:

 a. Scaling: the vertical scale fits the candles in view, plus the plan's lines (buy, stop, target) and tonight's levels when shown, with a small margin; moving averages never widen it. An average outside the range is clipped at the edge with its value at the left edge in the legend, for example "200-session average 160.21 at the left edge, off the chart above". The 20-, 50- and 200-session averages get distinct styles matching their legend entries, readable without colour. The price axis gets labelled gridlines (for example every $5 or $10, chosen to fit) so a price can be read without hovering.
 b. Hover: pointing at a candle (or tapping it on a phone) shows a box beside it, kept inside the chart, with the date (or the week's or month's dates on the weekly and monthly views); open, high, low and close; the change on the candle before in price and per cent, in the gain or loss colour with its sign; volume in full with thousands separators; and the 20-, 50- and 200-session averages at that candle. A thin vertical line runs through the candle and the volume bar under the pointer, and the close is marked on the price axis at the right. The line above the chart keeps showing the newest candle when nothing is pointed at. Works with mouse, touch and keyboard (arrow keys step candle to candle when the chart has focus).
 c. Every other flag the audit raises: its cause stated and fixed, or, where the data is right (a real crash, a real spin-off, a ticker change), a line on the chart saying what happened on that date.
 All of it on daily, weekly and monthly, every index, both palettes, at 1440 and 390, and on the read-only copy. Read back FISV and one stock with no big drop for comparison.

4. Keep the audit as a check in the proof that runs every night after the bars land, so a broken chart is listed the morning it appears: flags a, b, c and e without network, the Yahoo comparison weekly. (The Yahoo comparison dropped by the change above.)

My answers on the chart audit:

1. Go, in this order, each its own checkpoint:
 a. Data fixes first, since the rules read these bars:
  - CTVA: refetch its year so the stored bars carry EODHD's adjustment for the 1 Oct spin-off. Trace why the night's corporate action check missed it and fix that. List every pick, trade, paper trade and history setup on CTVA built on the wrong bars, and correct them in the remedy, stating what changes.
  - The 17 copied zero-volume candles on HR, HWKN, LPX, MOG-A, MSA and WINA: find what wrote copies of the session before on 21, 22 and 31 July and fix it; refetch those stocks.
  - HOS: fix the staleness check that reads ok while the newest bar is 7 sessions behind, and refetch it.
  - THRM, PMT, MSGK: refetch so their adjustments land on EODHD's dividend dates.
  - VSXY: join VSCO's history as FI to FISV is joined.
  - The 8 stocks with no pulled history: pull it. The 8 where the store and EODHD's pull disagree: refetch.
  - PECO, TALO, WOR: yes, adjust using EODHD's recorded split; the data stays EODHD's.
  - Yes, refetches may replace stored bars through the existing refetch path. State the calls before running. Rebuild the ledger history for every stock whose bars changed, and say which rules' records or readings moved.
 b. 3a, the scaling and price gridlines.
 c. 3b, the hover box.
 d. 3c, a line on the chart for each real event the audit confirmed.
 e. The nightly check, covering flags a, b, c and e with no network, plus three it found: a zero-volume candle identical to the session before; a newest bar behind the night while the stock's state reads ok; and a split or dividend among the stored corporate actions that the stored bars do not carry.

2. Before phase 23 uses older history: classify the pulled years' 2,812 missing sessions, 4,936 zero-volume candles and the 690 unexplained jumps on 224 stocks against EODHD's own data (a fresh fetch where needed, its splits and dividends, the stored earnings dates and filings). Report how many are real (thin trading, halts, earnings moves) and how many are bad bars, and fix the bad ones from EODHD. Put the count in the phase 23 plan as a data check its first checkpoint repeats over 2005 onward.

3. Your three rules stand (market-wide at 10 or more members over 25%, a dividend at least half the fall, an event on the session or the one before). Flag a need not count the plan's lines.

Added while the fixes were being built:

At the end can you also fix this random question mark on pages wherever they are. This one is on paper trading
