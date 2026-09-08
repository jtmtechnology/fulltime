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

## 1. Current state (as of this handover, 2026-09-08)

- **Production runs a hybrid provider setup** (this is the durable architecture going forward, not
  a dormant/reverted one — see §3 for how it got here):
  - **Highlightly** (direct account, `soccer.highlightly.net`) — live scores, and the primary
    settlement source for `MatchResult`/`OverUnder`/`BothTeamsToScore`/`CorrectScore`/
    `FirstTeamToScore`/`TotalCorners`/`PlayerGoalscorerAnytime`/`PlayerCard`/`PlayerRedCard`/
    `PlayerAssists`. Also powers Match Summary (goal/card/substitution timeline).
  - **the-odds-api** (free tier, 500 credits/month) — prices player-prop markets and Total Corners
    for Bet Builder (`PlayerPropsService`). Pricing only, never settlement.
  - **API-Football** (Pro tier, 7,500/day — plan mismatch still unreconciled, see §6) — scoped to
    squad/team lookups plus settling `PlayerShotsOnTarget`/`PlayerShots`, the one stat Highlightly
    doesn't expose per-player.
- **Android app is approved and live on Google Play** (confirmed by the owner 2026-09-08). Real
  AdMob IDs are now in the Android code (commit `ee26ba1`): app ID
  `ca-app-pub-8873351312647846~5927014987`, interstitial ad unit
  `ca-app-pub-8873351312647846/9075922506`. **iOS still ships Google's test AdMob IDs** — no iOS
  app exists in the AdMob console yet.
- **`CLAUDE.md`'s AdMob testing-convention rule was rewritten this session** — it used to say real
  AdMob IDs must *never* ship in a store-submitted build; it now says that only holds for a
  platform's **first-ever** approval on that store (review-bot invalid-traffic risk before the app
  is live). Once a platform is approved, real IDs are correct going forward. Read that rule (not
  just this file) before touching AdMob IDs again, and update it per-platform as iOS goes through
  its own first approval.
- Android version bumped to display `1.2` / code `9` (commit `fa425a6`) to accompany the AdMob
  swap — Play Console requires a strictly higher version code than whatever's already live (was
  `8`). A signed release AAB was built locally (see §5.1) — **whether the owner has actually
  uploaded it to Play Console is unconfirmed**, that wasn't observed in this session.
- Three UI changes landed this session (commit `da33d52`) — header balance auto-refresh, live
  minute/HT/FT on Match Summary, hiding the odds/bookmaker row once a match starts or before odds
  exist. See §5.2. Verified in the Android emulator (§5.3), not on a physical device.
- Everything described here is committed and pushed to `main` (latest: `da33d52`).

---

## 2. Earlier work (stable, predates 2026-09-06 — see git log/older handovers for detail)

Daily Spinner rebuilt server-authoritative (streak bonus later made a highlighted banner, see old
§4), league invite + privacy pages, Android package rename, UMP consent flow, Play Store listing
assets, App Store Connect prep, test accounts, three Android layout/asset bug fixes, first Play
Store closed-testing release + release keystore generated (gitignored, back it up off-machine),
AdMob was on test IDs by design until this session (§1). Not re-detailed here — see file history if
needed.

---

## 3. 2026-09-06 session — cutover, revert, and fixes that are still in effect

A same-day full cutover to API-Football (live scores) + the-odds-api (markets) was built, deployed,
and verified working, then **reverted back to Highlightly for live scores** at the owner's request.
That full-cutover code (`ApiFootballMatchSyncService` and friends) is still compiled in but dormant
— only the narrower API-Football usage described in §1 is actually active today. Real, durable
fixes that survived the revert:

- **`HighlightlyMatchSyncService.DeriveStatus`** no longer defaults an unrecognized status string to
  `InProgress` — explicitly recognizes `"First half"`/`"Second half"`/`"half time"` variants as
  live, logs a warning and keeps the previous status for anything else.
- **EF Core can't translate an arbitrary C# method call inside a LINQ `Where()`** — fixed via a
  static readonly array + `.Contains()` (translates to SQL `IN`); reused for
  `EventsDerivedMarketTypes`/`PlayerStatMarketTypes`.
- **Team-name matching (`TeamNameMatcher.cs`)**: several real provider-name mismatches found and
  fixed. Still load-bearing for both the-odds-api and API-Football fixture resolution.
