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
- `FullTime.Website/` — separate standalone marketing site (own `wwwroot`, own `styles.css`, not
  related to FullTime.App.Shared). Runs on the VM as `fulltime-website` (port 5300, nginx reverse
  proxy on port 80 for `fulltime.jtmtechnology.co.uk`).

**Per-host service pattern** (important convention, used throughout): any capability that differs
between MAUI and Web gets an interface in `FullTime.App.Shared/Services/`, with a `Maui*`
implementation in `FullTime.App/Services/` and a `Web*` implementation in
`FullTime.App.Web/Services/`, registered in `MauiProgram.cs` / `Program.cs` respectively. Examples:
`IJwtStore`, `ILocaleProvider`, `ISlipStore`, `IActiveContextStore`, `IPushRegistrar`,
`IAdsRemovalService`, `IInterstitialAdService`, `IMatchLeaguePreferenceStore`, `ICelebratedWinStore`,
`IHapticFeedback`, `IDailySpinStore` (new this session).

**VM:** `fulltime-vm`, GCP zone `us-east1-b`, external IP `34.23.16.148`. `e2-micro` + 30GB
pd-standard, GCP Always Free tier. Database: PostgreSQL, database name is **`friendsacca`** (not
"fulltime").

**Deployment pattern** (unchanged, works reliably):
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

