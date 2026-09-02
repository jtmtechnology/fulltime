# FullTime — Project Conventions

Personal/family project (not TelXL work). Blazor/.NET MAUI football betting-with-friends app.
No real money changes hands — deliberately scoped away from UK Gambling Act concerns. Keep it
that way; don't add real-money payment flows.

## Architecture

- `FullTime.Api/` — ASP.NET Core Web API, EF Core + PostgreSQL, JWT auth.
- `FullTime.App.Shared/` — Razor Class Library holding ALL the actual UI (pages, components,
  layouts, services, `wwwroot/app.css`). Both `FullTime.App` (MAUI head) and `FullTime.App.Web`
  (Blazor Server web head) reference this and render the same markup. Put UI/business logic here,
  not in either head, unless it's genuinely platform-specific.
- `FullTime.Website/` — separate standalone marketing site, own `wwwroot`/`styles.css`, not
  related to `FullTime.App.Shared`. Don't assume changes to one affect the other.

**Per-host service pattern**: any capability that differs between MAUI and Web gets an interface
in `FullTime.App.Shared/Services/`, a `Maui*` implementation in `FullTime.App/Services/`, and a
`Web*` implementation in `FullTime.App.Web/Services/`, registered in `MauiProgram.cs` /
`Program.cs` respectively. Follow this pattern for any new platform-different capability rather
than branching on `DeviceInfo.Platform` inside shared code.

**Database name is `friendsacca`**, not "fulltime" — easy to guess wrong.

## Environment quirks (this dev machine)

- **Use the PowerShell tool, not Bash, for `adb` commands with `/sdcard/...` paths.** Git-bash's
  POSIX-to-Windows path translation mangles `/sdcard/...` arguments before they reach `adb`.
- When converting a screenshot's displayed coordinates to real device coordinates for
  `adb shell input tap`, multiply by `actual_width / displayed_width` — don't pass displayed
  coordinates straight through.
- If a MAUI Android build fails with a file-lock error (e.g. `XARLP7024`, "being used by another
  process"), run `dotnet build-server shutdown` first, then retry. If that's not enough, force
  clean `obj`/`bin` with PowerShell's `Remove-Item -Recurse -Force` — bash `rm -rf` sometimes can't
  fully clear MAUI's `obj/Debug/net10.0-android/lp/...` directories on Windows.
- MAUI's SDK has its own `Microsoft.Maui.Devices.IHapticFeedback`, which collides by name (via
  global usings) with any app-defined `IHapticFeedback` in the MAUI head. Fully qualify one of
  them at both the implementation and the DI-registration call site.
- No Mac is available in this environment. iOS-specific changes can't be built or tested locally —
  verify via Codemagic CI and the real device only.

## Testing conventions

- **Never manually set a real `Match` row's `Status`/scores to a fake "Finished" result to test
  settlement or celebrations.** The live sync will reset it back to `Upcoming` on its own schedule
  once it notices the match hasn't actually finished, and if the `Result` column isn't *also*
  cleared alongside `Status`/scores, it leaves the row in a state that crashes
  `SettlementService`'s sweep for *all* users, not just test ones. If a fake-settlement test is
  ever genuinely needed, use an isolated/local database instead of the production VM's.
- When creating throwaway test users/leagues/bets against the live API for a test, delete them
  again afterward, in FK-safe order: `BetLegPicks` → `BetLegs` → `Bets` → `LeagueMemberships` →
  `Leagues` → `Users`.
- Don't trust elapsed-time reasoning inferred from a sequence of screenshot tool calls (e.g. "I
  slept 500ms so X should have happened by now") — tool round-trip latency adds unpredictable real
  time on top of any explicit sleep, and this has produced wrong conclusions repeatedly. For
  reliable timing data, add temporary `Console.WriteLine`/logcat diagnostics with a `Stopwatch` and
  read real timestamps back out, then remove the diagnostics once done.
- Real AdMob ad unit IDs must never ship in a build submitted to the App Store or Play Store —
  only Google's published test IDs are safe for that. Double-check `MauiInterstitialAdService.cs`
  and the `AndroidManifest.xml`/`Info.plist` AdMob app ID before any store submission.

## Deployment

Standard pattern for pushing a new build to the VM (`fulltime-vm`, GCP zone `us-east1-b`):
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

For local Postgres access (a service already runs on this machine, distinct from the VM's DB):
don't try to guess or hunt for credentials — ask the user directly.

## Working conventions

- Comments explain non-obvious *why* (a constraint, a past incident, a workaround) — never *what*
  the code does, since well-named identifiers already cover that.
- Confirm with the user before pushing to the VM or writing to the production database, and before
  any other destructive/live action — don't assume standing approval from an earlier turn.
- Git: commit messages explain why, not what. Always end commits with
  `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`. Never `--amend`, never force-push.
