namespace FullTime.Api.Models;

// One row per Highlightly match-timeline entry (goal/card/substitution) - fetched and stored
// alongside BetBuilderSyncService.ResolveMatchEventsAsync's existing FirstGoalScorerSide
// resolution, not a separate API call (see HANDOVER investigation 2026-09-07: Highlightly's
// events/{matchId} response already carries player/assist/substitution detail that was being
// fetched and discarded).
public class MatchEvent
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public SelectionSide Team { get; set; }

    // Raw as Highlightly gives it ("13", "45+2") - not parsed to an int, since stoppage-time
    // suffixes aren't numeric and display exactly as the source gives them anyway.
    public required string Minute { get; set; }

    // Highlightly's own vocabulary ("Goal", "Yellow Card", "Red Card", "Substitution", ...) - kept
    // as a plain string rather than an enum since the full vocabulary isn't confirmed, same
    // reasoning as BetBuilderMarket.MarketType being the one place that IS an enum (a small, fully
    // enumerated set) versus provider-vocabulary fields elsewhere that aren't.
    public required string Type { get; set; }

    // Null only for whistle-type events with no single player (none observed yet, but Highlightly's
    // full vocabulary isn't confirmed). For a Substitution, this is the player coming ON; the player
    // going OFF is SubstitutedPlayerName.
    public string? PlayerName { get; set; }
    public long? PlayerExternalId { get; set; }

    // Set only for Goal events with an assist.
    public string? AssistPlayerName { get; set; }
    public long? AssistPlayerExternalId { get; set; }

    // Set only for Substitution events - the player who came off.
    public string? SubstitutedPlayerName { get; set; }
}
