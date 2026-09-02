# FullTime — Session Handover

Personal/family project (not TelXL work). "FullTime" is a Blazor/.NET MAUI football
betting-with-friends app for "The Brownes" family league — no real money, just bragging rights
(deliberately scoped away from UK Gambling Act concerns). Owner: Alan Browne
(alan.browne@telxl.com), company name on the app is "JTM Technology". Repo:
`github.com/jtmtechnology/fulltime`, local path `c:\AB\Friends`, branch `main` (no PR workflow —
commits go straight to main and get pushed).

This file exists so a fresh Claude Code session can pick up this project without the user
re-explaining everything. Read this fully before touching code.

---

## 1. Current architecture

**Solution layout:**
- `FullTime.Api/` — ASP.NET Core Web API, EF Core + PostgreSQL, JWT auth. Runs on the VM as
  `fulltime-api` (port 5199).
- `FullTime.App/` — .NET MAUI Blazor Hybrid solution:
  - `FullTime.App.Shared/` — Razor Class Library with ALL the actual UI (pages, components,
    layouts, services, `wwwroot/app.css`). Both the MAUI head and the Web head reference this and
    render the same markup.
  - `FullTime.App/` — the MAUI native head (Android/iOS/MacCatalyst/Windows). This is what
    Codemagic builds for TestFlight/Play Store. **Package/bundle ID is now `com.jtmtechnology.fulltime.app`
    on BOTH platforms** (see §3 — Android used to be the unmodified `com.companyname.*` MAUI
    template default; fixed this session, before any Play Store publish, since it can never change
    after that).
  - `FullTime.App.Web/` — a Blazor Server web host of the same Shared UI, runs on the VM as
    `fulltime-web` (port 5200). Used for testing in a browser without needing a device/emulator.
    Prerenders statically before going interactive — see the JS-interop gotcha in §5.
- `FullTime.Website/` — separate standalone marketing site (own `wwwroot`, own `styles.css`, not
  related to `FullTime.App.Shared`). Runs on the VM as `fulltime-website` (port 5300, nginx reverse
  proxy on port 80 for `fulltime.jtmtechnology.co.uk`). Public domain is proxied through
  **Cloudflare** — see the caching gotcha in §5, it affects how CSS/JS deploys show up live. Now
  has three pages: `index.html` (marketing), `invite.html` (league invite landing page, §3),
  `privacy.html` (Play Store–required privacy policy, §3).

**Per-host service pattern** (important convention, used throughout): any capability that differs
between MAUI and Web gets an interface in `FullTime.App.Shared/Services/`, with a `Maui*`
implementation in `FullTime.App/Services/` and a `Web*` implementation in
`FullTime.App.Web/Services/`, registered in `MauiProgram.cs` / `Program.cs` respectively. Examples:
`IJwtStore`, `ILocaleProvider`, `ISlipStore`, `IActiveContextStore`, `IPushRegistrar`,
`IAdsRemovalService`, `IInterstitialAdService`, `IMatchLeaguePreferenceStore`, `ICelebratedWinStore`,
`IHapticFeedback`. **`IDailySpinStore` was removed this session** — the Daily Spinner moved from
local-device storage to a server-authoritative API (§3), so there's no longer a per-host storage
capability there at all.

**VM:** `fulltime-vm`, GCP zone `us-east1-b`, external IP `34.23.16.148`. `e2-micro` + 30GB
pd-standard, GCP Always Free tier. Database: PostgreSQL, database name is **`friendsacca`** (not
"fulltime"). **All three services (`fulltime-api`, `fulltime-web`, `fulltime-website`) are current
as of this handover** — redeployed multiple times this session, each after the code that landed on
`main` at the time. Don't assume staleness without checking `git log` first, but there's no known
gap right now.

