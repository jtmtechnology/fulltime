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

- **Android**: package `com.jtmtechnology.fulltime.app`, version `1.1` / versionCode `8`
  ([FullTime.App.csproj](FullTime.App/FullTime.App/FullTime.App.csproj)). A signed release `.aab`
  has been built and a first **Play Console closed-testing release was mid-flow** when this
  session ended (see §3.3) — verify the user actually finished that flow (added testers, rolled
  out) before assuming it's live anywhere.
- **iOS**: same package id, App Store Connect submission is prepped (IAP, screenshots, metadata,
  privacy/content declarations) but **no build has actually been uploaded yet** — still the
  biggest remaining iOS gap.
- **`codemagic.yaml`** currently runs all three workflows: `android-debug`, `ios-ad-hoc`,
  `ios-testflight` (see §3.2 — this was swapped back this session).
- AdMob is still on Google's **test IDs** everywhere (by design — see §3.4). Don't touch this
  until the app is actually live on a store.
- VM (`fulltime-vm`) services were **not touched this session** — no redeploys happened, don't
  assume staleness without checking `git log` against what's actually running.

---

## 2. Earlier work (stable, predates this session — see git log/older handovers for detail)

Daily Spinner rebuilt server-authoritative, league invite + privacy pages, Android package rename
(`com.companyname.*` → `com.jtmtechnology.*`), UMP consent flow, Play Store listing assets,
App Store Connect prep, test accounts (`test@jtmtechnology.co.uk` in production,
`alan@jtmtechnology.co.uk` locally). Not re-detailed here — see the file history if needed.

---

## 3. This session's work

