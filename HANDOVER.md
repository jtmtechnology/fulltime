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
    Codemagic builds for TestFlight/Play Store.
  - `FullTime.App.Web/` — a Blazor Server web host of the same Shared UI, runs on the VM as
    `fulltime-web` (port 5200). Used for testing in a browser without needing a device/emulator.
    Prerenders statically before going interactive — see the JS-interop gotcha in §5.
- `FullTime.Website/` — separate standalone marketing site (own `wwwroot`, own `styles.css`, not
  related to FullTime.App.Shared). Runs on the VM as `fulltime-website` (port 5300, nginx reverse
  proxy on port 80 for `fulltime.jtmtechnology.co.uk`). Public domain is proxied through
  **Cloudflare** — see the caching gotcha in §5, it affects how CSS/JS deploys show up live.

**Per-host service pattern** (important convention, used throughout): any capability that differs
between MAUI and Web gets an interface in `FullTime.App.Shared/Services/`, with a `Maui*`
implementation in `FullTime.App/Services/` and a `Web*` implementation in
`FullTime.App.Web/Services/`, registered in `MauiProgram.cs` / `Program.cs` respectively. Examples:
`IJwtStore`, `ILocaleProvider`, `ISlipStore`, `IActiveContextStore`, `IPushRegistrar`,
`IAdsRemovalService`, `IInterstitialAdService`, `IMatchLeaguePreferenceStore`, `ICelebratedWinStore`,
`IHapticFeedback`, `IDailySpinStore`. (The Daily Spinner's ticking sound did *not* need this
pattern — it's plain JS/Web Audio API run inside whichever WebView/browser is already hosting the
Blazor UI, identical on both hosts, so no Maui/Web split was needed there.)

**VM:** `fulltime-vm`, GCP zone `us-east1-b`, external IP `34.23.16.148`. `e2-micro` + 30GB
pd-standard, GCP Always Free tier. Database: PostgreSQL, database name is **`friendsacca`** (not
"fulltime").