**Local Postgres access:** a separate Postgres instance also runs on this dev machine (distinct
database from the VM's, same name `friendsacca`). `FullTime.Api`'s local connection string lives in
.NET User Secrets (`UserSecretsId` `fa9e94b4-5d83-4624-9c4b-28e396835b6e` in `FullTime.Api.csproj`),
at `%APPDATA%\Microsoft\UserSecrets\fa9e94b4-5d83-4624-9c4b-28e396835b6e\secrets.json` — **not
committed to git on purpose**, so it isn't reproduced here; read that file directly rather than
asking the user to retype it. Applying an EF migration locally is just `dotnet ef database update`
from `FullTime.Api/` (picks up User Secrets automatically). For the **VM's** Postgres, there's no
stored connection string to fetch — `sudo -u postgres psql -d friendsacca` over the same `gcloud
compute ssh` used for deploys authenticates via local peer auth, no password needed.

**⚠️ Running `FullTime.Api` locally burns the SAME shared RapidAPI/Highlightly quota as
production** — local and the VM use the identical API key (confirmed: both start `99c7a86a95...`),
and there's a single account-wide daily cap (confirmed 25,000/day). Confirmed happening this
session: leaving the local API running (and restarting it repeatedly, e.g. to release build file
locks) racked up 9,572 Highlightly calls and 98 already-rate-limited (429) responses in *one* local
run alone. Two compounding causes: (1) three background services
(`HighlightlyFixtureDiscoveryBackgroundService`, `HighlightlyMatchSyncBackgroundService`,
`BetBuilderSyncBackgroundService`) each fire an immediate, unconditional burst of calls on every
process **start** — no "last synced" gate — so every restart adds its own spike; (2) a **stale
local dev DB** (matches stuck at `Upcoming` with kickoff times weeks in the past) makes the
live-sync query treat far more dates as "still need polling" per tick than an up-to-date DB would.
Don't leave the local API running unattended, and don't restart it more than necessary — stop it
(`Stop-Process` on the `FullTime.Api` process, needed anyway to release its file lock before
rebuilding) as soon as you're done testing against it.

**Deployment pattern** (unchanged, works reliably — `X` = `api` / `web` / `website`):
```
dotnet publish -c Release -r linux-x64 --self-contained false -o publish-X
tar czf publish-X.tar.gz -C publish-X .
export CLOUDSDK_PYTHON="/c/Program Files (x86)/Google/Cloud SDK/google-cloud-sdk/platform/bundledpython/python.exe"
gcloud compute scp publish-X.tar.gz fulltime-vm:publish-X.tar.gz --zone=us-east1-b
gcloud compute ssh fulltime-vm --zone=us-east1-b --command='
  sudo systemctl stop fulltime-X
  sudo rm -rf /opt/fulltime-X-previous
  sudo cp -r /opt/fulltime-X /opt/fulltime-X-previous
  sudo rm -rf /opt/fulltime-X/*
  sudo tar xzf ~/publish-X.tar.gz -C /opt/fulltime-X
  sudo chown -R fulltime:fulltime /opt/fulltime-X
  sudo systemctl start fulltime-X
'
```
Clean up local `publish-X/` and `publish-X.tar.gz` afterward — don't commit them.

**⚠️ Database migrations need applying to BOTH local and VM Postgres separately, and the VM has no
auto-migrate on startup.** This session's pattern: after `dotnet ef migrations add`, run
`dotnet ef database update` locally, and for the VM generate an idempotent script
(`dotnet ef migrations script <lastAppliedMigration> --idempotent -o script.sql`, or omit the first
arg for "from empty" the very first time), `gcloud compute scp` it over, then
`sudo -u postgres psql -d friendsacca -f script.sql` over SSH. All migrations added this session are
applied to both as of this handover.

**Android testing:** Two AVDs exist locally: `FullTime_GoogleAPIs_API35` and `FullTime_Pixel8_API35`.
adb lives at `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`, emulator at
`C:\Program Files (x86)\Android\android-sdk\emulator\emulator.exe`. **Always use the PowerShell
tool for `adb screencap`/`pull`/`input tap` with `/sdcard/...` paths or coordinates** — git-bash
mangles `/sdcard/...` path arguments. When converting a screenshot's *displayed* coordinates to
real device coordinates for `adb shell input tap`, multiply by `actual_width / displayed_width` —
**don't skip this multiplication**, using displayed coordinates directly taps the wrong element.
If a MAUI Android build fails with a file-lock error, run `dotnet build-server shutdown` first,
then retry; if a `dotnet build`/`dotnet run` background process (very likely the local API, see
above) is still holding a DLL lock afterward, find and kill it by PID
(`Get-CimInstance Win32_Process -Filter "Name='FullTime.Api.exe'"` or `'dotnet.exe'` filtered by
`CommandLine`) rather than assuming `build-server shutdown` alone released it.

**Plain `adb install` crashes a Debug build on launch.** A plain `dotnet build -f net10.0-android -c
Debug` followed by `adb install` produces an app that aborts immediately (`SIGABRT`, logcat shows
`No assemblies found in '.../.__override__/...' ... Assuming this is part of Fast Deployment.
Exiting...`). **What works:** build with `-p:EmbedAssembliesIntoApk=true` to embed the assemblies
straight into the APK (sidesteps Fast Deploy entirely), then a fresh
`adb uninstall com.jtmtechnology.fulltime.app` + `adb install
<path>\com.jtmtechnology.fulltime.app-Signed.apk` (not `-r`), then
`adb shell am start -n com.jtmtechnology.fulltime.app/<activity>` to launch — the crc64-prefixed
activity class name can change between builds, so if `am start` says "Activity class does not
exist", re-resolve it first with `adb shell cmd package resolve-activity --brief
com.jtmtechnology.fulltime.app`. Confirm it's actually running (not silently crashed back to the
launcher, or stuck behind a Test Ad interstitial that needs dismissing) via
`adb shell dumpsys window | Select-String mCurrentFocus` before screenshotting. A one-off HWUI/EGL
SIGSEGV on cold start happened once this session — looked like an emulator graphics flake, not a
code issue; just retry the launch.

**Debugging via Chrome DevTools Protocol (CDP):** when a screenshot-based diagnosis is ambiguous
(e.g. "is this element actually clipped, or does it just look that way?"), get exact DOM/layout
truth from the *real* on-device Android WebView instead of guessing:
```
adb shell pidof <package>                                   # get the running process id
adb shell cat /proc/net/unix | grep devtools                 # confirm the debug socket name
adb forward tcp:9222 localabstract:webview_devtools_remote_<pid>
curl -s http://localhost:9222/json                            # lists the page + its ws:// URL
```
Then open a WebSocket to that `webSocketDebuggerUrl` (Node 22+ has a native `WebSocket` global) and
send `{"id":1,"method":"Runtime.evaluate","params":{"expression":"...","returnByValue":true}}` to
get real computed geometry. Far more reliable than iterating on screenshots for CSS layout bugs.

**iOS:** No Mac available in this environment — iOS changes are pushed and verified via Codemagic
CI. `codemagic.yaml` currently holds only the `ios-ad-hoc` and `ios-testflight` workflows (see §3
for why an Android workflow was added then deliberately removed again). The user's own real
iPhone (running whatever build was already installed, **not** a fresh build with this session's
changes) supplied real App Store screenshots (§3.9) confirming the core UI genuinely works well on
a real device. **This does NOT verify this session's Android package rename or the new UMP consent
code** — those landed in code but were never actually installed on that phone; both remain
unverified on iOS and need a fresh Codemagic build to confirm. The UMP iOS package in particular
(`MTAdmob.UMP.iOS.Binding`) compiles but has never actually been run.

**⚠️ `FullTime.App.Shared`'s UI has no responsive/tablet layout at all** — `.content` (the main
page container in `MainLayout.razor`) has no `max-width`, so on a large screen (iPad, desktop
browser) everything just stretches edge-to-edge: odds buttons ~500px wide, huge dead whitespace.
Confirmed by rendering the Blazor Web host at iPad Pro 13" dimensions (§3.9) — this is a real,
visually-confirmed limitation, not a hypothetical one. **Deliberately left as-is**: the iOS app is
configured `UIDeviceFamily` `[1, 2]` (Universal, iPhone *and* iPad) in `Info.plist`, and the user
explicitly chose to keep it that way and ship the unpolished iPad experience rather than restrict
to iPhone-only, after seeing exactly how it looks. Don't "fix" this unprompted.

---

## 2. Earlier work (stable, shipped before this session — see git log for detail)

Condensed background context, not open work:
- In-app splash bridge + native splash fix, win-celebration overlay (`WinCelebrationOverlay.razor`,
  once per newly-settled `Won` bet via `ICelebratedWinStore`), bet-placement confirmation in
  `BetSlipSheet.razor`, logo/branding assets.
- League invite by email (`POST api/leagues/{id}/invite`, `LeagueService.SendInviteAsync`, HTML
  emails via Brevo SMTP), a "Weekend League"-style leaderboard UI, join-by-code flow.
- **Daily Spinner** (prize wheel) was originally built as a 100% cosmetic placeholder: `/daily-spinner`
  page, a `FreeSpinBanner.razor` promo on the Matches page, a shared `SpinSegments.All` prize
  catalogue, local-device persistence, pointer-landing rotation math. **This session (§3) rebuilt
  its entire backing logic to be real and server-authoritative** — don't assume any pre-session
  description of "placeholder only, no real economy" still applies; it's now wired to actual
  balances and bet boosts.
- Marketing website feature grid, flexbox card-centering fix, Cloudflare cache-bust workaround
  (`styles.css?v=N`) first introduced.

---

## 3. This session's work

This was a large, multi-part session. Grouped by theme rather than chronologically.

### 3.1 Daily Spinner: wired to a real backend (biggest change this session)
The wheel itself (`DailySpinner.razor`, `SpinSegments.All`) is unchanged UI; everything behind it
moved server-side:
- New `FullTime.Api/Spin/` (`SpinService`, `SpinResults.cs`) + `SpinController`
  (`GET api/spin/status`, `POST api/spin`). The winning segment is picked **server-side** — a
  client-only "last spin date" (the old `IDailySpinStore`) could be spoofed by clearing local
  storage once real value was on the line, so that store is gone entirely (Maui/Web
  implementations deleted too).
- **Mystery Cash** and the **day-7 streak bonus** (now **£100**, up from a placeholder £50) credit
  every league membership the user has — both `Balance` and `StartingBalance`, same profit-neutral
  treatment `WeeklyTopUpService` already used, so a lucky spin can't skew the leaderboard.
- **Bet Boosts** (`Bet Boost 25%`, `Bet Boost 50%`, `2x Odds Boost`) are stored on `User`
  (`PendingBoostMultiplier`/`PendingBoostLabel`) and consumed by the very next bet placed
  (`BetService.PlaceBetAsync`) — multiplies combined odds, then clears. A new boost always
  **overwrites** an unused one, never stacks. The bet slip now **previews the boosted potential
  return live** while building a bet (fetches pending-boost status when the sheet opens), not just
  after placing. `Bet.BoostApplied` is now persisted, so **My Bets shows which past bets had a
  boost applied**.
- **Daily spin reminder push notification**: new `SpinReminderService`/`SpinReminderBackgroundService`
  nudges anyone who hasn't spun yet past 4pm their own local time, once a day. The MAUI app now
  sends the device's UTC offset alongside its push token on registration
  (`DevicesController.Register`, `User.UtcOffsetMinutes`) so this can be computed per user;
  Web-only accounts are naturally skipped (they never register a push token).
- **Countdown timer removed** from the "come back tomorrow" state — the daily gate was always
  calendar-day based (not a rolling 24h cooldown), so a ticking clock implied a cooldown that never
  actually existed. Now just a static message.
- **The TEMP once-a-day testing bypass is fully removed** (was carried over from earlier sessions,
  repeatedly flagged, finally taken out this session along with its API endpoint
  (`SpinController.ResetForTesting`) and `ApiClient.ResetSpinForTestingAsync`). Spinning is
  genuinely once-per-real-day everywhere now, including production.
  - **For streak testing without waiting real days**: there's no bypass anymore, so testing a
    7-day cycle means directly setting `Users.LastSpinDate` to yesterday (leaving `SpinStreak`
    untouched) via SQL before each test spin — a small throwaway Npgsql console script does this in
    a few lines (see §5 for the general pattern of writing one). Don't reintroduce an API-level
    bypass without being asked; it was removed deliberately.
- New `Users` columns (migrations `AddDailySpinToUser`, `AddSpinReminderToUser`): `LastSpinDate`,
  `SpinStreak`, `PendingBoostMultiplier`, `PendingBoostLabel`, `UtcOffsetMinutes`,
  `LastSpinReminderDate`. New `Bets` column (`AddBoostAppliedToBet`): `BoostApplied`. All applied to
  both local and VM Postgres.

### 3.2 League invite page + privacy policy (Play Store/App Store prep)
- Built `FullTime.Website/wwwroot/invite.html` from scratch — the invite email already linked to
  `/invite?code=...&league=...` from a previous session but the page never existed (404). It's a
  static page (no server routing on this site) that reads `code`/`league` from the query string via
  inline JS, shows a big glowing invite-code card, and links to the same placeholder store badges as
  the homepage. `LeagueService.cs`'s `inviteLink` now points at `/invite.html` (not `/invite` —
  static file serving needs the real extension). Copy now explicitly says to create an account
  before trying to join with the code (was missing, confusing without it).
- Built `FullTime.Website/wwwroot/privacy.html` — required for the Play Store listing. Covers what's
  collected (account details, country, league/betting activity, push token + device UTC offset),
  the third-party services involved (Firebase Cloud Messaging, Google AdMob, the football data
  provider), and how to request account deletion. Linked from the homepage footer.
- **Hit the Cloudflare cache-busting bug TWICE this session** adding CSS for both of the above
  (`.invite-code-card`, `.legal-page`) without bumping `styles.css?v=N` — see §5, now at **`v=5`**.
  The lesson: bump the version *in the same edit* as any `styles.css` change, don't treat it as a
  separate deploy-time step.

### 3.3 Android package name fixed before Play Store upload
Android's `ApplicationId` was still the **unmodified MAUI template default**,
`com.companyname.fulltime.app` — iOS already correctly used `com.jtmtechnology.fulltime.app`.
Package names can never change after a first Play Store publish, so this had to be fixed now:
- `FullTime.App.csproj`'s Android `ApplicationId` → `com.jtmtechnology.fulltime.app` (matches iOS,
  no more platform-conditional override needed).
- A new Android app was registered in the Firebase project (`fulltime-98cc9`) for the new package
  name **before** the code change, so push notifications wouldn't silently break —
  `Platforms/Android/Resources/google-services.json` now carries client entries for both the old and
  new package names (harmless to leave the old one in).
- `HANDOVER.md` and `.claude/settings.local.json`'s adb permission patterns updated to match.

### 3.4 Ad provider investigation → UMP consent flow instead
Asked to replace AdMob with Appodeal (real ads were showing 0 fill). Researched thoroughly before
touching code:
- **Appodeal**: no viable .NET MAUI path. Only NuGet package is `AppodealXamarinPlugin` — Android-only,
  targets the legacy `MonoAndroid` TFM (not the modern `net10.0-android`), unmaintained-feeling
  (single maintainer, ~1,900 downloads total), **no iOS package at all**.
- **AppLovin MAX** (checked as an alternative): real `net10.0-android`/`net10.0-ios` packages exist
  (`Anjo.Android.AppLovin`, `AppLovin.iOS`), but the iOS one has zero track record — all 8 published
  versions landed the same day.
- **Unity LevelPlay/ironSource** (checked too): official NuGet packages exist but are legacy
  `MonoAndroid`/`Xamarin.iOS` bindings, stale since May 2023.
- **Turned out to be moot**: AdMob only serves real ads to apps actually **live on a store** (an
  anti-invalid-traffic policy, confirmed by the user, not a bug in the integration). This should
  just start working once FullTime is actually published — no SDK migration needed.
- **Wired up the UMP (GDPR/UK) consent flow anyway**, since it's good practice regardless and was a
  real gap. NOT through `Plugin.MauiMtAdmob`'s own consent-form support — that's a **paid,
  undisclosed-price add-on** from the plugin vendor. Instead calls Google's own UMP SDK directly
  (itself a Certified CMP, which the plugin's own docs say is fine to use on the unlicensed path):
  - Android: Microsoft's own `Xamarin.Google.UserMessagingPlatform` (free, MIT, actively maintained).
  - iOS: `MTAdmob.UMP.iOS.Binding` (free, MIT, same author as the ad plugin, targets `net10.0-ios`).
  - New code in `MauiInterstitialAdService.cs`: `RequestConsentAsync()` runs once per session, ahead
    of ad-SDK init (Google's documented ordering). Android needs a small `Java.Lang.Object`-based
    listener class (no lambda-friendly wrapper exists for those interfaces); iOS's binding uses
    plain C# delegates, much simpler.
  - **Verified working end-to-end on the Android emulator**: a real GDPR consent dialog renders,
    resolves cleanly on tapping Consent, and the existing ad-loading pipeline proceeds normally
    afterward (confirmed via logcat + screenshot, no crashes). **iOS side compiles but is completely
    unverified** — needs a Codemagic build to confirm the delegate-based calls actually work at
    runtime.
- Test AdMob IDs are still in place (see §5 on why — unrelated to this investigation, just still
  true) and must stay until actual store submission.

### 3.5 Codemagic: added, then reverted, an Android debug workflow
Wanted to sideload a debug build to a physical Android phone but `adb` never detected it over USB
(driver/USB-mode issue, never resolved — worth re-diagnosing if picked up again). Instead added an
`android-debug` Codemagic workflow that builds and **emails** a directly-installable APK. Took
several iterations to get working on Codemagic's Mac runner (same runner type as the iOS workflows —
`instance_type: linux_x2` isn't in the current billing plan):
- **NETSDK1147** (missing ios workload) — `FullTime.App.csproj` multi-targets
  android/ios/maccatalyst, and a plain `dotnet build -f net10.0-android` still evaluates the full
  `TargetFrameworks` list during implicit restore even with `-f` narrowing the actual build.
- **`dotnet restore -f <TFM>` is a trap** — `-f` means `--force` for the `restore` subcommand
  specifically (unlike `build`/`publish`/`run`/`test`, where it means `--framework`), so it silently
  swallowed the TFM string as a bogus project-path argument (`MSB1009`).
- **XA5207** (missing Android SDK platform) — the `maui-android` *workload* only brings build
  tooling, not the actual SDK platform files; needed an explicit
  `dotnet build -t:InstallAndroidDependencies ... -p:AcceptAndroidSDKLicenses=true` step.
- The actual fix that stuck: a new **`AndroidOnlyBuild`** MSBuild property in `FullTime.App.csproj`
  (mirrors the existing `IosOnlyBuild` pattern already used by the iOS workflows), which collapses
  `TargetFrameworks` down to just `net10.0-android` for that project only — scoped so it can't leak
  into `FullTime.App.Shared`'s own restore the way a command-line `-p:TargetFramework=` override did
  (that one corrupted Shared's assets file, `NETSDK1005`, on the very next `--no-restore` step).
- Once working, **`codemagic.yaml` was reverted back to iOS-only** per explicit request. The
  working Android workflow (with all the above fixes) is preserved in a **new file,
  `codemagic-android-debug.yaml`** at the repo root — copy its content back into `codemagic.yaml`
  whenever an Android sideload build is needed again. `AndroidOnlyBuild` itself is left in
  `FullTime.App.csproj` (harmless when unused) since that saved workflow depends on it.

### 3.6 Play Store listing assets produced
All saved locally, **not yet confirmed actually uploaded/submitted** by the user (they were mid-flow
through Play Console when the session wrapped):
- Short + full description text (in conversation, not a file — ask if it's needed again, or check
  chat history).
- `Downloads\play-store-icon-512.png` — 512×512, the app icon composited onto its real navy
  (`#0D1117`) backdrop rather than uploaded with its raw transparent padding (which would've shown
  mostly empty space in the listing).
