namespace CasinoShiz.Services.SecretHitler.Presentation;

public abstract record ShCommand
{
    public sealed record Usage : ShCommand;
    public sealed record Unknown(string Action) : ShCommand;
    public sealed record Create : ShCommand;
    public sealed record Join(string Code) : ShCommand;
    public sealed record JoinMissingCode : ShCommand;
    public sealed record Start : ShCommand;
    public sealed record Leave : ShCommand;
    public sealed record Status : ShCommand;

    public sealed record Nominate(int ChancellorPosition) : ShCommand;
    public sealed record Vote(bool Ja) : ShCommand;
    public sealed record PresidentDiscard(int Index) : ShCommand;
    public sealed record ChancellorEnact(int Index) : ShCommand;
    public sealed record NominateMenu : ShCommand;
}