**Local Postgres access:** a separate Postgres instance also runs on this dev machine (distinct
database from the VM's, same name `friendsacca`). `FullTime.Api`'s local connection string lives in
.NET User Secrets (`UserSecretsId` `fa9e94b4-5d83-4624-9c4b-28e396835b6e` in `FullTime.Api.csproj`),
at `%APPDATA%\Microsoft\UserSecrets\fa9e94b4-5d83-4624-9c4b-28e396835b6e\secrets.json` — **not
committed to git on purpose**, so it isn't reproduced here; read that file directly rather than
asking the user to retype it. Applying an EF migration locally is just `dotnet ef database update`
from `FullTime.Api/` (picks up User Secrets automatically). For the **VM's** Postgres, there's no
stored connection string to fetch — `sudo -u postgres psql -d friendsacca` over the same `gcloud
compute ssh` used for deploys authenticates via local peer auth, no password needed.

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

**⚠️ `fulltime-api` and `fulltime-web` are stale as of this handover.** Only `fulltime-website` was
redeployed this session (three times, as changes landed). `fulltime-api` in particular now needs a
redeploy before the new invite-by-email feature (§3) will actually work in production — the code is
committed to `main` but the running VM instance predates it. Check `git log` vs. what's actually
running before assuming the VM is current.

**Android testing:** Two AVDs exist locally: `FullTime_GoogleAPIs_API35` and `FullTime_Pixel8_API35`.
adb lives at `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`, emulator at
`C:\Program Files (x86)\Android\android-sdk\emulator\emulator.exe`. **Always use the PowerShell
tool for `adb screencap`/`pull`/`input tap` with `/sdcard/...` paths or coordinates** — git-bash
mangles `/sdcard/...` path arguments. When converting a screenshot's *displayed* coordinates to
real device coordinates for `adb shell input tap`, multiply by `actual_width / displayed_width`. If
a MAUI Android build fails with a file-lock error, run `dotnet build-server shutdown` first, then
retry; if a `dotnet build`/`dotnet run` background process is still holding a DLL lock afterward,
find and kill it by PID (`Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"` filtered by
`CommandLine`, or just the PID printed in the MSB3026/MSB3027 error) rather than assuming
`build-server shutdown` alone released it.

**⚠️ New this session — plain `adb install` crashes a Debug build on launch.** A plain
`dotnet build -f net10.0-android -c Debug` followed by `adb install` produces an app that aborts
immediately (`SIGABRT`, logcat shows `No assemblies found in '.../.__override__/...' ... Assuming
this is part of Fast Deployment. Exiting...`). Debug builds use MAUI's "Fast Deploy", which expects
the .NET assemblies to be pushed to the device *separately* from the APK by the IDE/deploy tooling
— a bare `adb install` skips that step entirely. `dotnet build -t:Run -f net10.0-android` was tried
as the "proper" fix but did **not** actually deploy or launch anything in this environment (build
succeeded, no install/launch occurred, root cause not confirmed — possibly an adb-target-resolution
issue specific to this CLI setup). **What actually worked:** build with
`-p:EmbedAssembliesIntoApk=true` to embed the assemblies straight into the APK (sidesteps Fast
Deploy entirely), then a fresh `adb uninstall com.jtmtechnology.fulltime.app` + `adb install
<path>\com.jtmtechnology.fulltime.app-Signed.apk` (not `-r`), then
`adb shell am start -n com.jtmtechnology.fulltime.app/<activity>` to launch — the crc64-prefixed
activity class name can change between builds, so if `am start` says "Activity class does not
exist", re-resolve it first with `adb shell cmd package resolve-activity --brief
com.jtmtechnology.fulltime.app`. Confirm it's actually running (not silently crashed back to the
launcher) via `adb shell dumpsys window | Select-String mCurrentFocus` before screenshotting.

**New (prior session) — debugging via Chrome DevTools Protocol (CDP):** when a screenshot-based
diagnosis is ambiguous (e.g. "is this element actually clipped, or does it just look that way?"),
you can get exact DOM/layout truth from the *real* on-device Android WebView instead of guessing:
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
CI (ad-hoc IPA builds + TestFlight) and the user's own physical iPhone. **Still not confirmed on a
real iPhone as of this handover** — everything this session and last session was only verified via
the Android emulator and the Blazor Server web host.

---

## 2. Earlier work (stable, shipped before this session — see git log for detail)

Condensed background context, not open work:
- In-app splash bridge + native splash fix, win-celebration overlay (`WinCelebrationOverlay.razor`,
  once per newly-settled `Won` bet via `ICelebratedWinStore`), bet-placement confirmation in
  `BetSlipSheet.razor`, logo/branding assets.
- **Daily Spinner** (prize wheel) was originally built two sessions ago: `/daily-spinner` page, a
  `FreeSpinBanner.razor` promo on the Matches page with a live mini-preview wheel, a shared
  `SpinSegments.All` prize catalogue (placeholder only — no real economy/backend/redemption),
  `IDailySpinStore` persistence (per-host pattern), pointer-landing rotation math, a real football
  photo in a pulsing glow ring. This session (§3) substantially polished it — see below for current
  behaviour, don't assume the original build description still matches.
- **Known still-unresolved from before this session** (not touched, still true unless the user says
  otherwise): TestFlight beta submission was hitting a 422 (likely missing App Store Connect "Test
  Information"); real AdMob IDs still swapped for test IDs; store-listing icon exports not done; a
  stale plan exists at `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md` to retire
  the old football-data provider in favour of Highlightly (unverified, possibly stale — re-check
  before resuming).

---

## 3. This session's work

### 3.1 League invite by email (new feature)
- New `POST api/leagues/{id}/invite` endpoint (`LeaguesController.cs` + `LeagueService.SendInviteAsync`
  + a new `InviteOutcome` enum in `LeagueResults.cs`). Verifies the caller is a member of the league,
  then emails the target address.
- `IEmailSender` gained `SendHtmlAsync(toEmail, subject, htmlBody, textFallback, ct)` (MimeKit
  `MultipartAlternative`) alongside the existing plain-text `SendAsync` (still used unchanged by
  `AuthService` for verification/reset emails) — same Brevo SMTP relay (`SmtpEmailSender.cs`), no
  new config needed.
- The HTML email embeds the FullTime logo (`https://fulltime.jtmtechnology.co.uk/logo.png`), the
  invite code, and a link to `https://fulltime.jtmtechnology.co.uk/invite?code=...&league=...`.
  **That `/invite` page does not exist yet** — the user said they'll build it themselves later, once
  they have real App Store/Play Store links to put on it. Linking to it now is intentional even
  though it currently 404s.
- `Leaderboard.razor`: each league card now has an "✉ Invite a friend by email" button that opens an
  email field and posts via a new `ApiClient.SendLeagueInviteAsync`. New `InviteToLeagueRequest(string
  Email)` DTO mirrored in both `FullTime.Api/Controllers/LeaguesController.cs` and
  `FullTime.App.Shared/Models/ApiModels.cs`, per the project's existing mirrored-DTO convention.
- **Not yet live in production** — `fulltime-api` hasn't been redeployed since this was written (see
  §1's stale-VM warning).

### 3.2 Daily Spinner polish
All in `DailySpinner.razor` / `FreeSpinBanner.razor` / `app.css` unless noted:
- **Fixed a real bug**: dismissing a win used to `Nav.NavigateTo("/")` immediately, so the streak
  update was never actually visible. Dismiss now just closes the popup and stays on the page. The
  TEMP testing bypass (below) had to be adjusted to also clear the local `_lastSpinDate` field
  directly, since it can no longer rely on the old navigate-away forcing a remount that reloaded
  from the (cleared) store.
- **7-day streak now cycles instead of capping**: previously `Math.Min(_streak+1,7)` sat at 7
  forever; now it increments and wraps — hitting 7 awards a flat **£50** placeholder bonus
  (`StreakBonusAmount` const, shown on the win card) and resets the persisted streak to 0, so the
  next day starts a fresh 1..7 cycle.
- **Mystery Cash range**: £5–£100 → **£5–£50** (`Random.Shared.Next(1, 11) * 5`), per explicit ask.
- **Boost results** ("Bet Boost 25%/50%", "2x Odds Boost") now show "Applies automatically to your
  next bet." — copy only, no real bet-integration exists (the whole wheel is still 100%
  placeholder/cosmetic, no backend prize-granting).