- `Downloads\feature-graphic-1024x500.png` — built as an HTML file rendered via headless Edge
  (`msedge --headless --screenshot --window-size=1024,500`) rather than hand-drawn, reusing the
  site's actual brand colours/wordmark styling. This technique (author an HTML file, screenshot it
  headless at an exact pixel size) is reusable for any future pixel-exact marketing asset.
- `Downloads\play-store-screenshots\01-matches.png` through `05-leaderboard.png` — captured live
  from the emulator against production (Matches, Bet Slip, Daily Spinner, My Bets, Leaderboard),
  using a real test account (§3.7) so they show actual data, not empty states. **Had to be cropped**
  from the emulator's native 1080×2400 (2.22:1) down to 1080×2106 (1.95:1) — Play Store rejects
  screenshots over a 2:1 aspect ratio; the raw emulator capture would have failed upload.
- Android package name for the listing: `com.jtmtechnology.fulltime.app` (§3.3).
- Privacy policy URL for the listing: `https://fulltime.jtmtechnology.co.uk/privacy.html` (§3.2).

### 3.7 Test account + local test data
- **Production**: `test@jtmtechnology.co.uk` / `Testuser123`, registered via the real API then
  marked `EmailVerified = true` directly in the VM's Postgres (bypasses needing to click a real
  verification email). Has a **"Weekend League"** (invite code `SPQJJM`) and one **£10 pending bet**
  placed against it, used to populate the Play Store screenshots. **Not cleaned up** — ask before
  deleting, or leave in place if the user wants to keep using this account for further testing.