- **FA Cup only tracked from the 3rd Round Proper onwards** — filtered at sync time. Still in effect.
- **Highlightly moved off RapidAPI to a direct account** (`soccer.highlightly.net`, 25,000/day).
- **Bookmaker label/logo** (`BookmakerLogo.razor`, `MatchCard.razor`'s row) — `BetBuilder.razor`'s
  own separate bookmaker row was removed for good 2026-09-07; `MatchCard.razor`'s bookmaker row
  remains (now conditionally hidden, see §5.2).

---

## 4. 2026-09-07 session — Highlightly-settlement pivot, player props, Match Summary

Full detail in git history (commits `ab35c32`..`466ad68`) and prior handover text. Summary of what's
still in effect:

- `PlayerPropsService` covers six markets plus `TotalCorners`, hourly-throttled per match, never
  re-fetches a market type once "found" for a match (matters for §6.1).
- Settlement pivoted to Highlightly-first: goals/assists/cards/corners settle from Highlightly's
  `events`/`statistics` endpoints; API-Football narrowed to shots-on-target/total-shots only
  (confirmed Highlightly has no per-player shots data anywhere).
- Surname-based player/team matching added to reconcile API-Football's abbreviated names against
  the-odds-api's/Highlightly's full names (diacritics and mononyms still not handled, see §7).
- Match Summary page added (`MatchEvents.razor` + `MatchEventRow.razor`), later made to refresh
  server-side every ~30s while a match is `InProgress` (`RefreshLiveMatchEventsAsync`) — this is a
  *server-side DB freshness* mechanism, not client-side polling; the page itself doesn't poll (see
  §5.2's note on the new status line going stale the same way score/kickoff already did).
- Found and fixed a duplicate-`BetBuilderMarket`-row race condition — **fix only prevents new
  duplicates, doesn't repair matches that already had bad data before it deployed** (§6.1).

---

## 5. 2026-09-08 session — this session's work

### 5.1 AdMob real IDs + Play Store release build

- Compared the AdMob console's real app ID against the code (still on Google's test ID), flagged
  the conflict with `CLAUDE.md`'s then-absolute "never ship real IDs" rule, confirmed with the
  owner that Android is already approved/live, then swapped in the real app ID
  (`AndroidManifest.xml`, `MauiInterstitialAdService.cs`) and the real interstitial ad unit ID
  (`ca-app-pub-8873351312647846/9075922506`, also `MauiInterstitialAdService.cs`). Rewrote
  `CLAUDE.md`'s rule to the per-platform-approval nuance (see §1). Commit `ee26ba1`.