- Streak card subtitle: "Spin every day to build your streak — bonus prize on day 7".
- `FreeSpinBanner`'s badge text: "Available" → "**Available now**"; its pulse animation amplitude
  increased (scale peak 1.07 → 1.18, more noticeable).
- The big wheel's pulsing center football image enlarged (`.wheel-center` 86px → 108px, ball scales
  proportionally via its 72%/`object-fit:contain`).
- **Spin ticking sound added**: new `FullTime.App.Shared/wwwroot/spinSound.js`, a small Web Audio
  API module (`playSpinTicks(durationMs)`) synthesizing ~28 short oscillator clicks with an ease-out
  spacing curve (mimics a wheel-of-fortune ratchet slowing down) — deliberately not a shipped audio
  asset, nothing to bundle or license. Loaded via dynamic `import()` through `IJSRuntime`.
  **Real bug caught and fixed**: importing it from `OnInitializedAsync` threw
  `InvalidOperationException` under Blazor Server's static prerendering (JS interop isn't allowed
  until the interactive circuit exists) — moved to `OnAfterRenderAsync(firstRender)` instead. Watch
  for this on any *other* future JS-interop addition to a page rendered by `FullTime.App.Web`.
- **Icon fix**: the `ℹ` (U+2139) and `🎯` emoji rendered inconsistently (wrong/broken glyph on the
  Android emulator; emoji also ignore CSS `color` so couldn't be made green on request). Replaced
  both with a new reusable `.info-icon` CSS class — a small bordered circle with a plain "i",
  colored `var(--accent)` (green) — matching the existing `.wheel-icon-badge`/
  `.spin-banner-icon-badge` "2x"-badge visual language. Used in the streak card's "Miss a day..."
  note and the win card's boost note. **If any other emoji ever looks visually "off" or need a
  specific brand color, check whether it should become a plain-glyph-in-a-badge like this instead
  of fighting emoji-font rendering.**
- **TEMP once-a-day bypass is still in place** (`DismissResult()` clears the spin record on every
  dismiss). This was flagged as unexpected mid-session; asked to be removed, then the user said to
  leave it in for now. Still marked `// TEMP for testing only` in code — still must be removed
  before real shipping (carried over from before, unchanged).

### 3.3 Cosmetic
- `.back-btn` (‹, used across all pages) font-size 1.4rem → 1.8rem.
- `.spin-banner-arrow` (›, on the home-page Free Spin banner) font-size 1.3rem → 1.7rem.
- iOS app icon backdrop color changed from accent green (`#2FAE4F`) to the same dark navy Android
  and the splash screen already use (`#0D1117`), in `FullTime.App/FullTime.App/FullTime.App.csproj`.

### 3.4 Website (`FullTime.Website`)
- Feature grid content refreshed to match currently-shipped app features: added a "Daily Spinner"
  card; updated "Private leagues" copy to mention email invites; updated "Live scores" copy to
  mention win celebrations.
- **Fixed layout bug**: a lone last-row card (7 cards, 3-column layout) sat pinned to the left
  instead of centering. Switched `.feature-grid` from CSS Grid to Flexbox
  (`display:flex; flex-wrap:wrap; justify-content:center`) — flexbox centers every wrapped row
  *including* a partial final one, which CSS Grid does not do for leftover items. Then had to fix a
  second-order issue: `.feature-card`'s original `flex:1 1 240px` let a *lone* last-row card grow to
  fill its entire row, making it visibly wider than cards in full rows above — changed to
  `flex:0 1 296px` (fixed width, no grow) so every card is the same width regardless of row
  completeness.
- **⚠️ Discovered mid-session — the site is proxied through Cloudflare, and it edge-caches static
  assets independent of origin deploys.** `index.html` responses come back `cf-cache-status:
  DYNAMIC` (never cached), but `styles.css` came back `cf-cache-status: HIT` with `Cache-Control:
  max-age=14400` (4h) — a CSS-only redeploy did not show up live even though the file was correctly
  updated on the VM (confirmed via SSH). No Cloudflare dashboard/API access is configured in this
  environment, so purging isn't possible directly. **Workaround in place:** the stylesheet
  `<link>` in `index.html` uses a cache-busting query string, `styles.css?v=N` — bump `N` on every
  CSS-only deploy so Cloudflare treats it as a new URL and fetches fresh from origin immediately.
  Currently at **`v=3`**. If real Cloudflare purge access ever gets configured, this workaround can
  be dropped in favour of an actual purge-on-deploy step.
- Deployed to the VM (`fulltime-website` service) three times this session as changes landed; the
  live site at https://fulltime.jtmtechnology.co.uk reflects all of §3.4 as of this handover.

### 3.5 Files changed this session
- **New:** `FullTime.App.Shared/wwwroot/spinSound.js`.
- **Modified (App/API):** `FullTime.Api/Auth/IEmailSender.cs`, `FullTime.Api/Auth/SmtpEmailSender.cs`,
  `FullTime.Api/Controllers/LeaguesController.cs`, `FullTime.Api/Leagues/LeagueResults.cs`,
  `FullTime.Api/Leagues/LeagueService.cs`, `FullTime.App.Shared/Components/FreeSpinBanner.razor`,
  `FullTime.App.Shared/Models/ApiModels.cs`, `FullTime.App.Shared/Pages/DailySpinner.razor`,
  `FullTime.App.Shared/Pages/Leaderboard.razor`, `FullTime.App.Shared/Services/ApiClient.cs`,
  `FullTime.App.Shared/wwwroot/app.css`, `FullTime.App/FullTime.App/FullTime.App.csproj`.
- **Modified (Website):** `FullTime.Website/wwwroot/index.html`, `FullTime.Website/wwwroot/styles.css`.
- Committed as `55d3e44` ("Add league email invites, tune Daily Spinner, update marketing site") and
  a follow-up website-only commit for the card-width fix + `v=3` cache-bust (see git log for the
  exact hash — created and pushed alongside this handover update).

### 3.6 Verification done this session
- League invite email: backend builds clean; **not actually verified end-to-end** (no real SMTP
  send was triggered/observed — credentials live only in VM config, and `fulltime-api` is stale
  anyway, see §1).
- Daily Spinner + cosmetic changes: built and installed to `FullTime_Pixel8_API35` repeatedly
  (working around the Fast Deploy issue in §1), confirmed launching and rendering; user did their
  own hands-on testing on the emulator and gave several rounds of live feedback (icon color, pulse
  size, badge text, streak copy) that are all incorporated above.
- Website: verified live via `curl` against the public domain (including the `?v=N` cache-bust
  check) after each of the three deploys.
- **Not verified on a real iPhone or against a freshly-deployed `fulltime-api`/`fulltime-web`.**

---

## 4. Outstanding tasks (merged: carried-over + new)

1. **Redeploy `fulltime-api`** — the invite-by-email backend code is committed but not live; the
   feature won't actually work for real users until this happens.
2. **Redeploy `fulltime-web`** — stale relative to `main`, includes everything from §3.2/§3.3 plus
   the prior session's Daily Spinner build.
3. **Confirm everything works on a real iPhone** (Daily Spinner, league invites, all cosmetic
   changes) — still only verified via Android emulator + Blazor Server web host across two
   sessions now.
4. **Revert the TEMP once-a-day spin bypass** in `DailySpinner.razor`'s `DismissResult()` — flagged
   again this session, user asked for removal then explicitly said to leave it in for now. Do not
   ship with it in place; the one-line removal is documented in a comment at the call site.
5. **Design the real prize economy** — wheel/catalogue is still 100% placeholder UI, explicitly
   deferred by the user, unchanged since it was first flagged.
6. **Build the `/invite` website page** — the invite email already links to it
   (`fulltime.jtmtechnology.co.uk/invite?code=...&league=...`); user said they'll do this themselves
   once they have real App Store/Play Store links to include.
7. Real store-listing icon exports (1024×1024 no-alpha Apple / 512×512 with-alpha Google) — not
   produced.
8. Real AdMob IDs still need swapping back in before any store submission (test IDs currently in
   `MauiInterstitialAdService.cs` / `AndroidManifest.xml`).
9. TestFlight beta review 422 — not confirmed resolved, not touched.
10. The stale `free-api-live-football-data`-retirement plan at
    `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md` — untouched, possibly stale,
    re-verify before resuming if picked up.
11. If Cloudflare dashboard/API access ever gets configured, replace the `styles.css?v=N`
    cache-busting workaround (§3.4) with a real purge-on-deploy step.

---

## 5. Conventions reconfirmed/added this session

- **Per-host service pattern** (§1) — still the rule for any new platform-different capability; note
  that plain browser-standard JS interop (no platform difference) doesn't need it, as with the spin
  sound.
- **No comments explaining "what"**, only non-obvious "why" — followed throughout.
- **Confirm before pushing to the VM.** Every VM deploy this session (website × 3) was explicitly
  requested in the moment ("push the website to the vm", "then deploy to vm") — treat each as
  one-time authorization, not standing approval for future deploys including `fulltime-api`/`-web`.
- **Git**: commit messages explain *why*, not *what*; always end with `Co-Authored-By: Claude Sonnet
  5 <noreply@anthropic.com>`; never `--amend`; never force-push. When staging, name files
  explicitly rather than `git add -A`/`.` — this session had several unrelated untracked files
  sitting in the working tree (a stray `CLAUDE.md`, `new 1.txt`, `splash-check.png`, a local
  `scratch/` screenshot folder) that were deliberately left out of both commits.
- **Blazor Server prerendering blocks JS interop until after first render** (§3.2) — any
  `IJSRuntime` call (including dynamic `import()`) must happen in `OnAfterRenderAsync(firstRender)`
  or later, never `OnInitializedAsync`, on any page also served by `FullTime.App.Web`.
- **MAUI Debug builds need `-p:EmbedAssembliesIntoApk=true` for a plain `adb install` to work in
  this environment** (§1) — otherwise the app crashes on launch with a Fast Deploy assembly error.
- **The live website is behind Cloudflare and edge-caches static assets** (§3.4) — a CSS/JS-only
  change needs the `styles.css?v=N` cache-bust bumped, or it won't show up live for up to 4 hours
  even though the origin VM is already updated.
- **When a screenshot makes an on-device CSS bug look ambiguous, reach for CDP (§1) rather than
  iterating blindly on screenshots.**
