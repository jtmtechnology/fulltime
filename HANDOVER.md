# FullTime — Session Handover

Personal/family project (not TelXL work). "FullTime" is a Blazor/.NET MAUI football
betting-with-friends app for "The Brownes" family league — no real money, just bragging rights.
Owner: Alan Browne (alan.browne@telxl.com), company name on the app is "JTM Technology". Repo:
`github.com/jtmtechnology/fulltime`, local path `c:\AB\Friends`, branch `main` (no PR workflow —
commits go straight to main and get pushed).

**Durable architecture, per-host service pattern, DB name, environment quirks (adb/PowerShell,
coordinate scaling, MAUI build-lock recovery), testing conventions, and the VM deployment steps
now live in `CLAUDE.md` at the repo root — read that first.** This file covers recent session
history, current outstanding work, and gotchas discovered along the way. Read this fully before
touching code.

---

## 1. Current state (as of this handover)

- **Nothing from this session is committed to git yet.** `git status` shows 8 modified files plus
  two new untracked items: `FullTime.Api.Sandbox/` (a whole new project) and
  `FullTime.App.Shared/Components/AccordionSection.razor`. See §3 for what's in them.
- **`FullTime.App/FullTime.App/Services/ApiConfig.cs` currently points at the sandbox
  (`http://34.23.16.148:5299`), not production (`:5199`).** This is deliberate for this session's
  testing but **must be reverted before any real build/release** — the sandbox has no auth, no bet
  placement, no leagues.
- **`FullTime.App.Shared/Services/LeagueCatalog.cs` has temporary API-Football league IDs mixed
  into `AlwaysVisible`/`Names`/`LogoUrl`** (39/481/965/192, alongside the real Highlightly IDs) so
  sandbox data renders. **Revert alongside `ApiConfig.cs`.**
- **FA Cup (Highlightly ID `39079`) is tracked again in code** (`HighlightlyLeagueMap.cs`,
  `LeagueCatalog.cs`) after being removed earlier this session — **but not redeployed to
  production**, and the bug that caused its removal is still unfixed (see §3.1). Production's
  `FullTime.Api` is still running the FA-Cup-removed build from earlier in this session.
- **Production Highlightly quota**: was still stuck in a 15-minute exhaustion/retry loop as of the
  last check this session (exhausted ~01:43 UTC 2026-09-06). **Not rechecked since — check current
  status before assuming it has cleared.**
- **New isolated sandbox stack is live on `fulltime-vm`**, fully separate from production (see
  §3.2): systemd service `fulltime-sandbox` on port `5299`, Postgres DB `friendsacca_sandbox`,
  firewall rule `fulltime-allow-sandbox`. Safe to keep experimenting against; nothing there can
  touch real user data.
- **Android/iOS store-submission state, keystore, AdMob, codemagic.yaml**: unchanged since the
  previous session — see §2. Nothing here was touched this session.

---

## 2. Earlier work (stable, predates this session — see git log/older handovers for detail)

Daily Spinner rebuilt server-authoritative, league invite + privacy pages, Android package rename,
UMP consent flow, Play Store listing assets, App Store Connect prep, test accounts, three Android
layout/asset bug fixes (nav-bar collision, icon safe-zone clipping), first Play Store closed-testing
release + release keystore generated (gitignored, back it up off-machine), AdMob still on test IDs
by design. Outstanding items from that work are folded into §4 below. Not re-detailed here — see
file history if needed.

**Also investigated (not built): showing goal scorers/cards on a match's detail view.** Confirmed
Highlightly's `football/events/{id}` has the data (player/assist/card fields) but the app discards
everything except one settlement enum. Agreed approach if ever built: on-demand fetch when a user
opens a match (not background polling), cached server-side. See git history for full detail if
picked up.

---

## 3. This session's work (2026-09-06)

### 3.1 FA Cup live-score incident → RapidAPI quota exhaustion (root cause still unfixed)

User reported FA Cup matches showing no live score. Investigation found the real story was much
bigger: production had already burned its entire day's RapidAPI/Highlightly quota overnight.