### 3.1 Fixed three real Android layout/asset bugs (commit `c2c2dbc`)
Reported by the user testing a real **Samsung Galaxy A12**, reproduced on the
`FullTime_Pixel8_API35` emulator by forcing 3-button nav on
(`adb shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton`
— no need for a different emulator image, it's a runtime toggle on any existing AVD).

- **Bottom tab bar collided with the system nav bar.** Android's WebView never populates CSS
  `env(safe-area-inset-bottom)` the way iOS's WKWebView does, so the bar only ever had an 18px
  fallback — fine against Pixel's thin gesture pill, a real collision against a 3-button bar.
  Fixed by bridging the real inset in from native `WindowInsetsCompat`
  ([MainPage.xaml.cs](FullTime.App/FullTime.App/MainPage.xaml.cs)) into a CSS custom property
  (`--android-nav-inset-bottom` in
  [app.css](FullTime.App/FullTime.App.Shared/wwwroot/app.css)).
- **That fix initially squashed the tab bar's own icons down to a sliver** (~7px instead of 32px)
  — Bootstrap's global `box-sizing: border-box` makes `.tab-bar`'s `height` a fixed *total*, not
  content-only, so growing `padding-bottom` to the real inset ate into the icon/label area instead
  of growing the bar. Fixed by baking the inset into `--tab-bar-height` itself instead. **Caught
  by the user re-testing after the first fix landed** — worth remembering that a bottom-bar inset
  fix needs verifying against the bar's own content, not just the collision it was meant to solve.
- **App icon clipped on tighter OEM adaptive-icon masks** (fine on Pixel, clipped on the A12).
  Measured the shield's actual content bounding box: 61%/70% of the canvas, taller than the ~61%
  diameter safe-zone circle Android guarantees. Fixed with `ForegroundScale="0.85"` on the Android
  `MauiIcon` entry in
  [FullTime.App.csproj](FullTime.App/FullTime.App/FullTime.App.csproj).

**Splash screen was investigated and deliberately left as-is.** Doubling `BaseSize` produced
byte-identical generated output — confirmed empirically that Android 12+'s SplashScreen API
hard-caps the icon at a fixed ~95×108dp regardless of source config, for every app, by platform
design. The only way to make it look bigger is a secondary native "branding image" (a real theme
override, not a MAUI config tweak) — **user explicitly chose to leave it as standard Android 12+
behavior rather than pursue that.** Don't re-raise this as a bug; it's a closed decision.

### 3.2 codemagic.yaml swapped back to include Android debug workflow (commit `469a02a`)
`adb` still doesn't detect the physical Android phone over USB (unresolved, see outstanding #2),
so the email-a-debug-APK workflow is needed again. `codemagic.yaml` now holds `android-debug` +
`ios-ad-hoc` + `ios-testflight` together (this is the same content the old
`codemagic-android-debug.yaml` held). The previous **iOS-only** `codemagic.yaml` is saved as
**`codemagic-ios.yaml`** in case of a revert. `codemagic-android-debug.yaml` no longer exists
(merged into `codemagic.yaml`).

### 3.3 First Play Store release: keystore generated, signed AAB built, release started
No release keystore existed anywhere before this session — generated a new one:
- **`signing/fulltime-upload.jks`** (repo-local but **gitignored** — see below), alias
  `fulltime-upload`. Password is in `signing/README.md`, also gitignored.
- Built the first signed release bundle:
  `dotnet publish -f net10.0-android -c Release -p:AndroidPackageFormat=aab -p:AndroidKeyStore=true ...`
  (exact command in `signing/README.md`) →
  `FullTime.App/FullTime.App/bin/Release/net10.0-android/com.jtmtechnology.fulltime.app-Signed.aab`.
- User uploaded it to Play Console's "Create closed testing release" flow. Two warnings shown
  (no deobfuscation file, no native debug symbols) are **both non-blocking** — no R8/ProGuard is
  used so the first doesn't apply, and the second is only a crash-symbolication nicety. User was
  told it's fine to continue past them.
- **User asked to commit the keystore + password to git** — declined and explained why (a leaked
  signing key can't be rotated like an API key; anyone with it can sign updates Play/Android would
  treat as genuinely from this developer). Instead: `signing/` lives in the repo but is gitignored
  (explicit `/signing/` rule added, on top of the existing `*.jks` extension rule, since the
  README carries the plaintext password too). **Confirmed via `git status --ignored` that it's
  genuinely excluded.**
- **⚠️ Outstanding: confirm the user has backed up `signing/fulltime-upload.jks` and its password
  somewhere off this machine** (password manager, secure cloud backup) — being gitignored means a
  lost/corrupted local disk loses it for good otherwise. Also recommend accepting Play App
  Signing if Play Console offers it on first upload — gives a recovery path via identity
  verification if this upload key is ever lost.

### 3.4 AdMob real-ad discussion (no code changed)
Confirmed current state: all three AdMob IDs are still Google's published test IDs
(`AndroidManifest.xml:15`, `Info.plist:51`, `MauiInterstitialAdService.cs:48-49,122`). Swapping to
real IDs is a same-day, no-code-change task once the app is actually live on a store (per the
earlier finding that AdMob won't serve real ads to a not-yet-live app) — everything else (UMP
consent, ad-loading pipeline) is already wired and tested working.

---

## 4. Outstanding tasks

1. **Confirm the user finished the Play Console closed-testing release** (added testers, rolled
   out) — was mid-flow when this session ended.
2. **Confirm the keystore + password are backed up off this machine** (§3.3) before anything
   happens to this disk.
3. Confirm the UMP consent flow and Android package rename work on **iOS** — still unverified
   there (no Mac; compiles but never run). Needs a Codemagic build.
4. Diagnose why `adb` never detects the physical Android phone over USB — still unresolved;
   the Codemagic email-a-debug-APK workflow (§3.2) remains the workaround.
5. Decide what to do with the `test@jtmtechnology.co.uk` production test account — leave it or
   clean it up (FK-safe delete order in `CLAUDE.md`).
6. A stray ~100MB `com.jtmtechnology.fulltime.app-Signed.apk` still sits untracked at the repo
   root (`c:\AB\Friends\`) — unrelated to this session's work, origin still unclear, probably safe
   to delete but still not confirmed.
7. **Finish the App Store Connect submission** — still needs an actual build uploaded (none done
   yet), Apple's Age Suitability questionnaire, and final submission for review.
8. Once FullTime is live on both stores: swap in real AdMob IDs (§3.4), and generate free
   `remove_ads` promo codes for the Brownes league (minus one person) — both deliberately deferred
   to post-launch.
9. Real store-listing icon exports for Apple specifically (1024×1024, no alpha) — not yet
   produced (only the Google 512×512 with-alpha version exists).
10. TestFlight beta review 422 (from an earlier session) — not confirmed resolved.
11. The stale `free-api-live-football-data`-retirement plan at
    `C:\Users\alan.browne\.claude\plans\dazzling-giggling-engelbart.md` — still untouched.
12. If Cloudflare dashboard/API access ever gets configured, replace the `styles.css?v=N`
    (currently `v=5`) cache-busting workaround with a real purge-on-deploy step.

---

## 5. Gotchas discovered this session (in addition to `CLAUDE.md`'s environment quirks)

- **`MauiIcon`'s resizetizer step can silently skip regenerating on a metadata-only item change**
  (e.g. adding `ForegroundScale` with the source PNG unchanged) — there's no `mauiicon.stamp`
  tracking that metadata, so a plain incremental build produces byte-identical output. Force it by
  deleting `obj/Debug/net10.0-android/resizetizer` and `.../lp` before rebuilding, or do a full
  clean. **Verify the actual generated file changed** (e.g. re-measure the icon's content bounding
  box) rather than trusting that a successful build applied your change.
- **A Razor Class Library's `wwwroot` content (e.g. `FullTime.App.Shared/wwwroot/app.css`) can be
  packaged stale into the Android APK** even after a normal rebuild — the static-web-assets copy
  step doesn't always invalidate correctly. If a CSS/asset change doesn't seem to take effect,
  clean both the Shared project's `obj/.../staticwebassets*` **and** the consuming Android
  project's `obj/.../assets` folder (or do a full clean of both), then rebuild.
- Confirmed the ground truth for both of the above via **Chrome DevTools Protocol** against the
  running app's real WebView (`adb forward` to `webview_devtools_remote_<pid>`, then
  `Runtime.evaluate` over the websocket for `getComputedStyle`/`getBoundingClientRect`) — more
  reliable than trusting a screenshot or the source CSS when a build's incremental caching is in
  doubt. Same technique noted in earlier handovers for pure layout debugging; this session it
  specifically caught two build-staleness bugs a screenshot alone would've missed.
- **Bash `rm -rf` can leave a genuinely broken partial state** in MAUI's
  `obj/Debug/net10.0-android/lp/...` on Windows (matches the existing `CLAUDE.md` note about it
  not fully clearing — this session it went further and caused a hard build error, `XACDJ7028`,
  referencing a file a partial `rm -rf` had removed). Use PowerShell's `Remove-Item -Recurse
  -Force` for any clean of that tree, not bash.
- **Generating an Android release keystore**: `keytool` lives at the Android SDK's bundled JDK
  (`C:\Program Files\Android\openjdk\jdk-21.0.8\bin\keytool.exe`, found via `find`, not on PATH).
  Modern `keytool` defaults to PKCS12 format, which **requires the store password and key password
  to be identical** — it silently ignores a differing `-keypass`.
- **3-button vs gesture nav is a runtime toggle, not an emulator-image choice** — any existing AVD
  can switch via `adb shell cmd overlay enable-exclusive --category
  com.android.internal.systemui.navbar.threebutton` (list options with `adb shell cmd overlay list
  android | grep navbar`). Useful for reproducing OEM-specific nav bar complaints without a new
  device profile. Note: `FullTime_Pixel8_API35` was left in 3-button mode at the end of this
  session, not the emulator's gesture-nav default.
