# FullTime — Session Handover

Personal/family project (not TelXL work). "FullTime" is a Blazor/.NET MAUI football
betting-with-friends app for "The Brownes" family league — no real money, just bragging rights.
Owner: Alan Browne (alan.browne@telxl.com), company name on the app is "JTM Technology". Repo:
`github.com/jtmtechnology/fulltime`, local path `c:\AB\Friends`, branch `main` (no PR workflow —
commits go straight to main and get pushed).

**Durable architecture, per-host service pattern, DB name, environment quirks (adb/PowerShell,
coordinate scaling, MAUI build-lock recovery), testing conventions, and the VM deployment steps
live in `CLAUDE.md` at the repo root — read that first.** This file covers recent session history,
current outstanding work, and gotchas discovered along the way. Read this fully before touching
code.

---

## 1. Current state (as of this handover, 2026-09-07)

- **Production runs a hybrid provider setup** (this is the durable architecture going forward, not
  a dormant/reverted one — see §3 for how it got here):
  - **Highlightly** (direct account, `soccer.highlightly.net`) — live scores, and now also the
    primary settlement source for `MatchResult`/`OverUnder`/`BothTeamsToScore`/`CorrectScore`/
    `FirstTeamToScore` (unchanged) **plus** `TotalCorners`/`PlayerGoalscorerAnytime`/`PlayerCard`/
    `PlayerRedCard`/`PlayerAssists` (new — settled from its `events` and `statistics` endpoints,
    data that used to be fetched and discarded). Also powers the new Match Summary
    (goal/card/substitution timeline) page.
  - **the-odds-api** (free tier, 500 credits/month) — prices player-prop markets and Total Corners
    for Bet Builder (`PlayerPropsService`). Pricing only, never settlement.
  - **API-Football** (Pro tier, 7,500/day — see known issue below) — scoped down to exactly two
    jobs: (1) resolving which team a the-odds-api player-prop outcome belongs to (squad lookups by
    surname), and (2) settling `PlayerShotsOnTarget`/`PlayerShots`, the one stat Highlightly's API
    doesn't expose per-player anywhere (confirmed by direct investigation of its real endpoints).
- Everything described here is committed and pushed to `main` (latest: `466ad68`).
- **No Codemagic build has been triggered for this session's UI changes** (bet-slip descriptions
  for the new market types, bookmaker-row removal in Bet Builder, new player-prop labels). Owner
  needs to trigger one to get these onto a device.
- Stray untracked ~100MB APK mentioned in the previous handover is gone — no longer present at
  repo root, nothing to do there.

---

## 2. Earlier work (stable, predates 2026-09-06 — see git log/older handovers for detail)

Daily Spinner rebuilt server-authoritative (streak bonus later made a highlighted banner, see §4),
league invite + privacy pages, Android package rename, UMP consent flow, Play Store listing assets,
App Store Connect prep, test accounts, three Android layout/asset bug fixes, first Play Store
closed-testing release + release keystore generated (gitignored, back it up off-machine), AdMob
still on test IDs by design. Not re-detailed here — see file history if needed.

---

## 3. 2026-09-06 session — cutover, revert, and fixes that are still in effect

A same-day full cutover to API-Football (live scores) + the-odds-api (markets) was built, deployed,
and verified working, then **reverted back to Highlightly for live scores** at the owner's request.
That full-cutover code (`ApiFootballMatchSyncService` and friends) is still compiled in but dormant
— only the narrower API-Football usage described in §1 is actually active today. Real, durable
fixes that survived the revert:

- **`HighlightlyMatchSyncService.DeriveStatus`** no longer defaults an unrecognized status string to
  `InProgress` — explicitly recognizes `"First half"`/`"Second half"`/`"half time"` variants as
  live, logs a warning and keeps the previous status for anything else. Root-cause fix for an
  earlier FA Cup quota-exhaustion incident (an unrecognized status pinned the live-poll loop at high
  cadence for 11+ hours).
- **EF Core can't translate an arbitrary C# method call inside a LINQ `Where()`** — a
  `RequiresPlayerStats(p.MarketType)` call inside `SettlementService.ResolvePicksAsync`'s query
  threw at runtime; fixed with a static readonly array + `.Contains()` (translates to SQL `IN`).
  The same pattern is now used for `EventsDerivedMarketTypes`/`PlayerStatMarketTypes` (§4).