- **Root cause, confirmed via VM logs + DB**: `HighlightlyMatchSyncService.DeriveStatus`
  (`FullTime.Api/BetBuilder/HighlightlyMatchSyncService.cs`) silently defaults any unrecognized
  match-status string to `InProgress`. Something in a 110-match FA Cup preliminary-round batch
  (kicked off simultaneously) returned a status string this didn't recognize, pinning the live-sync
  loop at its 20-30s cadence for 11+ hours overnight instead of backing off once matches actually
  finished — burning the whole day's quota.
- **Symptom fix applied and deployed to production**: FA Cup removed from
  `HighlightlyLeagueMap.TrackedLeagueIds` and `LeagueCatalog`. **This does not fix the underlying
  bug** — any other competition (or FA Cup once re-added) can trigger the same incident again.
- **FA Cup was re-added in code later this session** at user request (both files), but **not
  redeployed** — pending a decision on whether to fix `DeriveStatus` first (recommended) or accept
  the risk.
- **Not done**: fixing `DeriveStatus` itself (e.g. logging/rejecting unrecognized statuses instead
  of defaulting to `InProgress`).

### 3.2 Evaluated API-Football + the-odds-api as Highlightly replacements/additions

User wants to know whether swapping providers would fix the quota problem and unlock features
Highlightly doesn't have (player props). Built `FullTime.Api.Sandbox` — a **brand new, fully
isolated** project (own systemd service, own DB, own firewall rule) to test this without any risk
to production. See §3.3 for what it contains.

**Findings:**
- **the-odds-api.com**: real player-prop markets confirmed working (anytime goalscorer, cards,
  shots on target, assists, corners, correct score — see below). Cost = 1 request per
  (market × region) per event. **Not a live-score feed** — no minute/clock data, only pre-match
  odds and eventual final result. Free tier used for testing: 500 requests/month (down to ~436
  remaining by end of session — check before further testing).
- **api-football.com**: `GET /fixtures?live=all` returns **every live match worldwide in one call**
  — a fundamentally cheaper model than Highlightly's one-call-per-league approach, and would
  directly solve the quota-multiplication problem. Two access channels exist (RapidAPI and a direct
  `dashboard.api-football.com` key) — **only the direct channel was usable**; the account's existing
  RapidAPI key wasn't subscribed to API-Football there. Free tier blocks current-season
  fixture-discovery entirely (`/fixtures?league=&season=` — only `/live=all` works); **user
  subscribed to PRO ($19/mo, 7,500 req/day, 300 req/min)**, confirmed working, current-season
  fixture-discovery now unblocked. Cost math: PRO comfortably supports 10-15s live polling with
  5-6x headroom even on the busiest realistic Saturday.
