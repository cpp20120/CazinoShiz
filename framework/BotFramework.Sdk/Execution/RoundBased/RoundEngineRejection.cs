namespace BotFramework.Sdk.Execution;

/// <summary>A machine-readable reason why a phase transition was not applied.</summary>
public sealed record RoundEngineRejection(RoundEngineRejectionReason Reason)
{
    public string Code => Reason switch
    {
        RoundEngineRejectionReason.GameNotWaiting => "game_not_waiting",
        RoundEngineRejectionReason.GameNotActive => "game_not_active",
        RoundEngineRejectionReason.GameNotSuspended => "game_not_suspended",
        RoundEngineRejectionReason.GameIsTerminal => "game_is_terminal",
        RoundEngineRejectionReason.StalePhase => "stale_phase",
        RoundEngineRejectionReason.DeadlineNotSet => "deadline_not_set",
        RoundEngineRejectionReason.PhaseNotExpired => "phase_not_expired",
        _ => throw new InvalidOperationException($"Unknown round rejection reason '{Reason}'."),
    };
}