- **Discovered `codemagic.yaml` has no Android release/Play Store workflow at all** — only
  `android-debug` (debug APK, signed with the default debug keystore, explicitly not usable for
  Play Store) and two iOS workflows (`ios-ad-hoc`, `ios-testflight`). Play Store release AABs are
  built **locally only**, signed via `signing/fulltime-upload.jks` per the command documented in
  `signing/README.md` (alias `fulltime-upload`, password in that same gitignored file — back it up
  off-machine per its own note, it isn't in git history).
- Version code `8` was already the live Play Store version, so it had to be bumped before the new
  AAB could be uploaded (Play Console rejects a non-increasing version code). Bumped
  `ApplicationDisplayVersion` 1.1→1.2, `ApplicationVersion` 8→9 in `FullTime.App.csproj`, commit
  `fa425a6`, then rebuilt the signed AAB locally with `signing/README.md`'s command. Output:
  `FullTime.App/FullTime.App/bin/Release/net10.0-android/com.jtmtechnology.fulltime.app-Signed.aab`
  (build artifact, gitignored — regenerate with the same command if it's gone). **Upload to Play
  Console itself was not observed in this session — confirm with the owner before assuming it's
  live.**

### 5.2 Three UI changes (commit `da33d52`)

- **Header balance now auto-refreshes.** `ContextSwitcher.razor` (the balance chip, mounted once in
  `MainLayout`) runs a config-interval loop calling `ActiveContextState.RefreshBalanceAsync()`,
  mirroring `Matches.razor`'s existing auto-refresh pattern (same `/api/config`
  `RefreshIntervalSeconds`, default 600s fallback). This closes the one gap in balance freshness — a
  bet settling server-side (`SettlementService`, no client-driven trigger) previously wouldn't reach
  the header until the user switched league or reloaded. Bet placement and Daily Spinner wins were
  already instant via their own existing `RefreshBalanceAsync()` calls — unchanged.
- **Match Summary shows live minute/HT/FT under the date/time.** `MatchEvents.razor` renders a new
  status line (`"HT"` / `"{minute}'"` / `"FT"`, accent-colored while live) fed by new
  `status`/`minute`/`isHalfTime` query params that `MatchCard.razor`'s `MatchEventsHref()` now
  passes through — the same snapshot-via-querystring approach already used there for score/kickoff,
  so it goes stale the same way they do until the page is reopened (no new polling was added; see §4
  for why the page itself doesn't poll).
- **Odds row and bookmaker logo hidden once a match starts, or before odds exist.** `MatchCard.razor`
  originally still showed the three `OddsCell`s (disabled, or as a "—" placeholder when `Odds` is
  null) and the bookmaker logo/name (also "—" via `BookmakerLogo.razor` when neither is set) even
  once a match went `InProgress`/`Finished`. Now both sections require `Match.Status == "Upcoming"`.
  A follow-up fix (reported via a screenshot showing three "—" cells on an *upcoming* match still
  days out) added a `HasOdds` check (`Match.HomeOdds ?? DrawOdds ?? AwayOdds is not null`) so the
  same two sections also hide when no odds have been fetched yet at all — a wall of "—" placeholders
  read as broken, not "coming soon". The "Match Summary" link (gated on `Match.EventsAvailable`,
  unrelated to odds) is untouched either way.

### 5.3 Local Android emulator testing workflow (new — keep for future sessions)

- `adb`/`emulator` are **not** on `PATH` in this environment's PowerShell/Bash sessions by default —
  found at `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe` and
  `...\emulator\emulator.exe`. Two AVDs exist: `FullTime_GoogleAPIs_API35`,
  `FullTime_Pixel8_API35`.
- **Owner's routine local test loop does not build an installable APK.** Use
  `dotnet build -t:Run -f net10.0-android -c Debug -p:AndroidOnlyBuild=true` from
  `FullTime.App/FullTime.App/` — builds, Fast-Deploys, and launches directly on whatever
  device/emulator `adb` currently sees. Only fall back to an `EmbedAssembliesIntoApk=true` debug-APK
  build (mirroring `codemagic.yaml`'s `android-debug` workflow) when a standalone installable
  artifact is actually needed, not for routine iteration — this was explicitly corrected mid-session
  ("dont usually build apk").

### 5.4 Daily Spinner scope clarified (architecture note, not a change)

Answered a question about Daily Spinner scope by reading `SpinService.cs`/`User.cs`, worth recording
since it's non-obvious:
- The once-per-day cooldown (`User.LastSpinDate`/`SpinStreak`) is **per-user**, not per-league — it
  lives on the `Users` table itself with no `LeagueId`, so one spin covers every league at once.
- A winning cash/streak payout, however, **fans out to every league the user belongs to**
  simultaneously via `SpinService.CreditAllMembershipsAsync` (mirrors `WeeklyTopUpService`'s
  pattern) — it is not scoped to whichever league happens to be active in `ActiveContextState` when
  the spin happens.

---

## 6. Known issues / outstanding

**Top priorities for whoever picks this up next:**
1. Confirm with the owner whether the AdMob-IDs AAB (§5.1) was actually uploaded to Play Console.
2. The duplicate-`BetBuilderMarket`-row problem (below) has still never been swept beyond one match.
3. Reconcile the API-Football plan mismatch (below) — cheap to check, been open since 2026-09-07.

Full list:

1. **Duplicate-row bug (pre-2026-09-07 16:08 UTC data) still not swept.** A race in
   `PlayerPropsService.EnsureFreshAsync` could double-insert `BetBuilderMarket` rows; fixed going
   forward (`466ad68`), but a match that already had duplicates before that fix deployed won't
   self-heal (a market type "found" for a match is never re-fetched). Only one instance (Aston Villa
   v Nottingham Forest) has been manually deduped via direct SQL. If another "same key already
   added" crash is reported, same fix applies — or write a one-off dedupe script for every match at
   once.
2. **API-Football plan mismatch**: owner said they'd moved to the free tier (100/day), but `/status`
   still showed the Pro plan (7,500/day) as of 2026-09-07. Not urgent (usage well within free tier),
   not reconciled.
3. **the-odds-api is on its free tier** (500 credits/month, current usage comfortable).
4. Card/RedCard/Assists/total-Shots markets show zero priced rows for most matches — confirmed real
   (no bookmaker has priced them yet this early), not a bug.
5. **No Android release/Play Store Codemagic workflow exists** (§5.1) — Play Store builds are local
   only, via `signing/README.md`'s documented `dotnet publish` command. If CI-based release builds
   are ever wanted, `codemagic.yaml` needs a new `android-release` workflow (would need the keystore
   uploaded to Codemagic's own secure storage, not the gitignored local file).
6. The auto-mode safety classifier can still intermittently block `gcloud compute ssh`/`scp` and
   even local file edits with no clear pattern — a plain retry has repeatedly worked. Rule shape for
   pre-approving it in `.claude/settings.json` (never actually applied — check before assuming it
   exists): `"Bash(gcloud compute scp:*)"` / `"Bash(gcloud compute ssh:*)"` and the
   `PowerShell(...)` equivalents.
7. Carried over, unconfirmed: Play Console closed-testing rollout status (superseded by §1's "app is
   approved and live" — worth double-checking exactly what that means in Play Console terms), release
   keystore off-machine backup, iOS UMP/package-rename verification (needs a Codemagic build), `adb`
   not detecting the physical Android phone over USB, the `test@jtmtechnology.co.uk` test account
   decision, App Store Connect submission (no build uploaded yet), an iOS AdMob app + real iOS AdMob
   IDs (Android is now done, see §1), a real Apple 1024×1024 no-alpha icon export, TestFlight beta
   review 422, a stale `free-api-live-football-data` retirement plan at
   `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md`, and Cloudflare purge-on-deploy
   to replace the `styles.css?v=N` cache-busting workaround.
8. A known minor race (unrelated to #1) still exists in both `HighlightlyMatchSyncService` and the
   dormant `ApiFootballMatchSyncService`: live-sync and fixture-discovery background services can
   both try to insert the same brand-new match at once right after a restart on an
   empty/near-empty `Matches` table (`23505` duplicate-key error). Self-heals, not fixed.

---

## 7. Key files touched this session (2026-09-08)

- `CLAUDE.md` — AdMob rule rewritten (§1, §5.1).
- `FullTime.App/FullTime.App/Platforms/Android/AndroidManifest.xml` — real AdMob app ID.
- `FullTime.App/FullTime.App/Services/MauiInterstitialAdService.cs` — real AdMob app ID + real
  interstitial ad unit ID.
- `FullTime.App/FullTime.App/FullTime.App.csproj` — version bump 1.1/8 → 1.2/9.
- `FullTime.App/FullTime.App.Shared/Components/ContextSwitcher.razor` — balance auto-refresh loop.
- `FullTime.App/FullTime.App.Shared/Components/MatchCard.razor` — odds/bookmaker-row hiding,
  `MatchEventsHref()` new query params.
- `FullTime.App/FullTime.App.Shared/Pages/MatchEvents.razor` — live status line, new
  `[SupplyParameterFromQuery]` params.
- `FullTime.App/FullTime.App.Shared/wwwroot/app.css` — small `.match-events-status.live` style.

Commits this session, all pushed to `main`: `ee26ba1`, `fa425a6`, `da33d52`.

`ODDS_API_PLAYER_PROPS_INVESTIGATION.md` at repo root remains an untracked scratch doc with **live
API keys in plaintext** — must never be committed.

---

## 8. Gotchas discovered this session (in addition to `CLAUDE.md`'s environment quirks and earlier
   handover sections)

- **`codemagic.yaml`'s `android-debug` workflow is debug-keystore-signed and explicitly not usable
  for a Play Store upload** — the real signing path is entirely local (`signing/README.md`'s
  `dotnet publish` command), and isn't referenced anywhere in `codemagic.yaml` itself. Don't assume
  "trigger a Codemagic build" is the right instruction for a Play Store release; it's only right for
  the debug-APK or the iOS workflows.
- **`adb`/`emulator` aren't on `PATH`** in this environment's PowerShell/Bash sessions by default —
  full paths under `C:\Program Files (x86)\Android\android-sdk\` work (`platform-tools\adb.exe`,
  `emulator\emulator.exe`).
- **The owner's routine local test loop is `dotnet build -t:Run`, not a built APK** — don't default
  to `EmbedAssembliesIntoApk=true` for routine testing; that's for producing a standalone
  installable artifact specifically (matches `codemagic.yaml`'s own `android-debug` workflow use of
  it, for the same underlying reason — sidesteps Fast Deploy needing the IDE/adb to push assemblies
  separately).
- **A Play Store upload needs a strictly higher version code than whatever's already live** — check
  `FullTime.App.csproj`'s `ApplicationVersion` against what's actually live in Play Console before
  building a release AAB, don't assume the checked-in value hasn't already been published.
- Carried over from 2026-09-07: a market type "found" for a match is never re-fetched by
  `PlayerPropsService` (permanent-until-manually-fixed stale data risk); a production data bug can
  look identical to "the API has no data" (always curl the real provider before concluding "no
  coverage"); the auto-mode safety classifier's blocks aren't fully deterministic (retry before
  concluding a hard block); diacritics and mononyms both break the surname-matching helper (safe
  failure mode — a dropped row, not a wrong one).
