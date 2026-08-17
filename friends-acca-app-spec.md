# Friends EPL Accumulator App — Technical Spec

## 1. Overview

A web app for a private group of friends to place accumulator ("acca") bets with play money on English Premier League matches, using real odds. Results settle automatically once match outcomes are entered, and a league table tracks everyone's winnings over time.

**Stack** (fits existing .NET/Angular experience):
- Backend: ASP.NET Core Web API (C#)
- Database: PostgreSQL (or SQL Server if preferred)
- Frontend: Angular, or a simpler server-rendered/Blazor option if you want less overhead
- Odds source: The Odds API (h2h market, soccer_epl)
- Hosting: any VPS or PaaS (Azure App Service, Railway, Fly.io, etc.)

---

## 2. Data Model

### User
| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| Name | string | display name, no password needed for MVP |
| Balance | decimal | current play-money balance |
| CreatedAt | datetime | |

Starting balance: configurable constant (e.g. 1000).

### Match
| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| ExternalId | string | ID from odds API, used to dedupe on refresh |
| HomeTeam | string | |
| AwayTeam | string | |
| KickoffTime | datetime | |
| Status | enum | Upcoming, Finished |
| Result | enum? | Home, Draw, Away — null until finished |

### OddsSnapshot
| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| MatchId | Guid | FK |
| HomeOdds | decimal | decimal odds at fetch time |
| DrawOdds | decimal | |
| AwayOdds | decimal | |
| FetchedAt | datetime | |
| Bookmaker | string | which bookmaker these came from (pick one consistent one, e.g. Pinnacle or an average) |

### Bet
| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| UserId | Guid | FK |
| Stake | decimal | deducted from balance at placement |
| CombinedOdds | decimal | product of all selection odds |
| PotentialReturn | decimal | Stake × CombinedOdds |
| Status | enum | Pending, Won, Lost |
| PlacedAt | datetime | |
| SettledAt | datetime? | |

### BetSelection
| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| BetId | Guid | FK |
| MatchId | Guid | FK |
| Pick | enum | Home, Draw, Away |
| OddsAtPlacement | decimal | snapshot — never changes after bet is placed |
| Outcome | enum | Pending, Correct, Incorrect — set when match settles |

A bet with 1 selection = single. 2+ selections = accumulator (all must be Correct for the bet to win).

---

## 3. Core Flows

### 3.1 Fetching odds
- Backend calls The Odds API (`GET /v4/sports/soccer_epl/odds?regions=uk&markets=h2h&oddsFormat=decimal`) for upcoming fixtures.
- Cache the response server-side for a short window (e.g. 5–10 minutes) to avoid burning API quota every time a friend opens the app.
- Upsert into `Match` (by ExternalId) and insert a new `OddsSnapshot` if odds have moved since the last stored snapshot.
- Endpoint: `GET /api/matches/upcoming` — returns upcoming matches with latest odds, for the frontend to display when a user is building a bet.

### 3.2 Placing a bet
- User selects 1+ matches, picks Home/Draw/Away for each, enters a stake.
- `POST /api/bets`
  - Validate: stake > 0, stake ≤ user's current balance, all matches still Upcoming.
  - Snapshot the current odds into each `BetSelection.OddsAtPlacement`.
  - CombinedOdds = product of all selections' OddsAtPlacement.
  - PotentialReturn = Stake × CombinedOdds.
  - Deduct Stake from `User.Balance` immediately.
  - Status = Pending.

### 3.3 Settling a match
- Someone (any friend, or a designated admin — your call) enters the final result.
- `POST /api/matches/{id}/settle` with `Result` (Home/Draw/Away).
- Sets `Match.Status = Finished`, `Match.Result`.
- For every `BetSelection` referencing this match:
  - Outcome = Correct if Pick matches Result, else Incorrect.
- For every `Bet` with a selection on this match, re-check: if **any** selection is Incorrect → `Bet.Status = Lost`. If **all** selections across the whole bet are Correct → `Bet.Status = Won`, credit `PotentialReturn` to `User.Balance`. If some selections are still Pending (other matches in the acca haven't been played yet) → leave as Pending.

### 3.4 League table
- `GET /api/leaderboard` — returns all users sorted by `Balance` descending, with `Profit = Balance - StartingBalance` shown alongside.

---

## 4. API Endpoints Summary

| Method | Route | Purpose |
|---|---|---|
| POST | /api/users | create/join as a friend |
| GET | /api/users/{id} | profile + balance |
| GET | /api/matches/upcoming | fixtures + latest odds |
| GET | /api/matches/{id} | match detail incl. odds history |
| POST | /api/matches/{id}/settle | enter final result, triggers settlement |
| POST | /api/bets | place a single or accumulator bet |
| GET | /api/bets?userId= | a user's bet history |
| GET | /api/leaderboard | league table |

---

## 5. Odds API Integration Notes

- Sign up at The Odds API, free tier gives 25 requests/day — fine for a small friend group if you cache responses (each cached call serves all users until the cache expires).
- Only need the `h2h` market (3-way match result) — cheapest and simplest, matches this scope exactly.
- Store `Bookmaker` on each snapshot so results are explainable ("odds were 2.10 from Pinnacle when you placed this") — pick one bookmaker consistently rather than averaging, to keep it simple and transparent.
- Odds only exist for matches the API has fixtures for (typically the next week or two) — you don't need historical odds for this use case.

---

## 6. Out of scope for MVP (possible v2 ideas)

- Bet builder / same-game multiples (dropped per your last call — could revisit with correlation-adjusted pricing later)
- Password-based auth (fine to start with name-only "login" for a closed friend group)
- Push notifications when a match settles
- Weekly/monthly leaderboard resets alongside all-time
- Native iOS app (per your original idea — once the web version is working, wrapping it or building a companion SwiftUI app that talks to the same API is a natural next step)

---

## 7. Suggested build order

1. Data model + EF Core migrations
2. Odds API integration + `/matches/upcoming` endpoint
3. User creation + balance tracking
4. Bet placement endpoint
5. Settlement logic (this is the fiddly bit — write tests for the acca win/lose logic)
6. Leaderboard endpoint
7. Frontend: fixtures list → bet builder → my bets → leaderboard