- **Widgets** (api-football.com's embeddable JS widgets): real, but the API key goes into a client-
  side HTML `data-key` attribute — visible to every viewer, and every viewer's browser calls the API
  directly against the same shared quota. Not evaluated further; flagged as a real trade-off if ever
  revisited, not a drop-in.
- **Team-name reconciliation**: the-odds-api gives only plain team-name strings, no ID shared with
  API-Football. Built `TeamNameMatcher` (token-Jaccard + Levenshtein + a subset-containment bonus
  for short-vs-full-name mismatches like "Brighton" vs "Brighton and Hove Albion") — validated at
  **11/11 (100%)** against real Premier League fixtures.
- **Player-team grouping**: the-odds-api gives no roster data either. Resolved via API-Football's
  `/players/squads?team={id}` endpoint, matched by **surname only** (API-Football abbreviates first
  names — "J. Pickford" vs the-odds-api's "Jordan Pickford"). ~86% match rate on a real squad; the
  rest are genuine data gaps (recent transfers not yet in the squad endpoint, e.g. Sesko, Grealish,
  de Ligt), not matching bugs — these are simply dropped rather than shown in a fake "Unmatched"
  bucket.
- **Markets confirmed real/working**: `h2h` (1X2), `alternate_totals` (total goals, **not** `totals`
  — that key only returns one "featured" line, `alternate_totals` returns the full set), `btts`,
  `alternate_totals_corners` (total match corners — **not** available per-team;
  `alternate_team_totals_corners` was checked and isn't actually priced by any bookmaker),
  `correct_score` (packed as `"Everton:1|Manchester United:0"`, parsed into home/away scores),
  `player_goal_scorer_anytime`, `player_to_receive_card`, `player_shots_on_target`,
  `player_assists`. **Confirmed NOT available**: any team-level "first team to score" market (only
  a player-level "first goal scorer" one exists, a different concept — not wired up).

### 3.3 `FullTime.Api.Sandbox` — new isolated evaluation project

Not part of any `.sln` (none exists in this repo); built/deployed standalone like every other
project here. Deployed to `fulltime-vm`:
- systemd service `fulltime-sandbox`, port `5299`, DB `friendsacca_sandbox`, firewall rule
  `fulltime-allow-sandbox` (all separate from production).
- `Services/ApiFootballClient.cs`, `Services/OddsApiClient.cs` — thin wrappers, return rate-limit
  headers alongside data so quota is visible without tailing VM logs.
- `Services/TeamNameMatcher.cs` — see §3.2.
- `Controllers/TestController.cs` — manual, on-demand endpoints only (deliberately **no background
  timers**, per earlier "don't poll" feedback): `/test/live-sync`, `/test/fixture-discovery`,
  `/test/matches`, `/test/odds-sports`, `/test/odds-events`, `/test/odds`, `/test/fuzzy-match`,
  `/test/player-props` (POST), `/test/match-markets` (POST), `/test/player-props/{matchId}`.
- `Controllers/AppCompatController.cs` — mimics the real `FullTime.Api`'s `/api/matches/upcoming`
  and `/api/matches/{id}/bet-builder-markets` exactly, so the **real** `FullTime.App.Shared` UI
  renders against it unmodified (this is what `ApiConfig.cs` currently points at).
- `Models/SandboxMatch.cs`, `Models/SandboxPlayerPropMarket.cs` — the latter doubles as storage for
  both per-player props and match-level markets (h2h/totals/btts/corners/correct-score) rather than
  two near-identical tables.
- **Test fixture used throughout**: Everton vs Manchester United, match id
  `8c77b28e-5b68-4ceb-9d9a-014d4ea1f225`, the-odds-api event id
  `8ea6c863f1852654399073b518b0a1f8`, sport key `soccer_epl`. API-Football team ids: Everton `45`,
  Man Utd `33`; league id (Premier League) `39`.
- **Real bug found and fixed**: repeated test calls were silently accumulating duplicate rows (no
  upsert/replace), and `AppCompatController`'s "oldest row wins" grouping was serving stale data
  back to the app. Fixed with `ExecuteDeleteAsync` before each re-fetch.
- **Real bug found and fixed**: API-Football HTML-encodes some squad names (`"J. O&apos;Brien"`) —
  needed `WebUtility.HtmlDecode` before surname comparison.

### 3.4 Real app changes — Bet Builder redesign

Changes to `FullTime.App.Shared` are real/permanent (not sandbox-only), verified end-to-end in the
`FullTime_Pixel8_API35` emulator against the sandbox:
- `BetBuilder.razor` — restructured: team crests either side of kickoff time, two tabs ("Popular"
  for existing standard markets, "Player Bets" for the-odds-api player props), every market section
  now a collapsible accordion (new `Components/AccordionSection.razor`, all collapsed by default),
  player-prop sections grouped into nested per-team accordions, new "Total corners" section, header
  always reads "Bet Builder" literally (not the league name).
- `BetSlipState.cs` / `Models/ApiModels.cs` — `SlipPick`/`BetBuilderMarketDto` gained optional
  `PlayerName` and `Team` fields.
- `Components/MatchCard.razor` — `BetBuilderHref()` now passes league name + team logo URLs
  through as query params (real improvement, used by the new header).
- `wwwroot/app.css` — new classes for the above (`.bb-teams-row`, `.bb-tabs`, `.accordion-*`,
  `.player-table`). A `.bb-toggle`/`.bb-toggle-row` pair is now unused (a "Dynamic Odds" toggle was
  added then removed per user request) — safe to delete if noticed.
- **Not done**: a reference screenshot the user wanted matched (`Downloads/Image (2).jpg`) could
  never be viewed — every attempt hit an image-size/context limit. Ask the user to describe it in
  words rather than re-attempting the same file.

### 3.5 Bet Builder UI polish (later the same session, after a reference screenshot was successfully
viewed inline in chat — the `Downloads/Image (2).jpg` issue in §3.4 was a different, unrelated file)

Also real/permanent changes to `FullTime.App.Shared`, verified end-to-end in the emulator against
the sandbox:
- `BetBuilder.razor` — Total corners moved to the top of the Popular tab (was last). Shots on
  target / Player assists (the two lined player-prop markets) now render as a "1+/2+/3+" table per
  team (new `PlayerPropRow`/updated `TeamPropGroup` records, `LineLabel` helper converting an
  Over-X.5 line to an "(X+1)+" column header) instead of a flat "PlayerName (Over 1.5)" list row per
  line — matches a real bookmaker reference screenshot the user shared. Anytime goalscorer/Player
  card are unchanged (single Yes/No price, no natural column axis for a table).
- `wwwroot/app.css` — `.player-table` (previously unused, prepped-but-not-wired CSS from earlier in
  the session) is now actually used; added horizontal cell padding (was 0, which caused adjacent
  odds columns to visually run together with no gap, e.g. "4/5333/100" reading as one garbled
  number — a real bug, not just a font-size issue) and a `.locked` cell style for lines with no price
  (rendered as "—", no lock-icon asset exists in the app). `.accordion-body` font-size dropped to
  0.85rem (cascades to everything inside an expanded accordion), `.correct-score-price`'s hardcoded
  1rem size dropped to match since it wouldn't otherwise inherit.
- Confirmed via emulator: **First team to score** legitimately does not appear in the sandbox's
  Popular tab — not a bug. The-odds-api has no team-level "first team to score" market at all (only
  a *player*-level "first goal scorer" concept, a different market never wired up anywhere), so
  `FullTime.Api.Sandbox` never populates it. That section only ever had real data from Highlightly
  in production; testing it needs production's real API, not the sandbox.

### 3.6 Production provider-cutover plan (designed, accepted, NOT implemented — parked for the day)

Owner upgraded the-odds-api subscription to 20,000 req/month and asked to wire both new providers
into real production `FullTime.Api` (currently 100% Highlightly-sourced). Given the size (schema
changes, a new live-score sync service, settlement logic), this went through full plan-mode
research (2 Explore agents mapping production architecture + everything the sandbox already proved,
1 Plan agent designing the cutover) rather than being implemented ad hoc. **The accepted plan is
saved at `C:\Users\alan.browne\.claude\plans\cached-sparking-pretzel.md`** — read that file first
before touching any of this. Owner explicitly said: accept the plan, but no code changes today.

Decisions locked in (do not re-litigate without asking):
- **Full scope in one plan**, not a narrower first slice: fix `DeriveStatus`'s root cause, cut live
  scores over to API-Football (`fixtures?live=all`, one call for every live match worldwide vs
  Highlightly's one-call-per-tracked-league model), cut markets/player-props over to the-odds-api
  (adds `TotalCorners` + the 4 player-prop `MarketType` values, all already rendered by the client).
- **Shots-on-target/assists settlement is gated by an unverified-endpoint spike** — pricing is easy
  (proven in the sandbox), but settling needs real per-player post-match stats which nothing has
  ever actually fetched (likely `GET fixtures/players?fixture={id}`, unconfirmed). Anytime-
  goalscorer/card settle off match events instead (same mechanism as existing `FirstTeamToScore`)
  and aren't blocked by this.
- **Caching model for the-odds-api (both the match-list's `h2h` odds and the Bet Builder page's full
  9-market pull) ended up much simpler than an initial TTL-based design**: fetch on-demand
  (triggered by an actual view, never a background timer — this is the same "on-demand fetch, not
  more polling" preference already recorded from an earlier session, see `[[feedback-no-polling-quota]]`
  in Claude's memory) until every required market is present for that match, then **never refetch it
  again** for the rest of its life (capped by a small attempt limit so a match that will simply never
  get full bookmaker coverage — a lower-league fixture, an early cup round — doesn't get re-hit on
  every single view forever). Combined with "never refetch once a match is no longer `Upcoming`"
  (confirmed via `BetService.PlaceBetAsync` — nobody can place a new bet on a match that's already
  kicked off anyway), this bounds total the-odds-api usage to roughly once per match, ever, rather
  than any repeating cadence — comfortably inside the 20,000/month budget even under pessimistic
  assumptions (see the plan file's quota sanity-check math).
- **Highlightly's code stays in place behind a `Providers:LiveScoreSource`/`Providers:MarketsSource`
  config toggle** for rollback-by-config-flip-and-restart rather than a code revert, until the new
  pipeline has proven itself in production.
- A real, previously-unflagged bug the planning surfaced: `BetService.GetCurrentOddsAsync`
  (`FullTime.Api/Betting/BetService.cs:149-180`) looks up a priced market by
  `(MatchId, MarketType, Line, Side)` — none of these include player identity, so once two different
  players are both priced e.g. "Over 0.5 shots on target" the lookup is ambiguous. Must be fixed
  (`LegPickInput`/`PickRequest`/`BetLegPickDto` all need a `PlayerName` field threaded through) as
  part of this work, not treated as a pre-existing issue to leave alone.
- The plan's step 1 (the `DeriveStatus` fix itself) already has the exact diff spelled out — a good
  candidate to implement first and independently next session, since it's zero-schema-risk and fixes
  the actual incident regardless of whether the rest of the cutover ever happens.

**Not started**: no production code has been touched for any of this. The new the-odds-api key the
owner shared in chat this session (not repeated here — see chat history, never write API keys into
this file) has **not** been placed anywhere yet — it needs to go into whatever VM-side secret
mechanism currently injects `Highlightly:ApiKey` onto `fulltime-vm` once implementation actually
starts (see plan §2), never committed to the repo.

---

## 4. Outstanding tasks

**From this session, roughly in priority order:**
1. **Decide on `HighlightlyMatchSyncService.DeriveStatus`** — the actual root cause of the quota
   incident (§3.1) is still unfixed. Recommend fixing before redeploying FA Cup.
2. **Decide whether/when to redeploy FA Cup to production** (`HighlightlyLeagueMap.cs`,
   `LeagueCatalog.cs` already have it re-added in code, unpushed).
3. **Check current Highlightly quota status** on production — unknown as of this handover.
4. **Revert `ApiConfig.cs` and `LeagueCatalog.cs`'s temporary sandbox additions** before any real
   build/testing/release (§1).
5. **Decide on a real integration path** if the API-Football/the-odds-api evaluation is judged
   worth it — nothing from `FullTime.Api.Sandbox` is wired into production `FullTime.Api`; it's
   purely a proof-of-concept so far. Would need: real `MarketType` enum values + a player-identity
   column on `BetBuilderMarket`/`BetLegPick`, settlement logic for player props (needs real
   per-player match-event data), and a decision on whether to keep Highlightly at all.
6. Optional: add a player-level "first goal scorer" market (5th player-prop type, same pattern as
   the other four) — confirmed available, not yet built.
7. Commit this session's work (nothing is committed yet) — review the diff first given how much
   changed, and decide whether `FullTime.Api.Sandbox/` should be committed as-is or trimmed.
8. Get the reference screenshot described in words (§3.4) if that comparison still matters.

**Carried over from earlier sessions, still open:**
9. Confirm the Play Console closed-testing release was finished (testers added, rolled out).
10. Confirm the release keystore + password (`signing/fulltime-upload.jks`) are backed up off this
    machine.
11. Confirm UMP consent/package rename work on iOS (unverified, no Mac — needs a Codemagic build).
12. Diagnose why `adb` never detects the physical Android phone over USB (workaround: Codemagic
    email-a-debug-APK).
13. Decide on the `test@jtmtechnology.co.uk` production test account (leave or clean up).
14. Stray untracked ~100MB `com.jtmtechnology.fulltime.app-Signed.apk` at repo root — origin still
    unclear, probably safe to delete.
15. Finish App Store Connect submission (no build uploaded yet, Age Suitability questionnaire).
16. Once live on both stores: swap in real AdMob IDs, generate `remove_ads` promo codes.
17. Real Apple 1024×1024 (no-alpha) icon export — not yet produced.
18. TestFlight beta review 422 — not confirmed resolved.
19. Stale `free-api-live-football-data`-retirement plan at
    `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md` — untouched.
20. Cloudflare purge-on-deploy to replace the `styles.css?v=N` cache-busting workaround.

---

## 5. Gotchas discovered this session (in addition to `CLAUDE.md`'s environment quirks)

- **The-odds-api's "featured" vs "alternate" market keys are genuinely different data, not just
  naming** — `totals`/`h2h` etc. return only one bookmaker-chosen line/price; the `alternate_*`
  version of the same market returns the full set. Confirmed this the hard way (Total Goals/corners
  both silently showed only one option until switched to `alternate_totals`/
  `alternate_totals_corners`).
- **Repeated test/dev calls into a table with no natural upsert key will silently accumulate
  duplicates** — always delete-before-insert (or `ExecuteDeleteAsync`) when an endpoint is meant to
  be re-run, or a later read (e.g. "oldest row wins" grouping) can serve stale data while looking
  like a completely different bug (this cost real debugging time before the actual cause was
  found).
- **API-Football HTML-encodes some data** (e.g. `&apos;` in squad player names) where the-odds-api
  doesn't — decode before comparing strings across the two providers.
- **Cross-provider player/team names never match exactly** — API-Football abbreviates first names
  (`J. Pickford`), the-odds-api doesn't (`Jordan Pickford`); team names also drift (`Brighton` vs
  `Brighton and Hove Albion`). Surname-only / subset-containment-bonus matching gets ~85-100%; the
  remainder is usually genuine data staleness (recent transfers), not a matching bug — worth
  checking directly (e.g. query the provider's own squad/roster endpoint) before assuming the
  matching logic is wrong.
- **GCP firewall rules are scoped per-port, not per-VM** — a new service on a new port needs its own
  `gcloud compute firewall-rules create` even if other ports are already open; testing only via SSH
  (localhost) will never surface this, only an external caller (e.g. the emulator) will.
- **The Android SDK on this machine lives at `C:\Program Files (x86)\Android\android-sdk`**, not the
  usual `%LOCALAPPDATA%\Android\Sdk` default — `adb`/`emulator` binaries are under
  `...\android-sdk\platform-tools\` and `...\android-sdk\emulator\`.
- **Emulator boot polling**: `adb shell getprop sys.boot_completed` in a short PowerShell loop after
  launching the emulator is reliable; don't assume a fixed sleep duration is enough.
- **Screenshot coordinate scaling**: when a screenshot is displayed scaled down (e.g. "displayed at
  900x2000, multiply by 1.20"), that scale factor applies to *every* subsequent `adb shell input
  tap`/`swipe` coordinate computed from it — easy to forget on a later tap and hit the wrong
  element (happened once this session; the accordion just silently didn't toggle).
- **Reading a user-supplied image can fail even when its own pixel dimensions are small**, if the
  conversation already has many images in context — the failure isn't always about the individual
  file. Don't keep retrying the same file at smaller sizes; ask the user to describe it instead
  once resizing hasn't helped.
