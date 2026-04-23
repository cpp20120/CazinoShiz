namespace Games.Horse;

/// <summary>Whether a race settles one chat's pool or every chat merged (legacy).</summary>
public enum HorseRunKind
{
    /// <summary>Only bets with <c>balance_scope_id == ChatScopeId</c>.</summary>
    ThisChat,

    /// <summary>All bets for the calendar day; result stored under scope <c>0</c>.</summary>
    Global,
}