**⚠️ `fulltime-api` and `fulltime-web` on the VM are still stale** as of this handover — this
session's Daily Spinner work (and the prior session's splash/win-celebration/bet-placement work)
has only been pushed to git, never deployed to the VM. The Daily Spinner feature itself needs no
API changes (it's 100% client-side/local-storage), but `fulltime-web` needs a redeploy for anyone
to see any of this session's or the prior session's work in a browser. Check `git log` vs. what's
actually running before assuming the VM is current.

**Android testing:** Two AVDs exist locally: `FullTime_GoogleAPIs_API35` and `FullTime_Pixel8_API35`.
adb lives at `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`. **Always use the
PowerShell tool for `adb screencap`/`pull`/`input tap` with `/sdcard/...` paths or coordinates** —
git-bash mangles `/sdcard/...` path arguments. When converting a screenshot's *displayed* coordinates
to real device coordinates for `adb shell input tap`, multiply by `actual_width / displayed_width`
(shown in the image attachment metadata) — mixing this up caused several missed taps this session
even though it's a known, previously-documented gotcha. If a MAUI Android build fails with a
file-lock error, run `dotnet build-server shutdown` first, then retry.

**New this session — debugging via Chrome DevTools Protocol (CDP):** when a screenshot-based
diagnosis is ambiguous (e.g. "is this element actually clipped, or does it just look that way?"),
you can get exact DOM/layout truth from the *real* on-device Android WebView instead of guessing:
```
adb shell pidof <package>                                   # get the running process id
adb shell cat /proc/net/unix | grep devtools                 # confirm the debug socket name
adb forward tcp:9222 localabstract:webview_devtools_remote_<pid>
curl -s http://localhost:9222/json                            # lists the page + its ws:// URL
```
Then open a WebSocket to that `webSocketDebuggerUrl` (Node 22+ has a native `WebSocket` global, no
package install needed) and send `{"id":1,"method":"Runtime.evaluate","params":{"expression":"...","returnByValue":true}}`
— e.g. `document.querySelector('.foo').getBoundingClientRect()` — to get real computed geometry.
This is far more reliable than iterating on screenshots for CSS layout bugs; it caught that an
"Android WebView won't clip rotated children" theory (below) was actually a misdiagnosis.

**iOS:** No Mac available in this environment — iOS changes are pushed and verified via Codemagic
CI (ad-hoc IPA builds + TestFlight) and the user's own physical iPhone.

---

## 2. Earlier session's work (stable, shipped to `main` before this session — see git log for detail)

Condensed from the previous handover; all of this was working and pushed before this session
started, so treat it as background context, not open work:
- In-app splash bridge + native splash fix (`MainLayout.razor`, `splash.svg`) — `OnInitialized` made
  synchronous so BlazorWebView's first paint isn't blocked; splash bridge is a plain colour div, no
  second logo render.
- Win-celebration overlay (`WinCelebrationOverlay.razor`) — shows once per newly-settled `Won` bet,
  tracked by `SettledAt` timestamp via `ICelebratedWinStore`, queues multiple wins in sequence.
- Bet-placement confirmation in `BetSlipSheet.razor` (2.4s hold + haptic via `IHapticFeedback`).
- Logo/branding: "football + banknotes shield" design (`logo.png`, `favicon.png`, `appicon.png` /
  `appicon_ios.png` split, `splash.svg`).
- **Known still-unresolved from that session** (not touched this session, still true unless the
  user says otherwise): splash fix never confirmed on a real iPhone; TestFlight beta submission was
  hitting a 422 (likely missing App Store Connect "Test Information"); real AdMob IDs still swapped
  for test IDs; `fulltime-api`/`fulltime-web` stale on the VM; store-listing icon exports not done;
  a stale plan exists at `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md` to
  retire the old football-data provider in favour of Highlightly.

---

## 3. This session: Daily Spinner feature (prize wheel)

Built a "spin once a day for a prize" feature end-to-end: a promo banner on the Matches page and a
full spinner page, per the user's explicit ask to build the **screen and spin mechanic first**,
before designing the real prize economy. No real money/prize-granting logic exists yet — see §5.

### 3.1 What's there
- **`/daily-spinner` page** (`DailySpinner.razor`): an 8-segment CSS wheel (icon + two-tone
  white/green uppercase label per segment), a fixed pointer, a "SPIN NOW" button, a result popup
  styled like the existing win-celebration card, a 7-day streak tracker, and a live "next free spin"
  countdown.
- **`FreeSpinBanner.razor`**: a promo banner shown above the "Matches" heading on the home page,
  linking to `/daily-spinner`. Renders a **live** mini-preview of the actual wheel (same
  `SpinSegments.All` data, same conic-gradient styling as the main wheel) rather than a static
  image, so it can never drift out of sync with the real prize catalogue. Auto-hides once today's
  spin is used.
- **Prize catalogue** (`Models/SpinSegment.cs`, `SpinSegments.All`) — a shared static array of 8
  `(Icon, Label)` placeholders, currently: Mystery Cash ×3 (❓), Bet Boost 25%/50% ×2 (🚀), Try
  Again Tomorrow ×2 (⏳), 2x Odds Boost ×1 ("2x" in a circle badge). Shared between the big wheel
  and the banner's mini preview specifically so they can't disagree.
- **Persistence**: `IDailySpinStore` (+ `MauiDailySpinStore` / `WebDailySpinStore`), same per-host
  pattern as `IMatchLeaguePreferenceStore`. Stores a single string `"yyyy-MM-dd|streak"` — the local
  date of the last spin and the streak count as of that spin.
- **"Mystery Cash" result**: picks a random multiple of 5 between 5 and 100 (`Random.Shared.Next(1,
  21) * 5`), shown in the user's currency via `ActiveContextState.CurrencySymbol` (already
  initialized elsewhere by `ContextSwitcher.razor` on app load — no extra init needed). Purely
  cosmetic; nothing is actually credited anywhere.
- **"Try Again Tomorrow" result**: deliberately does **not** say "You won!" / "Nice one!" — shows
  "So close!" and an "OK" button instead, since nothing was won.
- **Wheel centre**: a real photo-real football image (`wwwroot/football.png`, transparent
  background) inside a pulsing green radial-glow ring, used in both the big wheel and the banner's
  mini wheel (replaced the plain ⚽ emoji, which couldn't be shaded/lit to match a supplied
  reference image).

