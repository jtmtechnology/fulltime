# Friends EPL Accumulator App — Context Recap

Use this alongside `friends-acca-app-spec.md` to pick up where this conversation left off.

## The idea
A website for a private group of friends to place accumulator ("acca") bets with play money on EPL matches, using real odds. Results settle automatically once someone enters the match outcome, and a league table tracks everyone's balance/winnings over time. Eventually, a companion iOS app.

## Key decisions made so far
- **Scope**: dropped the original "bet builder" (same-game multiple markets) idea in favor of simpler **match-result singles and accumulators** — easier to price correctly, and fits standard odds APIs directly.
- **Odds source**: no scraping oddschecker (no public API, fragile, ToS risk). Using **The Odds API** instead — free tier covers 25 requests/day, `h2h` (3-way match result) market for EPL. Cache responses server-side so friends checking the app don't burn the quota.
- **Odds handling**: snapshot the odds at the moment a bet is placed (don't let later line moves change an already-placed bet).
- **Settlement rule**: an accumulator only wins if every leg is correct; any one wrong leg loses the whole bet.
- **Stack**: ASP.NET Core Web API backend (fits existing C#/.NET experience), Postgres database, Angular or simpler frontend, deployed as a Docker container.
- **Auth**: no passwords for MVP — simple name-based "login" since it's a closed friend group.

## Hosting plan
- **Local development**: run everything locally first (via Claude Code, which has real machine/network access) — local Postgres (Docker or installed directly), `dotnet run`, test against localhost before touching any hosting.
- **Backend hosting**: **Render**, free tier to start (750 free instance-hours/month, no credit card). Known limitation: free web services sleep after 15 min inactivity, ~30-60s cold start on next request — fine while building/testing.
- **Upgrade path**: once ready for friends to actually use it, upgrade the Render web service to the **Starter plan (~$7/month)** — removes cold starts entirely, one-click upgrade, no redeploy needed.
- **Database hosting**: **Neon** for Postgres — free tier, no expiry (Render's own free Postgres expires after 90 days, so avoided that).
- Ruled out: Fly.io (no longer has a free tier as of 2024), oddschecker scraping (fragile/risky).
- Considered but not chosen: Railway (~$5/month all-in via Hobby plan, no cold starts, but not free long-term the way Render+Neon is).

## Full technical spec
See `friends-acca-app-spec.md` for the complete data model (User, Match, OddsSnapshot, Bet, BetSelection), API endpoint list, odds integration details, and suggested build order.

## Not yet done
- No code written yet — next step is scaffolding the ASP.NET Core project (data model, EF Core migrations, odds API integration).
- Dockerfile / docker-compose for local Postgres not yet written.
- Render deployment config not yet set up.
