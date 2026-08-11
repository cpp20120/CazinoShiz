namespace BotFramework.Contracts.Cases;

public sealed record CaseEvidence(
    string EvidenceId,
    string Kind,
    string Reference,
    string AddedBy,
    DateTimeOffset AddedAt,
    string? Hash = null);
