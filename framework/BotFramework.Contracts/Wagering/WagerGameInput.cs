namespace BotFramework.Contracts.Wagering;

/// <summary>Transport envelope stored in WagerOperation.GameInput.</summary>
public sealed record WagerGameInput(
    long ChatId,
    string DisplayName,
    string Action,
    string Payload);
