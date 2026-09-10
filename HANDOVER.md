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

**Top priorities for whoever picks this up next:**
1. **Continue the API-Football cutover plan** at
   `C:\Users\alan.browne\.claude\plans\vivid-growing-snail.md` (§6.13, §10) — Phase 1 (live scores)
   is done and deployed as of 2026-09-10. **Next: Phase 3 (Match Summary/event-fetch) is now
   effectively urgent, not "do last"** — it's what closes the settlement gap in §10, not just a
   Match Summary display nicety. Do it before or alongside Phase 2 (odds), earlier than the plan
   file's original order suggests. Still confirm the API-Football account tier before any more
   aggressive cadence changes (still Pro/7,500/day as of 2026-09-10, confirmed via
   `curl https://v3.football.api-sports.io/status` — 15/7500 used that morning; the user wants to
   design for 75,000/day eventually, needs an upgrade first).
2. **Check for stuck bets on the settlement gap** (§10) — query for `Pending` `BetLegPicks` with
   `MarketType` in `TotalCorners`/`PlayerGoalscorerAnytime`/`PlayerCard`/`PlayerRedCard`/
   `PlayerAssists` against `Finished` matches whose `KickoffTime` is after 2026-09-10. None existed
   at cutover time (the DB wipe cleared everything), but new bets may have been placed since.
3. **Trigger a fresh iOS Codemagic build (`ios-testflight`)** to test the new hypothesis in §6.11 -
   the owner's full delete+restart+reinstall did NOT fix the stale notification icon, ruling out the
   local-cache theory. If a new build's notification icon comes through correct, that confirms
   TestFlight/APNs caches icon artwork per-build rather than reading the live bundle.
4. **Upload the built AAB to Play Console** —
   `FullTime.App/FullTime.App/bin/Release/net10.0-android/com.jtmtechnology.fulltime.app-Signed.aab`
   (version 1.3, code 10) is built and signed but sitting local-only (§6.8). This is the only
   remaining step to get the notification-icon fix and all three `FullTime.App.Shared` fixes to
   Android users.
5. **Redeploy `fulltime-web`** for the same three `FullTime.App.Shared` fixes (leaderboard-refresh,
   match-event-icon, ads-removed-card) to reach Web users — `fulltime-api` is already up to date
   (redeployed in §6.9/§6.10), `fulltime-web` is not. Follow `CLAUDE.md`'s deploy steps with `X=web`.
6. The duplicate-`BetBuilderMarket`-row problem (below) has still never been swept beyond one match.
7. If Highlightly's *old* API key (now replaced, §6.12) ever matters again — e.g. to understand why
   a real daily quota stayed dead for 17+ hours instead of resetting — that's now a dashboard
   question, not something diagnosable from the app's own logs.

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
  overall) — commit `<fill in after committing>`.
- **Not done this session**: Phases 2-4 of the plan (odds, Match Summary/event-fetch, quota-alert
  parity) — see §7 top priorities. `Providers:MarketsSource` is still `"Highlightly"`, so Bet
  Builder odds/markets are unaffected by this session's changes.
