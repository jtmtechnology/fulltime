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

## 1. Current state (as of this handover, 2026-09-09)

- **Production infra is a single VM, `fulltime-vm` (GCP, `us-east1-b`, machine type `e2-micro` — 1
  vCPU burstable, 1GB RAM, GCP's Always Free tier shape).** It runs everything: `fulltime-api`
  (the API — and Postgres itself, via `postgresql.service` on the same box, confirmed self-hosted
  since `gcloud sql instances list` shows the Cloud SQL Admin API has never even been enabled on
  this project), `fulltime-web` (the `FullTime.App.Web` Blazor Server head), `fulltime-website`
  (the marketing site), and `fulltime-sandbox`. Only one Compute instance exists on the whole
  project. This is a single point of failure — see the outage below.
- **The VM hung and was reset today (2026-09-09, ~06:21–07:09 UTC).** Both the API port and SSH
  accepted a TCP handshake but never answered a real request; the VM's own serial console log
  showed normal activity (a live-sync tick, DB queries) at 06:21:07 UTC, then nothing at all until
  the owner ran `gcloud compute instances reset fulltime-vm --zone=us-east1-b` at ~07:09 — i.e. the
  whole box froze, not just the API process crashing. Most likely cause is memory pressure on the
  1GB VM, but this is **unconfirmed** — a hard reset wipes `dmesg`'s ring buffer, so no OOM-kill
  message survived to prove it. Org policy blocks Claude from ever rebooting a box that hosts a
  database (this one does) — any future hang needs the **owner** to run the reset, not Claude.
- **New: a free GCP Cloud Monitoring uptime check + email alert now watches the API**, closing the
  gap that let today's outage go unnoticed for a while. Uptime check `FullTime API health` hits
  `http://34.23.16.148:5199/api/config` every 5 minutes and checks the response body actually
  contains `refreshIntervalSeconds` (not just any 200). Alert policy `FullTime API down` fires on
  the first failed check and emails `alan@jtmtechnology.co.uk`. Both are within GCP's always-free
  allotment — no billing change. If hangs recur even with this in place, the next lever is bumping
  the VM off `e2-micro` (e.g. `e2-small`, ~£/$12-13/mo) — deliberately not done yet, since the free
  alert was the agreed first fix, not the paid resize.
- **Four real bugs were found and fixed this session** (all committed to `main`, commits
  `4a37ac0`..`a555aa3`, pushed). The weekly top-up fix is backend-only and **is now live**
  (`fulltime-api` was redeployed in §6.9 below, for an unrelated reason, which carried it along).
  The other three live in `FullTime.App.Shared` and are **not yet live for either Web or Android
  users** — see §7 item 1 for exact deploy status:
  1. New league members got the £10 weekly top-up immediately instead of waiting for the next
     Sunday (`FullTime.Api/Betting/WeeklyTopUpService.cs` read a brand-new membership's
     `LastTopUpDate == null` as "overdue"). Fixed by stamping `LastTopUpDate` at membership-creation
     time to the most recently completed Sunday cutoff.
  2. The Worldwide leaderboard tab only ever showed a snapshot from when the Leaderboard page first
     loaded — switching to it never re-fetched, unlike "My Leagues". Fixed in
     `FullTime.App.Shared/Pages/Leaderboard.razor`.
  3. Match Summary's event icon fell back to a generic bullet for anything other than
     Goal/Yellow Card/Red Card/Substitution — confirmed via the production `MatchEvents` table that
     Highlightly actually also sends `Penalty`, `Missed Penalty`, `Own Goal`,
     `VAR Goal Confirmed`, `VAR Goal Cancelled`, `VAR Penalty`. Added icons for all of them, plus
     "(Penalty)"/"(Penalty missed)" text next to the player's name.
  4. The Profile page's "Ads" card stayed visible after a real ads-removal purchase (or restore),
     just showing a "thanks for supporting FullTime" message instead of the purchase buttons.
     `Profile.razor` now hides the whole card once `AdsRemoval.AdsRemoved` is true.
- **Android push notifications now have a dedicated icon.** One never existed before — Firebase was
  falling back to the full-colour launcher icon, which Android then auto-derived into whatever
  silhouette it could. Generated a proper white-on-transparent icon per density
  (`drawable-{m,h,x,xx,xxx}hdpi/ic_stat_notification.png`) from the launcher icon's own alpha
  channel, wired up via `AndroidManifest.xml` meta-data plus a new `notification_accent`
  (`#2FAE4F`) color. Verified building and deploying cleanly to the emulator.
- **iOS's "old notification icon" report turned out to be device-side, not a code bug.** Confirmed
  with the owner: after a fresh iOS build the day before, the home screen icon was already correct
  and only notifications showed the old one — that's iOS/Springboard's own notification-icon cache
  not refreshing after an in-place TestFlight update, not anything in the repo (`appicon_ios.png`,
  `Info.plist`, and the push payload — title/body only, no image — were all confirmed already
  correct). No code change made. Fix is device-side: delete the app, restart the phone, reinstall.
- **Website's Google Play badge now links to the real listing** (`index.html`, `invite.html`) — was
  a placeholder `href="#"` since launch. App Store badge stays a placeholder until iOS has its own
  first approval. **This one IS deployed** — pushed to `fulltime-website` on the VM and confirmed
  live via curl.
- **New Android release built: version 1.3 / code 10** (bumped from 1.2/9 — the owner confirmed
  code 9 was already uploaded to Play Console). Signed AAB built locally per `signing/README.md`;
  bundles all three pending `FullTime.App.Shared` fixes above plus the notification icon.
  **Not yet uploaded to Play Console — still the owner's action**, see §7.
- **Highlightly's daily quota (25,000/day) was fully exhausted from ~05:46 UTC, and was STILL
  exhausted as of 15:29 UTC** (checked repeatedly across ~9h43m, zero successful calls the whole
  time) — killing live scores/odds for every tracked league, not just one match. Root cause found
  and fixed: see §6.9. This has now clearly outlasted a simple UTC-midnight daily reset — check the
  Highlightly dashboard directly next session rather than assuming it'll clear on its own (§7).
- **New: an automated email alert now watches Highlightly's quota** (§6.10) so this doesn't need
  manual `journalctl` checking going forward — fires once per UTC day at 80% of budget, and again
  the moment a real 429 is hit. Confirmed working live (an exhaustion email actually sent 2026-09-09
  13:42 UTC).
- **iOS notification icon investigation continued** (§6.11) — the owner did the full delete +
  restart phone + reinstall fix from §6.5, and it **did not work**: home screen icon is correct,
  notifications still show the old one. This rules out the Springboard-cache theory from §6.5.
  Leading new hypothesis: TestFlight/APNs may serve notification icon artwork registered at
  build-upload time rather than reading the live installed bundle, so only a fresh Codemagic iOS
  build (not a reinstall of the same build) would refresh it. **Unverified, no build has been
  triggered yet** — see §7.
- **Highlightly's quota outage is resolved — the old key never actually recovered.** After ~17
  hours of continuous exhaustion (05:46 UTC 2026-09-09 through at least 22:40 UTC), the owner
  supplied a brand-new Highlightly API key (`368f49b5-...`), which worked immediately with zero
  throttling. See §6.12. **This means the old key's daily quota genuinely never reset on its own —
  it was a dead/exhausted key, not a slow provider-side reset.** The Oxford v Reading postponed
  match (§6.9) self-corrected to `Postponed` within ~30s of the new key going live, exactly as
  designed — no manual DB fix was needed.
- **New session (2026-09-10): investigated cutting over to API-Football entirely** (scores, odds,
  match summary) since Highlightly just proved unreliable. Live API testing found API-Football's
  odds coverage is far richer than expected (6 bookmakers, 338 bet types, real per-player props
  days before kickoff, all in one call per fixture). A full phased implementation plan was written
  and saved — **no code changes made yet, this is planning only**. See §6.13 and the plan file at
  `C:\Users\alan.browne\.claude\plans\vivid-growing-snail.md`.
- **Same-day follow-up (2026-09-10): Phase 1 (live scores) of that plan implemented and deployed.**
  All 117 `Upcoming` + 1 `Postponed` matches were wiped from the production DB first (owner's
  explicit request, to avoid stale Highlightly-sourced fixtures confusing the cutover) — this also
  meant deleting one real pending bet that had a leg on an Upcoming match (owner confirmed: delete
  it, don't try to preserve it). `Providers:LiveScoreSource` is now `"ApiFootball"` in production.
  **A real settlement gap was found and the owner explicitly accepted it rather than blocking on a
  fix** — see §10 below and §7 top priorities. `Providers:MarketsSource` is still `"Highlightly"` —
  Phases 2–4 (odds, match summary/settlement parity, quota alerts) are not built yet.

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
  re-fetches a market type once "found" for a match (matters for §7.1).
- Settlement pivoted to Highlightly-first: goals/assists/cards/corners settle from Highlightly's
  `events`/`statistics` endpoints; API-Football narrowed to shots-on-target/total-shots only
  (confirmed Highlightly has no per-player shots data anywhere).
- Surname-based player/team matching added to reconcile API-Football's abbreviated names against
  the-odds-api's/Highlightly's full names (diacritics and mononyms still not handled, see §9).
- Match Summary page added (`MatchEvents.razor` + `MatchEventRow.razor`), later made to refresh
  server-side every ~30s while a match is `InProgress` (`RefreshLiveMatchEventsAsync`) — this is a
  *server-side DB freshness* mechanism, not client-side polling; the page itself doesn't poll (see
  §5.2's note on the new status line going stale the same way score/kickoff already did).
- Found and fixed a duplicate-`BetBuilderMarket`-row race condition — **fix only prevents new
  duplicates, doesn't repair matches that already had bad data before it deployed** (§7.1).

---

## 5. 2026-09-08 session — AdMob live release, three UI changes, emulator workflow

### 5.1 AdMob real IDs + Play Store release build

- Compared the AdMob console's real app ID against the code (still on Google's test ID), flagged
  the conflict with `CLAUDE.md`'s then-absolute "never ship real IDs" rule, confirmed with the
  owner that Android is already approved/live, then swapped in the real app ID
  (`AndroidManifest.xml`, `MauiInterstitialAdService.cs`) and the real interstitial ad unit ID
  (`ca-app-pub-8873351312647846/9075922506`, also `MauiInterstitialAdService.cs`). Rewrote
  `CLAUDE.md`'s rule to the per-platform-approval nuance. Commit `ee26ba1`.
- **`codemagic.yaml` has no Android release/Play Store workflow at all** — only `android-debug`
  (debug APK, not usable for Play Store) and two iOS workflows (`ios-ad-hoc`, `ios-testflight`).
  Play Store release AABs are built **locally only**, signed via `signing/fulltime-upload.jks` per
  the command documented in `signing/README.md`.
- Version code `8` was already the live Play Store version, so it had to be bumped before the new
  AAB could be uploaded. Bumped `ApplicationDisplayVersion` 1.1→1.2, `ApplicationVersion` 8→9 in
  `FullTime.App.csproj`, commit `fa425a6`, then rebuilt the signed AAB locally.
  **Whether the owner actually uploaded that AAB to Play Console was never confirmed — still open,
  see §7 top priorities.**

### 5.2 Three UI changes (commit `da33d52`)

- Header balance now auto-refreshes (`ContextSwitcher.razor`, same `/api/config`
  `RefreshIntervalSeconds` pattern `Matches.razor` already used).
- Match Summary shows live minute/HT/FT under the date/time (`MatchEvents.razor`, new
  `status`/`minute`/`isHalfTime` query params from `MatchCard.razor`'s `MatchEventsHref()`) — same
  snapshot-via-querystring approach already used for score/kickoff, so it goes stale the same way.
- Odds row and bookmaker logo hidden once a match starts, or before odds exist
  (`MatchCard.razor` — requires `Match.Status == "Upcoming"` AND `HasOdds`).

### 5.3 Local Android emulator testing workflow (kept in use this session too)

- `adb`/`emulator` aren't on `PATH` — full paths under
  `C:\Program Files (x86)\Android\android-sdk\` (`platform-tools\adb.exe`, `emulator\emulator.exe`).
  Two AVDs: `FullTime_GoogleAPIs_API35`, `FullTime_Pixel8_API35`.
- Routine local test loop is `dotnet build -t:Run -f net10.0-android -c Debug
  -p:AndroidOnlyBuild=true` from `FullTime.App/FullTime.App/` — builds, Fast-Deploys, and launches
  directly on whatever device/emulator `adb` currently sees. Only fall back to a full
  `EmbedAssembliesIntoApk=true` debug-APK build when a standalone installable artifact is actually
  needed.

### 5.4 Daily Spinner scope clarified (architecture note, not a change)

- The once-per-day cooldown (`User.LastSpinDate`/`SpinStreak`) is per-user, not per-league. A
  winning cash/streak payout fans out to every league the user belongs to at once
  (`SpinService.CreditAllMembershipsAsync`).

---

## 6. 2026-09-09 session — VM outage + monitoring, three bug fixes, notification icons, website link

### 6.1 Diagnosing and fixing the VM hang

- User reported "api not working". Direct `curl`/`Invoke-WebRequest` to
  `http://34.23.16.148:5199/api/config` timed out completely. `gcloud compute instances describe`
  showed the VM as `RUNNING`, but an SSH command hung for 2+ minutes with zero output.
  `Test-NetConnection` on both port 22 and port 5199 reported `TcpTestSucceeded: True` — i.e. the
  kernel's network stack still completed a TCP handshake — while an actual HTTP request never got
  a response. That combination (handshake succeeds, nothing ever answers, and — confirmed via
  `gcloud compute instances get-serial-port-output` — the API's own log had gone completely silent
  for 45+ minutes after a normal-looking last entry) is the signature of the whole VM being frozen,
  not the API process crashing (a crash would refuse the connection outright, not swallow it).
- Confirmed via `gcloud compute instances list` (only one instance) and `gcloud sql instances list`
  (Cloud SQL Admin API never enabled on this project) that Postgres is self-hosted on the same VM —
  meaning org policy blocks Claude from ever rebooting it. Reported the diagnosis and asked the
  owner to run the reset themselves; they did (`gcloud compute instances reset fulltime-vm
  --zone=us-east1-b`), and the API + Postgres came back up cleanly (confirmed via SSH: both
  `active (running)` since the reset, disk 27% used, no dmesg OOM evidence — but dmesg's ring
  buffer is wiped by a hard reset, so OOM as the root cause is plausible but unconfirmed).
  `e2-micro` is only 1GB RAM; even 1 minute after boot, load average was already 3.75 with 382Mi
  available, consistent with (but not proof of) memory pressure being the underlying cause.

### 6.2 Uptime check + alert policy

- Set up for free, no billing impact: uptime check `FullTime API health`
  (`projects/fulltime-app-505813/uptimeCheckConfigs/fulltime-api-health-I2h6cmG88aU`) — HTTP GET
  `/api/config` on port 5199 every 5 minutes, 10s timeout, content matcher `refreshIntervalSeconds`.
  Alert policy `FullTime API down`
  (`projects/fulltime-app-505813/alertPolicies/15000199197021894125`) fires on the first failed
  check (`check_passed` metric, `COMPARISON_GT` threshold 1 over a 1200s aggregation window) and
  emails a notification channel currently pointed at `alan@jtmtechnology.co.uk`
  (`projects/fulltime-app-505813/notificationChannels/7411117596041173729`; an earlier channel to
  `alan.browne@telxl.com` was created first, then swapped out and deleted per the owner's request —
  don't recreate that one).
- `gcloud monitoring uptime create` / `gcloud monitoring policies create` work in stable `gcloud`,
  no extra components needed. `gcloud alpha/beta monitoring channels create` does need the `alpha`
  or `beta` component installed — worked around by hitting the Monitoring REST API directly
  (`gcloud auth print-access-token` + `curl`) instead of installing anything.

### 6.3 Three bug fixes (see §1 for the what/why; this is the where)

- `FullTime.Api/Betting/WeeklyTopUpService.cs` + `FullTime.Api/Leagues/LeagueService.cs` — extracted
  the Sunday-cutoff math into `WeeklyTopUpService.MostRecentCutoffSunday(DateTime)` (public static),
  reused by both `RunAsync` and the two `LeagueMembership`-creation call sites
  (`LeagueService.CreateAsync`/`JoinAsync`) to stamp `LastTopUpDate` at join time.
- `FullTime.App/FullTime.App.Shared/Pages/Leaderboard.razor` — added `ShowWorldwideAsync()`, wired
  to the "Worldwide Top 50" chip's `@onclick`, replacing the one-time `OnInitializedAsync` fetch.
- `FullTime.App/FullTime.App.Shared/Components/MatchEventRow.razor` — extended the `Icon` and
  `Text` switches. Confirmed the real Highlightly vocabulary by querying the production DB directly
  (`SELECT DISTINCT "Type" FROM "MatchEvents"` via SSH + `psql`) rather than guessing: `Goal`,
  `Missed Penalty`, `Own Goal`, `Penalty`, `Red Card`, `Substitution`, `VAR Goal Cancelled`,
  `VAR Goal Confirmed`, `VAR Penalty`, `Yellow Card`.

### 6.4 Android push-notification icon

- No dedicated notification icon existed anywhere in the repo before this — confirmed via search,
  and via the `Plugin.FirebasePushNotifications` package's own README (no icon-configuration option
  documented). `MauiProgram.cs`'s `.UseFirebasePushNotifications()` is called with zero options.
- Generated `drawable-{m,h,x,xx,xxx}hdpi/ic_stat_notification.png` from
  `Resources/AppIcon/appicon.png`'s own alpha channel (confirmed via pixel sampling that the source
  PNG has real transparency around the shield shape, not an opaque background) — every non-transparent
  pixel forced to solid white, alpha preserved, using System.Drawing via a throwaway PowerShell
  script (not checked in). Wired up via two new `AndroidManifest.xml` meta-data entries
  (`com.google.firebase.messaging.default_notification_icon` /
  `..._color`) and a new `notification_accent` (`#2FAE4F`) entry in `colors.xml`.
- **Gotcha hit while wiring this up**: naming the color resource `notificationAccent` (camelCase)
  caused `APT2260: resource color/notificationaccent not found` at manifest-link time — the error
  message itself showed the reference lowercased with no underscore, not matching the mixed-case
  definition. A full clean (`dotnet build-server shutdown` + deleting the Android `obj`/`bin`
  folders) did **not** fix it. Renaming the resource to snake_case (`notification_accent`, matching
  Android convention) fixed it immediately. **Lesson: always use snake_case for Android value
  resource names, never camelCase.**
- This was investigated because the owner reported "push notification icon old one" — turned out to
  be about iOS, not Android (see §6.5). Android's icon was never actually broken by the old
  behaviour in any way the owner had noticed, but the fix is a genuine improvement (a proper
  dedicated icon is correct regardless) so it was kept.

### 6.5 iOS "old notification icon" — first pass, since superseded by §6.11

- Checked `Info.plist` (`XSAppIconAssets` → `Assets.xcassets/appicon.appiconset`, generated at
  build time from `appicon_ios.png` — not committed to git), confirmed `appicon_ios.png` (1024×1024)
  matches the final shield design (last touched by the "Shrink app icon shield" commit,
  2026-09-01), and confirmed `PushNotificationService.cs`'s FCM payload carries only a title/body,
  no image/media attachment — so there is no code path that could serve a stale icon on iOS.
- Asked the owner directly: after a fresh iOS build the day before, was the home screen icon also
  wrong, or just notifications? Answer: home screen was already correct, only notifications showed
  the old icon. Theorized it's iOS/Springboard's own notification-icon cache not refreshing after an
  in-place TestFlight update, and suggested fully deleting the app, restarting the phone, then
  reinstalling. **This theory turned out to be wrong — see §6.11: the owner did exactly that and it
  didn't fix it.**

### 6.6 Website Google Play link

- `FullTime.Website/wwwroot/index.html` and `invite.html` both had `href="#"` placeholders for both
  store badges since launch. Updated the Google Play one to the real listing URL
  (`https://play.google.com/store/apps/details?id=com.jtmtechnology.fulltime.app&hl=en`); left the
  App Store one as a placeholder (commented to explain why) since iOS hasn't had its first App
  Store approval yet.
- **This one WAS deployed**: `dotnet publish -r linux-x64` → tar → `gcloud compute scp` →
  stop/swap/start `fulltime-website` on the VM, following `CLAUDE.md`'s documented per-host deploy
  pattern with `X=website`. Confirmed live via `curl` against the running site.

### 6.7 Ads-removed card fix

- `FullTime.App.Shared/Pages/Profile.razor` — the "Ads" card used to stay visible after a real
  ads-removal purchase (or a restore), just swapping the purchase buttons for a "thanks for
  supporting FullTime" message. Changed to wrap the whole card in
  `@if (!AdsRemoval.AdsRemoved)` so it doesn't render at all once ads are already removed. Code-only
  (same `FullTime.App.Shared` deploy situation as §6.3's leaderboard/match-event fixes — see §7
  item 1). Commit `a555aa3`.

### 6.8 Play Store release build v1.3 (code 10)

- Owner confirmed version code 9 (1.2, built §5.1) was already uploaded to Play Console, so bumped
  `ApplicationDisplayVersion`/`ApplicationVersion` 1.2/9 → 1.3/10 in `FullTime.App.csproj` (commit
  `46f4cbd`, pushed) and rebuilt the signed AAB per `signing/README.md`. Output:
  `FullTime.App/FullTime.App/bin/Release/net10.0-android/com.jtmtechnology.fulltime.app-Signed.aab`.
  **Upload to Play Console still needs the owner** — same open question as last session, now one
  version further on.

### 6.9 Highlightly quota exhaustion — root cause and fix

- Investigated "West Brom v Derby has no odds today" — turned out **every** tracked league had had
  zero successful Highlightly calls since 05:46 UTC, continuously throttled (`429`) in a
  self-perpetuating 15-minute cooldown loop (`HighlightlyClient`'s shared `_quotaExhaustedUntilUtc`)
  that had not recovered by the time this was investigated (~10:35 UTC). Confirmed this was a real
  provider-side exhaustion, not a stuck local timer, by redeploying (fresh process, cooldown state
  reset) and observing it get a fresh 429 within ~2 minutes of starting up regardless.
- Root cause: 2026-09-08 evening had 18 simultaneous fixtures (6 Champions League + EFL Cup +
  Championship + League One) — a genuinely heavy night — plus one of them, Oxford United v Reading
  FC (League One), was **postponed**. `HighlightlyMatchSyncService.DeriveStatus` recognizes
  Highlightly's `"Postponed"` description string correctly, but until this fix it mapped straight to
  `MatchStatus.Upcoming` (no dedicated status existed) — indistinguishable from "hasn't kicked off
  yet". That fed `RefreshLiveAsync`'s `staleKickoffs` query (`Status != Finished && KickoffTime <=
  now`), which re-added the postponed match's original kickoff date to the fetch list on **every
  single live-sync tick, forever**, permanently doubling that tick's Highlightly call count (14
  leagues → 28) for as long as it stayed postponed.
- **Fix (commit `385e2bc`, deployed to `fulltime-api`, confirmed live)**: added
  `MatchStatus.Postponed` (`Models/MatchStatus.cs` — appended, so no EF migration needed, it's a
  plain int column); `DeriveStatus` now maps `"Postponed"` to it instead of `Upcoming`;
  `staleKickoffs` now explicitly matches only `Upcoming`/`InProgress` (was `!= Finished`, which also
  caught `Postponed`); `MatchesController.GetUpcoming`'s specific-date query now also excludes
  `Postponed` so it never displays in the app at all, per owner request (the default no-date query
  already only returns `Upcoming`/`InProgress`, so it was already excluded there). Betting and
  odds/player-props fetching already gated on `Status == Upcoming`, so those get the same exclusion
  for free.
- The already-stuck Oxford v Reading row will self-correct (get reclassified `Postponed`, then stop
  being re-fetched) the next time a sync tick successfully reaches League One for 2026-09-08 — no
  manual DB fix needed, just waiting for Highlightly's quota to actually recover.
- **Confirmed still exhausted for ~9h43m straight** (checked at 10:35, 13:28, 14:44, and 15:29 UTC,
  zero successful calls the entire window, 30+ throttle/re-arm cycles) — this has now clearly
  outlasted a simple UTC-midnight daily reset. Also hit Highlightly's raw API directly with `curl`
  to rule out a header we might be missing: the 429 body is just
  `{"message":"You have breached your daily request limits."}` with **no** `Retry-After` or any
  rate-limit-remaining/reset header at all — there is no live endpoint to poll for "how much is
  left" or "when does it reset". Check the Highlightly account dashboard directly next session
  rather than continuing to poll blind.

### 6.10 Highlightly quota alert emails

- Built proactive alerting since there's no live quota endpoint to check (§6.9's finding above):
  `HighlightlyClient` now tracks its own daily call count (incremented right before each real
  outbound request - calls skipped by the existing cooldown gate don't count) and fires two kinds of
  email via the already-working `IEmailSender`/SMTP pipeline (same Brevo relay `AuthService` already
  used, confirmed live env vars on the VM): a proactive warning once the count crosses 80% of the
  known 25,000/day budget (`HighlightlyOptions.DailyCallBudget`/`AlertThresholdPercent`), and an
  immediate one the moment a real 429 hits. Both are deduped to once per UTC date via static
  `DateOnly?` fields (in-memory, resets on restart - acceptable for a monitoring feature).
  `AlertEmail` is blank in the checked-in `appsettings.json` (same pattern as other secrets);
  `Highlightly__AlertEmail=alan@jtmtechnology.co.uk` was added directly to the VM's
  `/etc/systemd/system/fulltime-api.service` (original backed up alongside as `.service.bak`) and
  `daemon-reload`d.
- Commit `29f05f2`, pushed, deployed to `fulltime-api`. **Confirmed genuinely working end-to-end**:
  redeploying while the quota was still exhausted immediately re-triggered the 429 path, and the
  exhaustion email actually sent (`Email sent to alan@jtmtechnology.co.uk — FullTime: Highlightly
  quota exhausted`, 2026-09-09 13:42:28 UTC).

### 6.11 iOS notification icon — Springboard-cache theory ruled out

- Owner did the full remedy from §6.5 (delete app, restart phone, reinstall) and the notification
  icon is **still** the old one, while the home screen icon is confirmed correct. A full
  delete+restart+reinstall should clear any local/Springboard-level cache tied to the app's bundle
  ID, so this rules out §6.5's theory entirely - the stale icon isn't coming from the currently
  installed app bundle at all.
- Checked for an iOS Notification Service/Content Extension with its own separate icon assets (would
  explain a different icon source) - **none exists**, `Info.plist` has only the one
  `XSAppIconAssets` reference, same asset catalog used everywhere.
- Leading hypothesis (unverified): this is a TestFlight build, and TestFlight/APNs notification
  icons may be sourced from artwork Apple registered at that specific build's upload/processing
  time, not read live from the installed bundle the way the home screen icon is - if the last
  submitted iOS build predates whatever most recently touched `appicon_ios.png`, no local action
  could ever fix it; only uploading a **new** build would re-register fresh artwork with Apple.
  Recommended next step: trigger the `ios-testflight` Codemagic workflow. **Not done this
  session** - can't build/test iOS locally (no Mac), and the owner was mid-decision on whether to
  trigger it themselves when the conversation moved on. Pick this up next session.

### 6.12 Highlightly API key swap — outage actually resolved

- Checked repeatedly through the day (10:35, 13:28, 14:44, 15:29, 22:36, 22:41 UTC) — the old key
  stayed continuously throttled the entire time, ~17 hours straight, including immediately after a
  fresh `systemctl restart fulltime-api` (new process, zero local cooldown memory, still got a 429
  within seconds) — conclusively ruling out a stuck local timer.
- Owner supplied a new key (`368f49b5-f29a-467e-894e-8c48a5ac9fd8`). Swapped into
  `/etc/systemd/system/fulltime-api.service`'s `Environment=Highlightly__ApiKey=...` line (backed
  up as `fulltime-api.service.bak2` — the AlertEmail addition from §6.10 was already backed up as
  `.bak`), `daemon-reload`d, restarted. **Worked immediately** — confirmed via successful, unthrottled
  `/matches`/`/odds` HTTP calls in the logs within seconds of restart.
- Confirmed via a direct `curl` to Highlightly with the new key that it currently reports
  `"plan":{"tier":"PRO","message":"All data available with current plan."}` for League One - i.e.
  this is a working, current-plan account.
- The Oxford v Reading match (`Status` was still `0`/`Upcoming` all day, per §6.9) flipped to `3`
  (`Postponed`) within ~30 seconds of the new key's first successful League One sync — the fix from
  §6.9 works exactly as designed once real data actually flows again.
- **Old key status unknown/unreconciled** — it may be worth checking the Highlightly dashboard to
  understand why it never recovered (a real daily quota should reset within 24h, not stay dead for
  17+ hours with no sign of clearing), but this is no longer blocking anything since the new key works.

### 6.13 API-Football cutover investigation — plan written, not yet implemented

- Prompted by the Highlightly outage above ("Highlightly let us down") - investigated promoting
  API-Football to the primary source for live scores, odds, and Match Summary, since it's already
  partially integrated (dormant live-score sync, always-on shots-prop settlement) from the
  2026-09-06 cutover-and-revert (§3).
- **Confirmed via live `curl` calls against the real API** (not docs/memory):
  - Account is still **Pro (7,500/day)** — the user asked to design for "up to 75,000/day," which
    is a higher tier (Ultra-class) than what's currently active. **The plan assumes this gets
    upgraded before rollout** — this is a real prerequisite, not a rounding error.
  - `/odds?fixture=` returns **6 bookmakers and up to 70 markets on a single fixture, in one call**
    - dramatically richer than Highlightly's per-market/per-league/per-date odds model, and includes
    real per-player prop odds (goalscorer, assists, shots, fouls) days before kickoff.
  - Confirmed via `/odds/bets` (338 total bet types) that API-Football has **no clean per-player
    card market** — team-level card totals exist, but nothing matching `PlayerCard`/`PlayerRedCard`
    today. Per the owner's call: keep these mapped in the future parser anyway so they light up
    automatically if a bookmaker ever prices them, rather than dropping the markets outright.
  - Confirmed several bet-name variants are **team-scoped** (`Home Anytime Goal Scorer` vs a
    combined `Anytime Goal Scorer`, etc.) - using these instead of the combined variant gives team
    attribution for free, avoiding the surname/squad-matching heuristic `PlayerPropsService` needs
    for the-odds-api.
- Spawned two research subagents to map the existing codebase in detail: the dormant
  `ApiFootballMatchSyncService`/`ApiFootballSettlementSupportService`/`Program.cs` toggle mechanism
  (found: API-Football's live-score sync is dormant but **structurally better** than Highlightly's -
  1 call total vs ~14/tick, structured score ints, cleaner half-time detection - but its
  `DeriveStatus` still has the exact pre-fix "Postponed→Upcoming" bug §6.9 just fixed for
  Highlightly, and `/api/config` is hardcoded to `HighlightlyMatchSyncService` regardless of which
  provider is active); and the `BetBuilderMarket`/`MarketType`/settlement/TTL-refresh data flow
  (found: new markets fit the existing schema with no changes if they're `Line`/`Side`/`PlayerName`/
  `Team`-shaped; `OddsApiMarketService`'s tiered-TTL freshness pattern - 6h far / 45min near / 15min
  imminent, boundaries at 24h/1h - is directly reusable for API-Football odds refresh).
- **Full phased plan written to `C:\Users\alan.browne\.claude\plans\vivid-growing-snail.md`**:
  1. Live scores - fix the Postponed mapping, make `/api/config` provider-aware, lower the poll
     interval (cheap since it's 1 call regardless of match count), flip `LiveScoreSource`.
  2. New `ApiFootballOddsService` + background sync - bet-name-to-`MarketType` mapping table, pin to
     Bet365 (richest coverage, already has a working logo), new `MarketType` values
     (`TeamCorners`, `TeamCards`, `PlayerFoulsCommitted`), small schema additions
     (`Match.HomeCorners`/`AwayCorners`/`HomeCards`/`AwayCards`, `MatchPlayerStat.FoulsCommitted`),
     new settlement cases (same shape as existing `TotalCorners`/`PlayerShots`), client-side
     `BetBuilder.razor` additions.
  3. Match Summary - extend `FixtureEventDto` with player/assist fields (needs confirming against a
     real fixture before implementing), normalize API-Football's event vocabulary to Highlightly's
     existing canonical strings so `MatchEventRow.razor` needs zero changes.
  4. Port the quota-alert-email feature (§6.10) to `ApiFootballClient` too, so the new primary
     provider can't fail silently either.
  Highlightly stays fully compiled in as the rollback path (same `ProvidersOptions` toggle already
  proven twice).
- **Nothing implemented this session — planning only.** Next session should read the plan file
  first, confirm the API-Football plan has actually been upgraded, then execute in the order above.

---

## 7. Known issues / outstanding

**Top priorities for whoever picks this up next (updated end of 2026-09-11, see §16 for the latest):**
1. **fulltime-web is now significantly behind `main`** — every commit from `9694f6b` onward (§16: the
   entire Bet Builder Boost feature, the Daily Spinner evens+ rule) has not been deployed there, per
   the owner's explicit "I never use the app on the web" this session. Don't assume web reflects
   current `main` the way it normally would; redeploy (`X=web`, standard publish/scp/restart) if the
   owner starts using it again or a family member reports it missing these features.
2. **Bet Builder Boost (§16.3) is brand new and only ever tested against one account** — the shared
   "one match per day" pick and per-user `LastBetBuilderBoostDate` gating are both designed to work
   correctly with multiple family members using it independently the same day, but that hasn't
   actually been observed with two real accounts yet. Worth watching the first time more than one
   person uses it on the same day.
3. **Trigger a fresh iOS Codemagic build (`ios-testflight`)** — still the owner's next step for iOS,
   untouched again this session. Also tests the §6.11 hypothesis (owner's full delete+restart+reinstall
   did NOT fix the stale notification icon, ruling out the local-cache theory) - if the new build's
   notification icon comes through correct, that confirms TestFlight/APNs caches icon artwork
   per-build rather than reading the live bundle.
4. **Upload a build to Play Console** —
   `FullTime.App/FullTime.App/bin/Release/net10.0-android/com.jtmtechnology.fulltime.app-Signed.aab`
   (version 1.4, code 11, §13.1, commit `a379ab3`, pushed) is now stale on **many** counts, most
   recently the entire §16 session (Bet Builder Boost, Daily Spinner evens+ rule, football tab icon,
   bet-card spacing fixes) on top of everything already listed from §13.3/§14/§15. Rebuild with all of
   it folded in before uploading (bump to 1.5/12 first, per §13's version-code convention).
5. **Club crests couldn't be verified on this dev machine this session** (§16.3.5) — confirmed a
   pre-existing network block (`media.api-sports.io` unreachable, same root cause as the already-known
   `v3.football.api-sports.io` block), not a code bug. Owner said they'd check crests on a real device
   later — worth confirming they actually do render there before treating this as fully closed.
6. **API-Football is now the live provider for everything** (`LiveScoreSource` and `MarketsSource`
   both `"ApiFootball"`, deployed and verified) — Highlightly and the-odds-api are fully dormant
   rollback paths, not in active use. Don't assume `HANDOVER.md` sections written before §10 still
   describe current behavior where they talk about Highlightly being primary.
7. **Build Phase 4 proper** (quota-alert *call-budget* parity — daily-count tracking + threshold
   email, porting `29f05f2`'s Highlightly pattern to `ApiFootballClient`) — still genuinely open.
   Don't confuse this with the narrower stale-InProgress-match alert added in §12.4, which is a
   different thing (a stuck-match detector, not a call-volume tracker).
8. **Watch `journalctl -u fulltime-api`** for both real 429s (§11.4's cadence math says a heavy
   multi-league day could plausibly approach or exceed Pro's 7,500/day ceiling) and the
   "Failed to re-fetch N match(es) dropped from live=all" warning added in §15's resilience fix
   (`5b72c41`) - it has never actually fired yet, so its log-and-continue path is code-reviewed but
   not battle-tested against a real API failure mid-tick.
9. **Confirm the account tier before pushing cadences any lower** — still Pro (7,500/day) as of
   2026-09-10; the owner wants to design for up to 75,000/day eventually (needs an Ultra-class
   upgrade first, see §6.13).
10. **Low priority**: R8/obfuscation for the Android build (§13.2) — Play Console flagged it, but the
    deadline is Feb 2027 and enabling it risks silently breaking push/ads/billing/UMP without careful
    proguard keep rules. Deferred on purpose, not forgotten.
11. **Low priority**: `BetSlipSheet.razor`'s pick-label switch still duplicates `BetDisplay.cs`'s (§14)
    for placing a bet - unrelated to §15's new `BetList.razor` (which dedups the *displaying* side,
    MyBets/FriendBetsPanel). Consider refactoring the slip to call `BetDisplay.PickLabel` directly so a
    future new `MarketType` can't fix one and silently miss the other again.
12. The duplicate-`BetBuilderMarket`-row problem (below) has still never been swept beyond one match.
13. If Highlightly's *old* API key (now replaced, §6.12) ever matters again — e.g. to understand why
    a real daily quota stayed dead for 17+ hours instead of resetting — that's now a dashboard
    question, not something diagnosable from the app's own logs.
14. **Don't add a "tap a name to see their bets" affordance to the Worldwide leaderboard tab** —
    tried in §15 and deliberately reverted at the owner's request, since Worldwide can list people you
    don't share a league with, who `GetUserBets`' own privacy check would just reject anyway (404).
    My Leagues only.

Full list:

1. **Duplicate-row bug (pre-2026-09-07 16:08 UTC data) still not swept.** A race in
   `PlayerPropsService.EnsureFreshAsync` could double-insert `BetBuilderMarket` rows; fixed going
   forward (`466ad68`), but a match that already had duplicates before that fix deployed won't
   self-heal (a market type "found" for a match is never re-fetched). Only one instance (Aston Villa
   v Nottingham Forest) has been manually deduped via direct SQL.
2. **API-Football plan reconciled 2026-09-10**: confirmed still Pro (7,500/day), not the free tier
   the owner once thought they'd moved to. Now directly relevant, not just a loose end - see §6.13's
   cutover plan, which needs a higher tier (the user wants up to 75,000/day) before its more
   aggressive cadence numbers are safe to use.
3. **the-odds-api is on its free tier** (500 credits/month, current usage comfortable).
4. Card/RedCard/Assists/total-Shots markets show zero priced rows for most matches — confirmed real
   (no bookmaker has priced them yet this early), not a bug.
5. **No Android release/Play Store Codemagic workflow exists** — Play Store builds are local only.
6. The auto-mode safety classifier can still intermittently block `gcloud compute ssh`/`scp` and
   even local file edits with no clear pattern — a plain retry has repeatedly worked. Never actually
   pre-approved in `.claude/settings.json` — check before assuming it exists.
7. Carried over, unconfirmed: Play Console closed-testing rollout status, release keystore
   off-machine backup, iOS UMP/package-rename verification (needs a Codemagic build), `adb` not
   detecting the physical Android phone over USB, the `test@jtmtechnology.co.uk` test account
   decision, App Store Connect submission (no build uploaded yet), an iOS AdMob app + real iOS
   AdMob IDs (Android is done), a real Apple 1024×1024 no-alpha icon export, TestFlight beta review
   422, a stale `free-api-live-football-data` retirement plan at
   `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md`, and Cloudflare
   purge-on-deploy to replace the `styles.css?v=N` cache-busting workaround.
8. A known minor race still exists in both `HighlightlyMatchSyncService` and the dormant
   `ApiFootballMatchSyncService`: live-sync and fixture-discovery background services can both try
   to insert the same brand-new match at once right after a restart on an empty/near-empty
   `Matches` table (`23505` duplicate-key error). Self-heals, not fixed.
9. **`fulltime-vm` is a single point of failure** (§1, §6.1) — one `e2-micro` (1GB RAM) box running
   the API, Postgres, the web head, and the marketing site, with no managed DB and no redundancy.
   An uptime check + email alert now catch an outage fast, but don't prevent one. Worth revisiting
   if hangs recur — see §1 for the resize option and why it wasn't done proactively.

---

## 8. Key files touched this session (2026-09-09)

- `FullTime.Api/Betting/WeeklyTopUpService.cs` — extracted `MostRecentCutoffSunday`, top-up timing
  fix.
- `FullTime.Api/Leagues/LeagueService.cs` — stamps `LastTopUpDate` at membership creation.
- `FullTime.App/FullTime.App.Shared/Pages/Leaderboard.razor` — `ShowWorldwideAsync()`, re-fetches on
  tab switch.
- `FullTime.App/FullTime.App.Shared/Components/MatchEventRow.razor` — icon/text for
  penalty/own-goal/VAR event types.
- `FullTime.App/FullTime.App/Platforms/Android/AndroidManifest.xml` — notification icon + color
  meta-data.
- `FullTime.App/FullTime.App/Platforms/Android/Resources/values/colors.xml` — `notification_accent`.
- `FullTime.App/FullTime.App/Platforms/Android/Resources/drawable-{m,h,x,xx,xxx}hdpi/
  ic_stat_notification.png` — new, generated notification icon assets.
- `FullTime.Website/wwwroot/index.html`, `invite.html` — live Google Play link.
- `FullTime.App/FullTime.App.Shared/Pages/Profile.razor` — hides the Ads card once ads are removed.
- `FullTime.App/FullTime.App/FullTime.App.csproj` — version 1.2/9 → 1.3/10 (§6.8).
- `FullTime.Api/Models/MatchStatus.cs` — added `Postponed` (§6.9).
- `FullTime.Api/BetBuilder/HighlightlyMatchSyncService.cs` — `DeriveStatus` maps `"Postponed"` to
  the new status; `staleKickoffs` query now explicit `Upcoming`/`InProgress` only (§6.9).
- `FullTime.Api/Controllers/MatchesController.cs` — specific-date query excludes `Postponed` (§6.9).
- `FullTime.Api/BetBuilder/HighlightlyClient.cs` — daily call counter + threshold/exhaustion email
  alerts (§6.10).
- `FullTime.Api/BetBuilder/HighlightlyOptions.cs` — `AlertEmail`/`DailyCallBudget`/
  `AlertThresholdPercent` (§6.10).
- `FullTime.Api/appsettings.json` — blank `Highlightly:AlertEmail` placeholder + budget/threshold
  defaults (§6.10).

Commits this session, all pushed to `main`: `4a37ac0`, `ceabddb`, `1c259f1`, `56509b5`, `3021bf3`,
`a555aa3`, `46f4cbd` (version bump), `385e2bc` (postponed-match fix), `29f05f2` (quota alerts).
`fulltime-api` on the VM is running `29f05f2` — up to date. `fulltime-web` has not been redeployed
since before this session's `FullTime.App.Shared` fixes (§7 item 4).

Non-code VM changes this session: `/etc/systemd/system/fulltime-api.service` got a new
`Environment=Highlightly__AlertEmail=alan@jtmtechnology.co.uk` line (original backed up as
`fulltime-api.service.bak`); then, separately (§6.12), `Environment=Highlightly__ApiKey=...` was
swapped to the owner's new key (previous version backed up as `fulltime-api.service.bak2`).
`daemon-reload`d and restarted both times.

No code changes for the VM outage/monitoring work (all done via `gcloud`/the Monitoring REST API
directly), the iOS notification-icon investigation (still unresolved — see §6.11), or the
API-Football cutover investigation (§6.13 — planning only, full plan saved to
`C:\Users\alan.browne\.claude\plans\vivid-growing-snail.md`, nothing implemented yet).

`ODDS_API_PLAYER_PROPS_INVESTIGATION.md` at repo root remains an untracked scratch doc with **live
API keys in plaintext** — must never be committed.

---

## 9. Gotchas discovered this session (in addition to `CLAUDE.md`'s environment quirks and earlier
   handover sections)

- **Android value-resource names must be snake_case, never camelCase.** A color named
  `notificationAccent` caused `APT2260: resource color/notificationaccent not found` at
  manifest-link time (note the reference in the error is lowercased with no underscore) — survived
  a full `dotnet build-server shutdown` + Android `obj`/`bin` clean, so it isn't a cache issue.
  Renaming to `notification_accent` fixed it immediately.
- **A TCP handshake succeeding is not proof a service is responsive.** `Test-NetConnection`/`curl`
  connecting to a port only proves the kernel's network stack answered — the application behind it
  can still be completely frozen and never actually respond. Don't trust a "port is open" check
  alone when diagnosing a hang.
- **`gcloud compute instances get-serial-port-output` reads a hung VM's console log without needing
  network access into the guest** — useful when SSH itself is part of what's hanging. Comparing its
  last log timestamp against the current time is a reliable way to tell whether the box is actually
  alive.
- **`gcloud compute instances list` + `gcloud sql instances list`** (even when the latter errors with
  "API never enabled") is a quick way to confirm whether a project's database is self-hosted on a
  Compute VM or a separate managed Cloud SQL instance — check this before ever considering a reboot,
  since org policy blocks rebooting anything that hosts a database.
- **`gcloud monitoring uptime create`/`gcloud monitoring policies create` are in stable `gcloud`**,
  no `alpha`/`beta` component needed — but `gcloud alpha/beta monitoring channels create`
  (notification channels) does need one of those components installed. Worked around by calling the
  Monitoring REST API directly (`gcloud auth print-access-token` + `curl`) instead of installing
  anything.
- **A recognized-but-unmodelled provider status is a real risk, not just an unrecognized one.**
  Highlightly's `"Postponed"` description was already correctly matched in `DeriveStatus`, but
  folding it into `MatchStatus.Upcoming` (no dedicated value existed) silently made it
  indistinguishable from "not kicked off yet" — which fed a completely different bug
  (`staleKickoffs` re-fetching it forever). Lesson: give every distinct real provider status its own
  model value rather than reusing an existing one that merely looks similar, even when the mapping
  seems harmless at the time.
- **Restarting the API process does not clear a genuine provider quota exhaustion** —
  `HighlightlyClient`'s cooldown timer is in-process/static, so a fresh process has no memory of a
  prior cooldown, but if the account is genuinely still rate-limited, the very next call gets a
  fresh 429 within moments regardless. Useful diagnostic: if a freshly-restarted process immediately
  re-throttles, that's strong evidence the exhaustion is real/provider-side, not a stuck local timer.
- **`MatchStatus` (and most other simple enums in this codebase) is stored as a plain int column**
  (no Npgsql native enum type, no `HasConversion` in `AppDbContext`) — appending a new value is a
  code-only change, no EF Core migration needed, as long as it's appended rather than inserted or
  reordered.
- **Highlightly's 429 response carries no quota/rate-limit headers of any kind** — confirmed by
  hitting the raw API directly with `curl -D -`: no `Retry-After`, no `X-RateLimit-*`, nothing but
  `{"message":"You have breached your daily request limits."}`. There is no way to poll "how much
  quota is left" or "when does it reset" from the API itself - any monitoring has to be self-tracked
  on our side (see §6.10) or checked manually on Highlightly's own dashboard.
- **A full delete + restart phone + reinstall did NOT fix iOS's stale notification icon** (§6.11),
  even though the home screen icon was already correct — ruling out a local/Springboard cache as the
  cause. When a home-screen icon and a notification icon disagree on iOS despite a clean reinstall,
  suspect something server-side tied to the specific build (e.g. TestFlight/APNs artwork registered
  at upload time) rather than anything fixable by clearing local device state.
- **A "daily" quota that stays exhausted for 17+ hours with no recovery is a strong sign the key
  itself is dead, not that the reset is just running late** (§6.12) — a brand-new Highlightly key
  worked instantly with zero throttling, while the old one never recovered on its own the entire
  time it was checked. Worth trying a fresh key well before assuming a multi-hour-plus outage is
  "almost about to reset."
- **A bookmaker's team-scoped bet-name variant (e.g. "Home Anytime Goal Scorer" vs a combined
  "Anytime Goal Scorer") gives player-prop team attribution for free** — confirmed real on
  API-Football's `/odds` response (§6.13). Prefer these over a combined market whenever both exist;
  it avoids needing the surname/squad-matching reconciliation `PlayerPropsService` requires for
  providers (like the-odds-api) that don't split by team in the bet name itself.
- Carried over from 2026-09-07/08: a market type "found" for a match is never re-fetched by
  `PlayerPropsService` (permanent-until-manually-fixed stale data risk); a production data bug can
  look identical to "the API has no data" (always curl the real provider before concluding "no
  coverage"); the auto-mode safety classifier's blocks aren't fully deterministic (retry before
  concluding a hard block); diacritics and mononyms both break the surname-matching helper (safe
  failure mode — a dropped row, not a wrong one); `codemagic.yaml`'s `android-debug` workflow is
  debug-keystore-signed and not usable for Play Store; `adb`/`emulator` aren't on `PATH` by default
  (full paths under `C:\Program Files (x86)\Android\android-sdk\`); the owner's routine local test
  loop is `dotnet build -t:Run`, not a built APK; a Play Store upload needs a strictly higher
  version code than whatever's already live.

---

## 10. 2026-09-10 session (same day as §6.13's planning) — API-Football Phase 1 deployed

- Owner asked to start executing the cutover plan (`C:\Users\alan.browne\.claude\plans\vivid-growing-snail.md`)
  and, separately, to delete all upcoming fixtures from the production DB first so stale
  Highlightly-sourced data wouldn't confuse the switch.
- **Before deleting anything**, checked the production DB for real data that would be affected:
  117 `Upcoming` + 1 `Postponed` match existed; exactly one real pending bet (£10, single-leg,
  Chelsea v Hull City, 2026-09-12) had a leg on an Upcoming match. Also confirmed via
  `information_schema` that every FK referencing `Matches` (`OddsSnapshots`, `BetBuilderMarkets`,
  `MatchPlayerStats`, `MatchEvents`, `BetLegs`) is `ON DELETE CASCADE`, so a direct `Matches` delete
  would clean up dependent rows without orphaning anything except the `Bet` row itself (which has no
  FK to `Match` directly — only via `BetLegs`).
  - Asked the owner explicitly what to do with the one conflicting bet and whether to include the
    Postponed match in the wipe. Confirmed: delete the bet too, include the Postponed match.
  - Executed via a transactional SQL script (SCP'd to the VM, run with `psql -f`, not inline `-c`
    with escaped quotes — `pscp`/`plink`'s quoting mangled single-quoted SQL string literals passed
    via `-c`, and remote-path arguments with a leading `~/` failed silently for both `scp` and `ssh`
    home-relative reads in this session — a bare filename (lands in the SSH user's home dir by
    default) worked reliably for both). Result: 118 matches deleted, 1 bet deleted, only the 48
    `Finished` matches remain.
- **Reconciled the plan against real code before implementing** (spawned a research subagent):
  confirmed everything in Phase 1 matched, with one correction — `ApiFootballMatchSyncService
  .DeriveStatus`'s `"PST"` wasn't *missing* a case, it was already grouped into the same case as
  `"NS"/"TBD"` → `Upcoming`. Fix was splitting it out into its own `case "PST": return
  MatchStatus.Postponed;`, not just adding a new case. Also saved as a memory
  (`project_apifootball_postponed_status`) given how easy that distinction is to miss.
- **Implemented Phase 1**:
  - `FullTime.Api/BetBuilder/ApiFootball/ApiFootballMatchSyncService.cs` — `DeriveStatus` now maps
    `"PST"` to `MatchStatus.Postponed` on its own line, separate from `"NS"/"TBD"`.
  - `FullTime.Api/Program.cs` — `/api/config` is now provider-aware: reads
    `IOptions<ProvidersOptions>.Value.LiveScoreSource` and calls `ApiFootballMatchSyncService
    .NextPollDelayAsync()` or `HighlightlyMatchSyncService.NextPollDelayAsync()` accordingly (both
    services are always DI-registered regardless of which is active, so this needed no other
    wiring change).
  - `FullTime.Api/appsettings.json` — `ApiFootball.LiveRefreshIntervalSeconds` 15 → 10 (the plan's
    conservative Pro-tier value, confirmed the account is still Pro/7,500/day, not yet upgraded, via
    `curl https://v3.football.api-sports.io/status` against the VM's real key: 15/7500 used that
    morning). `Providers.LiveScoreSource` `"Highlightly"` → `"ApiFootball"`. `Providers.MarketsSource`
    left as `"Highlightly"` — Phase 2 (odds) doesn't exist yet, flipping this now would break Bet
    Builder entirely.
  - Build verified clean (`dotnet build FullTime.Api/FullTime.Api.csproj -c Release`, 0 errors).
- **Found a real settlement gap before deploying, and surfaced it rather than shipping around it**:
  `Providers:LiveScoreSource` doesn't just choose the live-score provider — `Program.cs`'s hosted-
  service registration (lines ~99-118) also gates which *settlement* background service runs on it.
  `GoalScorerResolutionBackgroundService` (Highlightly-only, calls `BetBuilderSyncService
  .ResolveMatchEventsAsync`) is the **only** thing that ever sets `Match.EventsFinalizedAt` and
  populates `MatchEvent` rows — and `SettlementService.ResolvePicksAsync`'s readiness gate requires
  `EventsFinalizedAt` before it will settle `TotalCorners`, `PlayerGoalscorerAnytime`, `PlayerCard`,
  `PlayerRedCard`, or `PlayerAssists` picks (confirmed by reading `IsPickCorrect`'s
  `FindPlayerEvent`/`CountAssists`, both of which read `Match.Events`, populated nowhere else).
  Flipping `LiveScoreSource` to `ApiFootball` turns that background service off, so any future bet
  on those 5 market types would sit `Pending` forever with no error. (`PlayerShotsOnTarget`/
  `PlayerShots`/`FirstTeamToScore` are unaffected — `ApiFootballSettlementSupportService` already
  covers those independent of the toggle.)
  - **Presented the owner with the tradeoff (build Phase 3's event-fetch now vs. accept the gap) —
    they chose to accept the gap** rather than block Phase 1 on it. Saved as a memory
    (`project_apifootball_settlement_gap`) so it isn't forgotten, and flagged in §7's top priorities
    that Phase 3 is now effectively urgent (closes a real settlement hole), not just "do last" per
    the plan file's original ordering. No bets were actually at risk at the time (the DB wipe above
    cleared the only pending bet in the system), but new ones will accumulate as new fixtures come
    in.
- **Deployed to `fulltime-api`** on the VM per `CLAUDE.md`'s standard publish/scp/systemd-restart
  pattern, after the owner explicitly confirmed the commit-and-deploy step (not just the plan
  overall) — commit `916b1c2`.
- **Not done this session**: Phases 2-4 of the plan (odds, Match Summary/event-fetch, quota-alert
  parity) — see §7 top priorities. `Providers:MarketsSource` is still `"Highlightly"`, so Bet
  Builder odds/markets are unaffected by this session's changes.

---

## 11. 2026-09-10 session (continued) — Phase 2 (odds) + Phase 3 (Match Summary/settlement gap)

Owner asked to do Phase 2 and Phase 3 next, explicitly bumping Phase 3 ahead of its plan-file
ordering per §10's finding (it closes the settlement gap, not just a display nicety). Also asked
mid-session to work out safe live-score/odds-refresh cadences to stay within API-Football's quota.

### 11.1 Real data confirmed live before writing any parsing code

Per the plan's own instruction not to guess field shapes from memory - confirmed via direct `curl`
against the VM's real API-Football key, against real fixtures (not the sandbox):

- **Match events** (`/fixtures/events?fixture=`): real type/detail pairs seen were `Goal`/"Normal
  Goal", `Goal`/"Penalty", `Goal`/"Own Goal", `Card`/"Yellow Card", `subst`/"Substitution N",
  `Var`/"Goal cancelled", `Var`/"Penalty cancelled" - confirms `player`/`assist`/`detail` fields
  exist as the plan assumed (previously only `time`/`team`/`type` were mapped). `assist` is always
  a present object, just `{"id": null, "name": null}` when there's no assist.
- **Odds** (`/odds?fixture=&bookmaker=8`, Bet365): 105 bet types on one real upcoming fixture.
  Confirmed real names/shapes for `Match Winner` (Home/Draw/Away), `Goals Over/Under` ("Over
  2.5"/"Under 2.5"), `Both Teams Score` (Yes/No), `Exact Score` ("1:0" style), `Team To Score First`
  (Home/Away/"No goal"), `Home/Away Corners Over/Under`, `Home/Away Team Total Cards`, `Home/Away
  Anytime Goal Scorer` (value = full player name, Yes-only), `Home/Away Player Shots`/`Player Shots
  On Target Total`/`Player Fouls Committed` (all a `"PlayerName - N"` ladder - one discrete Over-N.5
  price per count, no matching Under price at all for this shape).
- **Two real discrepancies from the plan's assumptions, found this way rather than guessed**:
  - `Player Assists` is a **whole-match Yes/No proposition** under Bet365 via API-Football, not
    per-player - deliberately left unmapped (falls through the parser's tolerant null/skip path,
    same as everywhere else in this codebase) rather than wired up wrong.
  - No per-player card market exists under this bookmaker at all - `PlayerCard`/`PlayerRedCard`
    just never get rows from API-Football odds, same real gap the-odds-api already had (§7.4/§6.13).
  - Statistics endpoint field names confirmed too: `Corner Kicks`, `Yellow Cards`, `Red Cards`
    (nullable - comes back `null` rather than `0` when a team has none).

### 11.2 Phase 3 - Match Summary / settlement gap closed

- `FullTime.Api/BetBuilder/Dtos/ApiFootballDtos.cs` - `FixtureEventDto` extended with `Player`
  (required), `Assist` (nullable), `Detail` (required); new `EventPlayerInfo` (nullable id/name,
  since `assist` needs nullable fields the existing stricter `PlayerInfo` doesn't have). New odds
  DTOs (`OddsFixtureResponseDto`/`BookmakerOddsDto`/`BetOddsDto`/`BetValueDto`) for Phase 2.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballSettlementSupportService.cs` -
  `ResolveFirstGoalScorersAsync` renamed to `ResolveMatchEventsAsync` and broadened: now also fetches
  and stores `MatchEvent` rows and sets `EventsFinalizedAt` for every finished match (previously only
  set `FirstGoalScorerSide`) - this is the actual fix for §10's settlement gap.
  New `RefreshLiveMatchEventsAsync` powers Match Summary's live display while `InProgress`, on its
  own cadence (`ApiFootballOptions.LiveEventsRefreshIntervalSeconds`, see §11.4), separate from
  settlement (which only ever runs once a match is `Finished`).
  New `MapEventType` normalizes API-Football's `type`/`detail` pairs to the exact canonical strings
  Highlightly already produces (`"Goal"`, `"Penalty"`, `"Own Goal"`, `"Yellow Card"`, `"Red Card"`,
  `"Substitution"`, `"VAR Goal Cancelled"`, `"VAR Goal Confirmed"`, `"VAR Penalty"`) - anything
  genuinely unrecognized falls through to the raw `Detail` string, which `MatchEventRow.razor`
  already renders as a generic bullet + text rather than crashing (zero client changes needed).
  API-Football's `subst` events put the player going OFF in `player` and the player coming ON in
  `assist` (the reverse of every other event type) - handled, but this is a display-only nuance (no
  `MarketType` settles off substitutions), so a wrong guess there would be cosmetic, not a
  settlement risk.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballMatchSyncBackgroundService.cs` - now also calls
  `RefreshLiveMatchEventsAsync` each tick, same dual-call pattern
  `HighlightlyMatchSyncBackgroundService` already used.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballSettlementSupportBackgroundService.cs` - updated
  call site for the rename.
- Settlement gap from §10 (`project_apifootball_settlement_gap` memory) is now closed - can be
  deleted once this has run in production against a real finished match and confirmed working.

### 11.3 Phase 2 - odds via a new `ApiFootballOddsService`

- **New `MarketType` values** (appended, per the enum's own append-only rule): `TeamCorners`,
  `TeamCards`, `PlayerFoulsCommitted`.
- **New EF migration** `20260910081108_AddApiFootballOddsPhase2Columns` (additive only, reviewed via
  `dotnet ef migrations script` before applying anywhere - **not yet applied to production, see §7**):
  `Match.ApiFootballOddsLastFetchedAt`/`HomeCorners`/`AwayCorners`/`HomeCards`/`AwayCards`,
  `MatchPlayerStat.FoulsCommitted`.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballSettlementSupportService.cs`'s existing
  `ResolvePlayerStatsAsync` (fixtures/statistics fetch, already running) now also splits corners/
  cards per team into the new columns, instead of only the old summed `Match.TotalCorners` - no new
  API call, same fetch just kept more of what it already returns. Maps `fouls.committed` from the
  fixtures/players fetch into the new `MatchPlayerStat.FoulsCommitted` too.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballClient.cs` - new `GetOddsAsync(fixtureId,
  bookmakerId)`, filtering server-side to one bookmaker (`ApiFootballOptions.OddsBookmakerId`,
  default `8` = Bet365, confirmed live) rather than requesting all 6+ and discarding client-side.
- **New `FullTime.Api/BetBuilder/ApiFootball/ApiFootballOddsService.cs`** - the bet-name→`MarketType`
  mapping table (§11.1's confirmed real names), TTL-tiered freshness (mirrors
  `OddsApiMarketService.NeedsRefresh` exactly, new `ApiFootballOptions.Odds*` fields), delete-then-
  insert scoped to `MatchId` (no DB constraint stops duplicates, same discipline as every other odds
  sync in this codebase).
- **New `FullTime.Api/BetBuilder/ApiFootball/ApiFootballOddsSyncBackgroundService.cs`** - proactive
  background sync iterating every tracked `Upcoming` match each tick (per the owner's explicit "add
  odds to any fixture where we have the markets" request), not on-demand-only like the-odds-api -
  each match's own TTL gate makes most per-match calls same-tick no-ops.
- `FullTime.Api/Program.cs` - registers the new odds service/background service; `MarketsSource`
  branch is now three-way (`ApiFootball` / `Highlightly` / `OddsApi`). **`Providers:MarketsSource`
  has NOT been flipped to `"ApiFootball"` yet** - see §7, this needs the DB migration applied and a
  deploy first.
- **A real ambiguous-lookup bug found and fixed while wiring this up, in `BetService.FindMarketAsync`**:
  `TeamCorners`/`TeamCards` need `Team` as a disambiguator the same way `PlayerName` already
  disambiguates player props ("Home Over 5.5" and "Away Over 5.5" share the same
  MatchId/MarketType/Line/Side) - added `Team` all the way through the stack (`SlipPick` →
  `PickRequest` → `LegPickInput` → `FindMarketAsync`'s new `IsTeamScopedMarket` filter).
  **While fixing this, found the identical bug already live for `PlayerShots`/`PlayerRedCard`** -
  both were missing from `FindMarketAsync`'s `IsPlayerPropMarket` list, meaning two players sharing
  the same Line/Side on either market could have gotten an arbitrary (possibly wrong) player's price
  silently attached to a placed bet. Fixed in the same pass since it's the exact same function/bug
  class - not scope creep, just noticed while already there.
- **Client** (`FullTime.App/FullTime.App.Shared/Pages/BetBuilder.razor`): new `TeamLineSections`
  (four accordion sections - `{Team} corners`/`{Team} cards` - same construction pattern as the
  existing `_totalCorners` block, now factored into a shared `BuildTeamLines` helper). Fixed
  `IsSelected` to also compare `Team` (it previously didn't, which would have made a Home-corners
  selection incorrectly show an Away-corners row at the same line as also "selected").
  **`PlayerFoulsCommitted` deliberately NOT added to the client's player-prop tab** - per §11.1, this
  bookmaker's fouls market isn't team-split, and the existing player-prop rendering fundamentally
  groups by `Team` (`Home`/`Away`); a null-`Team` market would just silently render nothing. The
  settlement/DB side is fully wired regardless, in case a future bookmaker update splits it by team,
  or someone adds squad-based team resolution for it later.

### 11.4 Cadence/quota decisions (owner explicitly asked to work this out)

Confirmed still Pro (7,500/day) as of this session. Rough worst-case-Saturday budget:

| Source | Interval | Rough worst-case cost |
|---|---|---|
| Live score sync (Phase 1, already live) | 10s while any match live, else 3600s idle | ~3,400/day on a heavy multi-kickoff Saturday (~9.5h "something live") |
| Live match-events refresh (Phase 3, new) | 45s per live match (not batched - genuinely 1 call per live match per tick, unlike score sync's single `fixtures?live=all` call) | scales with concurrent live-match count; deliberately slower than Highlightly's ~30s equivalent for exactly this reason |
| Odds sync (Phase 2, new) | Background tick every 15min, but TTL (not the tick interval) actually gates calls: Far 6h / Near 45min (≤24h to kickoff) / Imminent 15min (≤1h to kickoff) | "a few thousand/day" per the original plan's own estimate, given ~14 tracked leagues and ~100+ tracked Upcoming matches at any time |
| Fixture discovery + settlement support | ~14/day + a few hundred/day | negligible |

**Explicitly flagged, not just assumed**: combining all of the above on a genuinely heavy day (many
simultaneous live matches, e.g. a full Champions League night) could plausibly approach or exceed
Pro's 7,500/day ceiling - exactly the plan's own "tight-to-over on a heavy multi-league Saturday"
warning. The account still needs upgrading before this is fully safe at scale (§7 carries this
forward); until then, watch for real 429s in the logs after this goes live, and consider Phase 4
(quota-alert email parity, still not built) sooner rather than later given the new call volume.

### 11.5 Update: everything below WAS completed later the same session

The bullets below were accurate when first written, but the owner approved the full rollout shortly
after — **superseded, see §12**: the EF migration was applied to production, `fulltime-api` and
`fulltime-web` were both deployed, and `Providers:MarketsSource` was flipped to `"ApiFootball"` and
is live. Phase 4 (quota-alert *call-budget* parity, i.e. porting Highlightly's daily-count/threshold
tracking) is still genuinely open - don't confuse it with the stale-match alert email added in §12,
which is a different, narrower thing.

---

## 12. 2026-09-10 session (continued) — full rollout, five live bugs found and fixed, one new safety net

Owner approved the full Phase 2/3 rollout (§11) in one go. Deployed, then spent the rest of the
session finding and fixing real bugs the owner caught by actually using the app - each one below was
found live, fixed, and redeployed the same session, not just theorized.

**Rollout**: EF migration applied to production DB; `fulltime-api` deployed; `Providers:MarketsSource`
flipped to `"ApiFootball"` and deployed; `fulltime-web` deployed. Confirmed via logs after each step
(zero warnings/exceptions) and via direct DB/API checks.

### 12.1 Five real bugs found live and fixed

1. **Odds parsing crashed for every fixture on the very first tick.** Some Bet365 bet values come
   back as raw JSON *numbers*, not strings (`System.Text.Json.JsonException`). Added
   `FlexibleStringConverter` to `BetValueDto.Value`/`Odd` (`ApiFootballDtos.cs`) to accept either
   shape. Also had to reset `Match.ApiFootballOddsLastFetchedAt` to `NULL` for all 117 matches, since
   the pre-fix crash still stamped them as "fetched" despite storing zero rows - without the reset
   they wouldn't have retried for hours under the TTL gate.
2. **No matches showing in the app at all.** `ApiFootballMatchSyncService.UpsertMatchAsync` was
   storing API-Football's own numeric league IDs straight into `Match.LeagueId`, but the client's
   `LeagueCatalog`/`MatchLeaguePreferences` (`FullTime.App.Shared/Services/`) are both keyed on
   **Highlightly's** league IDs regardless of which provider is live - every match failed
   `MatchLeaguePreferences.IsVisible` and got filtered out. Fixed by adding
   `HighlightlyToApiFootballLeagueMap.ApiFootballToHighlightlyLeagueIds` (the reverse of the existing
   map) and translating at upsert time. Self-healed all 117 rows on the next fixture-discovery tick,
   no backfill needed (`UpsertMatchAsync` re-sets `LeagueId` on every upsert, new or existing).
3. **No basic Home/Draw/Away odds on the match list.** `ApiFootballOddsService` only ever wrote
   `BetBuilderMarket` rows - it never populated `OddsSnapshot`, the separate table
   `MatchesController`/`MatchCard.razor` read for the match-card price. Added
   `SnapshotMatchResultIfChangedAsync` (mirrors `BetBuilderSyncService.SnapshotOneXTwoIfChangedAsync`
   exactly), sourced from the same "Match Winner" bet already parsed for Bet Builder.
4. **Skewed West Brom crest on the Bet Builder header.** Its real API-Football logo
   (`media.api-sports.io/football/teams/60.png`) is genuinely non-square (240×150px, confirmed by
   downloading and checking), unlike most crests. `.bb-team .crest` in `app.css` had no
   `object-fit: contain` (the equivalent Matches-list rule, `.match-team .crest`, already did) - added
   it.
5. **`TotalCorners` settlement race** (found via a full sweep review the owner asked for, checked
   against all 15 `MarketType` values) - its readiness gate only required `EventsFinalizedAt`, correct
   under Highlightly (one method set both that and `Match.TotalCorners` together) but wrong under
   API-Football, where `ResolveMatchEventsAsync` and `ResolvePlayerStatsAsync` are separate calls on
   separate cadences. A sweep landing between them would settle a pick against a still-null value,
   permanently. Added `PlayerStatsResolvedAt` as a second gate requirement
   (`SettlementService.TeamStatMarketTypes`). Currently dormant - Bet365 odds aren't mapped to
   `TotalCorners` at all today (see §11.1) - but a real latent bug if that market is ever wired up.

Also fixed in the same pass as #2/#3 above (not separately bug-reported, found while touching the
exact same code): `BetService.FindMarketAsync`'s `IsPlayerPropMarket` list was missing
`PlayerShots`/`PlayerRedCard`/`PlayerFoulsCommitted` - two players sharing the same Line/Side on
those markets could have gotten an arbitrary (possibly wrong) player's price attached to a placed
bet. Fixed alongside adding `Team` as a proper disambiguator for the new `TeamCorners`/`TeamCards`
markets (threaded through `SlipPick` → `PickRequest` → `LegPickInput` → `FindMarketAsync`).

### 12.2 Postponed-flag verified against real data

Checked API-Football directly across all 11 tracked leagues for a real `PST` fixture - found one
(Oxford Utd v Reading, League One, same match from the Highlightly-era saga in §6.9/§6.12, still
showing `PST`). Confirmed `"PST"` is a genuine real status code, and confirmed via code that
`DeriveStatus` is called from `UpsertMatchAsync`, which both `RefreshFixturesAsync` (discovery) and
`RefreshLiveAsync` (live sync) call - so any tracked fixture reporting `PST` gets classified
`Postponed` correctly regardless of which path found it. **Caveat**: that specific Oxford/Reading row
isn't in our tracked DB any more (wiped in §10's cleanup, and its kickoff date is now permanently
outside the 7-day discovery window), so this is code-path-verified against real data, not yet
observed flipping a live row in our own system end-to-end.

### 12.3 Bookmaker-coverage research (no code change)

Owner asked whether other bookmakers offer more player-prop/card/corner coverage than Bet365. Checked
live across all bookmakers API-Football returns for real fixtures (up to 14 bookmakers on one
fixture). Findings: Bet365 remains the richest single source (most markets, most complete Home+Away
pairing for shots/goalscorer); Team Cards/Corners exist on several other bookmakers too but Bet365
has them once close enough to kickoff (a timing thing, not a Bet365 gap); **`Player Assists` is a
whole-match Yes/No prop on every bookmaker checked, and no bookmaker offers a per-player card market
at all** - both confirmed universal, not something switching bookmakers would fix. Owner decided to
leave the bookmaker choice as-is.

### 12.4 New: stale-InProgress-match protection

Owner's idea, in case something *other* than the Postponed bug ever exhausts the quota again:
`HasLiveMatchAsync` (which drives the 10s-vs-3600s live-poll cadence) previously counted *any*
`InProgress` match - one stuck there forever (abandoned game, or API-Football drops it from
`fixtures?live=all` before a clean `FT`) would force the expensive cadence permanently, the same
quota-risk class as the Postponed bug, just a different trigger.

- `ApiFootballOptions.StaleInProgressMinutes` (default 210 = 3.5h, generous for ET/penalties/delays) -
  matches older than this while still `InProgress` are excluded from `HasLiveMatchAsync`'s check.
- New `ApiFootballMatchSyncService.CheckStaleInProgressMatchesAsync`, called every tick from
  `ApiFootballMatchSyncBackgroundService`, emails an alert (once per UTC day per match, same dedup
  pattern as Highlightly's quota alerts) to `ApiFootballOptions.AlertEmail` - excluding it from the
  cadence check doesn't fix the underlying stuck data, and any bets on it still can't settle until a
  human corrects it.
- New env var on the VM: `Environment=ApiFootball__AlertEmail=alan@jtmtechnology.co.uk` in
  `/etc/systemd/system/fulltime-api.service` (original backed up as `.bak3`).

### 12.5 Bet Builder UI: consolidated Corners/Cards, reinstated bookmaker row

- **Corners/Cards accordions consolidated**: was four flat top-level sections (Home Corners, Away
  Corners, Home Cards, Away Cards); now two (`Corners`, `Cards`), each holding a nested per-team
  section with the team's badge and name - same nested-accordion pattern the player-prop tab already
  used. `BetBuilder.razor`'s `TeamLineSection`/`TeamLineSections` became
  `TeamLineTeamGroup`/`TeamLineMarketGroup`/`TeamLineGroups`.
- **Bookmaker attribution row reinstated.** A "Prices from `<Bet365 logo>`" row existed on this page
  once before, removed in the 2026-09-07 Highlightly-settlement-pivot session
  (`BetBuilderMarketsResponse.Bookmaker`/`BookmakerLogoUrl` kept being populated server-side the whole
  time, just never rendered client-side). Re-added the row and the `_bookmaker`/`_bookmakerLogoUrl`
  fields. Also fixed the server side while there: `MatchesController.GetBetBuilderMarkets`'s
  bookmaker resolution only ever branched on `OddsApi` vs "everything else", so under the live
  `ApiFootball` provider it was labelling Bet365's own prices using **Highlightly's**
  `BookmakerName` config value - coincidentally the same string today ("bet365"), but the wrong
  source and a latent bug if either config ever changed independently. Added a proper third branch
  plus `ApiFootballOptions.BookmakerName`.

### 12.6 Files touched this session, beyond what §11 already listed

- `FullTime.Api/BetBuilder/Dtos/ApiFootballDtos.cs` — `FlexibleStringConverter`.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballMatchSyncService.cs` — league-ID translation fix;
  `HasLiveMatchAsync` stale-exclusion; new `CheckStaleInProgressMatchesAsync`; `IEmailSender` added to
  constructor.
- `FullTime.Api/BetBuilder/ApiFootball/HighlightlyToApiFootballLeagueMap.cs` — reverse map added.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballMatchSyncBackgroundService.cs` — wired in the new
  stale-match check.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballOddsService.cs` — `SnapshotMatchResultIfChangedAsync`.
- `FullTime.Api/BetBuilder/ApiFootball/ApiFootballOptions.cs` — `StaleInProgressMinutes`, `AlertEmail`,
  `BookmakerName`.
- `FullTime.Api/Betting/SettlementService.cs` — `TotalCorners` added to `TeamStatMarketTypes`.
- `FullTime.Api/Betting/BetService.cs` — `IsPlayerPropMarket` completed; `Team` disambiguator threaded
  through for team-scoped markets.
- `FullTime.Api/Controllers/BetsController.cs`, `FullTime.Api/Controllers/MatchesController.cs` —
  `Team` on `PickRequest`; provider-aware bookmaker resolution.
- `FullTime.Api/appsettings.json` — new `ApiFootball` fields (see above); `Providers:MarketsSource` →
  `"ApiFootball"`.
- `FullTime.App/FullTime.App.Shared/Pages/BetBuilder.razor` — accordion restructure; bookmaker row;
  `IsSelected` Team fix.
- `FullTime.App/FullTime.App.Shared/Services/BetSlipState.cs`, `.../Models/ApiModels.cs`,
  `.../Components/BetSlipSheet.razor` — `Team` threaded through `SlipPick`/`PickRequest`.
- `FullTime.App/FullTime.App.Shared/wwwroot/app.css` — `.bb-team .crest` `object-fit: contain`.
- VM: `/etc/systemd/system/fulltime-api.service` — `ApiFootball__AlertEmail` added (`.bak3`).

Commits this part of the session, all pushed to `main`: `49c258f`, `a3f71f1`, `1b253fd`, `2b50ef4`,
`618b774`, `bb82e4c`, `0f272f2`. `fulltime-api` and `fulltime-web` are both running the latest of
these as of this handover.

---

## 13. 2026-09-10 session (new session) — Android release build v1.4, R8 obfuscation investigation

New conversation, same calendar day as §10-§12's rollout. Two things done, neither touching the
backend/API-Football work above.

### 13.1 Play Store release build v1.4 (code 11)

- Owner confirmed version code 10 (1.3, built §6.8) was already uploaded to Play Console, so bumped
  `ApplicationDisplayVersion`/`ApplicationVersion` 1.3/10 → 1.4/11 in `FullTime.App.csproj` and
  rebuilt the signed AAB per `signing/README.md`. This build bundles everything from §10-§12 that
  touches `FullTime.App.Shared` (the Bet Builder accordion restructure, bookmaker row, crest fix,
  etc.) — none of that had reached Android before this build.
- Output: `FullTime.App/FullTime.App/bin/Release/net10.0-android/com.jtmtechnology.fulltime.app-Signed.aab`.
- Commit `a379ab3`, pushed to `main`. **The AAB itself still has not been uploaded to Play
  Console** — that's the only remaining step, and it now also needs the form-dots feature (§13.3)
  folded in, since that landed after this build.

### 13.2 Play Console "app optimisation" warning — R8 obfuscation — investigated, not implemented

- Owner shared a Play Console screenshot: "App optimisation is below our threshold — Obfuscation
  (1%)", deadline Feb 2027 (Google's mandatory 25% floor takes effect then, but only enforced once
  DEX exceeds 10MB for apps — worth checking whether this app is even near that size before treating
  it as urgent).
- **Root cause confirmed empirically, not guessed**: `FullTime.App.csproj` never sets
  `AndroidLinkTool`, so R8 doesn't run at all today. Checked the just-built Release AAB's output
  folder for `mapping.txt` (R8 generates this automatically whenever it actually runs, per
  Microsoft's own docs) — absent, confirming R8 is fully inactive, not just non-obfuscating.
- **The fix is one line** (`<AndroidLinkTool Condition="'$(Configuration)'=='Release'">r8</AndroidLinkTool>`)
  but carries real risk: this app binds four Java/reflection-heavy plugins —
  `Plugin.FirebasePushNotifications`, `Plugin.MauiMTAdmob` (AdMob), `Plugin.InAppBilling` (Google
  Play Billing), `Xamarin.Google.UserMessagingPlatform` (UMP consent) — and R8 obfuscation is
  confirmed (via Firebase's own and dotnet/maui's issue trackers) to break libraries like these
  **silently in Release builds only**, while Debug keeps working fine. Any fix needs a `proguard.cfg`
  with explicit `-keep` rules for those four packages' Java namespaces, plus an actual on-device
  smoke test of push notifications, ads, in-app purchase, and the UMP consent dialog before it ever
  reaches a real release — not just a build-succeeds check.
- **Owner's call: not now** — deferred to a dedicated future session given the Feb 2027 runway. No
  code changes made. If picked up later, start from `AndroidLinkTool=r8` + the four keep-rule
  packages above, and treat "build succeeds" as necessary but nowhere near sufficient.

### 13.3 New: "last 5 results" form dots on the Bet Builder header

Owner asked for a form-guide widget (colored dots under each team name, one per recent result) after
sharing a reference screenshot. Built, then improved once the owner asked whether API-Football had a
dedicated endpoint for it - it does, and it's a better source than what was first built.

- **First pass**: computed from our own `Matches` table (last 5 `Finished` rows for a team, any
  tracked competition, W/D/L relative to that team). Worked, but confirmed via direct DB query that
  **zero teams currently have both a finished and an upcoming match** in the DB (the only `Finished`
  rows left are clustered right around yesterday's API-Football cutover) - every team would show 0
  dots for weeks until enough new fixtures complete under the new provider's team-ID scheme.
- **Switched to API-Football's own `/teams/statistics?league=&season=&team=` endpoint** instead -
  confirmed live via `curl` that it returns a bare `form` string (e.g. `"LWD"`), and cross-checked
  against Manchester United's actual fixture dates/results to confirm the ordering is chronological,
  oldest-first (matches the design decision below exactly, no reordering needed). The endpoint is
  **league-scoped** (`league` is a required param, confirmed the API rejects a call without one) -
  so "form" means "form in this specific competition," the same convention most football apps use.
- **New `ApiFootballTeamFormService`** - 6h in-memory cache per (team, league) so repeated Bet
  Builder views don't each cost a fresh API call; tolerates failures (never breaks the rest of the
  page, form is display-only).
- **A real bug found and fixed while testing against a live match, not just a hypothetical**:
  `Match.LeagueId` is *always* stored in Highlightly's own ID space regardless of which provider is
  live (a deliberate fix from §12.1 for the client's league catalog) - calling API-Football's form
  endpoint with it directly returned wrong/empty data. Fixed by translating through the existing
  `HighlightlyToApiFootballLeagueMap.LeagueIds` first. Caught this because the very first test match
  (Man Utd v Sabah FA, a friendly) returned empty form for both teams, which led to checking the
  actual `LeagueId` value rather than assuming the fetch was just failing quietly.
- Design decisions (asked explicitly, not assumed): green=win/red=loss/grey=draw, oldest-to-newest
  left-to-right (most recent dot rightmost) - both confirmed against the owner's reference
  screenshot before building.
- **Verified end-to-end, not just build-succeeds**: deployed to `fulltime-api`+`fulltime-web`,
  curled the real endpoint against Stevenage v Luton (`D,L,D,D,W` / `W,D,L,W,D`), confirmed the
  rendered HTML had the matching dot counts/colors, then built and ran the actual MAUI app on the
  `FullTime_Pixel8_API35` emulator and the owner confirmed it visually.
- Files: `FullTime.Api/BetBuilder/ApiFootball/ApiFootballClient.cs` (new `GetTeamFormAsync`),
  `.../ApiFootballTeamFormService.cs` (new), `FullTime.Api/BetBuilder/Dtos/ApiFootballDtos.cs`
  (`TeamStatisticsResponseDto`/`TeamStatisticsInfo`), `FullTime.Api/Controllers/MatchesController.cs`
  (`BetBuilderMarketsResponse.HomeForm`/`AwayForm`), `FullTime.Api/Program.cs` (DI registration),
  `FullTime.App/FullTime.App.Shared/Models/ApiModels.cs` (client DTO),
  `.../Pages/BetBuilder.razor` (`.bb-form` dots under each team name),
  `.../wwwroot/app.css` (`.bb-form`/`.bb-form-dot` rules, reusing `--accent`/`--danger`/`--text-muted`).
- Commit `d57e348`, pushed. `fulltime-api`/`fulltime-web` both running it as of this handover -
  **already fully live**, nothing pending here except folding it into the next Android AAB (§13.1).

---

## 14. 2026-09-10 session (new session) — Team Corners/Cards bet-description bug fix

Owner reported "cards and corners bets not showing in bet descriptions." Client-only fix, no
backend/DB involved.

- **Root cause**: `TeamCorners`/`TeamCards` (new `MarketType` values from Phase 2, §11.3) were never
  added to the pick-label formatters that turn a pick's `MarketType`/`Line`/`Side`/`Team` into
  readable text (e.g. "Man Utd Over 8.5 corners") - they fell through to the bare `pick.Side` default
  ("Over"/"Under" with no team name or market word at all).
- **Found the same formatter duplicated in two places that had drifted apart**:
  `FullTime.App.Shared/Services/BetDisplay.cs` (used by `MyBets.razor` and Leaderboard's Live Bets -
  shown *after* a bet is placed) and a separate, near-identical inline switch inside
  `FullTime.App.Shared/Components/BetSlipSheet.razor` (the bet slip shown *before* placing - most
  likely what the owner actually saw). `BetDisplay.cs`'s own header comment only mentions the first
  two consumers, not the slip - this duplication is why the bug could exist in one place but not
  necessarily be caught by testing the other. **Not deduplicated in this pass (kept the fix minimal
  and matched the existing pattern) - worth refactoring `BetSlipSheet.razor` to call `BetDisplay`
  directly next time either one needs a change, so they can't drift again.**
- **Fix**: added `TeamCorners`/`TeamCards` cases to both switches (new shared-shape `TeamName` local
  helper mapping the pick's `"Home"`/`"Away"` `Team` field to the actual team name, same convention
  `FirstTeamToScore` already used). Also added `PlayerFoulsCommitted` to both, which had the exact
  same gap (a real `MarketType` with no case, silently falling through) even though the owner hadn't
  reported it yet.
- Verified via `dotnet build` on `FullTime.App.Shared` (0 errors) - not yet run in the emulator/web
  head this session.
- Files: `FullTime.App/FullTime.App.Shared/Services/BetDisplay.cs`,
  `FullTime.App/FullTime.App.Shared/Components/BetSlipSheet.razor`.
- Commit `bc9a77b`, pushed to `main`. **Not yet deployed** - `fulltime-web` needs redeploying to pick
  it up, and it needs folding into the next Android AAB alongside the still-pending form-dots feature
  (§13.1/§13.3). Owner's stated next step is triggering an iOS TestFlight build to test on a real
  device - this fix will be included in that build once triggered, along with a chance to verify
  §6.11's stale-notification-icon hypothesis at the same time.

---

## 15. 2026-09-11 session — live-score stuck-at-90 fix, added time, per-pick bet coloring, friend bet-history

New session, continuing directly from §14. `fulltime-web` was deployed for §14's fix as part of this
session's own work (see §15.2) rather than as a separate step.

### 15.1 Matches stuck `InProgress` at 90' forever — real production bug, found and fixed

- Owner reported "tonight's matches are stuck at 90 mins". Checked the production DB directly: every
  match that had kicked off that evening was `Status = InProgress`, `Minute = 90`, scores already at
  their correct final values — confirmed via a direct `curl` against API-Football (from the VM; this
  dev machine's own network can't reach `v3.football.api-sports.io` at all, TLS handshake fails at the
  Windows `schannel` layer) that one of them (Man Utd 4-0 Sabah FA) was genuinely `"FT"` per the
  provider already.
- **Root cause**: `ApiFootballMatchSyncService.RefreshLiveAsync` only ever polls `fixtures?live=all`
  (the whole point of the cutover - 1 call regardless of match count). API-Football drops a fixture
  from that live list the moment it actually finishes, sometimes before a clean `"FT"` tick ever
  reaches us - once dropped, nothing calls `UpsertMatchAsync` for it again until the **once-a-day**
  fixture-discovery tick happens to notice, which is why the 2026-09-06..09 backlog eventually
  self-corrected but that evening's matches (finished within the last couple of hours) hadn't yet.
- **Fix (commit `7e7b74a`, deployed same session)**: new `ApiFootballClient.GetFixturesByIdsAsync`
  (`fixtures?ids=id1-id2-...`, chunked at 20 per API-Football's documented limit).
  `RefreshLiveAsync` now also finds any DB match still `InProgress` that didn't come back in that
  tick's live list and re-fetches those specific IDs directly. Verified live: all 7 stuck matches
  flipped to `Finished` with correct scores within seconds of deploying, no manual DB fix needed.
- **Follow-up robustness fix (commit `5b72c41`, deployed same session)**: the new follow-up fetch
  wasn't wrapped in a try/catch, so a transient failure fetching the dropped-from-live matches would
  throw *before* `SaveChangesAsync` ran, discarding that tick's already-processed normal live-score
  upserts too - not something that had actually happened yet, just a decompiled-while-writing-this-
  handover gap. Wrapped in the same log-and-continue pattern `RefreshFixturesAsync` already uses per
  league; never observed firing in production, see §7 item 5.

### 15.2 "Shots and cards bets haven't settled" — investigated, turned out to be a harmless one-off race

- Same evening, owner reported Stevenage v Luton's `PlayerShotsOnTarget`/`PlayerShots`/
  `TeamCorners`/`TeamCards` picks hadn't settled. Traced to `EventsFinalizedAt`/`PlayerStatsResolvedAt`
  both still `null` on the just-fixed matches.
- **Not a new bug**: `ApiFootballSettlementSupportBackgroundService`'s first tick after the §15.1
  redeploy happened to run its DB query in the narrow window *before* the live-sync tick had actually
  flipped those matches to `Finished`, so it found nothing to resolve that cycle. Its next scheduled
  tick (5 minutes later, `GoalScorerResolutionIntervalMinutes`) picked everything up normally and the
  picks settled correctly. No code change or manual DB correction was needed - confirmed by waiting for
  the next tick and re-checking.

### 15.3 Live minute now shows stoppage time ("90+3'") instead of capping at the base minute

- API-Football's fixture status carries `elapsed` (base minute) and a separate `extra` (added-time)
  field that was never being parsed or stored - the client had no way to distinguish "90th minute" from
  deep into stoppage time, both showed as a stuck-looking `"90'"`.
- Added end-to-end: `StatusInfo.Extra` (`ApiFootballDtos.cs`), `Match.AddedTimeMinutes` (new nullable
  int column, migration `20260911070744_AddMatchAddedTimeMinutes` - additive only, applied to
  production), populated in `ApiFootballMatchSyncService.UpsertMatchAsync` and included in the
  `changed`-detection and `MatchLiveUpdate` SignalR broadcast, added to `MatchesController`'s
  `UpcomingMatchDto` and the client's mirror in `ApiModels.cs`, threaded through
  `MatchUpdatesClient.cs`/`Matches.razor`'s live-patch handler, and formatted in both
  `MatchCard.razor.LiveClockText()` and `MatchEvents.razor`'s status line as `"{minute}+{extra}'"` when
  extra is present, else the plain `"{minute}'"` as before.
- **Highlightly has no equivalent field** - its `MatchLiveUpdate` call site now passes `null` for
  `AddedTimeMinutes` explicitly; harmless since Highlightly is a dormant rollback path (§7 item 3).
- Commit `e649e10` (same commit as §15.4 below). Deployed to `fulltime-api`/`fulltime-web` same
  session; migration applied to production DB first.

### 15.4 Club crests added to Bet Builder's First Team to Score and Correct Score picks

- Both markets showed plain team-name text with no crest, unlike every other market row on the page.
  `BetBuilder.razor`'s `PickOption` record gained an optional `LogoUrl`; `FirstScorerOptions` now
  passes `HomeLogo`/`AwayLogo` (already available as page query params) for the Home/Away options
  (None stays logo-less). Reused the existing `player-team-logo` CSS class and the `player-name-cell`
  wrapper pattern the player-prop rows already used, rather than inventing new markup. Correct Score's
  two `score-stepper` team-name spans got the same crest, same `HomeLogo`/`AwayLogo` source.
- Same commit `e649e10` as §15.3.

### 15.5 My Bets: each pick in a multi-pick leg now gets its own line and its own red/green

- A same-game multi (e.g. BTTS + a player shots-on-target prop, both on one match, one "leg") was
  joined into a single line and colored by the *leg's* aggregate `Outcome` - a leg with one winning and
  one losing pick read as fully one color. `BetLegPickDto` already carries each pick's own `Outcome`;
  `MyBets.razor` just wasn't using it. Now every pick renders on its own line under a shared match
  header, each colored by `BetDisplay.StatusClass(pick.Outcome)` independently.
- New CSS: `.bet-leg-header`, `.bet-leg-picks` (indented nested list).
- Commit `6fe1953`.

### 15.6 New: tap a leaderboard name to see their last 5 bets (My Leagues only)

- Owner asked for this assuming it already existed - it didn't (confirmed via search: no click handler
  anywhere, no per-user bets endpoint beyond `GET /api/bets/me`). Built from scratch:
  - **New `GET /api/bets/user/{userId}`** (`BetsController.cs`) - deliberately *not* open to any
    authenticated caller for any `userId` (would let a stranger enumerate another family's bet history
    by guessing a GUID). Only returns data if the caller shares at least one league with the target
    (same boundary `GetLeagueLeaderboard`/`GetPendingBets` already draw), 404 otherwise per those
    endpoints' existing "don't confirm the id exists" convention. Returns the same `BetDto` shape as
    `/me`, capped at 5, via the controller's existing `ToBetDto` projection.
  - **New `ApiClient.GetUserBetsAsync(Guid userId)`** mirroring `GetMyBetsAsync`.
  - **New `Components/BetList.razor`** - the bet-card/leg/pick markup extracted out of `MyBets.razor`
    (which now just calls `<BetList Bets="_bets" CurrencySymbol="..." />`) so the friend-bets view and
    My Bets render identically and can't drift apart - same reasoning `BetDisplay.cs`'s own header
    comment already documents for its two original consumers.
  - **New `Components/FriendBetsPanel.razor`** - takes `UserId`/`CurrencySymbol`, fetches on mount
    (one on-demand call per expand, no pre-loading every row - consistent with the standing
    no-background-polling preference), renders `LoadingSpinner` / an error / "No bets placed yet." /
    `BetList`.
  - **`Leaderboard.razor`**: rows in the **My Leagues** table are now tappable (`_expandedUserId`
    toggle state, one row expanded at a time), expanding a `<tr>` below with the panel. A
    `"Tap a name to see their last 5 bets."` hint sits above the table (with a dedicated `.tap-hint`
    margin-top class added after the owner flagged it sitting flush against the "Invite friend" button
    above it).
  - **Initially also added to the Worldwide Top 50 table, then explicitly reverted** at the owner's
    request (commit `8177b3a`) - Worldwide can list people outside your leagues, who the endpoint's own
    privacy check would just reject (404) anyway; the tap affordance only belongs where every row is
    guaranteed shared-league. See §7 item 11.
- Commits `4c0a7c6`, `8177b3a` (Worldwide revert), `33b27df` (hint-text spacing).

### 15.7 Emulator testing workflow — new build-error variant of a known gotcha

- Used the `FullTime_Pixel8_API35` emulator repeatedly this session to verify each change live (crests,
  added-time formatting once a live match reaches it, My Bets coloring, friend-bets panel).
- Hit a build error not previously documented: `APT2258: The data is invalid. (13)` on a `.flata`
  resource archive under `obj/Debug/net10.0-android/lp/...` - same underlying class of problem
  `CLAUDE.md`'s `XARLP7024` file-lock gotcha already covers (a corrupted incremental Android build
  artifact), just a different symptom/error code. Same fix worked: `dotnet build-server shutdown`,
  then PowerShell `Remove-Item -Recurse -Force` on the MAUI head's `obj`/`bin` (bash `rm -rf` is the
  one already flagged as sometimes insufficient here). Worth updating `CLAUDE.md`'s gotcha wording
  next time it's touched to mention APT2258 as another possible symptom, not just XARLP7024.

### 15.8 Deploy state as of this handover

- `fulltime-api` running commit `5b72c41` (latest - includes §15.1's fix, its own follow-up resilience
  fix, and §15.6's new endpoint).
- `fulltime-web` running commit `33b27df` (latest client-affecting commit; `5b72c41` is API-only, no
  web redeploy needed for it).
- Production DB has migration `20260911070744_AddMatchAddedTimeMinutes` applied.
- All commits this session pushed to `main`: `7e7b74a`, `e649e10`, `6fe1953`, `4c0a7c6`, `8177b3a`,
  `33b27df`, `5b72c41`.
- Nothing from this session has reached an Android build yet - see §7 item 2.

---

## 16. 2026-09-11 session (new session, same day as §15) — Bet Builder Boost feature, Daily Spinner evens+ rule, Matches tab football icon

New session, continuing directly from §15. Three separate pieces of work: a small UI-polish pass, a
new tab icon, and a substantial new feature (Bet Builder Boost) built, then iterated on across
several rounds of the owner testing it live on the emulator and reporting real bugs.

### 16.1 Three quick UI fixes (commit `33d8f4c`)

- **Uneven spacing between bet cards** (`BetList.razor`, used by both My Bets and the friend-bets
  panel) - `.bet-card` had `margin-top` on every card including the first, which stacked with
  whatever padding the parent already had, making the gap above the first card bigger than the gap
  between cards. Fixed with a `.bet-list` flex container using `gap` instead.
- **Leaderboard's "Bets Placed" column overflowed the screen** - that header's own nowrap text
  forced the whole table wider than the viewport, clipping the last column flush against the right
  edge with zero margin. Renamed to "Pending" (also more accurate - it shows a stake amount, not a
  count), which lets the table fit without horizontal scroll.
- **"Over 2.5" read as ambiguous** next to labelled picks like "corners"/"cards" in bet descriptions
  - added "goals" (`BetDisplay.cs` and its duplicate switch in `BetSlipSheet.razor`).

### 16.2 Matches tab icon: house → football (commit `5840ae6`)

The bottom-nav "Matches" tab used a generic house icon (`BottomTabBar.razor`), unrelated to what the
tab shows. Two rounds of hand-drawn pentagon/hexagon SVG geometry (symmetric line-art, then a
filled-panel variant) were tried and rejected as not reading like a football at 32px - mocked up via
an Artifact for side-by-side comparison each round. Settled on pulling the real, unmodified
**ball-football** icon from Tabler Icons (MIT licensed) instead of continuing to guess coordinates -
its hand-tuned, slightly asymmetric seam angles read correctly as a ball where a perfectly symmetric
attempt didn't.

### 16.3 New feature: Bet Builder Boost (bet365-style daily promo)

Owner asked for a "Boost your Bet Builder"-style banner (shown a real bet365 screenshot as
reference: 25% winnings boost, 3+ selections, combined odds of Evens or greater). Built, then
refined over several rounds of the owner testing it live and reporting real bugs - see 16.3.2
onward. Final design:

- **One shared match per day** (`BetBuilderBoost` table: `Date` unique, `MatchId`), not randomized
  per user - the same "match of the day" every family member sees, matching how bet365 runs its own
  promo. Picked at random (`BetBuilderBoostService.GetOrPickTodaysMatchAsync`) from that day's
  Upcoming fixtures in the Premier League/Championship/League One/League Two (same four IDs as
  `LeagueCatalog.AlwaysVisible[..4]`, mirrored server-side since `FullTime.Api` can't reference the
  client RCL) that already have Bet Builder odds priced. Never re-picked once chosen for the day.
- **Per-user usage tracking** (`User.LastBetBuilderBoostDate`) is separate from the shared match
  pick - each family member can use the day's featured match's boost once, independent of whether
  others already have.
- **Terms, enforced server-side, matching bet365's own wording exactly**: 3+ selections
  (`BettingOptions.BetBuilderBoostMinSelections`, default 3) AND combined odds over Evens (2.00) -
  both required, checked in `BetBuilderBoostService.TryConsumeBoostAsync`, never trusting the client.
- **Opt-in only, not automatic** - the boost (and its rules/messaging) only ever apply if the owner
  actually reached that match's Bet Builder through the banner's own link, not by browsing to the
  same match normally from the Matches list. Carried via a `boost=1` query param
  (`BetBuilderBoostBanner.Go()` → `BetBuilder.razor`'s `BoostFlag` → `SlipLeg.ViaBoost` →
  `PlaceBetRequest.ViaBetBuilderBoost`) all the way through to placement. Reaching the same match's
  Bet Builder any other way behaves as a completely normal Bet Builder page, no rules shown or
  applied - an explicit owner correction after the first pass applied the boost to *any* qualifying
  bet on the featured match regardless of how it was reached.
- **Blocks placement while opted in but short of the criteria** - deliberately not "place anyway,
  just unboosted": if the owner tapped the banner and is building a bet on that match, `BetSlipSheet`
  hides the Place Bet button and shows which requirement (selections, odds, or both) is still unmet,
  rather than letting a bet someone built specifically for the boost quietly go through without it.
  Reaching the match any other way is completely unaffected by this gate.
- **Priority over an already-won Daily Spinner boost** - see 16.3.2 point 3, a real bug found live.
- 25% multiplier applied to `CombinedOdds` at placement (same mechanism as the existing Daily
  Spinner boost - stake untouched, `PotentialReturn` falls out of the boosted odds), label stored in
  the existing `Bet.BoostApplied` column, so My Bets/friend bet history needed zero changes to
  display it.

New files: `FullTime.Api/Models/BetBuilderBoost.cs`, `FullTime.Api/Betting/BetBuilderBoostService.cs`,
`FullTime.Api/Controllers/BetBuilderBoostController.cs`,
`FullTime.App/FullTime.App.Shared/Components/BetBuilderBoostBanner.razor`. Migration
`20260911091829_AddBetBuilderBoost` (additive: `BetBuilderBoosts` table, `Users
.LastBetBuilderBoostDate`) - **applied to production** via an idempotent
`dotnet ef migrations script` reviewed then run with `psql -f` on the VM, same pattern as prior
migrations.

**New durable dev-tooling fix**: `dotnet ef migrations add`/`database update` were failing on this
dev machine because EF's tooling boots the *entire* `Program.cs` host (including `FirebaseApp
.Create`) just to get a `DbContext`, and there's no local `Push:ServiceAccountPath` file configured.
Added `FullTime.Api/Data/AppDbContextFactory.cs` (`IDesignTimeDbContextFactory<AppDbContext>`) so EF
tooling builds just the `DbContext` directly from `appsettings.json`'s connection string, bypassing
Program.cs entirely. Permanent fix, not a one-off workaround - needed again for any future migration
on this machine.

#### 16.3.1 Design questions resolved via AskUserQuestion before building

- One global match/day, not per-user (bet365-style, simpler to store/reason about).
- Fixed 25%, not tied to the spinner's own boost mechanism or made configurable in appsettings.json.
- Block placing until the slip clears the criteria (later refined further - see 16.3.3).
- League scope: owner specified all four English pyramid tiers (Premier League/Championship/League
  One/League Two), wider than either offered option (Premier League alone, or +Championship).

#### 16.3.2 Real bugs found live-testing on the emulator, fixed same session

1. **"Add to slip" unconditionally navigated back to Matches and opened the bet slip** - every tap
   bounced the owner out of Bet Builder before they'd finished picking toward the 3+ minimum. This
   was the actual mechanism behind the owner's "don't close the Bet Builder page" report. Fixed:
   while opted into the boost and still short of the minimum, `AddToSlipAsync` now stays on the page
   and shows how many more selections are needed instead of navigating away (`2e4b64d`).
2. **Banner's link only carried `matchId` + `boost=1`**, unlike every other Bet Builder entry point
   (`MatchCard.BetBuilderHref()`), which also sends `home`/`away`/`kickoff`/`league`/logo query
   params - landing via the banner showed blank team names, the current time instead of kickoff, and
   (initially suspected, then ruled out - see 16.3.5) no crests. `BetBuilderBoostStatus`/Dto extended
   with `KickoffTime`/`HomeLogoUrl`/`AwayLogoUrl`/`LeagueId` so the banner can build the same query
   string (`2e4b64d`).
3. **Bet Builder Boost silently lost to an already-won Daily Spinner boost** - the owner won a spin
   boost, then deliberately opted into the Bet Builder Boost via the banner and built a qualifying
   bet, but `BetService.PlaceBetAsync` checked the spinner's pending boost first, unconditionally -
   so the spinner boost consumed itself instead, `LastBetBuilderBoostDate` never got stamped, and the
   banner kept showing as if unused even though a boosted bet had genuinely just been placed. Fixed
   by trying the Bet Builder Boost first whenever `viaBetBuilderBoost` is set (a deliberate per-bet
   opt-in) - the spinner boost is only considered afterward, and stays fully pending, untouched, if
   the Bet Builder Boost took this bet instead. Client preview (`BetSlipSheet.EffectiveBoostMultiplier`)
   reordered to match, so it never shows a different boost than what will actually be applied
   (`9831c6b`). Found by the owner noticing the banner "still visible" after using the boost, then
   confirming "I've used it" - DB inspection showed the spinner boost's label on the actual placed bet
   rather than the Bet Builder Boost's.

#### 16.3.3 Placement-gating went back and forth - final rule

First pass blocked placing *any* bet on the featured match while short of the criteria, full stop.
Owner asked to relax this (`5e18bd8`) - any bet should still be placeable, just without the boost, if
the owner never actually opted in via the banner. Implemented by scoping the whole boost concept
(rules, gating, preview) to only kick in when reached through the banner (`ViaBoost`/
`ViaBetBuilderBoost`, see 16.3 above) rather than to *any* bet on the featured match (`2b59d70`). Once
that opt-in scoping existed, the owner clarified the gate should come back for the opted-in case
specifically: if you tapped the banner and built a bet for the boost, it should be blocked from
placing unboosted if it doesn't qualify - you should notice and fix it, not have it quietly go
through as a normal bet (`5cb4acf`). Net result: never opted in → completely normal betting, no rules
shown or applied at all; opted in but short of 3+ selections or Evens → blocked with an explanation
of which requirement is unmet.

#### 16.3.4 Daily Spinner boosts also now require Evens or greater (commit `dd605cd`)

Owner asked for the same evens-or-above rule to apply to the Daily Spinner's own boost prizes
(`BetBoost25`/`BetBoost50`/`2x Odds Boost`), not just the new Bet Builder Boost. Previously
`BetService.PlaceBetAsync` applied a pending spinner boost to *any* next bet unconditionally,
regardless of odds. Now:

- Only applies at `combinedOdds >= 2m` (evens or better) - this check runs *after* the Bet Builder
  Boost is tried (16.3.2 point 3), so the two can never fight over the same bet.
- **If the bet doesn't qualify, the boost is left pending rather than consumed and wasted** -
  `User.PendingBoostMultiplier`/`PendingBoostLabel` stay untouched, so it carries over to a future
  bet that does qualify, instead of being burned on a bet that could never have used it.
- Told to the owner in two places: the spin-win screen's note now states the requirement up front
  (`DailySpinner.razor`), and the bet slip (`BetSlipSheet.razor`) shows either a live "applied"
  preview once a pending boost would actually qualify, or an explanatory note when it's short of
  Evens - both before placing (via `PendingBoostQualifies`) and after (`BetDto.BoostSkippedReason`,
  new field, only ever set transiently on the placement response, not persisted).

#### 16.3.5 Crest images not testable on this dev machine - not a code bug

While chasing the "blank team names" bug (16.3.2 point 2), club crests also didn't render anywhere
on the emulator - including on the Matches list and Leaderboard, unrelated to anything touched this
session. Confirmed via direct `curl` from this dev machine that `media.api-sports.io` fails at the
schannel/TLS layer, the same pre-existing network block `CLAUDE.md`/HANDOVER already document for
`v3.football.api-sports.io` - not a code regression. Crests should render normally on a real device
with unrestricted internet; owner said they'd check there later. No code change made for this.

#### 16.3.6 UI polish, same feature (commits `d6a971e`, `e011979`, `04fe171`)

- Added a small eligibility-terms line to the banner ("3+ selections, combined odds Evens or above"),
  matching bet365's own small-print convention, using the real `MinSelections` value rather than a
  hardcoded "3".
- Banner made noticeably shorter (tightened padding/margins/icon sizes) once the terms line pushed
  its height up, and match names (e.g. "West Ham v Wrexham") switched from muted grey to bold
  full-contrast text so they don't read as an afterthought between the bold title and the badge.
- The "›" arrow glyph in the banner's circular button sat visibly off-centre at this circle's smaller
  30px size (the same technique looked fine on the larger 36px `.spin-banner-arrow` it was copied
  from) - fixed with a pinned `line-height` and a small `padding-bottom` nudge.

### 16.4 Deploy state as of this handover

- `fulltime-api` running commit `04fe171` (latest - includes the full Bet Builder Boost feature and
  the Daily Spinner evens+ rule).
- `fulltime-web` running commit `5840ae6` (the bet-spacing fixes and football icon only) - **every
  commit from `9694f6b` onward (the entire Bet Builder Boost feature, the Daily Spinner evens+ rule)
  has NOT reached `fulltime-web`**, per the owner's explicit "I never use the app on the web" this
  session - don't assume web is in sync with `main` the way it normally would be. Redeploy if that
  changes.
- Production DB has migration `20260911091829_AddBetBuilderBoost` applied (additive only).
- MAUI app tested this session via the `FullTime_Pixel8_API35` emulator, rebuilt after nearly every
  commit above - reflects commit `04fe171`. **Nothing from this session has reached an Android
  build** (AAB) - see updated §7 item 4.
- Today's featured Bet Builder Boost match (confirmed via direct DB query): **West Ham v Wrexham**
  (Championship, kicks off 2026-09-11 20:00 BST), the only eligible match today.
- Test account "Dad" (`eff4d5b2-ceb3-4bfa-b728-25fbece33e4d`) had `LastSpinDate` and
  `LastBetBuilderBoostDate` manually reset several times over the course of testing - **as of this
  handover, both are reset to unused** (owner's last two explicit requests), so the next session
  should expect a "clean" test account, not organic activity.
- All commits this session, pushed to `main`: `33d8f4c`, `5840ae6`, `9694f6b`, `9ae9e06`, `5e18bd8`,
  `2b59d70`, `5cb4acf`, `2e4b64d`, `dd605cd`, `d6a971e`, `e011979`, `9831c6b`, `04fe171`.

### 16.5 Gotchas discovered this session

- **`dotnet ef` design-time tooling boots the whole `Program.cs` host** (Firebase init included) -
  fails on any dev machine without local push-notification credentials configured, even though
  generating a migration has nothing to do with push. Fixed for good via `AppDbContextFactory.cs`
  (see 16.3) - a standard, reusable EF Core pattern, not project-specific.
- **A hand-drawn symmetric icon can fail to read as its intended subject even when geometrically
  "correct"** - two rounds of pentagon/hexagon football-icon geometry (line-art, then filled-panel)
  were both rejected as not looking like a football. Pulling a real, professionally-designed icon
  (Tabler Icons, MIT licensed) worked immediately - its asymmetric, hand-tuned seam placement is
  likely exactly why a "perfect" symmetric attempt reads as a flower/hex-nut instead of a ball. Worth
  reaching for an existing open icon set before hand-computing SVG path coordinates for anything
  representational.
- **Screenshot-based emulator testing coordinates must be recomputed from each new screenshot, not
  reused** - tapping based on a previous screenshot's layout repeatedly landed on the wrong element
  this session (accordions toggling shut/open unexpectedly, a "Cards" section opening instead of
  "Total goals") whenever the page's own content had shifted height since that screenshot was taken
  (e.g. an accordion collapsing above it). Always screenshot immediately before computing a tap
  target, not "close enough" from an earlier one.
- **A locally-run `dotnet ef migrations script --idempotent` is safe to replay against production in
  full** - every already-applied migration is wrapped in `IF NOT EXISTS(SELECT 1 FROM
  "__EFMigrationsHistory" ...)` guards and becomes a no-op, so there's no need to hand-extract just
  the new migration's statements before running it via `psql -f` on the VM.
- **Reaching a page via a promotional banner is easy to forget query params for** - `MatchCard.razor`
  and `BetBuilderBoostBanner.razor` both link to the same `/bet-builder/{id}` route, but only one of
  them originally carried the `home`/`away`/`kickoff`/`league`/logo query params the page actually
  needs; the other left every one of those fields blank/wrong. Worth checking new banner-style
  entry points against existing ones for exactly this next time.