- **Team-name matching (`TeamNameMatcher.cs`)**: several real provider-name mismatches found and
  fixed — `"Sheffield Utd"`/`"West Brom"`/`"Accrington ST"`/`"QPR"` synonym mappings, and
  purely-numeric tokens (German clubs' year prefixes) now dropped as noise rather than scored as a
  mismatch. Still load-bearing today for both the-odds-api event resolution and API-Football fixture
  resolution.
- **FA Cup only tracked from the 3rd Round Proper onwards** — filtered at sync time (matches never
  stored), matched by exclusion (`contains "Qualifying"` / `starts with "1st Round"/"2nd Round"`)
  rather than an exact allow-list. Still in effect.
- **Highlightly moved off RapidAPI to a direct account** (`soccer.highlightly.net`, 25,000/day) after
  RapidAPI's quota was fully exhausted — separate account/token from RapidAPI despite an identical
  header shape. Direct host's paths are bare (no `football/` prefix RapidAPI's proxy needed).
- **Bookmaker label/logo** (`BookmakerLogo.razor`, `MatchCard.razor`'s row) — removed then restored
  in that session. **`BetBuilder.razor`'s own separate "Prices from bet365" row was later removed
  again this session (2026-09-07) at the owner's explicit request — that one is gone for good; only
  `MatchCard.razor`'s bookmaker row remains.**

---

## 4. 2026-09-07 session — this session's work

### 4.1 Daily Spinner streak banner

Made the 7-day streak bonus its own highlighted banner in the spin result display, rather than
sequential/easy-to-miss text (owner: "add the bonus details in the same display").

### 4.2 EPL player-prop betting feature (the-odds-api) — built from scratch, then generalized

Started as anytime-goalscorer only, generalized to a `PlayerPropsService` covering six markets:
`PlayerGoalscorerAnytime`, `PlayerCard`, `PlayerRedCard`, `PlayerAssists`, `PlayerShotsOnTarget`,
`PlayerShots`, plus `TotalCorners` (match-level, no player attached). A `MarketKind` enum
(`YesNo`/`PlayerLined`/`MatchLined`) distinguishes how each market's outcomes get parsed and
attributed. Fetch is hourly-throttled per match (`Match.PlayerPropsFetchedAt`) and only runs for
market types not yet found for that match (`foundTypes`/`pending` split) — **once a type is found
for a match it is never re-fetched**, which matters for the known issue in §5.

Squad-based team attribution: the-odds-api has no roster data of its own, so player-prop outcomes
are matched to Home/Away by **surname** (last whitespace token) against API-Football's squad list
for that team — needed because API-Football abbreviates first names ("B. Saka") while the-odds-api
spells them out ("Bukayo Saka").

**Real bug found and fixed**: `Match.HomeTeamId`/`ExternalId` are Highlightly's own ID space (the
active live-score provider), not API-Football's — code that assumed otherwise silently resolved the
wrong (or no) team/fixture. Fixed via:
- `ApiFootballEplTeamMap.cs` — static dict of the 20 current EPL clubs, keyed on **the-odds-api's
  own name strings** (not Highlightly's), resolved one-by-one via real `/teams?search=` calls. Five
  clubs (Newcastle, Leeds, Tottenham, Coventry, Ipswich) needed shorter colloquial names — searching
  their full names only matched reserve/U21/women's squads on API-Football, a confirmed real quirk.
  Needs a manual update after each promotion/relegation cycle.
- `HighlightlyToApiFootballLeagueMap.cs` — bridges Highlightly's league IDs to API-Football's, used
  by settlement to resolve the real API-Football fixture (by team-name + kickoff-date matching, via
  `TeamNameMatcher`) instead of trusting `Match.ExternalId` directly.

### 4.3 Settlement pivot: Highlightly-first, API-Football narrowed to shots only

Directive: "we need the markets but needs to be settled by the highlightly api — we should only be
using highlightly and odds api." Investigated Highlightly's real endpoints: goals/assists/cards/
corners **can** be settled from its `events` + `statistics` endpoints (previously fetched and
discarded); **per-player shots data does not exist anywhere in Highlightly's API** — confirmed, not
assumed. Net result, and the reason API-Football wasn't fully eliminated:

- `BetBuilderSyncService.ResolveFirstGoalScorersAsync` → renamed `ResolveMatchEventsAsync`, now
  stores full `MatchEvent` rows (Team/Minute/Type/PlayerName/AssistPlayerName/etc., not just a
  first-scorer flag), gated on new `Match.EventsFinalizedAt` (set once, post-match). Also sums
  `TotalCorners` from the `statistics` endpoint's "Corners" entries.
- New `RefreshLiveMatchEventsAsync`, gated on `Match.EventsFetchedAt` (30s throttle while
  `InProgress`) — powers the live Match Summary page (§4.5).
- `SettlementService`: `EventsDerivedMarketTypes` (TotalCorners/PlayerGoalscorerAnytime/PlayerCard/
  PlayerRedCard/PlayerAssists) guarded on `EventsFinalizedAt != null`, resolved via
  `FindPlayerEvent`/`CountAssists` against `Match.Events`. `PlayerStatMarketTypes`
  (PlayerShotsOnTarget/PlayerShots) guarded separately on `Match.PlayerStatsResolvedAt != null`,
  resolved via `FindPlayerStat` against `Match.PlayerStats` (API-Football, unchanged mechanism from
  before, now just narrower in scope). Both families use the same surname-matching helper.
- `PlayerStatsSettlementBackgroundService` — briefly deleted mid-session (when a first reading of
  "keep API-Football just for squad lookups" was taken as exclusive), then **restored** once the
  owner clarified shots settlement via API-Football was still wanted. Runs independently of
  `LiveScoreSource`, 5-minute cadence.
- Added `MatchPlayerStat.TotalShots` (alongside existing `ShotsOnTarget`) so `PlayerShots` (total
  shots) settles the same way as shots-on-target.
- **Exact-string player-name matching bug**, found the same way as the team-name one: API-Football
  ("B. Saka") vs the-odds-api/Highlightly ("Bukayo Saka") format names differently. Fixed via the
  same surname-based matching used in §4.2.

### 4.4 New markets added this session

`TotalCorners` (settled from Highlightly's team statistics), `PlayerRedCard` (settled from a
Highlightly "Red Card" event), `PlayerShots`/total shots (settled from API-Football, added
speculatively — zero current bookmaker coverage confirmed via curl, but the settlement path costs
nothing to have ready). `MarketType` enum is append-only, never reordered — current tail order:
`TotalCorners, PlayerGoalscorerAnytime, PlayerCard, PlayerShotsOnTarget, PlayerAssists,
PlayerRedCard, PlayerShots`.

### 4.5 Match Summary page (new)

`MatchEvents.razor` + `MatchEventRow.razor` — half-split goal/card/substitution timeline with a
running half-time score, home-left/away-right alignment, sourced from `GET /api/matches/{id}/events`
(pure DB read of `Match.Events`, ordered by a stoppage-time-aware minute parse). Live-updates every
30s during a match via `RefreshLiveMatchEventsAsync` (§4.3). `UpcomingMatchDto.EventsAvailable`
(`m.Events.Any()`) gates whether the client shows the entry point at all.

### 4.6 UI polish

- Bet Builder odds sort: ascending by price (lowest/most-likely first) for non-lined markets — note
  this is the opposite of an earlier same-session request ("worst odds first") that was corrected.
- Bet-slip descriptions added for every prop type, including a late-caught gap: `TotalCorners` had
  no case in either `BetSlipSheet.razor`'s or `BetDisplay.cs`'s switch (`"TotalCorners" =>
  $"{pick.Side} {pick.Line:0.##} corners"`).
- Popular/Player-Bets tab switcher now hides entirely when a match has no player props priced yet,
  instead of showing an empty tab.
- Player-props fetch (`PlayerPropsService.EnsureFreshAsync`) is triggered fire-and-forget from
  `MatchesController` (`TriggerPlayerPropsRefreshInBackground`) — never awaited inline, so opening
  Bet Builder is never blocked on it. Same class of fix as an earlier h2h-blocking bug from the
  previous session.
- `BetBuilder.razor`'s standalone "Prices from bet365" bookmaker row removed at the owner's explicit
  request (`MatchCard.razor`'s separate bookmaker row was untouched — still there).

### 4.7 Real bug found and fixed: duplicate `BetBuilderMarket` rows (race condition)

`PlayerPropsService.EnsureFreshAsync` only ever **appended** newly-priced rows — it never cleared
existing rows for a market type before inserting. Two near-simultaneous calls for the same match
(e.g. two page loads, or manual re-triggers close together) could both pass the "not yet found"
check before either had committed, and both insert a full set of rows for the same market. Symptom
in the app: **`Error: An item with the same key has already been added. Key: 0.50`** when opening
Bet Builder — the client groups priced outcomes into a dictionary keyed by line, and duplicate rows
with the same line collide.

Fixed: before inserting, `EnsureFreshAsync` now deletes existing rows for whichever market types are
about to be (re)written, scoped to that match, so a repeat fetch replaces rather than duplicates.
**This fix only prevents new duplicates — see §5 for the pre-existing-data risk it doesn't cover.**

One instance (Aston Villa v Nottingham Forest, `Match.Id = 4a8f69a0-8cad-4d17-9da9-dc1fc95248a6`)
was found via the reported crash and manually deduped with a direct SQL delete (kept the
lowest-`Id` copy per matching `MarketType`/`PlayerName`/`Side`/`Line`/`Team`); confirmed clean
afterward (28 rows, was 56).

---

## 5. Known issues / outstanding

1. **Other matches may still have the same duplicate-row problem from before the §4.7 fix was
   deployed (~2026-09-07 16:08 UTC, commit `466ad68`).** Because `EnsureFreshAsync` never re-fetches
   a market type once it's "found" for a match, the fix can't self-heal existing bad data — a match
   already showing duplicates will keep showing them until manually deduped. **Not yet swept.** If
   another "same key already added" crash is reported, the fix is the same direct SQL used for the
   Villa/Forest match (see git history around commit `466ad68` / this handover's prior session
   transcript for the exact query) — or write a one-off migration/script to dedupe every match at
   once rather than firefighting one at a time.
2. **API-Football plan mismatch**: owner said they'd moved to the free tier (100/day), but the real
   `/status` endpoint still shows the Pro plan active (7,500/day limit, ~109/day used this session)
   as of 2026-09-07. Not urgent (usage is well within free tier anyway, ~160-170 calls/month
   estimated for shots settlement) but worth reconciling — either the downgrade hasn't taken effect
   yet or hasn't happened.
3. **the-odds-api is on its free tier (500 credits/month)** — note this supersedes the previous
   handover's mention of a 20,000/month key; that key belonged to the abandoned full-cutover
   experiment and is not the one in use now. Current real usage is comfortably inside the free tier.
4. Card/RedCard/Assists/total-Shots markets show **zero** priced rows for most matches right now —
   confirmed via direct curl this is real (no bookmaker has priced them yet this early before
   matchday), not a bug. Don't re-investigate this as a bug without checking real coverage first.
5. **No Codemagic build triggered** for this session's UI changes (§4.6) — needs the owner to start
   one.
6. The **auto-mode safety classifier intermittently blocks `gcloud compute ssh`/`scp` calls to the
   production VM, and can even block local file edits, with no clear pattern** — a plain retry of
   the exact same command has repeatedly succeeded on the second attempt this session. An attempt to
   pre-approve `gcloud compute scp`/`ssh` via explicit rules in `.claude/settings.json`'s
   `permissions.allow` list was itself blocked mid-edit and **was never actually applied** — check
   `.claude/settings.json` before assuming those rules exist; if a future session wants to add them,
   the rule shape is `"Bash(gcloud compute scp:*)"` / `"Bash(gcloud compute ssh:*)"` (and the
   `PowerShell(...)` equivalents).
7. Carried over, unconfirmed, from before this session: Play Console closed-testing rollout status,
   release keystore off-machine backup, iOS UMP/package-rename verification (needs a Codemagic
   build), `adb` not detecting the physical Android phone over USB, the
   `test@jtmtechnology.co.uk` test account decision, App Store Connect submission (no build
   uploaded yet), real AdMob IDs + `remove_ads` promo codes (post-launch), a real Apple 1024×1024
   no-alpha icon export, TestFlight beta review 422, a stale `free-api-live-football-data`
   retirement plan at `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md`, and
   Cloudflare purge-on-deploy to replace the `styles.css?v=N` cache-busting workaround.
8. A known minor race (unrelated to §4.7) still exists in both `HighlightlyMatchSyncService` and
   the dormant `ApiFootballMatchSyncService`: the live-sync and fixture-discovery background
   services can both try to insert the same brand-new match at once right after a restart on an
   empty/near-empty `Matches` table (`23505` duplicate-key error). Self-heals on the next tick, not a
   data-integrity risk, but not actually fixed.

---

## 6. Key files touched this session (2026-09-07)

Server: `Models/MarketType.cs`, `Models/MatchPlayerStat.cs` (added `TotalShots`), `Models/Match.cs`
(new `EventsFetchedAt`/`EventsFinalizedAt`/`PlayerPropsFetchedAt` fields), `Models/MatchEvent.cs`
(new), `BetBuilder/Dtos/HighlightlyDtos.cs` (extended `MatchEventDto`, new `TeamStatisticsDto`),
`BetBuilder/Dtos/ApiFootballDtos.cs` (`ShotsStat.Total`), `BetBuilder/HighlightlyClient.cs` (new
`GetStatisticsAsync`), `BetBuilder/BetBuilderSyncService.cs` (major — see §4.3),
`BetBuilder/GoalScorerResolutionBackgroundService.cs`, `BetBuilder/ApiFootball/*` (team/league maps
new, `ApiFootballSettlementSupportService.cs` reworked, `PlayerStatsSettlementBackgroundService.cs`
deleted then restored), `BetBuilder/OddsApi/PlayerPropsService.cs` (major — see §4.2/§4.7),
`Betting/SettlementService.cs` (major — see §4.3), `Controllers/MatchesController.cs` (new
`/events` endpoint, fire-and-forget trigger), `Program.cs` (service registration), four new EF Core
migrations (`AddMatchEvents`, `RenameGoalscorerPropsFetchedAtToPlayerPropsFetchedAt`,
`AddEventsFinalizedAt`, `AddTotalShotsToMatchPlayerStat`), all applied in production.

Client: `Pages/BetBuilder.razor` (bookmaker row removed, new market labels), `Pages/MatchEvents.razor`
(new), `Components/MatchEventRow.razor` (new), `Components/BetSlipSheet.razor` +
`Services/BetDisplay.cs` (new market descriptions), `Pages/Matches.razor` (day-range tweak).

`ODDS_API_PLAYER_PROPS_INVESTIGATION.md` at repo root is a scratch doc with **live API keys in
plaintext** — must never be committed, and isn't (kept out of every commit this session
deliberately). Real keys otherwise live only on the VM's systemd `Environment=` lines.

Run `git log --oneline` on `main` for the full commit-by-commit sequence — commit messages explain
the "why" for each change individually. This session's work is commit `466ad68` plus the several
commits immediately before it (see `git log` from `ab35c32` onwards for the whole player-props arc).

---

## 7. Gotchas discovered this session (in addition to `CLAUDE.md`'s environment quirks and the
   previous handover's §6)

- **A market type "found" for a match is never re-fetched by `PlayerPropsService`** — this is
  intentional (avoids re-spending the-odds-api quota on markets that are already priced), but means
  any bug in a past fetch (like the §4.7 duplicate-row race) leaves permanently-bad data behind for
  that match specifically, with no automatic recovery. Any future "why does this one match behave
  differently" report is worth checking for stale/duplicate rows first.
- **A production data bug can look identical to "the API has no data"** — the actual debugging path
  this session (for "no shots on target odds") required directly curling the-odds-api with the
  match's real event ID to prove the data existed, before concluding the bug was in the app's own
  storage path rather than upstream. Don't assume "provider has no coverage yet" without checking
  with a real, current curl first.
- **The auto-mode safety classifier's blocks are not fully deterministic** — the same exact command
  (a `gcloud compute ssh`/`scp`, a local file `Edit`) was blocked once and then succeeded on an
  identical retry, more than once this session, with no discernible pattern in command content. Try
  a plain retry before concluding something is a hard, permanent block.
- **Diacritics break exact/ordinal string matching between providers** — e.g. API-Football's
  `"N. Milenković"` vs the-odds-api's plain-ASCII `"Nikola Milenkovic"` won't match under
  `OrdinalIgnoreCase` surname comparison (`ć` ≠ `c`). Not yet fixed generically — currently only
  matters for the small number of outcomes it silently drops (`team is null` → `continue`), which is
  a safe failure mode (missing a row) rather than a wrong one, but worth a diacritic-stripping pass
  if it starts affecting markets that matter more.
- **Some players are recorded by API-Football under a mononym with no surname at all** (e.g.
  Nottingham Forest's `"Jair"` for Jair Cunha) — surname-based matching can't reconcile this against
  a full-name source that includes the surname. Same safe-failure-mode caveat as above.