### 3.2 Key decisions and why
1. **Pointer-landing rotation math** (`SpinAsync` in `DailySpinner.razor`): pointer is fixed at the
   top; the wheel rotates by `R` degrees clockwise, so landing segment `i` under the pointer needs
   `R mod 360 == (360 - i*45) mod 360`. `_rotationDeg` only ever *increases* (adds `6*360 + delta`
   each spin, `delta` always in `[0,360)`), which is also what guarantees the wheel always spins the
   same direction (clockwise), never backward, across repeated spins in a session.
2. **Segment labels read along the spoke, not screen-upright** (`.wheel-segment-content` has no
   counter-rotation) — deliberately matches a reference wheel graphic where the bottom segment's
   text renders upside-down. This is a stylistic choice specific to the *big* wheel; the *banner's*
   mini-wheel icons **do** counter-rotate to stay upright (`.spin-banner-segment-icon`), since a
   tiny preview icon reads better the normal way up.
3. **A rotated CSS square escapes a circular `overflow:hidden` clip** — real bug, not a
   misdiagnosis: `.wheel-segment`/`.spin-banner-segment` are full-square `position:absolute;inset:0`
   elements rotated to a fixed angle; an un-clipped rotated square's corners project past a
   circle inscribed in it. Fixed by adding `overflow: hidden` (plus `clip-path: circle(50%)` as a
   belt-and-braces second clip) to the wheel disc elements. Without this, a wedge's invisible
   corner could sit on top of controls below the wheel and swallow clicks — this is exactly what
   broke the Spin button once during testing.
