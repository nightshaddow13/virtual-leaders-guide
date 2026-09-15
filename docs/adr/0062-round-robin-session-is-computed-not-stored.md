# A Round Robin Session is computed from start/duration/gap, not a stored row

A Round Robin (#144) needs a clock time for each Session to print on the grid. We store three numbers on the
`RoundRobin` itself — `StartsAt` (UTC, per ADR-0043), `SessionMinutes`, and `ChangeoverMinutes` — and derive
every Session's start as `StartsAt + n × (SessionMinutes + ChangeoverMinutes)` for `n` in `0..SessionCount-1`,
rather than persisting a `Sessions` table with one row per slot.

The deciding case is a day that runs late: with computed Sessions, correcting the printed grid is one edit to
`StartsAt`. With stored per-Session rows, the same correction means editing every remaining row, or building a
bulk-shift operation to do it — real complexity purely to answer "the whole day moved by twenty minutes,"
which is common at a live event and was the scenario that settled this.

`SessionMinutes` and `ChangeoverMinutes` are both plain durations, not `Sessions` count as a derived value —
`SessionCount` is stored directly, since a Round Robin can generate a repeating rotation deliberately when
`SessionCount` exceeds the participating Station count (see the Phase 7 issue's generation algorithm), and a
count that could be reverse-derived from something else would make that intentional repeat inexpressible.

## Considered options

- **A `Sessions` table, one row per slot, each with its own start/end** — rejected as the default: maximum
  per-Session flexibility (a longer lunch slot, an odd-length opener) but re-timing the whole day means
  editing every row, and the common case (the day ran late) is exactly the one this makes expensive.
- **Computed by default, with a per-Session override table for exceptions** — considered for a future phase,
  not built now: more model and more UI than the first slice needs, and nothing in the current scope (a
  same-length rotation) requires it yet.
- **No clock times at all, just ordinal Sessions** — rejected: the printed guide needs a time, and CONTEXT.md's
  Starts at/Ends at precedent already establishes this app renders stored UTC times in each viewer's own
  timezone (ADR-0043) rather than omitting times where they matter.

## Consequences

- Every Session in a Round Robin has the same length and the same changeover gap — there is no way to make one
  Session longer than the rest without a schema change. Acceptable for this phase; flagged above as the
  natural extension point if a future story needs it.
- Editing `SessionMinutes`, `ChangeoverMinutes`, or `StartsAt` re-times every Session's *displayed* clock time
  immediately, with no migration or bulk-update step, since nothing is stored per Session to begin with.
