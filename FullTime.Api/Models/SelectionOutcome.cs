namespace FullTime.Api.Models;

public enum SelectionOutcome
{
    Pending,
    Correct,
    Incorrect,

    // The match was abandoned - the leg counts as odds 1.0 in its bet (see MatchStatus.Abandoned).
    Void
}