- **Local**: `alan@jtmtechnology.co.uk`'s password reset to `matthew2003` (direct DB write, BCrypt
  hash generated via a throwaway console app referencing the same `BCrypt.Net-Next` version as
  `FullTime.Api`, to guarantee a matching hash). +£500 added to both the "Browne" and "WBA" league
  membership `Balance`s (not `StartingBalance` — a deliberate one-off top-up for testing, not meant
  to be profit-neutral like the in-app mechanisms).

### 3.8 Smaller fixes
- App icon shield shrunk to 78% on both `appicon.png` (Android) and `appicon_ios.png` (iOS) — was
  edge-to-edge with almost no safe-zone margin.
- Boost-message icon misalignment in the Daily Spinner win card (`.win-note` now a flex row like its
  working sibling `.streak-note`, was relying on a hand-tuned `vertical-align` offset tuned for a
  different context).
- Invite "Send" button had no CSS class at all (unstyled) — now `.primary-btn`. Send/Cancel now
  stack correctly on their own row instead of wrapping individually. Button text shortened to
  "Invite friend", pulled up tighter under the invite code.
- Tried adding a real recorded spin-wheel sound (`spinSound.js` → a Web Audio synth, then swapped for
  an actual mp3) — **ultimately removed entirely** per explicit ask; the wheel is silent again.