4. **The "Android WebView won't clip rotated children" theory was a misdiagnosis** — worth
   remembering so it doesn't get "fixed" again unnecessarily. Mid-session, the banner's CSS
   mini-wheel appeared to render icons outside its circle on-device (but not in desktop
   Edge/Playwright), which was blamed on a WebView compositing bug and "solved" by swapping in a
   static PNG image instead. Later, CDP inspection of the *actual* running WebView (see §1) proved
   the DOM geometry was correct all along — nothing was really escaping the circle. The banner was
   later switched back to a live CSS mini-wheel (per user request, so it can't drift from the real
   prize list) using the exact same technique, and it renders correctly on-device. **If a rotated
   wheel element ever again appears to visually escape its circle on Android but not desktop, check
   real geometry via CDP before assuming it's an engine bug** — it's more likely a genuinely
   low-contrast/small element (this happened with the "2x" badge, which was actually rendering
   correctly but was hard to see as plain text — fixed by wrapping it in a small circular badge to
   match the other icons' visual weight, not by touching its position).
5. **`SpinSegments.All` is a shared static model**, not duplicated between the big wheel and the
   banner, specifically so a future change to the real prize catalogue only needs to happen in one
   place.
6. **⚠️ TEMP: the once-a-day gate is currently bypassed on dismiss.** `DismissResult()` in
   `DailySpinner.razor` calls `await SpinStore.SetAsync("")` right after recording the streak, which
   erases the "already spun today" flag — explicitly requested by the user so they could keep
   spinning repeatedly during iPhone/emulator testing instead of asking to reset it by hand each
   time. **This must be removed before the feature ships for real** — it's marked with a `// TEMP
   for testing only` comment at the call site; removing just that one `await SpinStore.SetAsync("")`
   line (keep the `Nav.NavigateTo("/")` after it) restores the real once-a-day behaviour.
7. **Resetting the daily-spin flag by hand on the emulator** (used constantly this session to
   re-test without waiting for the temp bypass, and still useful once that bypass is reverted):
   ```
   adb shell am force-stop com.companyname.fulltime.app
   adb shell run-as com.companyname.fulltime.app sed -i '/fulltime_daily_spin/d' shared_prefs/com.companyname.fulltime.app_preferences.xml
   adb shell monkey -p com.companyname.fulltime.app -c android.intent.category.LAUNCHER 1
   ```
   This edits only the one SharedPreferences key, leaving login session/streak-adjacent keys alone.
   Full `pm clear`/uninstall also works but wipes the login session and forces re-auth — prefer the
   targeted `sed` unless a clean-slate install is actually wanted.

### 3.3 Files changed this session
- **New:** `FullTime.App.Shared/Models/SpinSegment.cs`, `FullTime.App.Shared/Services/IDailySpinStore.cs`,
  `FullTime.App/Services/MauiDailySpinStore.cs`, `FullTime.App.Web/Services/WebDailySpinStore.cs`,
  `FullTime.App.Shared/Pages/DailySpinner.razor`, `FullTime.App.Shared/Components/FreeSpinBanner.razor`,
  `FullTime.App.Shared/wwwroot/football.png`.
- **Modified:** `FullTime.App/MauiProgram.cs` / `FullTime.App.Web/Program.cs` (DI registration for
  `IDailySpinStore`), `FullTime.App.Shared/Pages/Matches.razor` (banner placed above the `<h1>`),
  `FullTime.App.Shared/wwwroot/app.css` (all wheel/banner/streak styling).
- Committed as `7cf5fd5` ("Add Daily Spinner: prize wheel screen and spin mechanic") and pushed to
  `main`.

### 3.4 Verification done this session
- Built and ran in a real browser (Playwright against `FullTime.App.Web`) and repeatedly on the
  `FullTime_Pixel8_API35` emulator (native MAUI build) — spin animation, correct segment-to-popup
  matching, streak increment/persistence, disabled state after spinning, live countdown, banner
  auto-hide, and the Mystery Cash currency amount all confirmed working.
- **Not yet verified on a real iPhone** — that's the reason this was just pushed to `main`; next
  step is a Codemagic build for the user to test on their physical device.

---

## 4. Outstanding tasks (merged: carried-over + new)

1. **Confirm the Daily Spinner works on a real iPhone** (this session's main open item) — trigger/
   wait for a Codemagic build off the latest `main` push.
2. **Revert the TEMP once-a-day bypass** in `DailySpinner.razor`'s `DismissResult()` once iPhone
   testing is done (§3.2 point 6) — do not ship with it in place.
3. **Design the real prize economy** — the wheel/catalogue is currently 100% placeholder UI with no
   backend, no real currency awarded, no redemption flow. Was explicitly deferred by the user
   ("just for now") — needs a real design pass before this is a genuine feature.
4. Real store-listing icon exports (1024×1024 no-alpha Apple / 512×512 with-alpha Google) still not
   produced.
5. Redeploy `fulltime-api` and `fulltime-web` to the VM — stale since before this session, now more
   so.
6. Real AdMob IDs still need swapping back in before any store submission (test IDs currently in
   `MauiInterstitialAdService.cs` / `AndroidManifest.xml`).
7. TestFlight beta review 422 (likely missing App Store Connect "Test Information") — not confirmed
   resolved, not touched this session.
8. The stale `free-api-live-football-data`-retirement plan at
   `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md` still exists, untouched,
   possibly stale — re-verify before resuming if picked up.

---

## 5. Conventions reconfirmed/added this session

- **Per-host service pattern** (§1) — still the rule for any new platform-different capability.
- **No comments explaining "what"**, only non-obvious "why" — followed throughout.
- **Confirm before pushing to the VM or production DB.** Pushing to `main`/GitHub was explicitly
  requested this session ("commit and push so i can test in iphone") and done directly — that
  authorization was for this specific push, not standing approval for future ones.
- **Git**: commit messages explain *why*, not *what*; always end with `Co-Authored-By: Claude Sonnet
  5 <noreply@anthropic.com>`; never `--amend`; never force-push.
- **adb screenshot coordinates**: always multiply *displayed* image coordinates by
  `actual_width / displayed_width` before passing to `input tap` — got this wrong at least twice
  this session despite it being a known gotcha; double-check every time, especially right after a
  layout change shifts button positions (e.g. the wheel getting bigger moved the Spin button down).
- **When a screenshot makes an on-device CSS bug look ambiguous, reach for CDP (§1) rather than
  iterating blindly on screenshots** — saved a lot of time once actually used, should have reached
  for it sooner in this session's debugging back-and-forth over the banner wheel.