### 3.9 App Store Connect submission progress (same session, after the last handover update)
No code changes — all guidance/asset production while the user worked through App Store Connect
directly. Nothing here was committed since none of it is code, but worth knowing where things stand:
- **In-app purchase**: code side was already complete (`MauiAdsRemovalService.cs`, product ID
  `remove_ads`, non-consumable via `Plugin.InAppBilling`) — just needed creating in App Store
  Connect with that exact product ID, walked through step by step.
- **iPhone screenshots**: the user supplied 7 real screenshots from their own iPhone
  (`Downloads\ios\*.jpg`, 1290×2796 native) — resized to Apple's accepted **1284×2778** and saved to
  `Downloads\ios\appstore-6.5in\`. These are genuinely real device captures (not emulator), and
  incidentally the first real confirmation this project has had that the app works properly on
  actual iOS hardware.
- **iPad screenshot**: Apple requires one for "13-inch iPad displays" whenever `UIDeviceFamily`
  includes iPad (see §1's new limitation note) — no Mac/iPad available, so generated one from the
  **Blazor Web host** (`FullTime.App.Web`, same Razor components as the iOS app) via headless Edge
  at `2064×2752` (iPad Pro 13" M4 resolution), saved to `Downloads\ios\ipad-13in-screenshot.png`.
  **Gotcha hit and fixed**: the first attempt came out almost black — `MainLayout.razor`'s
  `.page-transition` fade-in animation was mid-transition when the screenshot fired. Adding
  `--virtual-time-budget=5000` to the `msedge --headless` command (lets the renderer advance virtual
  time before capturing) fixed it. Add this flag to *any* future headless-Edge screenshot of a page
  with CSS transitions/animations, not just this one.
- **Description/keywords/URLs**: drafted in conversation (not saved to a file) — Promotional Text,
  the same long Description used for Google Play, Keywords, Support URL
  (`https://fulltime.jtmtechnology.co.uk/#contact`), Marketing URL, Copyright (`2026 JTM
  Technology`). Ask the user to re-paste if a fresh session needs these again, they weren't
  persisted anywhere else.
- **App Tracking Transparency / App Privacy**: `Info.plist`'s `NSUserTrackingUsageDescription` is
  solely for AdMob's ad personalization — confirmed no duplicate ATT prompt in code (only the ad
  plugin's own `handleTrackingAuthorization: true` triggers it). Correct App Privacy declaration:
  `Identifiers → Device ID`, used for tracking = Yes, purpose = Third-Party Advertising, not linked
  to the user's account identity.
- **Content Rights question**: answered "Yes, contains third-party content" (football data/odds
  from the Highlightly provider, club crests, bookmaker logos, AdMob ads) — "No" would have been
  factually wrong and risked a rejection or later strike.
- **Test/demo account for App Review**: same one from §3.7 (`test@jtmtechnology.co.uk` /
  `Testuser123`) — already has real league/bet data so a reviewer doesn't see an empty app.
- **Ads-removal for family members**: asked to comp free ads-removal to everyone in "the Brownes"
  league except one person. **There is no server-side mechanism for this at all** — ads-removed
  state is purely a local-device cache of a real store purchase (`IAdsRemovalService`/
  `Plugin.InAppBilling`), nothing in the `Users` table. Offered to build a server-side override;
  **user chose instead to wait and use real store promo codes** (App Store Connect → the
  `remove_ads` IAP → Promo Codes; Google Play Console has an equivalent) once the app is actually
  published — no code needed, but only usable post-launch.

---

## 4. Outstanding tasks

1. **Confirm the UMP consent flow and the Android package rename work on iOS** — both are
   unverified there (no Mac; compiles but never run). Needs a Codemagic build.
2. **Diagnose why `adb` never detected the physical Android phone over USB** — driver? USB mode?
   Never resolved; the Codemagic email-a-debug-APK route was used instead as a workaround.
3. **Decide what to do with the `test@jtmtechnology.co.uk` production test account** (§3.7) — leave
   it, or clean it up (delete in FK-safe order: BetLegPicks → BetLegs → Bets → LeagueMemberships →
   Leagues → Users, per the project's stated convention).
4. **A stray 100MB `com.jtmtechnology.fulltime.app-Signed.apk` sits untracked at the repo root**
   (`c:\AB\Friends\`) — not committed, origin unclear, probably safe to delete but wasn't confirmed.
5. **Actually finish the Play Store submission** — all assets are ready (§3.6) but the user was
   still filling in Play Console fields (app name, package name, screenshots, feature graphic, age
   suitability, IAP server notifications — the last two were explicitly skipped as optional/not
   needed yet) when that part of the session ended.
6. **Finish the App Store Connect submission** — further along than Play Store (§3.9): IAP product,
   both screenshot sizes, description/keywords/URLs, App Privacy tracking declaration, and Content
   Rights are all done/answered. Still needs: an actual build uploaded (this session did no iOS
   build/upload, only listing metadata and asset prep), Apple's "Age Suitability" content
   questionnaire, and final submission for review.
7. **Once FullTime is live on both stores**: generate free `remove_ads` promo codes for everyone in
   the Brownes league except one specific person (§3.9) — was explicitly deferred to this point
   rather than building a server-side override.
8. **Design the real prize economy is done** (§3.1) — this item from earlier handovers is now
   resolved, no longer outstanding. (Left as a note so it isn't accidentally re-flagged.)
9. Real AdMob IDs still need swapping back in (`MauiInterstitialAdService.cs` /
   `AndroidManifest.xml` / iOS `Info.plist`) — but only **after** the app is actually live on a
   store, per §3.4's finding. Doing it earlier would just show real ads with guaranteed 0 fill.
10. Real store-listing icon exports for Apple specifically (1024×1024, no alpha) — not produced this
    session (only the Google 512×512 with-alpha version was).
11. TestFlight beta review 422 (from an earlier session) — not confirmed resolved, not touched this
    session either.
12. The stale `free-api-live-football-data`-retirement plan at
    `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md` — still untouched, still
    possibly stale, re-verify before resuming if picked up.
13. If Cloudflare dashboard/API access ever gets configured, replace the `styles.css?v=N`
    cache-busting workaround with a real purge-on-deploy step.

---

## 5. Conventions reconfirmed/added this session

- **Per-host service pattern** (§1) — still the rule for any new platform-different capability.
- **No comments explaining "what"**, only non-obvious "why" — followed throughout.
- **Confirm before pushing to the VM or writing to production.** Every deploy and every production
  write this session (test account creation, marking it verified) was explicitly requested or
  confirmed in the moment — treat each as one-time authorization, not standing approval.
- **Git**: commit messages explain *why*, not *what*; always end with `Co-Authored-By: Claude Sonnet
  5 <noreply@anthropic.com>`; never `--amend`; never force-push. Stage files explicitly rather than
  `git add -A`/`.` — stray untracked files (`CLAUDE.md`, `new 1.txt`, `splash-check.png`, a local
  `scratch/` folder, and now a stray `.apk`) keep accumulating in the working tree; leave them out
  of commits unless asked.
- **Blazor Server prerendering blocks JS interop until after first render** — any `IJSRuntime` call
  must happen in `OnAfterRenderAsync(firstRender)` or later, never `OnInitializedAsync`, on any page
  also served by `FullTime.App.Web`.
- **MAUI Debug builds need `-p:EmbedAssembliesIntoApk=true`** for a plain `adb install` to work in
  this environment (§1).
- **The live website is behind Cloudflare and edge-caches static assets.** A CSS-only change needs
  the `styles.css?v=N` cache-bust bumped **in the same edit**, or it won't show up live for up to 4
  hours even though the origin VM is already updated. Got bitten by this twice in one session
  despite already knowing about it — treat it as a reflex, not a checklist item to remember later.
  Currently at **`v=5`**.
- **`dotnet restore -f <value>` means `--force`, not `--framework`** — unlike `build`/`publish`/
  `run`/`test`. To scope a restore/build to one TFM of a multi-target project without corrupting a
  referenced project's own restore, add a project-scoped MSBuild property (see `IosOnlyBuild` /
  `AndroidOnlyBuild` in `FullTime.App.csproj`) rather than a command-line
  `-p:TargetFramework=` override, which propagates globally to every project in the graph.
- **Play Store phone screenshots must be ≤2:1 aspect ratio** — a raw Android emulator/device
  screenshot (e.g. 1080×2400, 2.22:1) will be rejected on upload; crop it down (removing the status
  bar and nav bar is a reasonable way to claw back the needed margin) before handing it over.
- **For pixel-exact marketing graphics** (feature graphics, banners), author plain HTML/CSS and
  render it with headless Edge at an exact viewport size
  (`msedge --headless --disable-gpu --screenshot=out.png --window-size=W,H file.html`) rather than
  trying to compose one with an image library — much easier to get typography and brand colours
  right. **If the page has any CSS transition/animation** (this app's own `.page-transition`
  fade-in included), add `--virtual-time-budget=5000` too, or the capture can fire mid-transition
  and come out looking broken (near-black, in this app's case) even though nothing is actually
  wrong.
- **The real running app's own Blazor Web host (`FullTime.App.Web`) is a legitimate way to capture
  "real UI" screenshots at sizes the native app can't easily produce locally** (e.g. iPad
  dimensions, with no Mac/iPad available) — it renders the exact same Razor components as the MAUI
  heads, so a screenshot of it is an accurate representation of the app's actual UI, not a mockup.
- **When a NuGet package's API isn't documented anywhere usable** (common for smaller binding
  libraries), a small throwaway console app using `System.Reflection.Metadata`/`PEReader` to dump a
  DLL's public types/methods directly from its metadata (no need to load/resolve its actual runtime
  dependencies) is a reliable way to confirm exact method signatures before writing code against
  them — used this session to correctly wire up two undocumented UMP consent binding packages on
  the first attempt.
- **A throwaway Npgsql console app in the scratchpad directory** (add the package with
  `dotnet add package Npgsql --source https://api.nuget.org/v3/index.json` if the org NuGet feed is
  unreachable) is the established pattern for any one-off local-DB read/write that doesn't warrant a
  full migration — used repeatedly this session (password resets, balance top-ups, streak-testing
  date manipulation). Delete the throwaway project afterward, especially since it embeds the DB
  password inline.
- **When a screenshot makes an on-device CSS bug look ambiguous, reach for CDP (§1) rather than
  iterating blindly on screenshots.**
